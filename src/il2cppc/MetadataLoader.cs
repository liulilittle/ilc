using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler;

public static class MetadataLoader
{
    //--------------------------------------------------------------------------
    // LoadAssembly -- read every top-level type of 'path' into the model.
    //
    // M2 scope:
    //   * instance fields are classified (Int32 / Reference) from their field
    //     signature and collected in TypeDef.InstanceFields (the type emitter
    //     lays them out);
    //   * the base type name is decoded for the descriptor's baseClass slot;
    //   * static fields / methods keep their M1 behaviour.
    //--------------------------------------------------------------------------
    public static List<TypeDef> LoadAssembly(string path)
    {
        var types = new List<TypeDef>();
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        foreach (var tdHandle in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(tdHandle);
            // Skip ALL nested types -- they are handled by TypeEmitter.ExpandNestedTypes
            // which properly sets IsNested, DeclaringTypeName, and unique C++ names.
            if (td.IsNested) continue;
            var name = md.GetString(td.Name);
            var ns = md.GetString(td.Namespace);
            if (name == "<Module>") continue; // module initializer type, handled in M2

            var typeDef = new TypeDef
            {
                Name = name,
                Namespace = ns,
                IsModuleType = false,
                BaseTypeName = DecodeBaseType(md, td.BaseType),
                IsInterface = (td.Attributes & TypeAttributes.Interface) != 0,
                // Devirt S1 (devirtualization-plan.md section S1): the metadata sealed
                // bit (TypeAttributes.Sealed 0x1) -- a sealed class cannot be
                // subclassed, the basis for callvirt -> direct-call lowering.
                // Also the abstract bit (0x20): abstract types (incl. static
                // classes, which are abstract+sealed) are never concrete
                // dispatch targets.
                IsSealed = (td.Attributes & TypeAttributes.Sealed) != 0,
                IsAbstract = (td.Attributes & TypeAttributes.Abstract) != 0,
                // Guard promotion (cctor audit): beforefieldinit types (compiler-generated
                // .cctor, no explicit static constructor) may be initialized at any time
                // before first access (ECMA II.7.5.4.1).
                BeforeFieldInit = (td.Attributes & TypeAttributes.BeforeFieldInit) != 0,
                // Value types (M3 Task 11): a struct inherits directly from System.ValueType
                // (not a class).
                IsValueType = DecodeBaseType(md, td.BaseType) == "System.ValueType",
                // Generic type definitions (M3 Task 7): GetGenericParameters count = arity.
                GenericArity = td.GetGenericParameters().Count,
                // where-clause constraints -- the first decode of the GenericParamConstraint
                // table in this transpiler. Until now the table was never read, which is why
                // every unconstrained `T` was forced into an int32 slot even though at the
                // IL level an unconstrained T is System.Object (value-type args arrive boxed).
                // A null/empty entry per index means "unconstrained".
                GenericConstraints = TypeDef.GenericConstraintReader.Read(
                    md, td.GetGenericParameters(), h => ResolveTypeName(md, h)),
            };

            // Interface implementations (Task 8): the InterfaceImpl table's (class, interface)
            // pairs, collected onto the type by class. An interface may be a TypeRef (outside
            // this assembly) or a TypeDef.
            foreach (var iih in td.GetInterfaceImplementations())
            {
                var impl = md.GetInterfaceImplementation(iih);
                // The interface is a TypeDefOrRef: TypeRef (outside this
                // assembly) or TypeDef, plus TypeSpec (a generic-instantiation
                // interface like IBar<int>) -- ResolveTypeName covers all three.
                var ifaceName = ResolveTypeName(md, impl.Interface);
                typeDef.InterfaceNames.Add(ifaceName);
            }

            foreach (var fh in td.GetFields())
            {
                var fd = md.GetFieldDefinition(fh);
                var fName = md.GetString(fd.Name);
                // NOTE: compiler-internal fields (names starting with '<',
                // e.g. auto-property backing fields "<X>k__BackingField") are
                // loaded TOO -- the property-folding pass (iron rule 2j)
                // needs the backing field in the model to fold auto
                // properties into direct field access. Sanitize maps the name
                // to a valid C++ member ("_X_k__BackingField").

                var isStatic = (fd.Attributes & FieldAttributes.Static) != 0;
                // Every field -- instance AND static -- has its signature
                // decoded into TypeKind/FieldTypeName (see DecodeFieldSignature
                // contract). Static fields were historically loaded type-less
                // (the static-field typing gap -- decimal sf_Euler root cause:
                // a static System.Decimal field needs 16-byte Il2CppDecimal
                // storage, and the model is what EmitStaticStorage /
                // EmitTypeInfo classify from).
                var (kind, typeName) = DecodeFieldSignature(md, fd.Signature, extended: isStatic);
                // raudit4: an enum's compiler-generated value__ instance field
                // carries the enum's REAL underlying primitive (System.Int64
                // for `enum LE : long`), but the instance decode returns an
                // untyped int32 slot (the M3 contract), so the enum metadata
                // registration (TryGetUserEnumRegisterInfo) always saw Int32.
                // Re-decode THIS field with the extended name table -- the
                // Kind/layout stays the int32 slot; only the reflected name
                // (and thus the registered underlying TypeCode) is fixed.
                if (!isStatic && fName == "value__" && typeDef.BaseTypeName == "System.Enum")
                {
                    (kind, typeName) = DecodeFieldSignature(md, fd.Signature, extended: true);
                }
                // R2b P2: VAR/MVAR field (generic parameter) -- parallel flag,
                // the M3 default kind/name above stays for definitions.
                bool fieldIsVar;
                var fieldVarIndex = -1;
                {
                    var fsig = md.GetBlobReader(fd.Signature);
                    _ = fsig.ReadByte(); // FIELD (0x06)
                    var fb = fsig.ReadByte();
                    fieldIsVar = fb is 0x13 or 0x1E;
                    // GN-14 (c): a bare VAR (0x13) field carries its class
                    // parameter index; MVAR (0x1E) is method-level -> -1.
                    if (fb == 0x13) fieldVarIndex = fsig.ReadCompressedInteger();
                }
                var field = new FieldDef
                {
                    Name = fName,
                    Owner = typeDef,
                    IsStatic = isStatic,
                    IsLiteral = (fd.Attributes & FieldAttributes.Literal) != 0,
                    IsPublic = (fd.Attributes & FieldAttributes.Public) != 0,
                    ConstantValue = TryReadConstant(md, fd),
                    TypeKind = kind,
                    FieldTypeName = typeName,
                    IsGenericVar = fieldIsVar,
                    GenericVarIndex = fieldVarIndex,
                };
                if (isStatic)
                    typeDef.StaticFields.Add(field);
                else
                    typeDef.InstanceFields.Add(field);
            }

            foreach (var mh in td.GetMethods())
            {
                var mdInfo = md.GetMethodDefinition(mh);
                var mName = md.GetString(mdInfo.Name);
                var (paramCount, returnType, paramTypes, returnTypeName) = MethodSignatureCoder.Decode(md, mdInfo.Signature);
                // R2b P2: parallel VAR channel (0x13/0x1E positions) -- the raw
                // encodings above stay untouched (hash contract).
                var (returnIsVar, paramIsVars, paramIsByrefVars, returnVarIndex, paramVarIndex) = MethodSignatureCoder.DecodeVarFlags(md, mdInfo.Signature);
                var attrs = mdInfo.Attributes;
                // P/Invoke (M4 Task 1): the ImplMap entry names the native
                // library + imported symbol ([DllImport] in source).
                string? pinvokeModule = null;
                string? pinvokeEntry = null;
                var pinvokeFlags = 0;
                var import = mdInfo.GetImport();
                if (!import.Module.IsNil)
                {
                    pinvokeModule = md.GetString(md.GetModuleReference(import.Module).Name);
                    pinvokeEntry = md.GetString(import.Name);
                    pinvokeFlags = (int)import.Attributes;
                }
                typeDef.Methods.Add(new MethodDef
                {
                    Name = mName,
                    Owner = typeDef,
                    IsStatic = (attrs & MethodAttributes.Static) != 0,
                    IsPublic = (attrs & MethodAttributes.Public) != 0,
                    ParamCount = paramCount,
                    ParamTypes = paramTypes,
                    ReturnType = returnType,
                    ReturnTypeName = returnTypeName,
                    // Virtual method flags (Task 7 vtable planning): the Virtual bit covers
                    // override/new slot; ReuseSlot bit = explicit override (C# override keyword).
                    // abstract/final are used by the planner for validation.
                    IsVirtual = (attrs & MethodAttributes.Virtual) != 0,
                    IsOverride = (attrs & MethodAttributes.ReuseSlot) != 0,
                    IsAbstract = (attrs & MethodAttributes.Abstract) != 0,
                    IsFinal = (attrs & MethodAttributes.Final) != 0,
                    IsPInvoke = pinvokeModule != null,
                    PinvokeModule = pinvokeModule,
                    PinvokeEntryName = pinvokeEntry,
                    PinvokeFlags = pinvokeFlags,
                    // SetLastError flag = MethodImportAttributes.SetLastError (0x40)
                    PinvokeSetLastError = (pinvokeFlags & 0x40) != 0,
                    // M7 genlist: the method's OWN generic arity (MVAR count --
                    // `T Id<T>(T x)` has arity 1). MethodSignatureCoder.Decode
                    // skips the generic parameter count of generic signatures,
                    // so it is read here from the raw signature header.
                    GenericArity = mdInfo.GetGenericParameters().Count,
                    // R2b P2: parallel VAR channel for closed clones.
                    ParamIsVar = paramIsVars,
                    ReturnIsVar = returnIsVar,
                    ParamIsByrefVar = paramIsByrefVars,
                    // GN-14 (c): the VAR (0x13) per-slot class-parameter indices.
                    ReturnVarIndex = returnVarIndex,
                    ParamVarIndex = paramVarIndex,
                });
            }

            // Reflected properties (refl3/m7d): the PropertyDefinition table.
            // The accessor handles (get_X/set_X) are resolved to METHOD NAMES
            // -- the typeinfo property table (Il2CppPropertyInfo) carries the
            // accessor names, and the runtime resolves them by name through
            // the SAME methods table (typeinfo.h "single source of truth").
            // A property without a getter or setter leaves that slot null.
            foreach (var ph in td.GetProperties())
            {
                var pd = md.GetPropertyDefinition(ph);
                var (idxParams, pType) = MethodSignatureCoder.DecodeProperty(md, pd.Signature);
                var accessors = pd.GetAccessors();
                string? getterName = null;
                string? setterName = null;
                if (!accessors.Getter.IsNil)
                    getterName = md.GetString(md.GetMethodDefinition(accessors.Getter).Name);
                if (!accessors.Setter.IsNil)
                    setterName = md.GetString(md.GetMethodDefinition(accessors.Setter).Name);
                typeDef.Properties.Add(new PropertyDef
                {
                    Name = md.GetString(pd.Name),
                    Owner = typeDef,
                    TypeName = pType,
                    GetterName = getterName,
                    SetterName = setterName,
                    IndexParamCount = idxParams,
                });
            }

            // Reflected events (COM-C3 / RF-N1): the EventDefinition table (the
            // EventMap, II.22.13/14). Only the NAME + add/remove ACCESSOR
            // names are read here (the compiler emits add_Tick / remove_Tick
            // accessors; a field-like event may omit the raise accessor). The
            // eventIID (the folded [Guid] of the event's COM SOURCE interface)
            // is resolved in the SECOND pass -- it needs the resolved
            // interface graph (the "event -> source interface -> [Guid]"
            // chain is only meaningful once InterfaceTypes are built).
            foreach (var eh in td.GetEvents())
            {
                var ed = md.GetEventDefinition(eh);
                var evAccessors = ed.GetAccessors();
                string? evAddName = null;
                string? evRemoveName = null;
                if (!evAccessors.Adder.IsNil)
                    evAddName = md.GetString(md.GetMethodDefinition(evAccessors.Adder).Name);
                if (!evAccessors.Remover.IsNil)
                    evRemoveName = md.GetString(md.GetMethodDefinition(evAccessors.Remover).Name);
                typeDef.Events.Add(new EventDef
                {
                    Name = md.GetString(ed.Name),
                    Owner = typeDef,
                    AddName = evAddName,
                    RemoveName = evRemoveName,
                });
            }
            types.Add(typeDef);
        }

        // Second pass: resolve BaseTypeName -> TypeDef (type graph for the
        // vtable planner). System.Object and any BCL base stay null (runtime
        // types are not translated user types in M2).
        // Use a custom loop instead of ToDictionary to handle duplicate FullNames
        // (e.g., multiple nested types with the same short name like <>c).
        var byName = new Dictionary<string, TypeDef>(StringComparer.Ordinal);
        foreach (var t in types)
        {
            if (!byName.ContainsKey(t.FullName))
                byName[t.FullName] = t;
        }
        foreach (var t in types)
        {
            // Base type resolution (System.Object stays null -- runtime type).
            if (t.BaseTypeName != "System.Object" && byName.TryGetValue(t.BaseTypeName, out var baseType))
                t.BaseType = baseType;
            // Interface resolution: every type's InterfaceNames -> TypeDef.
            // F1 (R2b P2): a generic-instantiation interface records its
            // canonical angle-bracket name ("Cases.IMyList`1<T>" from
            // TypeSpec/GenericInst -- VAR args decode to "T") or a VAR->int
            // suffix name; the definition FullName is the substring before
            // '<' / the "_" prefix. Lookup order mirrors
            // ResolveInstantiationOwner: exact -> '<' strip -> prefix.
            foreach (var ifaceName in t.InterfaceNames)
                if (byName.TryGetValue(ifaceName, out var ifaceType))
                    t.InterfaceTypes.Add(ifaceType);
                else
                {
                    var lt = ifaceName.IndexOf('<');
                    if (lt > 0 && byName.TryGetValue(ifaceName[..lt], out var ifaceDef))
                        t.InterfaceTypes.Add(ifaceDef);
                    else if (types.FirstOrDefault(x => x.GenericArity > 0
                        && ifaceName.StartsWith(x.FullName + "_", StringComparison.Ordinal)) is { } ifacePrefix)
                        t.InterfaceTypes.Add(ifacePrefix);
                }
        }

        // C3-1 (COM-C3): fold each event's COM SOURCE interface [Guid] into the
        // eventIID -- the "event -> source interface -> [Guid]" chain. The
        // source interface = the FIRST interface in the type's own+base chain
        // (resolved to a TypeDef in THIS assembly) whose [Guid] attribute is
        // present AND which has a METHOD named exactly like the event (the COM
        // event-source convention: one source-interface method per event --
        // interface EVENTS are not methods, so you cannot missfire on them).
        //
        // Failure policy: a type whose event has NO matching interface keeps a
        // ZEROED eventIID (a managed-only event -- still enumerated by
        // GetEvents; the C3-2 RCW attach path LOUD-rejects it at RUNTIME). A
        // matching interface WITHOUT a parseable [Guid] is WARNED, not fatal --
        // LOUD would break every event-bearing fixture (the 0-event/0-delta
        // shape must stay zero-regression). This keeps the transpile sweep safe
        // while the C3-2 RCW attach carries the decision's parse-failure LOUD.
        foreach (var t in types)
        {
            if (t.Events.Count == 0) continue;
            foreach (var e in t.Events)
            {
                TypeDef? src = null;
                for (var scan = t; scan != null && src == null; scan = scan.BaseType)
                    src = scan.InterfaceTypes.FirstOrDefault(i => i.Methods.Any(m => m.Name == e.Name));
                if (src == null) continue; // managed-only event -- zeroed IID
                var iid = ReadComSourceEventIid(md, src);
                if (iid == null)
                {
                    Console.WriteLine($"event: source interface '{src.FullName}' of event '{e.Name}' on '{t.FullName}' has no valid [Guid] -- the eventIID stays zeroed (managed-only; the C3-2 RCW attach LOUD-rejects a zeroed IID)");
                    continue;
                }
                e.EventIID = iid;
            }
        }
        return types;
    }

    //--------------------------------------------------------------------------
    // C3-1 (COM-C3): read a COM event-source interface's [Guid] CustomAttribute
    // and fold it into the 16 raw IID bytes (the .NET Guid byte layout the
    // runtime's kComIid* constants use -- Data1/2/3 little-endian, Data4 in
    // order -- see il2cpp_com.h). Returns null when the interface TypeDef has
    // no GuidAttribute or its blob cannot be decoded / parsed.
    //--------------------------------------------------------------------------
    private static byte[]? ReadComSourceEventIid(MetadataReader md, TypeDef iface)
    {
        foreach (var h in md.TypeDefinitions)
        {
            if (md.GetFullName(h) != iface.FullName) continue;
            List<CustomAttributeDef> attrs;
            try { attrs = ReadCustomAttributes(md, h); }
            catch (NotSupportedException) { return null; }
            foreach (var a in attrs)
            {
                if (a.AttributeType != "System.Runtime.InteropServices.GuidAttribute") continue;
                if (a.FixedArgs.Count == 0 || a.FixedArgs[0] is not string s) return null;
                return Guid.TryParse(s, out var g) ? g.ToByteArray() : null;
            }
            return null;
        }
        return null;
    }

    //--------------------------------------------------------------------------
    // LoadManifestResources -- RES-1 (iron rule 18): read the assembly's
    // embedded manifest resources.
    //
    // The ManifestResource table names every resource; only rows whose
    // Implementation is nil are embedded in THIS file (the others live in a
    // separate .resources file / another assembly). For an embedded row, the
    // Offset field is relative to the CLI header's Resources directory RVA
    // (ECMA-335 II.22.24). The bytes there are the PE resource blob: a 4-byte
    // int32 length prefix followed by the payload -- the payload is exactly
    // what Assembly.GetManifestResourceStream returns (the prefix is framing,
    // not data). A PE with no resource section yields an empty list (error
    // tolerance, not an exception).
    //--------------------------------------------------------------------------
    public static List<ManifestResourceDef> LoadManifestResources(string assemblyPath)
    {
        var resources = new List<ManifestResourceDef>();
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        var resourcesDir = pe.PEHeaders.CorHeader?.ResourcesDirectory ?? default;
        if (resourcesDir.Size == 0)
            return resources; // no resource section: nothing embedded

        foreach (var mrh in md.ManifestResources)
        {
            var mr = md.GetManifestResource(mrh);
            if (!mr.Implementation.IsNil) continue; // embedded-only (Implementation == nil)

            var block = pe.GetSectionData(resourcesDir.RelativeVirtualAddress + (int)mr.Offset);
            var framed = block.GetContent(0, block.Length);
            if (framed.Length < 4) continue; // malformed: no length prefix
            var payloadLen = BinaryPrimitives.ReadInt32LittleEndian(framed.AsSpan());
            if (payloadLen < 0 || 4 + payloadLen > framed.Length) continue; // corrupt framing: skip row

            resources.Add(new ManifestResourceDef(
                md.GetString(mr.Name),
                framed.AsSpan(4, payloadLen).ToArray()));
        }
        return resources;
    }

    //--------------------------------------------------------------------------
    // GetEntryPointFullName -- the assembly's managed entry point straight from
    // the CLI header's entry-point token (ECMA-335 II.25.4.2), as
    // "Namespace.Type::Method". Returns null when the module declares none (a
    // library) or the token is not a MethodDef. The generator's EmitMain finds
    // Main by NAME (M2 contract); this is the metadata-literal answer the
    // loader exposes for audit/validation, independent of naming conventions.
    //--------------------------------------------------------------------------
    public static string? GetEntryPointFullName(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var token = pe.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0;
        if (token == 0) return null; // no CLI entry point (a library / native module)
        var md = pe.GetMetadataReader();
        try
        {
            // The CLI entry point is a MethodDef token (0x06) per ECMA-335
            // II.25.4.2 -- the metadata reader exposes no entry-point accessor,
            // so the handle is rebuilt from the token.
            var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(token);
            if (handle.IsNil) return null;
            var method = md.GetMethodDefinition(handle);
            return md.GetFullName(method.GetDeclaringType()) + "::" + md.GetString(method.Name);
        }
        catch
        {
            // A malformed/non-MethodDef entry-point token: no usable entry.
            return null;
        }
    }

    //--------------------------------------------------------------------------
    // LoadCustomAttributes -- the CustomAttribute table rows of one type
    // (ATTRIBUTE domain; MethodMap.Attribute.cs register contract): attribute
    // class name + ctor name + the FULLY DECODED blob (fixed ctor args per the
    // ctor signature + named field/property args). The blob layout is
    // ECMA-335 II.23.3: 0x0001 prolog, then one value per ctor parameter
    // (untyped -- the SHAPE comes from the ctor signature), then the named-arg
    // list. Values are boxed .NET constants; enums decode to
    // CustomAttributeEnumValue. An unknown type full name returns an empty list.
    //--------------------------------------------------------------------------
    public static IReadOnlyList<CustomAttributeDef> LoadCustomAttributes(string assemblyPath, string typeFullName)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var tdHandle = default(TypeDefinitionHandle);
        foreach (var h in md.TypeDefinitions)
        {
            if (md.GetFullName(h) == typeFullName)
            {
                tdHandle = h;
                break;
            }
        }
        return tdHandle.IsNil
            ? Array.Empty<CustomAttributeDef>()
            : ReadCustomAttributes(md, tdHandle);
    }

    //--------------------------------------------------------------------------
    // LoadAllCustomAttributes -- EVERY type's CustomAttribute rows (the
    // D-ATTR-1 item c batch surface the code generator consumes). Returns the
    // metadata full name of each annotated type (GetFullName on a
    // TypeDefinitionHandle -- the SHORT name for nested types, the same
    // literal the typeof fold and the class descriptor carry) plus its decoded
    // attributes. A type whose blob cannot be decoded is SKIPPED (warned, not
    // fatal -- a single unreadable attribute must never sink a transpile; the
    // unsupported rows simply stay unregistered, a documented simplification).
    //--------------------------------------------------------------------------
    public static List<(string TypeName, List<CustomAttributeDef> Attributes)> LoadAllCustomAttributes(MetadataReader md)
    {
        var result = new List<(string, List<CustomAttributeDef>)>();
        foreach (var h in md.TypeDefinitions)
        {
            var typeName = md.GetFullName(h);
            try
            {
                var attrs = ReadCustomAttributes(md, h);
                if (attrs.Count > 0)
                    result.Add((typeName, attrs));
            }
            catch (NotSupportedException ex)
            {
                Console.WriteLine($"attribute: skipping custom attributes of '{typeName}' (unsupported blob: {ex.Message})");
            }
        }
        return result;
    }

    //--------------------------------------------------------------------------
    // LoadAllMemberCustomAttributes -- EVERY type's METHOD / FIELD / PROPERTY
    // CustomAttribute rows (RF-N6, NEXT2-DECISION section ② RF-N6). Returns
    // (type metadata full name, member name, decoded attributes) in metadata
    // declaration order per type. A member whose blob cannot be decoded is
    // SKIPPED (warned, not fatal -- the same simplification as the type-level
    // loader). The member-registry key is built by the emitter from the type
    // full name + member name ("FullName::memberName").
    //--------------------------------------------------------------------------
    public static List<(string TypeName, string MemberName, List<CustomAttributeDef> Attributes)> LoadAllMemberCustomAttributes(MetadataReader md)
    {
        var result = new List<(string, string, List<CustomAttributeDef>)>();
        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            var typeName = md.GetFullName(th);
            foreach (var mh in td.GetMethods())
            {
                var mdef = md.GetMethodDefinition(mh);
                var mName = md.GetString(mdef.Name);
                try
                {
                    var attrs = DecodeCustomAttributes(md, mdef.GetCustomAttributes());
                    if (attrs.Count > 0) result.Add((typeName, mName, attrs));
                }
                catch (NotSupportedException ex)
                {
                    Console.WriteLine($"attribute: skipping custom attributes of member '{typeName}::{mName}' (unsupported blob: {ex.Message})");
                }
            }
            foreach (var fh in td.GetFields())
            {
                var fdef = md.GetFieldDefinition(fh);
                var fName = md.GetString(fdef.Name);
                try
                {
                    var attrs = DecodeCustomAttributes(md, fdef.GetCustomAttributes());
                    if (attrs.Count > 0) result.Add((typeName, fName, attrs));
                }
                catch (NotSupportedException ex)
                {
                    Console.WriteLine($"attribute: skipping custom attributes of member '{typeName}::{fName}' (unsupported blob: {ex.Message})");
                }
            }
            foreach (var ph in td.GetProperties())
            {
                var pdef = md.GetPropertyDefinition(ph);
                var pName = md.GetString(pdef.Name);
                try
                {
                    var attrs = DecodeCustomAttributes(md, pdef.GetCustomAttributes());
                    if (attrs.Count > 0) result.Add((typeName, pName, attrs));
                }
                catch (NotSupportedException ex)
                {
                    Console.WriteLine($"attribute: skipping custom attributes of member '{typeName}::{pName}' (unsupported blob: {ex.Message})");
                }
            }
        }
        return result;
    }

    private static List<CustomAttributeDef> ReadCustomAttributes(MetadataReader md, TypeDefinitionHandle tdHandle)
    {
        var td = md.GetTypeDefinition(tdHandle);
        return DecodeCustomAttributes(md, td.GetCustomAttributes());
    }

    private static List<CustomAttributeDef> DecodeCustomAttributes(MetadataReader md, CustomAttributeHandleCollection handles)
    {
        var result = new List<CustomAttributeDef>();
        foreach (var cah in handles)
        {
            var ca = md.GetCustomAttribute(cah);
            string ctorName;
            string attrType;
            List<CtorParam> paramTypes;
            if (ca.Constructor.Kind == HandleKind.MemberReference)
            {
                var mr = md.GetMemberReference((MemberReferenceHandle)ca.Constructor);
                ctorName = md.GetString(mr.Name);
                attrType = ResolveTypeName(md, mr.Parent);
                var sig = md.GetBlobReader(mr.Signature);
                paramTypes = DecodeCtorParamTypes(ref sig);
            }
            else if (ca.Constructor.Kind == HandleKind.MethodDefinition)
            {
                // An attribute class defined in THIS assembly: the ctor is a
                // MethodDef (its declaring type is the attribute class).
                var mdef = md.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor);
                ctorName = md.GetString(mdef.Name);
                attrType = md.GetFullName(mdef.GetDeclaringType());
                var sig = md.GetBlobReader(mdef.Signature);
                paramTypes = DecodeCtorParamTypes(ref sig);
            }
            else
            {
                throw new NotSupportedException($"custom attribute ctor handle kind {ca.Constructor.Kind} not supported in M2");
            }

            var blob = md.GetBlobReader(ca.Value);
            if (blob.ReadUInt16() != 0x0001)
                throw new NotSupportedException("custom attribute blob must start with prolog 0x0001 in M2");
            var fixedArgs = new List<object?>(paramTypes.Count);
            foreach (var p in paramTypes)
                fixedArgs.Add(ReadFixedArgValue(md, ref blob, p));
            var namedArgs = ReadNamedArgs(md, ref blob);
            // The ctor token: the emitter resolves the attribute class's
            // generated ctor C++ name from it (MethodRefResolver.Resolve).
            result.Add(new CustomAttributeDef(attrType, ctorName, MetadataTokens.GetToken(ca.Constructor), fixedArgs, namedArgs));
        }
        return result;
    }

    // The ctor signature (calling convention 0x20 + param count + void return +
    // one type per parameter) decoded into the ARG SHAPES the blob needs. For
    // SZARRAY params the element type byte is captured (fixed-arg array
    // elements are NOT individually tagged -- the shape comes from here).
    private static List<CtorParam> DecodeCtorParamTypes(ref BlobReader sig)
    {
        sig.ReadByte();                      // calling convention (0x20 HAS_THIS)
        var count = sig.ReadCompressedInteger();
        var ret = sig.ReadByte();            // return type (void for ctors)
        ConsumeTypeTrailing(ref sig, ret);
        var result = new List<CtorParam>(count);
        for (var i = 0; i < count; i++)
        {
            var b = sig.ReadByte();
            byte? elem = null;
            if (b == 0x1D)
                elem = sig.ReadByte();       // SZARRAY element type byte
            ConsumeTypeTrailing(ref sig, b);
            result.Add(new CtorParam(b, elem));
        }
        return result;
    }

    // Consume a type's trailing payload (TypeDefOrRef / generic index /
    // GenericInst args / array bounds / fnptr signature) so the next param
    // byte stays aligned. Primitive / string / object / typedref / byref carry
    // no trailing bytes.
    private static void ConsumeTypeTrailing(ref BlobReader sig, byte b)
    {
        switch (b)
        {
            case 0x11: // VALUETYPE: TypeDefOrRef follows
            case 0x12: // CLASS
            case 0x40: // ELEMENT_TYPE type-handle (nested)
                _ = sig.ReadTypeHandle();
                break;
            case 0x13: // VAR / MVAR: compressed parameter index follows
            case 0x1E:
                _ = sig.ReadCompressedInteger();
                break;
            case 0x1D: // SZARRAY: element type follows (recursively consumed)
                ConsumeTypeTrailing(ref sig, sig.ReadByte());
                break;
            case 0x14: // ARRAY: element + rank + sizes/lobounds lists
                ConsumeTypeTrailing(ref sig, sig.ReadByte());
                _ = sig.ReadCompressedInteger();             // rank
                var numSizes = sig.ReadCompressedInteger();
                for (var i = 0; i < numSizes; i++) _ = sig.ReadCompressedInteger();
                var numLo = sig.ReadCompressedInteger();
                for (var i = 0; i < numLo; i++) _ = sig.ReadCompressedInteger();
                break;
            case 0x15: // GENERICINST: marker + TypeDefOrRef + arg count + args
                sig.ReadByte();
                _ = sig.ReadTypeHandle();
                var argCount = sig.ReadCompressedInteger();
                for (var i = 0; i < argCount; i++)
                    ConsumeTypeTrailing(ref sig, sig.ReadByte());
                break;
            case 0x1B: // FNPTR: embedded method signature
                sig.ReadByte();
                var fnCount = sig.ReadCompressedInteger();
                ConsumeTypeTrailing(ref sig, sig.ReadByte());  // return
                for (var i = 0; i < fnCount; i++)
                    ConsumeTypeTrailing(ref sig, sig.ReadByte());
                break;
            // No trailing payload for the remaining element types.
        }
    }

    // One decoded ctor parameter: its leading ELEMENT_TYPE byte + (for SZARRAY)
    // the element type byte the fixed-arg array value needs.
    private readonly record struct CtorParam(byte TypeByte, byte? ArrayElementType);

    private static object? ReadFixedArgValue(MetadataReader md, ref BlobReader blob, CtorParam p)
    {
        switch (p.TypeByte)
        {
            case 0x02: return blob.ReadBoolean();
            case 0x03: return (char)blob.ReadUInt16();
            case 0x04: return (sbyte)blob.ReadSByte();
            case 0x05: return blob.ReadByte();
            case 0x06: return (short)blob.ReadInt16();
            case 0x07: return (ushort)blob.ReadUInt16();
            case 0x08: return blob.ReadInt32();
            case 0x09: return blob.ReadUInt32();
            case 0x0A: return blob.ReadInt64();
            case 0x0B: return blob.ReadUInt64();
            case 0x0C: return blob.ReadSingle();
            case 0x0D: return blob.ReadDouble();
            case 0x0E: return blob.ReadSerializedString();
            case 0x12: return blob.ReadSerializedString(); // System.Type arg (assembly-qualified name)
            case 0x11: return blob.ReadInt32();            // enum arg: underlying int32
            case 0x1C: return ReadTaggedValue(md, ref blob); // object arg: tagged
            case 0x1D:
                return p.ArrayElementType is byte elem
                    ? ReadFixedArrayValue(md, ref blob, elem)
                    : throw new NotSupportedException("SZARRAY custom-attribute arg without an element type in M2");
            default:
                throw new NotSupportedException($"custom-attribute fixed arg type byte 0x{p.TypeByte:X2} not supported in M2");
        }
    }

    // SerArray (II.23.3): int32 count (0xFFFFFFFF = null) + one value per
    // element, each encoded per the element type (untyped -- the shape comes
    // from the ctor signature).
    private static object? ReadFixedArrayValue(MetadataReader md, ref BlobReader blob, byte elemByte)
    {
        var count = blob.ReadInt32();
        if (count == unchecked((int)0xFFFFFFFF)) return null; // null array
        if (count < 0) throw new NotSupportedException($"negative custom-attribute array length {count} in M2");
        var elems = new object?[count];
        for (var i = 0; i < count; i++)
            elems[i] = ReadFixedArrayElem(md, ref blob, elemByte);
        return elems;
    }

    private static object? ReadFixedArrayElem(MetadataReader md, ref BlobReader blob, byte elemByte) => elemByte switch
    {
        0x02 => blob.ReadBoolean(),
        0x03 => (char)blob.ReadUInt16(),
        0x04 => (sbyte)blob.ReadSByte(),
        0x05 => blob.ReadByte(),
        0x06 => (short)blob.ReadInt16(),
        0x07 => (ushort)blob.ReadUInt16(),
        0x08 => blob.ReadInt32(),
        0x09 => blob.ReadUInt32(),
        0x0A => blob.ReadInt64(),
        0x0B => blob.ReadUInt64(),
        0x0C => blob.ReadSingle(),
        0x0D => blob.ReadDouble(),
        0x0E => blob.ReadSerializedString(),
        0x12 => blob.ReadSerializedString(),
        0x11 => blob.ReadInt32(),
        0x1C => ReadTaggedValue(md, ref blob),
        _ => throw new NotSupportedException($"custom-attribute array element byte 0x{elemByte:X2} not supported in M2"),
    };

    // Tagged value (System.Object fixed args and 0x51 boxed named args): the
    // runtime type is carried INLINE as an element-type tag before the value.
    private static object? ReadTaggedValue(MetadataReader md, ref BlobReader blob)
    {
        var tag = blob.ReadByte();
        return tag switch
        {
            0x02 => blob.ReadBoolean(),
            0x03 => (char)blob.ReadUInt16(),
            0x04 => (sbyte)blob.ReadSByte(),
            0x05 => blob.ReadByte(),
            0x06 => (short)blob.ReadInt16(),
            0x07 => (ushort)blob.ReadUInt16(),
            0x08 => blob.ReadInt32(),
            0x09 => blob.ReadUInt32(),
            0x0A => blob.ReadInt64(),
            0x0B => blob.ReadUInt64(),
            0x0C => blob.ReadSingle(),
            0x0D => blob.ReadDouble(),
            0x0E => blob.ReadSerializedString(),
            0x50 => blob.ReadSerializedString(),   // System.Type
            0x51 => ReadTaggedValue(md, ref blob), // boxed object (recursive)
            var other => throw new NotSupportedException($"custom-attribute object tag 0x{other:X2} not supported in M2"),
        };
    }

    private static List<CustomAttributeNamedArg> ReadNamedArgs(MetadataReader md, ref BlobReader blob)
    {
        var result = new List<CustomAttributeNamedArg>();
        var count = blob.ReadCompressedInteger();
        for (var i = 0; i < count; i++)
        {
            var isField = blob.ReadByte() == 0x53; // 0x53 Field / 0x54 Property
            var tag = blob.ReadByte();
            var name = blob.ReadSerializedString();
            result.Add(new CustomAttributeNamedArg(isField, name ?? "", ReadNamedValue(md, ref blob, tag)));
        }
        return result;
    }

    private static object? ReadNamedValue(MetadataReader md, ref BlobReader blob, byte tag) => tag switch
    {
        0x02 => blob.ReadBoolean(),
        0x03 => (char)blob.ReadUInt16(),
        0x04 => (sbyte)blob.ReadSByte(),
        0x05 => blob.ReadByte(),
        0x06 => (short)blob.ReadInt16(),
        0x07 => (ushort)blob.ReadUInt16(),
        0x08 => blob.ReadInt32(),
        0x09 => blob.ReadUInt32(),
        0x0A => blob.ReadInt64(),
        0x0B => blob.ReadUInt64(),
        0x0C => blob.ReadSingle(),
        0x0D => blob.ReadDouble(),
        0x0E => blob.ReadSerializedString(),
        0x50 => blob.ReadSerializedString(),      // System.Type
        0x51 => ReadTaggedValue(md, ref blob),    // boxed object
        0x55 => ReadNamedEnum(md, ref blob),      // ELEMENT_TYPE_ENUM: name + underlying
        0x1D => ReadNamedArray(md, ref blob),
        var other => throw new NotSupportedException($"custom-attribute named value tag 0x{other:X2} not supported in M2"),
    };

    private static CustomAttributeEnumValue ReadNamedEnum(MetadataReader md, ref BlobReader blob)
    {
        var enumName = blob.ReadSerializedString();
        return new CustomAttributeEnumValue(enumName ?? "", blob.ReadInt32());
    }

    // Named-arg array: int32 count (0xFFFFFFFF = null) + one TYPE-TAGGED value
    // per element (unlike fixed-arg arrays, each element carries its own tag).
    private static object? ReadNamedArray(MetadataReader md, ref BlobReader blob)
    {
        var count = blob.ReadInt32();
        if (count == unchecked((int)0xFFFFFFFF)) return null; // null array
        if (count < 0) throw new NotSupportedException($"negative custom-attribute array length {count} in M2");
        var elems = new object?[count];
        for (var i = 0; i < count; i++)
        {
            var elemTag = blob.ReadByte();
            elems[i] = ReadNamedValue(md, ref blob, elemTag);
        }
        return elems;
    }

    //--------------------------------------------------------------------------
    // DecodeBaseType -- full ECMA name of the base type ("System.Object" for
    // the root of the hierarchy). Used for the descriptor's baseClass slot.
    //--------------------------------------------------------------------------
    private static string DecodeBaseType(MetadataReader md, EntityHandle baseType)
    {
        if (baseType.IsNil) return "System.Object";
        // The extends of a TypeDef is a TypeDefOrRef, so a TypeSpec base
        // (generic instantiation) is legal CLI -- decoded via ResolveTypeName.
        return ResolveTypeName(md, baseType);
    }

    //--------------------------------------------------------------------------
    // DecodeFieldSignature -- classify one field (instance or static) from its
    // signature blob. FieldSignature is 0x06 followed by the field type:
    //   0x08 ELEMENT_TYPE_I4            -> Int32
    //   0x0E ELEMENT_TYPE_STRING        -> Reference (System.String)
    //   0x12 ELEMENT_TYPE_CLASS         -> Reference (followed by TypeDefOrRef)
    //   0x1C ELEMENT_TYPE_OBJECT        -> Reference (System.Object)
    //   0x11 ELEMENT_TYPE_VALUETYPE     -> M3 (no value-type fields in M2)
    //   0x15 ELEMENT_TYPE_GENERICINST   -> M5 (Reference; Func`N/delegate fields)
    //   0x1D/0x14 ELEMENT_TYPE_SZARRAY/ARRAY -> M5 (Reference; System.Array)
    // Everything else throws so unsupported input fails loudly at load time.
    //
    // `extended` (static fields): accepts the FULL ECMA field-type set -- value
    // types (0x11, named -- System.Decimal drives the 16-byte storage path) and
    // the primitive widths/floats. Static fields were loaded type-less before
    // (the decimal sf_Euler gap) and their emission tolerated any signature, so
    // EXOTIC static shapes (pointers 0x0F, byrefs 0x10, fnptrs 0x1B, typedrefs
    // 0x16, modifiers 0x1F/0x20) keep the historical tolerant Int32 value slot
    // instead of newly failing the load. Instance fields (extended == false)
    // keep the M2 fail-loudly contract exactly.
    //--------------------------------------------------------------------------
    private static (FieldTypeKind Kind, string TypeName) DecodeFieldSignature(MetadataReader md, BlobHandle sigHandle, bool extended)
    {
        var sig = md.GetBlobReader(sigHandle);
        var header = sig.ReadByte();
        if (header != 0x06)
            throw new NotSupportedException($"field signature must start with FIELD(0x06), got 0x{header:X2}");
        // ECMA-335 II.23.1.16: volatile fields carry CMOD_REQD(IsVolatile)
        // (e.g. `static volatile int` = 06 1F 09 08) -- strip custom
        // modifiers and classify the REAL field type.
        var typeByte = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        switch (typeByte)
        {
            case 0x0E:
                return (FieldTypeKind.Reference, "System.String");
            case 0x1C:
                return (FieldTypeKind.Reference, "System.Object");
            case 0x12:
                return (FieldTypeKind.Reference, ResolveTypeName(md, sig.ReadTypeHandle()));
            // M3 generics: 0x13 ELEMENT_TYPE_VAR (generic parameter T) -- the
            // default instantiation maps T to int32_t (value-type
            // specialization, iron rule 2g). The VAR byte is followed by the
            // parameter index (consumed to keep the reader aligned).
            case 0x13:
                sig.ReadCompressedInteger();
                return (FieldTypeKind.Int32, "");
            case 0x08:
                return extended
                    ? (FieldTypeKind.Int32, "System.Int32")
                    : (FieldTypeKind.Int32, "");
            // GN-18 B1 (fnptr fields): a `delegate*` (0x1B ELEMENT_TYPE_FNPTR)
            // INSTANCE field rides the full-width VOID* pointer carrier. The
            // fnptr value in the model IS void* (FuncPtrExpr -> void*, the "p"
            // slot); emitting the member intptr_t would make the stfld store
            // (void* value) and the ldfld load (into a void* local) C++ type
            // errors. Reuse FieldTypeKind.NativeInt as the KIND (non-reference
            // value slot: the class refFieldBitmap only marks Reference
            // fields, so the GC never scans it) but distinguish from a real
            // IntPtr (0x18/0x19) field by the EMPTY TypeName -- IntPtr fields
            // carry "System.IntPtr", a fnptr has no managed full name.
            case 0x1B:
                return (FieldTypeKind.NativeInt, "");
            case 0x11:
                if (!extended)
                    throw new NotSupportedException("value-type instance fields not supported in M2");
                return (FieldTypeKind.Int32, ResolveTypeName(md, sig.ReadTypeHandle()));
            // M5 BCL wiring: generic instantiations (delegate Func`N fields) and
            // arrays are managed references (GC-pointer storage slots).
            case 0x15:
                return (FieldTypeKind.Reference, GenericInstantiationName(md, ref sig));
            case 0x1D:
            case 0x14:
            case 0x1F:
                return (FieldTypeKind.Reference, "System.Array");
            // wf8 (winforms1 stage-7): IntPtr/UIntPtr (0x18/0x19 ELEMENT_TYPE_I/U)
            // CLASS INSTANCE fields (WfPropControl.Handle). The struct/nested
            // path (TypeEmitter.DecodeNestedFieldSignature) already emits these
            // as the full-width intptr_t carrier (TypeEmitter.EmitClassStruct
            // FieldTypeKind.NativeInt); the M2 class-instance path was the only
            // decoder left rejecting them. Same NativeInt kind -> same intptr_t
            // member emission. Static IntPtr fields keep the historical int32
            // value slot (DecodeExtendedStaticType, below).
            case 0x18:
            case 0x19:
                return extended
                    ? DecodeExtendedStaticType(typeByte)
                    : (FieldTypeKind.NativeInt, "System.IntPtr");
            // Primitive value-type fields not covered above: treat as Int32.
            // Static (extended) fields keep the System name for the
            // reflection/static-classifier surface.
            case 0x02: case 0x03: case 0x04: case 0x05:
            case 0x06: case 0x07: case 0x09: case 0x0A:
            case 0x0B: case 0x0C: case 0x0F:
                if (extended) return DecodeExtendedStaticType(typeByte);
                return (FieldTypeKind.Int32, "");
            default:
                if (!extended)
                    return (FieldTypeKind.Int32, "");
                return DecodeExtendedStaticType(typeByte);
        }
    }

    // Primitive value-type field shapes for STATIC fields: the storage slot
    // stays the historical int32_t (value semantics; widths beyond 32 bits are
    // an M3 layout concern), the System full name rides the model so the
    // reflection field table and the static-field classifier see the real type.
    private static (FieldTypeKind Kind, string TypeName) DecodeExtendedStaticType(byte typeByte) => typeByte switch
    {
        0x02 => (FieldTypeKind.Int32, "System.Boolean"),
        0x03 => (FieldTypeKind.Int32, "System.Char"),
        0x04 => (FieldTypeKind.Int32, "System.SByte"),
        0x05 => (FieldTypeKind.Int32, "System.Byte"),
        0x06 => (FieldTypeKind.Int32, "System.Int16"),
        0x07 => (FieldTypeKind.Int32, "System.UInt16"),
        0x09 => (FieldTypeKind.Int32, "System.UInt32"),
        0x0A => (FieldTypeKind.Int32, "System.Int64"),
        0x0B => (FieldTypeKind.Int32, "System.UInt64"),
        0x0C => (FieldTypeKind.Int32, "System.Single"),
        0x0D => (FieldTypeKind.Int32, "System.Double"),
        0x18 => (FieldTypeKind.Int32, "System.IntPtr"),
        0x19 => (FieldTypeKind.Int32, "System.UIntPtr"),
        // GN-18 B1 (fnptr fields): a STATIC `delegate*` (0x1B) field needs the
        // full-width void* pointer carrier -- the old int32_t storage truncated
        // the x64 address. FieldTypeKind.NativeInt (reuse, no new kind) plus
        // the EMPTY TypeName marks a fnptr field: StaticFieldSemantics routes
        // NativeInt + "" to the FnPtr semantic (StorageCppType void*), the
        // same classification the 0x18 arm uses for real IntPtr (which keeps
        // "System.IntPtr").
        0x1B => (FieldTypeKind.NativeInt, ""),
        // Tolerant fallback (documented in DecodeFieldSignature): exotic static
        // signatures keep the historical untyped int32_t value slot.
        var b => (FieldTypeKind.Int32, ""),
    };

    // TypeDefOrRef full-name resolution covering ALL three handle kinds:
    // TypeDef / TypeRef (md.GetFullName) and TypeSpec (the specification's
    // signature blob decoded into a canonical name -- generic instantiations
    // "List`1<int>", arrays, nested CLASS/VALUETYPE forms). TypeSpec is what a
    // field typed as a generic instantiation (or a base/interface declared as
    // one) carries.
    private static string ResolveTypeName(MetadataReader md, EntityHandle handle) =>
        handle.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)handle),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)handle),
            HandleKind.TypeSpecification => ResolveTypeSpec(md, (TypeSpecificationHandle)handle),
            _ => throw new NotSupportedException($"type handle kind {handle.Kind} not supported in M2"),
        };

    //--------------------------------------------------------------------------
    // ResolveTypeSpec -- canonical full name of a TypeSpec row from its
    // signature blob (ECMA-335 II.23.2.14). The dominant form is the generic
    // instantiation (0x15 GENERICINST); the CLASS/VALUETYPE forms (nested type
    // access) and the array forms decode to the expected "[]" names. VAR/MVAR
    // name the generic parameter ("T"). Everything else is beyond M2's surface.
    //--------------------------------------------------------------------------
    private static string ResolveTypeSpec(MetadataReader md, TypeSpecificationHandle handle)
    {
        var spec = md.GetTypeSpecification(handle);
        var sig = md.GetBlobReader(spec.Signature);
        var header = sig.ReadByte();
        switch (header)
        {
            case 0x15:
                return GenericInstantiationName(md, ref sig);
            case 0x1D:
                return ReadTypeName(md, ref sig) + "[]";
            case 0x14:
                return MultidimArrayName(md, ref sig);
            case 0x12:
            case 0x11:
                return ResolveTypeName(md, sig.ReadTypeHandle());
            case 0x13:
                sig.ReadCompressedInteger();
                return "T";
            case 0x1E:
                sig.ReadCompressedInteger();
                return "!!T";
            default:
                throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not supported in M2");
        }
    }

    // ELEMENT_TYPE_GENERICINST (0x15): the CLASS/VALUETYPE marker byte + the
    // generic DEFINITION's TypeDefOrRef + compressed arg count + one type per
    // argument (recursively decoded). The canonical name is
    // "{definition}<{arg},{arg},...>".
    private static string GenericInstantiationName(MetadataReader md, ref BlobReader sig)
    {
        sig.ReadByte(); // 0x12 CLASS / 0x11 VALUETYPE marker
        var defName = ResolveTypeName(md, sig.ReadTypeHandle());
        var argCount = sig.ReadCompressedInteger();
        var args = new string[argCount];
        for (var i = 0; i < argCount; i++)
            args[i] = ReadTypeName(md, ref sig);
        return defName + "<" + string.Join(",", args) + ">";
    }

    // ELEMENT_TYPE_ARRAY (0x14): element type + rank + sizes/lobounds lists
    // (bounds consumed for alignment; the name carries the rank only).
    private static string MultidimArrayName(MetadataReader md, ref BlobReader sig)
    {
        var elem = ReadTypeName(md, ref sig);
        var rank = sig.ReadCompressedInteger();
        var numSizes = sig.ReadCompressedInteger();
        for (var i = 0; i < numSizes; i++) _ = sig.ReadCompressedInteger();
        var numLoBounds = sig.ReadCompressedInteger();
        for (var i = 0; i < numLoBounds; i++) _ = sig.ReadCompressedInteger();
        return elem + "[" + rank + "]";
    }

    // Recursive type-name decode for TypeSpec arguments and GenericInst args:
    // the full ECMA element-type set a field/base/interface type can carry.
    private static string ReadTypeName(MetadataReader md, ref BlobReader sig)
    {
        var b = sig.ReadByte();
        return b switch
        {
            0x02 => "System.Boolean",
            0x03 => "System.Char",
            0x04 => "System.SByte",
            0x05 => "System.Byte",
            0x06 => "System.Int16",
            0x07 => "System.UInt16",
            0x08 => "System.Int32",
            0x09 => "System.UInt32",
            0x0A => "System.Int64",
            0x0B => "System.UInt64",
            0x0C => "System.Single",
            0x0D => "System.Double",
            0x0E => "System.String",
            0x1C => "System.Object",
            0x18 => "System.IntPtr",
            0x19 => "System.UIntPtr",
            0x11 => ResolveTypeName(md, sig.ReadTypeHandle()),
            0x12 => ResolveTypeName(md, sig.ReadTypeHandle()),
            0x13 => ConsumeGenericIndex("T", ref sig),
            0x1E => ConsumeGenericIndex("!!T", ref sig),
            0x1D => ReadTypeName(md, ref sig) + "[]",
            0x15 => GenericInstantiationName(md, ref sig),
            0x14 => MultidimArrayName(md, ref sig),
            var other => throw new NotSupportedException($"type byte 0x{other:X2} not supported in M2"),
        };
    }

    private static string ConsumeGenericIndex(string name, ref BlobReader sig)
    {
        sig.ReadCompressedInteger();
        return name;
    }

    //--------------------------------------------------------------------------
    // LoadPortablePdb -- SYMLN-1: read the assembly's associated Portable PDB
    // and build an IL-offset -> (line, document) map per method token.
    //
    // Mechanism (System.Reflection.Metadata, .NET 8 shared framework):
    //   PEReader.TryOpenAssociatedPortablePdb locates the .pdb file (same
    //   directory, same base name -- derived from the PE's CodeView debug
    //   directory entry), verifies it is a Portable PDB and that it matches the
    //   image (CodeView id), then hands back a MetadataReaderProvider over the
    //   PDB. The Portable PDB's MethodDebugInformation table holds the sequence
    //   points (IL offset -> C# line/column + document index); the Document
    //   table holds the source file paths.
    //
    // Fallback contract: native (Windows) PDB / missing PDB / corrupt or
    // mismatched PDB -> null (the generator then emits NO #line directives --
    // hand-written .il input without a PDB keeps its current output).
    //--------------------------------------------------------------------------
    public static SequencePointMap? LoadPortablePdb(string assemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            if (!pe.TryOpenAssociatedPortablePdb(assemblyPath, imgPath => File.OpenRead(imgPath), out var provider, out _))
                return null; // native PDB / missing / non-portable: no sequence points
            if (provider == null) return null; // true return always carries a provider, but guard the null contract
            using (provider)
                return SequencePointMap.Build(provider.GetMetadataReader());
        }
        catch
        {
            // A corrupt or mismatched PDB: no usable sequence points -- callers
            // fall back to no #line emission.
            return null;
        }
    }

    private static string? TryReadConstant(MetadataReader md, FieldDefinition fd)
    {
        var constant = fd.GetDefaultValue();
        if (constant.IsNil) return null;
        var c = md.GetConstant(constant);
        if (c.Value.IsNil) return null;
        var value = md.GetBlobReader(c.Value).ReadConstant(c.TypeCode);
        // InvariantCulture: numeric constant formatting is consistent across
        // processes/machines/regions (de-DE would emit "1,5" instead of "1.5"),
        // keeping the transpiled output deterministic (spec: same input, same output).
        return value == null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}

//------------------------------------------------------------------------------
// SequencePointMap -- SYMLN-1: IL-offset -> C# (line, document) maps.
//
// One entry = one Portable PDB sequence point (a contiguous IL range starting
// at ILOffset that maps to a C# line in DocumentPath). The generator resolves
// a statement's IR SourceOffset (the IL instruction offset it was folded from)
// via Lookup: the largest sequence-point offset <= the statement offset, i.e.
// the C# line whose source range the instruction falls in. Entries are sorted
// by ILOffset so Lookup is a binary search (O(log n) per statement).
//------------------------------------------------------------------------------
public sealed class SequencePointMap
{
    private readonly Dictionary<int, SequencePointEntry[]> _byToken;

    private SequencePointMap(Dictionary<int, SequencePointEntry[]> byToken) => _byToken = byToken;

    // Build the map from a Portable PDB metadata reader. Document rows are
    // enumerated in table order -- a sequence point's Document handle row
    // number (1-based) - 1 is the index into that enumeration (ECMA-335
    // II.24.2.4: Document rows are addressable by row id).
    public static SequencePointMap Build(MetadataReader pdbReader)
    {
        var documents = new List<string>();
        foreach (var dh in pdbReader.Documents)
            documents.Add(pdbReader.GetString(pdbReader.GetDocument(dh).Name));

        var byToken = new Dictionary<int, SequencePointEntry[]>();
        foreach (var mih in pdbReader.MethodDebugInformation)
        {
            var mi = pdbReader.GetMethodDebugInformation(mih);
            if (mi.SequencePointsBlob.IsNil) continue;
            // Portable PDB: the MethodDebugInformation row id == the row id of
            // the MethodDef it describes (ECMA-335 IV.4.10.2) -- the map must
            // be keyed by the ASSEMBLY's MethodDef token (0x06, the token
            // MethodBodyReader reports), so rebuild the MethodDef handle from
            // the debug-information row number.
            var methodToken = MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(MetadataTokens.GetRowNumber(mih)));
            var entries = new List<SequencePointEntry>();
            foreach (var sp in mi.GetSequencePoints())
            {
                if (sp.IsHidden) continue; // compiler-generated (0xfeefee): no C# line to map
                var docIndex = MetadataTokens.GetRowNumber(sp.Document) - 1;
                var doc = docIndex >= 0 && docIndex < documents.Count ? documents[docIndex] : "";
                entries.Add(new SequencePointEntry(sp.Offset, sp.StartLine, doc));
            }
            if (entries.Count == 0) continue;
            byToken[methodToken] = entries.OrderBy(e => e.ILOffset).ToArray();
        }
        return new SequencePointMap(byToken);
    }

    // Largest sequence-point ILOffset <= ilOffset (see the type comment). The
    // IR SourceOffset is the offset of the IL instruction that produced the
    // statement, which lands inside the sequence point's IL range -- exactly
    // the C# line the debugger would report for that instruction.
    public SequencePointEntry? Lookup(int methodToken, int ilOffset)
    {
        if (!_byToken.TryGetValue(methodToken, out var entries)) return null;
        if (ilOffset < 0 || entries.Length == 0) return null;
        var lo = 0;
        var hi = entries.Length - 1;
        var best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (entries[mid].ILOffset <= ilOffset)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best < 0 ? null : entries[best];
    }

    // Diagnostic surface (tests + the CLI symbols line): number of methods
    // that carry at least one non-hidden sequence point.
    public int MethodCount => _byToken.Count;

    public bool Contains(int methodToken) => _byToken.ContainsKey(methodToken);
}

// One non-hidden sequence point: the C# line that the IL range starting at
// ILOffset maps to, and the source document (absolute path from the PDB).
public readonly record struct SequencePointEntry(int ILOffset, int Line, string DocumentPath);

//------------------------------------------------------------------------------
// ManifestResourceDef -- RES-1 (iron rule 18): one embedded manifest resource
// as loaded from the ManifestResource table: the logical name (the key
// Assembly.GetManifestResourceStream is called with) + the raw payload bytes
// (the length-prefix-stripped blob -- what GetManifestResourceStream returns).
//------------------------------------------------------------------------------
public sealed class ManifestResourceDef
{
    public ManifestResourceDef(string name, byte[] data)
    {
        Name = name;
        Data = data;
    }

    public string Name { get; }
    public byte[] Data { get; }
    public int Length => Data.Length;
}

//------------------------------------------------------------------------------
// CustomAttributeDef -- one CustomAttribute row of a type (ATTRIBUTE domain):
// the attribute class full name, the ctor method name, and the DECODED blob --
// fixed ctor args (in ctor-parameter order) + named field/property args. The
// register-side contract (MethodMap.Attribute.cs): the transpiler builds
// attribute INSTANCES from these and registers them (il2cpp_attribute_register)
// for the runtime GetCustomAttribute(s) lookup. Values are boxed .NET
// constants; enum values decode to CustomAttributeEnumValue.
//------------------------------------------------------------------------------
    public sealed class CustomAttributeDef
    {
        public CustomAttributeDef(
            string attributeType,
            string ctorName,
            int constructorToken,
            IReadOnlyList<object?> fixedArgs,
            IReadOnlyList<CustomAttributeNamedArg> namedArgs)
        {
            AttributeType = attributeType;
            CtorName = ctorName;
            ConstructorToken = constructorToken;
            FixedArgs = fixedArgs;
            NamedArgs = namedArgs;
        }

        public string AttributeType { get; }
        public string CtorName { get; }
        public int ConstructorToken { get; }
        public IReadOnlyList<object?> FixedArgs { get; }
        public IReadOnlyList<CustomAttributeNamedArg> NamedArgs { get; }
    }

// One named (field or property) argument of a custom attribute.
public readonly record struct CustomAttributeNamedArg(bool IsField, string Name, object? Value);

// An enum-typed custom-attribute value: the enum's full name + the decoded
// underlying integer (the blob serializes the underlying type).
public readonly record struct CustomAttributeEnumValue(string TypeName, int Underlying);
