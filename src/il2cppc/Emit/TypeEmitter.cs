using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// TypeEmitter -- C++ emission for translated types.
//
// M2 class emission model:
//
//   Layout (LayoutInstanceFields)
//     REFERENCE type: the instance is a flat grid of 8-byte pointer slots. Slot 0
//     is the object header (Il2CppObject._klass, the type descriptor pointer).
//     Each instance field occupies exactly one slot in metadata declaration
//     order (slots 1..N). An Int32 field uses only the low 4 bytes of its slot
//     (the high 4 stay zero from allocation zero-init); a Reference field uses
//     the whole slot. This is deliberately simpler than the CLR's alignment
//     rules (M3) and keeps the reference-field bitmap exact: bit i is set
//     exactly when slot i is a GC reference, so the collector scans slot-wise
//     with no ambiguity. InstanceSize = (slotCount) * pointerSize, always >=
//     sizeof(Il2CppObject) (pointerSize = the TARGET pointer width -- 8 on
//     x64/arm64, 4 on x86/arm32; see G1).
//
//     VALUE type (LayoutValueTypeFields, GN-20.4): the net48 [StructLayout(
//     Sequential)] COMPACT layout -- InstanceSize does NOT include the object
//     header and equals sizeof(T). Each field is placed at its own natural
//     alignment (Int32/Float 4, Int64/UInt64/Double 8, NativeInt/Reference
//     pointerSize), and a Reference field necessarily lands on a whole slot so
//     the collector's slot scan still finds every reference (SlotIndex is
//     derived as ByteOffset / pointerSize, so reference types keep the exact
//     slot grid above -- zero behaviour change for them).
//
//   Struct emission (EmitClassStruct)
//     struct {Ns}_{Type} {           // C++ name via TypeCppName
//         Il2CppObject _obj;         // embedded object header, offset 0
//         int32_t     field;         // value fields: int32_t at slot n*pointerSize
//         {Ns}_{T}*   refField;      // reference fields: typed pointer
//     };
//     The header is the FIRST member so an Il2CppObject* aliases the struct
//     (the runtime casts freely between the two). Reference fields are typed
//     pointers to the concrete generated struct where the target type is a
//     user type, and Il2CppObject* for System.Object / System.String.
//
//   Descriptor emission (EmitClassDescriptor)
//     static Il2CppClass {Type}_class = {
//         "Calc.Point",          // name
//         sizeof({Type}),        // instanceSize
//         bitmap,                // refFieldBitmap (slot grid)
//         refFieldCount,         // number of reference fields
//         baseClassDescriptor,   // nullptr for System.Object in M2
//     };
//     Registered with the runtime by generated startup code (entry.cpp) so
//     the type registry and the GC root table see the type.
//------------------------------------------------------------------------------
public static class TypeEmitter
{
    //--------------------------------------------------------------------------
    // ExpandNestedTypes -- nested types -> independent top-level emission entries.
    //
    // The pipeline never emits nested types (MetadataLoader skips IsNested
    // TypeDefs), yet every reference side resolves nested-type tokens from
    // metadata with the SHORT name: MetadataExtensions.GetFullName on a
    // TypeDefinitionHandle returns just "Node" for "Cases.Recurse+Node"
    // (nested types carry an EMPTY namespace, and the declaring type is not
    // part of the name). StackFold's newobj/throw capture, the field-cast and
    // callvirt owners and the method-name hash ALL use that short name, so
    // the generated C++ references
    //     il2cpp_object_new(&Node_class)          (CodeGenerator:1107)
    //     ((Node*)il2cpp_null_check(...))->Id     (FieldAccess cast)
    //     Node__ctor_b3740a9592788e34             (MethodRefResolver:193)
    // while no struct/descriptor/ctor definition is ever emitted -- the
    // recurse e2e gap (Cases.Recurse+Node used but undefined).
    //
    // This pass "un-nests" every nested TypeDef: it is loaded from metadata
    // with Name = the short metadata name and Namespace = "" (its metadata
    // namespace IS empty), so TypeDef.FullName == the short name every
    // reference point uses -- TypeCppName/Sanitize then produce IDENTICAL
    // C++ names on the reference side and the definition side. The nested
    // type is appended to the types list as an independent top-level entry
    // (flat struct + descriptor + methods, exactly like a top-level type).
    //
    // Base chains are resolved across the WHOLE list after expansion: a
    // nested type whose base is another nested type (MyException2 : MyException)
    // gets its BaseType pointer set, so CodeGenerator.ExceptionChain's walk
    // (throw_MyException2 -> "MyException2,MyException2,MyException,...") is
    // complete; BCL bases (System.Exception) stay null, matching the
    // top-level convention (descriptor baseClass nullptr, the exception
    // chain adds the terminal BCL name from BaseTypeName).
    //
    // Caller contract: run AFTER MetadataLoader.LoadAssembly and BEFORE
    // CodeGenerator.Generate (VTablePlanner.PlanAll needs the complete type
    // graph). Idempotent: re-running after expansion hits the collision
    // guard (the nested type is already in the list).
    //
    // NOTE: field/method/base decode mirrors MetadataLoader's private logic
    // so the expanded TypeDefs are model-identical to what LoadAssembly
    // would have produced (MetadataLoader.cs is outside this file's edit
    // domain; the duplication is deliberate).
    //--------------------------------------------------------------------------
    public static void ExpandNestedTypes(MetadataReader md, List<TypeDef> types)
    {
        var added = new List<TypeDef>();
        foreach (var tdHandle in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(tdHandle);
            if (!td.IsNested) continue;
            // Nested types must model as their SHORT metadata name (empty
            // namespace): that is exactly the full name every reference point
            // produces (MetadataExtensions.GetFullName on a TypeDefinition
            // handle). A non-empty namespace would break the mapping silently
            // (undefined C++ symbols) -- reject loudly instead.
            var name = md.GetString(td.Name);
            var ns = md.GetString(td.Namespace);
            if (!string.IsNullOrEmpty(ns))
                throw new NotSupportedException(
                    $"nested type '{name}' has namespace '{ns}' -- nested types must emit under their short metadata name to match reference-point naming");
            // r78 fix: use DeclaringTypeName+Name as the unique key so that
            // multiple nested types with the same short name (e.g., 25 different
            // enclosing types each have a <>c closure class) are NOT collapsed
            // into one entry. DeclaringTypeName is "" for top-level types and
            // the declaring chain full name ("System.Foo+Bar") for nested types.
            var declName = td.GetDeclaringType().IsNil
                ? ""
                : DeclaringChainFullName(md, td.GetDeclaringType());
            var uniqueKey = declName + "\0" + name; // '\0' separator is safe -- not a valid metadata char
            if (types.Concat(added).Any(t => (t.DeclaringTypeName + "\0" + t.Name) == uniqueKey))
            {
                continue; // skip duplicate nested type (same enclosing type + same short name)
            }

            var nested = new TypeDef
            {
                Name = name,
                Namespace = "",
                // refl2 gt_nested (m1): record the nesting identity so the
                // TypeInfo table's registry KEY can be the net48 '+' full name
                // ("Cases.Outer+Inner" -- NormalizeTypeName rule 3 keeps '+'),
                // not the short name ("Inner") every C++ symbol uses. The
                // registry lookup compares info->name against the normalized
                // query, so a table registered under "Inner" never answers
                // Type.GetType("Cases.Outer+Inner").
                IsNested = true,
                DeclaringTypeName = td.GetDeclaringType().IsNil
                    ? ""
                    : DeclaringChainFullName(md, td.GetDeclaringType()),
                // refl6b (item A): carry the nested type's ECMA visibility mask
                // (TypeAttributes & 0x7) into the enclosing typeinfo's
                // nestedVis column (GetNestedType / GetNestedTypes public-only
                // default filter).
                VisibilityMask = (int)td.Attributes & 0x7,
                MetadataToken = 0x02000000 | System.Reflection.Metadata.Ecma335.MetadataTokens.GetRowNumber(tdHandle),
                RawAttributes = (int)td.Attributes,
                BaseTypeName = DecodeNestedBaseType(md, td.BaseType),
                IsInterface = (td.Attributes & TypeAttributes.Interface) != 0,
                // Devirt S1: nested sealed/abstract types parse the same
                // TypeAttributes bits as top-level types (IsEffectivelySealed
                // excludes abstract).
                IsSealed = (td.Attributes & TypeAttributes.Sealed) != 0,
                IsAbstract = (td.Attributes & TypeAttributes.Abstract) != 0,
                BeforeFieldInit = (td.Attributes & TypeAttributes.BeforeFieldInit) != 0,
                IsValueType = DecodeNestedBaseType(md, td.BaseType) == "System.ValueType",
                GenericArity = td.GetGenericParameters().Count,
                // flagsenum: the REAL [Flags] bit, decoded from the nested
                // TypeDef's own CustomAttribute rows (the same authority
                // MetadataLoader.LoadAssembly uses for top-level types). A
                // nested [Flags] enum would otherwise reach the runtime with
                // isFlags=0 and silently print numeric combinations.
                IsFlags = MetadataLoader.HasFlagsAttribute(md, tdHandle),
            };
            foreach (var iih in td.GetInterfaceImplementations())
            {
                var impl = md.GetInterfaceImplementation(iih);
                var ifaceName = impl.Interface.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)impl.Interface),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)impl.Interface),
                    HandleKind.TypeSpecification => ResolveNestedTypeSpec(md, (TypeSpecificationHandle)impl.Interface),
                    _ => throw new NotSupportedException($"interface impl kind {impl.Interface.Kind} not supported in M2"),
                };
                nested.InterfaceNames.Add(ifaceName);
            }
            // P/Invoke fixed-buffer fields ([MarshalAs]): the same decode the
            // top-level loader applies (nested structs must not silently keep
            // the old GC-pointer degrade).
            var nestedCharSize = MetadataLoader.ReadStructCharSizeBytes(md, tdHandle);
            foreach (var fh in td.GetFields())
            {
                var fd = md.GetFieldDefinition(fh);
                var fName = md.GetString(fd.Name);
                var isStatic = (fd.Attributes & FieldAttributes.Static) != 0;
                if (isStatic)
                {
                    nested.StaticFields.Add(new FieldDef
                    {
                        Name = fName,
                        Owner = nested,
                        IsStatic = true,
                        IsLiteral = (fd.Attributes & FieldAttributes.Literal) != 0,
                        IsPublic = ((int)fd.Attributes & 0x7) == 0x6,
                        IsPrivate = ((int)fd.Attributes & 0x7) is 0x0 or 0x1,
                        AccessMask = (int)fd.Attributes & 0x7,
                        ConstantValue = TryReadNestedConstant(md, fd),
                    });
                }
                else
                {
                    var (kind, typeName) = DecodeNestedFieldSignature(md, fd.Signature, nested.FullName, fName);
                    var nullableUnderlying = kind == FieldTypeKind.Nullable
                        ? TypeKindResolver.NullableUnderlyingName(typeName)
                          ?? throw new NotSupportedException(
                              $"nested type '{nested.FullName}' field '{fName}': cannot decode the underlying T of '{typeName}'")
                        : "";
                    var (marshalKind, marshalSizeConst, marshalElem) = MetadataLoader.ReadFieldMarshalAs(md, fh);
                    if (marshalKind == FieldMarshalKind.ByValTStr)
                    {
                        if (!nested.IsValueType || kind != FieldTypeKind.Reference || typeName != "System.String")
                            throw new NotSupportedException(
                                $"nested type '{nested.FullName}' field '{fName}': [MarshalAs(ByValTStr)] requires a System.String field of a value type");
                        if (marshalSizeConst <= 0)
                            throw new NotSupportedException(
                                $"nested type '{nested.FullName}' field '{fName}': [MarshalAs(ByValTStr)] without a positive SizeConst");
                        if (nestedCharSize != 2)
                            throw new NotSupportedException(
                                $"nested type '{nested.FullName}' field '{fName}': [MarshalAs(ByValTStr)] with a 1-byte CharSet is not implemented");
                    }
                    nested.InstanceFields.Add(new FieldDef
                    {
                        Name = fName,
                        Owner = nested,
                        IsStatic = false,
                        IsPublic = ((int)fd.Attributes & 0x7) == 0x6,
                        IsPrivate = ((int)fd.Attributes & 0x7) is 0x0 or 0x1,
                        AccessMask = (int)fd.Attributes & 0x7,
                        TypeKind = kind,
                        FieldTypeName = typeName,
                        MarshalKind = marshalKind,
                        MarshalSizeConst = marshalSizeConst,
                        MarshalCharSize = nestedCharSize,
                        MarshalElementTypeName = marshalElem,
                        NullableUnderlyingName = nullableUnderlying,
                        // The ByValArray element resolver re-decodes the field
                        // signature through this token (nested fields have no
                        // other path back to metadata).
                        MetadataToken = 0x04000000 | MetadataTokens.GetRowNumber(fh),
                    });
                }
            }
            foreach (var mh in td.GetMethods())
            {
                var mdInfo = md.GetMethodDefinition(mh);
                var mName = md.GetString(mdInfo.Name);
                var (paramCount, returnType, paramTypes, returnTypeName) = MethodSignatureCoder.Decode(md, mdInfo.Signature);
                var attrs = mdInfo.Attributes;
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
                nested.Methods.Add(new MethodDef
                {
                    Name = mName,
                    Owner = nested,
                    IsStatic = (attrs & MethodAttributes.Static) != 0,
                    IsPublic = ((int)attrs & 0x7) == 0x6,
                    IsPrivate = ((int)attrs & 0x7) is 0x0 or 0x1,
                    AccessMask = (int)attrs & 0x7,
                    ParamCount = paramCount,
                    ParamTypes = paramTypes,
                    ReturnType = returnType,
                    ReturnTypeName = returnTypeName,
                    IsVirtual = (attrs & MethodAttributes.Virtual) != 0,
                    IsOverride = (attrs & MethodAttributes.ReuseSlot) != 0,
                    IsAbstract = (attrs & MethodAttributes.Abstract) != 0,
                    IsFinal = (attrs & MethodAttributes.Final) != 0,
                    IsPInvoke = pinvokeModule != null,
                    PinvokeModule = pinvokeModule,
                    PinvokeEntryName = pinvokeEntry,
                    PinvokeFlags = pinvokeFlags,
                    PinvokeSetLastError = (pinvokeFlags & 0x40) != 0,
                });
            }
            added.Add(nested);
        }

        if (added.Count == 0) return;

        // Second pass over the FULL list (top-level + nested): resolve the
        // base/interface graph. Re-resolution of already-resolved top-level
        // types is idempotent (same lookup result); InterfaceTypes is cleared
        // first so re-runs never double-add.
        var byName = new Dictionary<string, TypeDef>(StringComparer.Ordinal);
        foreach (var t in types.Concat(added))
        {
            if (!byName.ContainsKey(t.FullName))
                byName[t.FullName] = t;
            // Nested types share short FullNames (Namespace="") AND decode
            // to '+'-qualified BaseTypeNames -- index both so base/interface
            // resolution hits regardless of which form the reference uses.
            if (t.IsNested)
            {
                var nestedKey = t.DeclaringTypeName + "+" + t.Name;
                if (!byName.ContainsKey(nestedKey))
                    byName[nestedKey] = t;
            }
        }
        foreach (var t in types.Concat(added))
        {
            if (t.BaseTypeName != "System.Object" && byName.TryGetValue(t.BaseTypeName, out var baseType))
                t.BaseType = baseType;
            t.InterfaceTypes.Clear();
            foreach (var ifaceName in t.InterfaceNames)
                if (byName.TryGetValue(ifaceName, out var ifaceType))
                    t.InterfaceTypes.Add(ifaceType);
        }
        types.AddRange(added);
    }

    // Nested-type base decode -- mirrors MetadataLoader.DecodeBaseType.
    private static string DecodeNestedBaseType(MetadataReader md, EntityHandle baseType)
    {
        if (baseType.IsNil) return "System.Object";
        return baseType.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)baseType),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)baseType),
            HandleKind.TypeSpecification => ResolveNestedTypeSpec(md, (TypeSpecificationHandle)baseType),
            _ => "System.Object",
        };
    }

    // Full name of a nested type's declaring chain, with the net48 '+'
    // separator for every level: "Cases.Outer" for a top-level declaring
    // type, "Cases.A+B" for a type nested two levels deep. The OUTERMOST
    // level contributes its namespace; each inner level contributes only its
    // short metadata name (nested TypeDefs carry an empty namespace). The
    // typeinfo registry key of a nested type is this + "+" + its own short
    // name ("Cases.Outer+Inner"), which NormalizeTypeName rule 3 preserves
    // verbatim (refl2 gt_nested).
    private static string DeclaringChainFullName(MetadataReader md, TypeDefinitionHandle declaring)
    {
        var parts = new List<string>();
        string ns = "";
        for (var cur = declaring; !cur.IsNil; cur = md.GetTypeDefinition(cur).GetDeclaringType())
        {
            var dtd = md.GetTypeDefinition(cur);
            var curNs = md.GetString(dtd.Namespace);
            if (curNs.Length > 0) ns = curNs; // outermost level carries the namespace
            parts.Add(md.GetString(dtd.Name));
        }
        parts.Reverse(); // outermost -> innermost
        var name = string.Join("+", parts);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    // Nested-type field signature decode -- mirrors
    // MetadataLoader.DecodeFieldSignature (FIELD 0x06 + element type).
    private static (FieldTypeKind Kind, string TypeName) DecodeNestedFieldSignature(MetadataReader md, BlobHandle sigHandle, string ownerName, string fieldName)
    {
        var sig = md.GetBlobReader(sigHandle);
        var header = sig.ReadByte();
        if (header != 0x06)
            throw new NotSupportedException($"field signature must start with FIELD(0x06), got 0x{header:X2}");
        // ECMA-335 II.23.1.16: strip custom modifiers (volatile's
        // CMOD_REQD(IsVolatile) = 0x1F) before classifying the real type.
        var typeByte = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        return typeByte switch
        {
            0x08 => (FieldTypeKind.Int32, "System.Int32"),
            // nb9 F5: NESTED compiler-generated types (closure display
            // classes!) must keep the REAL wide primitive width and type name;
            // the old Int32 fallback truncated captured long/double/float and
            // let the callers read 8 bytes out of a 4-byte member.
            0x0A => (FieldTypeKind.Int64, "System.Int64"),
            0x0B => (FieldTypeKind.UInt64, "System.UInt64"),
            0x0C => (FieldTypeKind.Float, "System.Single"),
            0x0D => (FieldTypeKind.Double, "System.Double"),
            // wfm1 (winforms1): IntPtr/UIntPtr (0x18/0x19) struct fields --
            // full-width pointer carriers (WNDCLASSEX/MSG hwnd/lpfnWndProc).
            // The emitted C++ member is intptr_t (see EmitClassStruct).
            0x18 or 0x19 => (FieldTypeKind.NativeInt, "System.IntPtr"),
            0x0E => (FieldTypeKind.Reference, "System.String"),
            0x1C => (FieldTypeKind.Reference, "System.Object"),
            0x12 => (FieldTypeKind.Reference, ResolveNestedTypeName(md, sig.ReadTypeHandle())),
            // D-GC-1 (newarrdeep): SZARRAY (0x1D) / ARRAY (0x14) INSTANCE field
            // on a NESTED type -- the field type emission path. The field is a
            // GC reference to an array OBJECT (the C++ member is Il2CppObject*,
            // the same uniform shape as System.Object fields); the D-GC-2 fix
            // already generalized the NEWARR ELEMENT classification (the
            // allocation stride / GC scan), so a nested class with a
            // reference-element array field transpiles end to end. Mirrors the
            // top-level STATIC array decode (MetadataLoader.DecodeFieldSignature
            // 0x1D/0x14 -> (Reference, "System.Array")); top-level INSTANCE
            // array fields stay rejected by MetadataLoader (read-only domain).
            0x1D or 0x14 => (FieldTypeKind.Reference, "System.Array"),
            0x13 => (FieldTypeKind.Int32, ""),
            0x15 => DecodeNestedGenericField(md, ref sig),
            0x11 => ValueTypeNestedField(md, sig.ReadTypeHandle(), ownerName, fieldName),
            // GN-18 B1 (fnptr fields): a `delegate*` (0x1B) NESTED-type field
            // rides the same full-width void* pointer carrier as top-level
            // fields (NativeInt kind + empty TypeName marker -- see
            // EmitClassStruct; the refFieldBitmap never scans it).
            0x1B => (FieldTypeKind.NativeInt, ""),
            // Primitive and exotic field types not covered above: treat as Int32.
            var b => (FieldTypeKind.Int32, ""),
        };
    }

    // A GENERICINST (0x15) instance field of a nested (closure display)
    // class. System.Nullable`1<T> is a VALUE type -- it must not ride the
    // reference-slot path: the display class carries the underlying T value
    // slot plus a hasValue(bool) slot (see EmitClassStruct /
    // LayoutInstanceFields). Every other generic instantiation keeps the
    // established uniform reference slot.
    private static (FieldTypeKind Kind, string TypeName) DecodeNestedGenericField(MetadataReader md, ref BlobReader sig)
    {
        var name = GenericNestedInstantiationName(md, ref sig);
        if (TypeKindResolver.IsNullableFullName(name))
            return (FieldTypeKind.Nullable, name);
        return (FieldTypeKind.Reference, name);
    }

    //--------------------------------------------------------------------------
    // A nested (closure display class) INSTANCE field of a VALUE type.
    //
    // Four cases, in this order:
    //   1. a BCL runtime value struct (Guid / DateTime / TimeSpan / Decimal /
    //      DateTimeOffset) -> the REAL runtime value shape. The member is the
    //      runtime struct itself, so the slot holds the whole value; the old
    //      int32 fallback truncated it (a captured DateTime store (->d = t1)
    //      did not compile, and the read silently took 8 bytes out of a
    //      4-byte slot);
    //   2. an ENUM -> the int32 carrier slot (the D5 / com17type doctrine:
    //      the enum rides its underlying int32; the IR models every enum
    //      value as an int64 constant);
    //   3. a USER STRUCT (a TypeDef whose base is System.ValueType) -> the
    //      generated struct VALUE, laid out with its real metadata layout
    //      (natural alignment, ceil(sizeof/pointer) slot span) and its inner
    //      GC references marked in the display class refFieldBitmap at their
    //      ABSOLUTE slots;
    //   4. any OTHER value type (an unknown BCL struct) -> LOUD rejection.
    //      There is no runtime value shape for it on the nested path, and an
    //      int32 member would be a silently mis-typed slot.
    //--------------------------------------------------------------------------
    private static (FieldTypeKind Kind, string TypeName) ValueTypeNestedField(
        MetadataReader md, EntityHandle handle, string ownerName, string fieldName)
    {
        var name = ResolveNestedTypeName(md, handle);
        if (TypeKindResolver.TryGetValueStructFieldKind(name, out var valueKind))
            return (valueKind, name);
        if (IsEnumValueType(md, handle, name))
            return (FieldTypeKind.Int32, name);
        if (IsUserValueType(md, handle))
            return (FieldTypeKind.UserStruct, name);
        throw new NotSupportedException(
            $"nested type '{ownerName}' field '{fieldName}': value-type field of type '{name}' has no runtime value slot -- supported value types are System.Guid / System.DateTime / System.TimeSpan / System.Decimal / System.DateTimeOffset (the runtime value struct), enums (the int32 carrier) and translated user structs; other value types are not supported");
    }

    // A translated USER value type: a TypeDef in this assembly whose base is
    // System.ValueType (and which is not an enum -- IsEnumValueType runs
    // first). Such a type has a real generated C++ struct
    // (EmitClassStruct/LayoutValueTypeFields) and a P25b descriptor.
    private static bool IsUserValueType(MetadataReader md, EntityHandle handle)
    {
        if (handle.Kind != HandleKind.TypeDefinition) return false;
        var td = md.GetTypeDefinition((TypeDefinitionHandle)handle);
        if (td.BaseType.IsNil) return false;
        var baseName = ResolveNestedTypeName(md, td.BaseType);
        return baseName == "System.ValueType";
    }

    // Whether the field's value type is an ENUM -- the int32 carrier. The
    // decision is taken from the METADATA (not from the expanded model), so
    // it does not depend on the expansion order: a USER enum is a TypeDef
    // whose base is System.Enum; a BCL enum is a TypeRef on the whitelist
    // (CodeGenerator.IsKnownBclEnumName).
    private static bool IsEnumValueType(MetadataReader md, EntityHandle handle, string name)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
            {
                var td = md.GetTypeDefinition((TypeDefinitionHandle)handle);
                return !td.BaseType.IsNil && ResolveNestedTypeName(md, td.BaseType) == "System.Enum";
            }
            case HandleKind.TypeReference:
                return CodeGenerator.IsKnownBclEnumName(name);
            default:
                return false;
        }
    }

    private static string ResolveNestedTypeName(MetadataReader md, EntityHandle handle) =>
        handle.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)handle),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)handle),
            HandleKind.TypeSpecification => ResolveNestedTypeSpec(md, (TypeSpecificationHandle)handle),
            _ => throw new NotSupportedException($"type handle kind {handle.Kind} not supported in M2"),
        };

    // P25: decode a TypeSpec interface implementation on a nested type
    // (e.g. IComparable<int>). Mirrors MetadataLoader.ResolveTypeSpec.
    private static string ResolveNestedTypeSpec(MetadataReader md, TypeSpecificationHandle handle)
    {
        var spec = md.GetTypeSpecification(handle);
        var sig = md.GetBlobReader(spec.Signature);
        var header = sig.ReadByte();
        switch (header)
        {
            case 0x15:
                return GenericNestedInstantiationName(md, ref sig);
            case 0x1D:
                return ResolveNestedTypeName(md, sig.ReadTypeHandle()) + "[]";
            case 0x12:
            case 0x11:
                return ResolveNestedTypeName(md, sig.ReadTypeHandle());
            case 0x13:
                sig.ReadCompressedInteger();
                return "T";
            case 0x1E:
                sig.ReadCompressedInteger();
                return "!!T";
            default:
                throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not supported in nested interface impl");
        }
    }

    // batch 156a (asynccase): GENERICINST (0x15) field type on a NESTED
    // state-machine struct. Shape: 0x15 CLASS/VALUETYPE marker + generic
    // definition TypeDefOrRef + arg count + per-arg element type (primitives
    // are a single byte, CLASS/VALUETYPE follow with their own TypeDefOrRef).
    // Mirrors MetadataLoader.GenericInstantiationName; primitives decode to
    // their System names so the resulting FieldTypeName is the full
    // instantiation ("System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<System.Int32>").
    private static string GenericNestedInstantiationName(MetadataReader md, ref BlobReader sig)
    {
        sig.ReadByte(); // 0x11 VALUETYPE / 0x12 CLASS marker
        var defName = ResolveNestedTypeName(md, sig.ReadTypeHandle());
        var argCount = sig.ReadCompressedInteger();
        var args = new string[argCount];
        for (var i = 0; i < argCount; i++)
            args[i] = NestedTypeArgName(md, ref sig);
        return defName + "<" + string.Join(",", args) + ">";
    }

    private static string NestedTypeArgName(MetadataReader md, ref BlobReader sig)
    {
        // ECMA-335 II.23.1.16 custom modifiers (0x1F CMOD_REQD / 0x20
        // CMOD_OPT) precede the modified type -- strip before decoding.
        var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
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
            0x11 or 0x12 => ResolveNestedTypeName(md, sig.ReadTypeHandle()),
            // batch 165b: SZARRAY generic argument (Task<int[]> -- the
            // <AF>d__3 awaiter field signature carries TaskAwaiter`1<int[]>):
            // the element type follows the 0x1D/0x1F byte (recursive).
            0x1D => NestedTypeArgName(md, ref sig) + "[]",
            // GN-14 (a-2): VAR/MVAR generic argument -- the compressed
            // parameter index MUST be consumed (mirrors
            // MethodBodyReader.ConsumeGenericParamSuffix) or the next argument
            // byte (the index) is misread through the tolerant fallback as
            // "System.Object" (the silent wrong closure-field name). This is a
            // local-kind naming authority: the value stays the call-side
            // default "int" (NOT hint-aware -- body-pass global state must not
            // leak into this shared helper).
            0x13 or 0x1E => ConsumeNestedGenericParamIndex(ref sig),
            // Tolerant fallback: exotic arg types map to Object.
            var other => "System.Object",
        };
    }

    // GN-14 (a-2): consume a VAR (0x13) / MVAR (0x1E) generic-argument
    // compressed parameter index and return the call-side default name "int"
    // (mirrors MethodBodyReader.ConsumeGenericParamSuffix; no InstantiationSuffix
    // hint lookup -- this shared nested-type name helper must not observe
    // body-pass global state).
    private static string ConsumeNestedGenericParamIndex(ref BlobReader sig)
    {
        sig.ReadCompressedInteger(); // VAR/MVAR: parameter index
        return "int";
    }

    // Nested-type constant decode -- mirrors MetadataLoader.TryReadConstant.
    private static string? TryReadNestedConstant(MetadataReader md, FieldDefinition fd)
    {
        var constant = fd.GetDefaultValue();
        if (constant.IsNil) return null;
        var c = md.GetConstant(constant);
        if (c.Value.IsNil) return null;
        var value = md.GetBlobReader(c.Value).ReadConstant(c.TypeCode);
        return value == null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    // H2 (transpiler compile-time perf): per-type C++ name memo.
    //
    // TypeCppName is called once per emitted symbol reference -- 55k calls for
    // a 2.4k-type assembly, 8 of them inside a single EmitTypeInfo (the
    // struct/typeinfo/registration name for the same t). Every call rebuilt
    // the sanitized base name (Sanitize = Split + Join) and the
    // DeclaringTypeName/Name/InstantiationSuffix identity string before doing
    // one dictionary lookup, so the same pure result was recomputed dozens of
    // times per type.
    //
    // Keyed by TypeDef REFERENCE (TypeDef overrides neither Equals nor
    // GetHashCode), which is exactly the granularity the collision map uses.
    // The value is tagged with the _cppNameOverrides instance it was computed
    // under: that map is replaced once per Generate by
    // ResolveTypeCppNameCollisions, and a cached name computed BEFORE that
    // pass (CodeGenerator's generic-usage scan calls TypeCppName at :840,
    // before :913) would otherwise keep the pre-collision bare name. The token
    // comparison is the actual invalidation mechanism; the Clear() in
    // ResolveTypeCppNameCollisions only bounds the cache to one Generate's
    // types (it is NOT what keeps the names correct -- removing it alone still
    // yields correct names because every entry is token-tagged, which the
    // cache unit test pins down).
    // ConcurrentDictionary because xUnit test classes call TypeCppName in
    // parallel; the CLI itself is single-threaded (CodeGenerator.Generate
    // holds _poolLock).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<TypeDef, (object? Token, string Name)> _typeCppNameCache = new();

    public static string TypeCppName(TypeDef t)
    {
        var token = _cppNameOverrides;
        if (_typeCppNameCache.TryGetValue(t, out var cached) && ReferenceEquals(cached.Token, token))
            return cached.Name;
        var core = TypeCppNameCore(t);
        // GN-N5: append the deterministic disambiguation suffix when THIS type's
        // C++ name collides with another type's (Sanitize maps '.' and '_' both
        // to '_', so e.g. 'Cases.A_B' and nested 'Cases.A.B' both become
        // 'Cases_A_B'). The suffix is keyed by the type identity
        // (ResolveTypeCppNameCollisions); null/absent = no collision.
        // name-clash: a top-level managed type whose emitted C++ name is a
        // Windows SDK typedef name (LOGFONTW, TEXTMETRICW, MSG, ...) cannot be
        // declared as `struct <name>` after <windows.h> ("using typedef-name
        // after 'struct'"). Those types get the same per-Generate map entry
        // (_sdk) in ResolveTypeCppNameCollisions; the direct check keeps this
        // mapping correct for callers that run before that pass.
        var identity = TypeCppIdentity(t);
        var name = token != null && token.TryGetValue(identity, out var sfx)
            ? core + sfx
            : (!t.IsNested && WindowsSdkTypedefNames.Contains(core))
                ? core + SdkTypedefSuffix
                : core;
        _typeCppNameCache[t] = (token, name);
        return name;
    }

    // name-clash / GN-N5: TypeCppName appends the override suffix AFTER the
    // bare core is computed. Suppress the string-side suffix lookup for the
    // duration of the core computation: otherwise SanitizeIdentChars would
    // append it here and TypeCppName would append it a second time.
    static string TypeCppNameCore(TypeDef t)
    {
        var prev = _nameSuffixSuppressed;
        _nameSuffixSuppressed = true;
        try
        {
            return TypeCppNameCoreInner(t);
        }
        finally
        {
            _nameSuffixSuppressed = prev;
        }
    }

    static string TypeCppNameCoreInner(TypeDef t)
    {
        // r78 fix: nested types need declaring-type prefix to avoid name collisions.
        // Multiple enclosing types can have nested types with the same short name
        // (e.g., 25 different types each have a `<>c` nested class -- all sanitize to __c).
        // Prefix with declaring type name for uniqueness.
        // r78b: when DeclaringTypeName is empty (defensive), use FullName to avoid
        // collapsing all nested types into one name.
        var baseName = t.IsNested
            ? (!string.IsNullOrEmpty(t.DeclaringTypeName)
                ? Sanitize(t.DeclaringTypeName) + "_" + Sanitize(t.Name)
                : Sanitize(t.FullName))
            : Sanitize(t.FullName);
        // R77 generic multi-instantiation: if this TypeDef carries an explicit
        // instantiation suffix ("int", "object", ...), use it. Otherwise fall
        // back to the M3 default (T -> int32_t, "_int") for generic defs that
        // have no explicit instantiation. Nested closure types never get a
        // generic suffix (they are concrete types, not instantiations).
        if (!t.IsNested && t.GenericArity > 0)
        {
            // A5 (myIList): the closed suffix may be a dotted full name
            // ("System.Exception" for user-generic instantiations) -- it must
            // be sanitized like the base name or the struct name carries a
            // '.' (generated.h "expected unqualified-id"). Short suffixes
            // ("int"/"string"/...) are Sanitize-fixed points.
            // P-G2 (arity>1): the M3 default instantiation is "int" PER
            // generic parameter ("Pair`2" default = "int_int"), so the
            // definition's own name matches the method-body's expanded
            // instance cast (`Pair_2_int_int`) and the shared "object" x arity
            // clone name is symmetric.
            var defSuffix = t.InstantiationSuffix;
            if (defSuffix == null && t.GenericArity > 1)
                defSuffix = string.Join("_", Enumerable.Repeat("int", t.GenericArity));
            return baseName + "_" + Sanitize(defSuffix ?? "int");
        }
        return baseName;
    }

    //--------------------------------------------------------------------------
    // GN-N5 (p-namediv): deterministic C++-name collision resolution. The
    // Sanitize rule maps '.' and '_' both to '_', so two DIFFERENT managed
    // types can produce the SAME C++ identifier ("Cases.A_B" and the nested
    // "Cases.A.B" both -> "Cases_A_B" -- duplicate struct at build time, or a
    // generic-instance clone Box_1_Cases_A_B shared by two args). The session
    // map records, per type identity, a deterministic hash suffix applied by
    // TypeCppName (and the generic-instance name producers). Identity:
    //   nested  -> DeclaringTypeName + "+" + Name ("Cases.A+B")
    //   top     -> FullName
    //   clone   -> FullName + "|" + InstantiationSuffix (the arg suffix
    //              distinguishes Box<Cases.A_B> from Box<Cases.A.B>)
    // The FIRST identity (ordinal) keeps the bare name; the rest get
    // "_h{fnv1a}" so the suffix is stable across runs.
    //--------------------------------------------------------------------------
    private static System.Collections.Generic.Dictionary<string, string>? _cppNameOverrides;

    // name-clash: the string-side view of _cppNameOverrides (see
    // SanitizeIdentChars). Built in the same pass; keys are exactly the
    // sanitized bare names of top-level non-generic managed types that carry
    // an override, so a hit needs no per-identifier string concatenation.
    private static System.Collections.Generic.Dictionary<string, string>? _sanitizedNameOverrides;

    // True while TypeCppNameCore computes the BARE core (the override suffix
    // is appended by TypeCppName after this returns); stops the string-side
    // lookup in SanitizeIdentChars from appending the suffix twice.
    [ThreadStatic]
    private static bool _nameSuffixSuppressed;

    // Deterministic suffix for a managed type whose C++ name is a Windows
    // SDK typedef name.
    private const string SdkTypedefSuffix = "_sdk";

    // Identity separators used by Sanitize's Split/Join; an identity that
    // carries any of them is not a bare top-level managed name and must not
    // enter _sanitizedNameOverrides.
    private static readonly char[] NameIdentitySeparators =
        { '.', '+', '`', '<', '>', ',', '[', ']', '=' };

    private static string TypeCppIdentity(TypeDef t)
        => (t.IsNested && t.DeclaringTypeName.Length > 0 ? t.DeclaringTypeName + "+" + t.Name : t.FullName)
           + "|" + (t.InstantiationSuffix ?? "");

    // H3 (transpiler compile-time perf): nested-type index.
    //
    // EmitTypeInfo used to answer "which types are DIRECTLY nested in t?" with
    // a full Where() over every type, per type -- N^2 string comparisons, plus
    // a ReflectionFullName(t) rebuild for every element the predicate reached
    // (it sat inside the lambda, so it was NOT hoisted). Measured on a
    // 2401-type assembly: 5,764,801 predicate iterations (= 2401^2) for 2401
    // scans, 85.6/72.3 ms; on a 5001-type assembly 100,020,001 iterations,
    // 1410.0 ms.
    //
    // The index is built in ResolveTypeCppNameCollisions -- the one per-Generate
    // pass that already walks the final type list (CodeGenerator.cs:913) -- so
    // its lifetime is exactly that Generate's. Keys are the raw
    // DeclaringTypeName (NOT ReflectionFullName: a nested type's reflection name
    // is "Outer+Inner", which never equals a child's DeclaringTypeName) and
    // values keep the source list order, so the emitted table order is
    // unchanged. EmitTypeInfo only uses the index when it is handed that SAME
    // list instance AND that list still has the element count the index was
    // built from; any other list (mode-B filtering, ad-hoc callers, or the same
    // List mutated in place after the pass) falls back to the original linear
    // scan, so output is unchanged. The arrays are read-only after construction
    // and never handed to a caller -- no shared mutable buffer is exposed.
    private static Dictionary<string, TypeDef[]>? _nestedByDeclaring;
    private static IReadOnlyList<TypeDef>? _nestedIndexSource;
    private static int _nestedIndexSourceCount = -1;

    // p0linq4 (D1): the FINAL type list of the current Generate (session
    // scope, set by ResolveTypeCppNameCollisions). MethodMap.Linq needs it to
    // resolve a user-struct generic argument to its TypeDef so a List<T>
    // materializer can box with the struct's OWN descriptor and sizeof(T) --
    // the resolver carries no type list of its own.
    //
    // D1 (fix #2): the storage is [ThreadStatic] and that is load-bearing. A
    // session/Generate context is single-threaded by construction -- emission is
    // synchronous per body, so the list is written and read on the same thread
    // with no interleaving -- hence per-thread storage preserves today's
    // behaviour EXACTLY while making two concurrent Generates (and xUnit's
    // parallel test classes) independent. With a plain static, one writer's
    // synthetic list (see MethodMapTestsGroupByKey.SessionTypesScope) leaked into
    // a concurrently reading class, which surfaced as a real flake:
    // "value-type (Cases.Color) key has no runtime key carrier" in the full
    // parallel suite while the same test passed alone. AsyncLocal is unnecessary
    // and wrong here: there is no await between the set and its uses, so a
    // thread-affine slot already matches the lifetime, and AsyncLocal would add
    // per-access overhead plus copy-on-write semantics nobody relies on.
    // The property name, type and private setter are preserved: 6 production read
    // sites use the name and the test helper installs via GetSetMethod(true).
    [ThreadStatic]
    private static IReadOnlyList<TypeDef>? _sessionTypes;

    public static IReadOnlyList<TypeDef>? SessionTypes
    {
        get => _sessionTypes;
        private set => _sessionTypes = value;
    }

    // Public for CodeGenerator (called once per Generate, after all clones are
    // created -- the session map is deterministic on the final type list).
    public static void ResolveTypeCppNameCollisions(IReadOnlyList<TypeDef> types)
    {
        SessionTypes = types;
        var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var t in types)
        {
            var core = TypeCppNameCore(t);
            if (!byName.TryGetValue(core, out var ids))
                byName[core] = ids = new List<string>();
            ids.Add(TypeCppIdentity(t));
        }
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in byName)
        {
            if (kv.Value.Count < 2) continue;
            var unique = kv.Value.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (unique.Count < 2) continue; // same identity twice (impossible)
            for (var i = 1; i < unique.Count; i++)
                overrides[unique[i]] = "_h" + Fnv1a(unique[i]);
        }
        // name-clash: a top-level managed type whose emitted C++ name is a
        // Windows SDK typedef name would be emitted as `struct LOGFONTW`,
        // which is ill-formed after <windows.h> (the SDK owns that typedef).
        // Give it a deterministic per-Generate suffix through the SAME map
        // TypeCppName already applies; the string-side producers read the
        // derived name map in SanitizeIdentChars. A type that already got a
        // GN-N5 hash suffix above no longer collides with the SDK typedef,
        // so it is left untouched.
        foreach (var t in types)
        {
            if (t.IsNested) continue;
            var core = TypeCppNameCore(t);
            if (!WindowsSdkTypedefNames.Contains(core)) continue;
            var id = TypeCppIdentity(t);
            if (!overrides.ContainsKey(id))
                overrides[id] = SdkTypedefSuffix;
        }
        _cppNameOverrides = overrides;
        // name-clash / GN-N5: string-side view of the SAME map -- only
        // identities that are bare top-level non-generic managed names (no
        // identity separator) can ever be looked up from SanitizeIdentChars.
        var sanitizedNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in overrides)
        {
            if (!kv.Key.EndsWith("|", StringComparison.Ordinal)) continue;
            if (kv.Key.IndexOfAny(NameIdentitySeparators) >= 0) continue;
            sanitizedNames[kv.Key[..^1]] = kv.Value;
        }
        _sanitizedNameOverrides = sanitizedNames;
        // H2: the override map just changed identity -- drop every name memoized
        // against the previous instance (pre-collision bare names from the
        // generic-usage scan, or another assembly's Generate).
        _typeCppNameCache.Clear();
        // H3: rebuild the nested-type index for THIS type list (see
        // _nestedByDeclaring). Built here so it can never outlive the Generate
        // whose final type list it describes.
        var nested = new Dictionary<string, List<TypeDef>>(StringComparer.Ordinal);
        foreach (var t in types)
        {
            if (!t.IsNested || t.DeclaringTypeName.Length == 0) continue;
            if (!nested.TryGetValue(t.DeclaringTypeName, out var bucket))
                nested[t.DeclaringTypeName] = bucket = new List<TypeDef>();
            bucket.Add(t);
        }
        var index = new Dictionary<string, TypeDef[]>(nested.Count, StringComparer.Ordinal);
        foreach (var kv in nested)
            index[kv.Key] = kv.Value.ToArray();
        _nestedByDeclaring = index;
        _nestedIndexSource = types;
        _nestedIndexSourceCount = types.Count;
    }

    // GN-N5: the disambiguation suffix for a GENERIC-INSTANCE name produced by
    // the string-side (MethodRefResolver/StackFold/CodeGenerator
    // GenericInstantiationName -- defName + "_" + args). Keyed by the SAME
    // identity the clone TypeDef uses (FullName|suffix), so the call-site name
    // matches the emitted clone struct. "" = no collision.
    public static string GenericInstanceCppNameSuffix(string defName, string[] args)
    {
        if (_cppNameOverrides == null) return "";
        var key = defName + "|" + string.Join("_", args);
        return _cppNameOverrides.TryGetValue(key, out var sfx) ? sfx : "";
    }

    // H3: DIRECT nested types of t, in source-list order. Uses the per-Generate
    // index when the caller passed the very list the index was built from and
    // that list's element count still matches (a List mutated in place after
    // ResolveTypeCppNameCollisions would otherwise silently under-report
    // children); otherwise (unit tests / ad-hoc callers / mutated lists) falls
    // back to the original linear scan so output is unchanged. The returned
    // array is shared and read-only.
    private static IReadOnlyList<TypeDef> NestedTypesOf(TypeDef t, IReadOnlyList<TypeDef>? allTypes)
    {
        var key = ReflectionFullName(t);
        var index = _nestedByDeclaring;
        if (index != null && allTypes != null
            && ReferenceEquals(allTypes, _nestedIndexSource)
            && allTypes.Count == _nestedIndexSourceCount)
            return index.TryGetValue(key, out var direct) ? direct : Array.Empty<TypeDef>();
        if (allTypes == null) return Array.Empty<TypeDef>();
        var list = new List<TypeDef>();
        foreach (var x in allTypes)
            if (x.IsNested && x.DeclaringTypeName == key) list.Add(x);
        return list;
    }

    // FNV-1a 32-bit hex (lowercase) -- deterministic, no random state.
    private static string Fnv1a(string s)
    {
        uint h = 2166136261;
        foreach (var c in s)
        {
            h ^= c;
            h *= 16777619;
        }
        return h.ToString("x8");
    }

    //--------------------------------------------------------------------------
    // ReflectionFullName -- the typeinfo registry KEY of a type.
    //
    // Top-level types: t.FullName ("Cases.Refl2A"). NESTED types (refl2
    // gt_nested): the expanded TypeDef models the SHORT metadata name
    // ("Inner") so every C++ symbol (Inner_class / Inner_typeinfo) matches the
    // reference-point naming, but the typeinfo registry lookup compares
    // info->name against the normalized Type.GetType query string -- and
    // NormalizeTypeName rule 3 KEEPS the net48 '+' separator ("Cases.Outer+Inner"
    // is looked up verbatim, never "Cases.Outer.Inner"). A table registered
    // under "Inner" therefore never answers Type.GetType("Cases.Outer+Inner");
    // the table must register under DeclaringTypeName + "+" + Name.
    //--------------------------------------------------------------------------
    public static string ReflectionFullName(TypeDef t)
        => t.IsNested && t.DeclaringTypeName.Length > 0
            ? t.DeclaringTypeName + "+" + t.Name
            : t.FullName;

    // Public for CodeGenerator reuse (the field path -> C++ storage name needs the
    // same sanitize rule). refl2 S2/G3: '=' is an invalid C++ identifier
    // character that the compiler-generated array-initializer struct
    // (<PrivateImplementationDetails>.__StaticArrayInitTypeSize=N) carries in
    // its short metadata name -- replacing it with '_' makes such types emit a
    // VALID C++ name (their TypeInfo table/extern/registration now compile;
    // the old whole-type exclusion is removed). No valid C# identifier contains
    // '=', so no other generated name changes.
    public static string Sanitize(string s)
    {
        var result = string.Join("_", s.Split('.', '+', '`', '<', '>', ',', '[', ']', '='));
        // FIX r77: strip V_ prefix leaked from MethodSignatureCoder encoding
        if (result.StartsWith("V_"))
            result = result[2..];
        return SanitizeIdentChars(result);
    }

    // WHITELIST tail step (local-function fix): the Split/Join above is a
    // BLACKLIST -- every character it does not name survives into the emitted
    // C++ identifier. csc's local-function metadata name
    // "<Add>g__Local|1_0" carries '|', which was NOT in that list, so the
    // generated header contained
    //   int32_t Cases_...__Add_g__Local|0_0_<hash>(int32_t);
    // -> "error: expected initializer before '|' token". Enumerating hostile
    // characters one by one guarantees the next one is another phantom, so the
    // rule is now closed: anything outside [A-Za-z0-9_] becomes '_'.
    //
    // The Split/Join step is deliberately KEPT and runs FIRST: it collapses a
    // RUN of separators into a single '_' ("a..b" -> "a__b", "<>c" -> "__c"),
    // whereas per-character replacement would produce one '_' per character.
    // Keeping it preserves every already-emitted symbol name verbatim (all of
    // them are fixed points of this step) and makes the new step idempotent.
    public static string SanitizeIdentChars(string s)
    {
        var clean = true;
        foreach (var c in s)
        {
            if (!IsIdentChar(c)) { clean = false; break; }
        }
        if (!clean)
        {
            var buf = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
                buf.Append(IsIdentChar(c) ? c : '_');
            s = buf.ToString();
        }
        // name-clash / GN-N5 string-side suffix: TypeCppName appends the
        // override suffix on the STRUCT DEFINITION side; every string-side
        // producer (P/Invoke parameter casts, MethodRefResolver method
        // symbols, StackFold) reaches this shared tail, so it must append
        // the SAME suffix or references diverge from the definition. Only
        // per-Generate entries whose identity is a top-level non-generic
        // managed name can match here (all other identities carry
        // '.'/'+'/'`'/'<'/'>'/'='/','/'['/']' and are filtered when the
        // map is built). TypeCppNameCore suppresses this lookup while it
        // computes the bare core; the override is appended afterwards.
        if (!_nameSuffixSuppressed && _sanitizedNameOverrides != null
            && _sanitizedNameOverrides.TryGetValue(s, out var sfx))
            return s + sfx;
        return s;
    }

    private static bool IsIdentChar(char c)
        => c is >= 'A' and <= 'Z'
        or >= 'a' and <= 'z'
        or >= '0' and <= '9'
        or '_';

    // Static field C++ file-scope name: {TypeCppName}_sf_{fieldName}. The file-scope
    // static guarantees uniqueness within the type.
    public static string FieldCppName(TypeDef t, string fieldName) => $"{TypeCppName(t)}_sf_{Sanitize(fieldName)}";

    //--------------------------------------------------------------------------
    // LayoutInstanceFields -- assign slots / byte offsets / instanceSize /
    // refFieldBitmap.
    //
    // REFERENCE types keep the historical whole-pointer slot grid: slot i is
    // at byte offset i*PointerSize, refFieldBitmap bit i marks slot i, and
    // InstanceSize = slots * PointerSize. Slot 0 is the object header; the
    // collector scans slot-wise with no ambiguity, and a derived class's slots
    // continue the base chain's grid.
    //
    // VALUE types (structs) take the net48 sequential layout instead
    // (LayoutValueTypeFields): a struct instance has no object header and is
    // passed to native code by pointer (P/Invoke out/ref parameters, pointer
    // returns), so the field offsets and InstanceSize MUST be the compact
    // byte layout the native callee uses -- a run of int32 fields occupies
    // 4-byte strides, not 8-byte slots. See LayoutValueTypeFields.
    //--------------------------------------------------------------------------
    public static void LayoutInstanceFields(TypeDef t, IReadOnlyList<TypeDef>? allTypes = null)
    {
        if (t.InstanceSize != 0) return; // idempotent: already laid out
        if (t.IsValueType)
        {
            LayoutValueTypeFields(t);
            return;
        }
        uint bitmap = 0;
        int refCount = 0;
        // Slot 0 = object header for reference types; value types have NO
        // header (M3 Task 11) -- their slots start at 0.
        // rf-props (RF-N2): a DERIVED class embeds its base instance fields
        // (generated accessors cast to the base struct and read/write the
        // base slots directly), so the layout must continue the base chain's
        // slot grid and inherit its reference bits -- otherwise an empty
        // derived class gets instanceSize = 8 (header only), GC bumps too few
        // bytes and the next allocation lands INSIDE the instance (the
        // PropertyInfo.GetValue arg-array overwrote the inherited _name slot,
        // probe-exposed). Value types have no user inheritance (only
        // System.ValueType/System.Enum BCL roots, zero fields), so they keep
        // slot 0.
        int slot;
        if (t.BaseType != null && t.BaseTypeName != "System.Object")
        {
            LayoutInstanceFields(t.BaseType, allTypes); // base chain laid out first (multi-level)
            slot = t.BaseType.InstanceSize / t.PointerSize;
            bitmap = t.BaseType.RefFieldBitmap;
            refCount = t.BaseType.RefFieldCount;
        }
        else
        {
            slot = 1;
        }
        foreach (var f in t.InstanceFields)
        {
            // Embedded VALUE-STRUCT members (a captured user struct, or the
            // value half of a captured Nullable<T>) keep their real native
            // layout: start on the struct's natural alignment and span
            // ceil(size/pointerSize) slots so the following field (and the
            // collector's slot grid) stay in lockstep. A Nullable<T> adds one
            // whole flag slot for hasValue right after the value.
            if (f.TypeKind is FieldTypeKind.UserStruct or FieldTypeKind.Nullable)
            {
                var shape = ResolveFieldValueShape(f, allTypes, t.PointerSize);
                if (shape.Align > t.PointerSize)
                    slot = RoundUpSlots(slot, (shape.Align + t.PointerSize - 1) / t.PointerSize);
                f.SlotIndex = slot;
                f.ByteOffset = slot * t.PointerSize;
                MarkEmbeddedReferences(t, f, slot, shape.RefBitmap, ref bitmap, ref refCount);
                var valueSlots = SlotSpan(shape.Size, t.PointerSize);
                if (f.TypeKind == FieldTypeKind.Nullable)
                {
                    f.NullableHasValueOffset = f.ByteOffset + shape.Size;
                    slot += valueSlots + 1;
                }
                else
                {
                    slot += valueSlots;
                }
                continue;
            }
            // A runtime VALUE struct member whose natural alignment exceeds
            // the pointer size (the 8-byte alignment of the int64-based value
            // structs on a 32-bit target) starts on an aligned slot -- exactly
            // where the C++ compiler places it, so the emitted member offsets
            // and the metadata offsets stay in lockstep.
            if (TypeKindResolver.IsValueStructFieldKind(f.TypeKind))
            {
                var shape = TypeKindResolver.ValueStructFieldShape(f.TypeKind);
                if (shape.AlignBytes > t.PointerSize)
                {
                    int alignSlots = shape.AlignBytes / t.PointerSize;
                    slot = (slot + alignSlots - 1) / alignSlots * alignSlots;
                }
            }
            f.SlotIndex = slot;
            f.ByteOffset = slot * t.PointerSize;
            if (f.TypeKind == FieldTypeKind.Reference)
            {
                // Audit H6: the 32-bit refFieldBitmap can only express slots
                // 0..31 (1u << slot is UB for slot >= 32 and the collector
                // scans slot < 32). A reference field beyond that would be
                // silently omitted from the GC scan -- objects reachable only
                // through it would be collected. Warn and skip instead of rejecting.
                if (slot >= 32)
                {
                    Console.Error.WriteLine($"warning: type {t.FullName}: reference field '{f.Name}' lands on slot {slot} -- the 32-bit refFieldBitmap cannot represent slots >= 32 (audit H6), skipping field from bitmap");
                    continue;
                }
                bitmap |= 1u << slot;
                ++refCount;
            }
            // B3: a 16-byte Guid occupies multiple pointer-size slots, so the
            // emitted C++ member (Il2CppGuid) and the metadata offsets stay in
            // lockstep for the following fields. The same holds for the other
            // runtime value structs (DateTime/Decimal/DateTimeOffset 16
            // bytes, TimeSpan 8 bytes) -- their member is the runtime struct
            // itself, so the slot must span the whole value.
            if (TypeKindResolver.IsValueStructFieldKind(f.TypeKind))
            {
                var shape = TypeKindResolver.ValueStructFieldShape(f.TypeKind);
                slot += (shape.SizeBytes + t.PointerSize - 1) / t.PointerSize;
            }
            else
            {
                slot += 1;
            }
        }
        t.RefFieldBitmap = bitmap;
        t.RefFieldCount = refCount;
        // Whole pointer-size grid (G1: t.PointerSize = the TARGET width, 4|8;
        // default = host IntPtr.Size so the historical x64 layout is unchanged).
        t.InstanceSize = slot * t.PointerSize;
    }

    //--------------------------------------------------------------------------
    // LayoutValueTypeFields -- net48 sequential layout for a translated struct.
    //
    // A struct instance is a plain C++ value (no object header, M3 Task 11) and
    // is handed to native code BY POINTER: a P/Invoke out/ref parameter, a
    // pointer return, or a marshal buffer. The native side reads and writes the
    // fields at THEIR OWN byte offsets, so the emitted C++ struct -- whose
    // members are declared in metadata declaration order, exactly like this
    // model -- already packs them with C++'s natural alignment. The metadata
    // that used to describe the same struct (field offsets + InstanceSize) was
    // instead computed on the whole-pointer slot grid: a struct of fifteen int
    // fields was advertised as instanceSize 120 with 8-byte strides while the
    // actual C++ object was the compact 60 bytes the native callee expects
    // (GN-20.4 F1 -- the gdifont TEXTMETRIC probe). Reflection
    // (Il2CppFieldInfo.offset), boxing and any size-bearing consumer then
    // disagree with the real object.
    //
    // This pass derives the offsets from the field widths themselves (no
    // hard-coded sizes): align each field up to its natural alignment, place
    // it there, and set InstanceSize to the struct's C++ size (end rounded up
    // to the largest member alignment). SlotIndex is kept as
    // ByteOffset / PointerSize so the collector's slot-wise scan still works
    // for the reference fields a struct may carry (a reference is always
    // PointerSize-aligned, so it always lands on a whole slot).
    //
    // Structs have no user inheritance (only the System.ValueType /
    // System.Enum BCL roots, which carry no layout fields), so there is no
    // base-chain region to continue.
    //--------------------------------------------------------------------------
    private static void LayoutValueTypeFields(TypeDef t, IReadOnlyList<TypeDef>? allTypes = null)
    {
        uint bitmap = 0;
        int refCount = 0;
        int offset = 0;
        int maxAlign = 1;
        foreach (var f in t.InstanceFields)
        {
            // P/Invoke fixed-buffer field ([MarshalAs]): net48 lays it INLINE
            // (WCHAR[SizeConst] for ByValTStr, T[SizeConst] for ByValArray), so
            // the member is a fixed array and there is NO GC reference to
            // record. Size/alignment follow the array: ByValTStr is
            // SizeConst * charSize bytes at charSize alignment; ByValArray is
            // SizeConst * elementSize bytes at the ELEMENT's natural alignment
            // (measured on the net48 oracle: { int32, long[3] } puts the array
            // at offset 8 and makes the struct 32 bytes). The final
            // InstanceSize expression below rounds the tail up to maxAlign, so
            // byte[5] after an int header is 9 bytes of extent -> size 12.
            if (f.MarshalKind == FieldMarshalKind.ByValTStr)
            {
                var marshalSize = f.MarshalSizeConst * f.MarshalCharSize;
                var marshalAlign = f.MarshalCharSize;
                if (marshalAlign > maxAlign) maxAlign = marshalAlign;
                offset = (offset + marshalAlign - 1) / marshalAlign * marshalAlign;
                f.ByteOffset = offset;
                f.SlotIndex = offset / t.PointerSize;
                offset += marshalSize;
                continue;
            }
            if (f.MarshalKind == FieldMarshalKind.ByValArray)
            {
                var elem = DescribeMarshalArray(f.MarshalArrayElement, t.FullName, f.Name);
                if (elem.NativeAlign > maxAlign) maxAlign = elem.NativeAlign;
                offset = (offset + elem.NativeAlign - 1) / elem.NativeAlign * elem.NativeAlign;
                f.ByteOffset = offset;
                f.SlotIndex = offset / t.PointerSize;
                offset += f.MarshalSizeConst * elem.NativeSize;
                continue;
            }
            // Embedded user struct / Nullable<T> (a NESTED struct may carry
            // them; top-level struct fields of a value type are decoded by
            // MetadataLoader). The member pair emitted by EmitClassStruct is
            // the layout authority: the value at its natural alignment, the
            // hasValue flag directly after it, then pad to the following
            // pointer-slot boundary.
            if (f.TypeKind is FieldTypeKind.UserStruct or FieldTypeKind.Nullable)
            {
                var shape = ResolveFieldValueShape(f, allTypes, t.PointerSize);
                if (shape.Align > maxAlign) maxAlign = shape.Align;
                offset = (offset + shape.Align - 1) / shape.Align * shape.Align;
                f.ByteOffset = offset;
                f.SlotIndex = offset / t.PointerSize;
                MarkEmbeddedReferences(t, f, f.SlotIndex, shape.RefBitmap, ref bitmap, ref refCount);
                var valueSlots = SlotSpan(shape.Size, t.PointerSize);
                if (f.TypeKind == FieldTypeKind.Nullable)
                {
                    f.NullableHasValueOffset = offset + shape.Size;
                    offset += valueSlots * t.PointerSize + t.PointerSize;
                }
                else
                {
                    offset += shape.Size;
                }
                continue;
            }
            var (size, align) = FieldNativeShape(f.TypeKind, t.PointerSize);
            if (align > maxAlign) maxAlign = align;
            // align the field up to its own natural alignment (C++ layout)
            offset = (offset + align - 1) / align * align;
            f.ByteOffset = offset;
            f.SlotIndex = offset / t.PointerSize;
            if (f.TypeKind == FieldTypeKind.Reference)
            {
                if (f.SlotIndex >= 32)
                {
                    Console.Error.WriteLine($"warning: type {t.FullName}: reference field '{f.Name}' lands on slot {f.SlotIndex} -- the 32-bit refFieldBitmap cannot represent slots >= 32 (audit H6), skipping field from bitmap");
                }
                else
                {
                    bitmap |= 1u << f.SlotIndex;
                    ++refCount;
                }
            }
            offset += size;
        }
        t.RefFieldBitmap = bitmap;
        t.RefFieldCount = refCount;
        t.InstanceAlignment = maxAlign;
        // C++ struct size: the extent rounded up to the struct's own alignment
        // (the largest member alignment). An int-only struct therefore reports
        // exactly its compact size (15 int32 -> 60), which is what the native
        // callee writes through.
        t.InstanceSize = (offset + maxAlign - 1) / maxAlign * maxAlign;
    }

    //--------------------------------------------------------------------------
    // ByValArray element resolution ([MarshalAs(UnmanagedType.ByValArray,
    // SizeConst = N)] on a primitive SZARRAY field).
    //
    // net48 lays the array INLINE in the value type, so the emitted C++ member
    // is a fixed array and the struct layout must use the ELEMENT's native
    // width/alignment. Measured on the net48 oracle (this seat's probe):
    //   { int32 head; ByValArray(4) int[]  vals; int32 tail } -> size 24,
    //       vals@4, tail@20                                           (4+16+4)
    //   { int32 head; ByValArray(5) byte[] b } -> size 12, b@4
    //       (4+5 = 9, tail rounded up to the struct's int32 alignment)
    //   { int32 head; ByValArray(3) long[] l } -> size 32, l@8
    //       (8-byte element alignment inserts 4 bytes after head)
    //   { int32 head; ByValArray(4) bool[] b } -> size 20, b@4
    //       -- bool elements are 4-byte Win32 BOOLs, NOT the 1-byte managed
    //       bool cell (explicit ArraySubType=U1 gives size 8: 1-byte cells)
    //
    // The element kind comes from the FieldMarshal blob's optional native type
    // byte when [MarshalAs(ArraySubType = ...)] is explicit; the C# compiler
    // OMITS it otherwise (metadata probe: int[] -> FieldMarshal 1E 04, bool[]
    // -> 1E 04, explicit U1 -> 1E 04 04), in which case the SZARRAY element
    // type in the field signature is the authority (06 1D 08 for int[]).
    // Reference/struct/char element arrays stay LOUD: an inline GC pointer or
    // a nested non-blittable element has no representable native shape here.
    //--------------------------------------------------------------------------
    public static void ResolveByValArrayElements(MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        foreach (var t in types)
            foreach (var f in t.InstanceFields)
                if (f.MarshalKind == FieldMarshalKind.ByValArray)
                    f.MarshalArrayElement = DecodeByValArrayElement(md, t.FullName, f);
    }

    private static MarshalArrayElementKind DecodeByValArrayElement(MetadataReader md, string owner, FieldDef f)
    {
        if (f.MarshalSizeConst <= 0)
            throw new NotSupportedException(
                $"struct '{owner}' field '{f.Name}': [MarshalAs(ByValArray)] without a positive SizeConst has no inline array length (net48 requires SizeConst)");
        if ((f.MetadataToken & unchecked((int)0xFF000000)) != 0x04000000)
            throw new NotSupportedException(
                $"struct '{owner}' field '{f.Name}': [MarshalAs(ByValArray)] field has no FieldDef token -- cannot decode its SZARRAY element type");
        var fh = MetadataTokens.FieldDefinitionHandle(f.MetadataToken & 0x00FFFFFF);
        var sig = md.GetBlobReader(md.GetFieldDefinition(fh).Signature);
        if (sig.ReadByte() != 0x06)
            throw new NotSupportedException(
                $"struct '{owner}' field '{f.Name}': [MarshalAs(ByValArray)] field signature does not start with FIELD (0x06)");
        var lead = sig.ReadByte();
        if (lead != 0x1D)
            throw new NotSupportedException(
                $"struct '{owner}' field '{f.Name}': [MarshalAs(ByValArray)] requires a single-dimensional array field (signature lead 0x{lead:x2})");
        var sigElem = sig.ReadByte();
        if (string.IsNullOrEmpty(f.MarshalElementTypeName))
            return MapManagedArrayElement(owner, f, sigElem);
        var text = f.MarshalElementTypeName;
        if (text.Length != 4 || !text.StartsWith("0x", StringComparison.Ordinal)
            || !byte.TryParse(text.Substring(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var nativeElem))
            throw new NotSupportedException(
                $"struct '{owner}' field '{f.Name}': [MarshalAs(ByValArray)] ArraySubType descriptor '{text}' is not a native type byte");
        return MapNativeArrayElement(owner, f, nativeElem);
    }

    // ELEMENT_TYPE_* of the managed array element (0x02 BOOLEAN, 0x04 I1,
    // 0x05 U1, 0x06 I2, 0x07 U2, 0x08 I4, 0x09 U4, 0x0A I8, 0x0B U8,
    // 0x0C R4, 0x0D R8).
    private static MarshalArrayElementKind MapManagedArrayElement(string owner, FieldDef f, byte elem)
        => elem switch
        {
            0x02 => MarshalArrayElementKind.Bool,
            0x04 => MarshalArrayElementKind.I1,
            0x05 => MarshalArrayElementKind.U1,
            0x06 => MarshalArrayElementKind.I2,
            0x07 => MarshalArrayElementKind.U2,
            0x08 => MarshalArrayElementKind.I4,
            0x09 => MarshalArrayElementKind.U4,
            0x0A => MarshalArrayElementKind.I8,
            0x0B => MarshalArrayElementKind.U8,
            0x0C => MarshalArrayElementKind.R4,
            0x0D => MarshalArrayElementKind.R8,
            _ => throw new NotSupportedException(
                $"struct '{owner}' field '{f.Name}': [MarshalAs(ByValArray)] managed element type 0x{elem:x2} is not a supported primitive -- reference/struct/char element arrays are out of scope"),
        };

    // NATIVE_TYPE_* of an explicit [MarshalAs(ArraySubType = ...)] (the
    // CorNativeType/UnmanagedType ordinals: BOOLEAN 0x02, I1 0x03, U1 0x04,
    // I2 0x05, U2 0x06, I4 0x07, U4 0x08, I8 0x09, U8 0x0A, R4 0x0B,
    // R8 0x0C).
    private static MarshalArrayElementKind MapNativeArrayElement(string owner, FieldDef f, byte elem)
        => elem switch
        {
            0x02 => MarshalArrayElementKind.Bool,
            0x03 => MarshalArrayElementKind.I1,
            0x04 => MarshalArrayElementKind.U1,
            0x05 => MarshalArrayElementKind.I2,
            0x06 => MarshalArrayElementKind.U2,
            0x07 => MarshalArrayElementKind.I4,
            0x08 => MarshalArrayElementKind.U4,
            0x09 => MarshalArrayElementKind.I8,
            0x0A => MarshalArrayElementKind.U8,
            0x0B => MarshalArrayElementKind.R4,
            0x0C => MarshalArrayElementKind.R8,
            _ => throw new NotSupportedException(
                $"struct '{owner}' field '{f.Name}': [MarshalAs(ByValArray)] ArraySubType native type 0x{elem:x2} is not a supported primitive -- reference/struct element arrays are out of scope"),
        };

    // C++ member/copy shape of one resolved ByValArray element kind:
    //   NativeCpp/NativeSize/NativeAlign -- the INLINE buffer declaration and
    //       its layout contribution (BOOL is 4 bytes; every other kind is
    //       element-width identical on both sides).
    //   ManagedCpp/ManagedSuffix -- the managed array cell (bool[] cells are
    //       1 byte; the runtime's stelem/ldelem width suffixes match).
    //   ElemClass -- the element descriptor expression the managed array
    //       allocation records (the same one CodeGenerator.newarr passes).
    public static (string NativeCpp, int NativeSize, int NativeAlign, string ManagedCpp, string ManagedSuffix, string ElemClass)
        DescribeMarshalArray(MarshalArrayElementKind kind, string owner, string fieldName)
        => kind switch
        {
            MarshalArrayElementKind.Bool => ("int32_t", 4, 4, "bool", "bool", "nullptr"),
            MarshalArrayElementKind.I1 => ("int8_t", 1, 1, "int8_t", "i1", "nullptr"),
            MarshalArrayElementKind.U1 => ("uint8_t", 1, 1, "uint8_t", "u1", "nullptr"),
            MarshalArrayElementKind.I2 => ("int16_t", 2, 2, "int16_t", "i2", "nullptr"),
            MarshalArrayElementKind.U2 => ("uint16_t", 2, 2, "uint16_t", "u2", "nullptr"),
            MarshalArrayElementKind.I4 => ("int32_t", 4, 4, "int32_t", "i4", "il2cpp::il2cpp_class_by_name(\"System.Int32\")"),
            MarshalArrayElementKind.U4 => ("uint32_t", 4, 4, "uint32_t", "u4", "il2cpp::il2cpp_class_by_name(\"System.UInt32\")"),
            MarshalArrayElementKind.I8 => ("int64_t", 8, 8, "int64_t", "i8", "il2cpp::il2cpp_class_by_name(\"System.Int64\")"),
            MarshalArrayElementKind.U8 => ("uint64_t", 8, 8, "uint64_t", "u8", "nullptr"),
            MarshalArrayElementKind.R4 => ("float", 4, 4, "float", "r4", "il2cpp::il2cpp_class_by_name(\"System.Single\")"),
            MarshalArrayElementKind.R8 => ("double", 8, 8, "double", "r8", "il2cpp::il2cpp_class_by_name(\"System.Double\")"),
            _ => throw new NotSupportedException(
                $"struct '{owner}' field '{fieldName}': [MarshalAs(ByValArray)] element kind {kind} was not resolved -- reference/struct element arrays are out of scope"),
        };

    // Native (size, alignment) of one field kind on the TARGET pointer width.
    // Derived from the C++ member the emitter declares for the kind (see
    // EmitClassStruct): int32/float are 4-byte values, the 64-bit value kinds
    // and the pointer-width carriers (references, IntPtr/UIntPtr, void* fnptr
    // fields) are PointerSize. An unrecognized kind keeps the old pointer-wide
    // stride rather than guessing a narrower one.
    private static (int Size, int Align) FieldNativeShape(FieldTypeKind kind, int pointerSize)
        => kind switch
        {
            FieldTypeKind.Int32 => (4, 4),
            FieldTypeKind.Float => (4, 4),
            FieldTypeKind.Int64 => (8, 8),
            FieldTypeKind.UInt64 => (8, 8),
            FieldTypeKind.Double => (8, 8),
            FieldTypeKind.NativeInt => (pointerSize, pointerSize),
            // B3: net48 Guid alignment is 4 (Data1 int32 first); size 16.
            FieldTypeKind.Guid => (16, 4),
            // The runtime value structs keep their own size/alignment
            // (il2cpp_time.h / decimal.h / il2cpp_dtoffset.h).
            FieldTypeKind.DateTime => (16, 8),
            FieldTypeKind.TimeSpan => (8, 8),
            FieldTypeKind.Decimal => (16, 8),
            FieldTypeKind.DateTimeOffset => (16, 8),
            FieldTypeKind.Reference => (pointerSize, pointerSize),
            _ => (pointerSize, pointerSize),
        };

    //--------------------------------------------------------------------------
    // Embedded value-struct shapes (UserStruct / Nullable<T>).
    //
    // A closure display-class member of a translated user struct is the
    // generated C++ struct VALUE; a Nullable<T> member expands to the
    // underlying T value plus a hasValue flag. Both need the real native
    // (size, alignment, reference bitmap) of the VALUE, which is the layout
    // pass's own output -- resolved here from the model (never guessed from a
    // mangled name).
    //--------------------------------------------------------------------------
    private readonly record struct ValueShape(string CppType, int Size, int Align, uint RefBitmap);

    // The C++ member names of an expanded Nullable<T> field. Single authority
    // for layout/emission and the field-access sites in CodeGenerator.
    public static string NullableValueMemberName(string fieldCppName) => fieldCppName + "__value";
    public static string NullableHasValueMemberName(string fieldCppName) => fieldCppName + "__hasValue";
    public static string NullablePadMemberName(string fieldCppName) => fieldCppName + "__pad";

    // ceil(size / pointerSize) -- the pointer-slot span of a value.
    private static int SlotSpan(int size, int pointerSize) => (size + pointerSize - 1) / pointerSize;

    private static int RoundUpSlots(int slot, int alignSlots)
        => alignSlots <= 1 ? slot : (slot + alignSlots - 1) / alignSlots * alignSlots;

    // The padding member that makes an expanded Nullable<T> occupy its value
    // slots plus ONE whole pointer slot: the bool flag is 1 byte, the rest of
    // the slot is padding, so the following member's byte offset (and the GC
    // slot grid) stay exactly where the layout pass put them.
    public static int NullablePadBytes(int valueSize, int pointerSize)
        => SlotSpan(valueSize, pointerSize) * pointerSize + pointerSize - valueSize - 1;

    private static ValueShape ResolveFieldValueShape(FieldDef f, IReadOnlyList<TypeDef>? allTypes, int pointerSize)
    {
        if (f.TypeKind == FieldTypeKind.Nullable)
            return ResolveUnderlyingValueShape(f.NullableUnderlyingName, allTypes, pointerSize);
        if (f.TypeKind == FieldTypeKind.UserStruct)
        {
            var def = f.UserStructType
                      ?? TypeLookup.Find(allTypes ?? Array.Empty<TypeDef>(), f.FieldTypeName);
            if (def == null)
                throw new NotSupportedException(
                    $"user-struct field '{f.Name}': type '{f.FieldTypeName}' is not in the translated type set -- cannot resolve its value layout");
            LayoutInstanceFields(def, allTypes);
            f.UserStructType = def;
            return new ValueShape(TypeCppName(def), def.InstanceSize,
                Math.Max(1, def.InstanceAlignment), def.RefFieldBitmap);
        }
        return ShapeForKind(f.TypeKind, f.FieldTypeName, allTypes, pointerSize);
    }

    private static ValueShape ResolveUnderlyingValueShape(string name, IReadOnlyList<TypeDef>? allTypes, int pointerSize)
    {
        if (TypeKindResolver.TryGetValueStructFieldKind(name, out var valueKind))
        {
            var s = TypeKindResolver.ValueStructFieldShape(valueKind);
            return new ValueShape(s.CppType, s.SizeBytes, s.AlignBytes, 0);
        }
        var def = TypeLookup.Find(allTypes ?? Array.Empty<TypeDef>(), name);
        if (def != null && def.BaseTypeName == "System.Enum")
            return ShapeForKind(FieldTypeKind.Int32, name, allTypes, pointerSize);
        if (def is { IsValueType: true })
        {
            LayoutInstanceFields(def, allTypes);
            return new ValueShape(TypeCppName(def), def.InstanceSize,
                Math.Max(1, def.InstanceAlignment), def.RefFieldBitmap);
        }
        if (CodeGenerator.IsKnownBclEnumName(name))
            return ShapeForKind(FieldTypeKind.Int32, name, allTypes, pointerSize);
        var (kind, _, _) = TypeKindResolver.ResolveFromFullName(name);
        if (kind != FieldTypeKind.Reference || name is "System.String" or "System.Object")
            return ShapeForKind(kind, name, allTypes, pointerSize);
        throw new NotSupportedException(
            $"Nullable<{name}>: the underlying type has no value layout in the translated model");
    }

    private static ValueShape ShapeForKind(FieldTypeKind kind, string typeName,
        IReadOnlyList<TypeDef>? allTypes, int pointerSize)
    {
        if (TypeKindResolver.IsValueStructFieldKind(kind))
        {
            var s = TypeKindResolver.ValueStructFieldShape(kind);
            return new ValueShape(s.CppType, s.SizeBytes, s.AlignBytes, 0);
        }
        return kind switch
        {
            FieldTypeKind.Int32 => new ValueShape("int32_t", 4, 4, 0),
            FieldTypeKind.Int64 => new ValueShape("int64_t", 8, 8, 0),
            FieldTypeKind.UInt64 => new ValueShape("uint64_t", 8, 8, 0),
            FieldTypeKind.Float => new ValueShape("float", 4, 4, 0),
            FieldTypeKind.Double => new ValueShape("double", 8, 8, 0),
            // A `delegate*` field rides void*; a real IntPtr/UIntPtr intptr_t.
            FieldTypeKind.NativeInt => string.IsNullOrEmpty(typeName)
                ? new ValueShape("void*", pointerSize, pointerSize, 0)
                : new ValueShape("intptr_t", pointerSize, pointerSize, 0),
            FieldTypeKind.Reference => new ValueShape(
                ReferenceCppType(typeName, allTypes), pointerSize, pointerSize, 1u),
            _ => throw new NotSupportedException(
                $"field kind {kind} has no embedded value shape"),
        };
    }

    // Mark the GC references INSIDE an embedded value at their ABSOLUTE slot
    // in the containing object. The inner bitmap is relative to the value
    // start (bit i <=> byte offset i*pointerSize inside the value), which is
    // exactly what LayoutValueTypeFields records for a struct.
    private static void MarkEmbeddedReferences(TypeDef owner, FieldDef f, int baseSlot,
        uint innerBitmap, ref uint bitmap, ref int refCount)
    {
        if (innerBitmap == 0) return;
        for (var b = 0; b < 32; b++)
        {
            if ((innerBitmap & (1u << b)) == 0) continue;
            var absSlot = baseSlot + b;
            if (absSlot >= 32)
            {
                Console.Error.WriteLine($"warning: type {owner.FullName}: reference field '{f.Name}' lands on slot {absSlot} -- the 32-bit refFieldBitmap cannot represent slots >= 32 (audit H6), skipping field from bitmap");
                continue;
            }
            bitmap |= 1u << absSlot;
            ++refCount;
        }
    }

    // O-1 (RF-N4 audit A): the instance fields of a class in INLINE layout
    // order -- the base chain's fields (base-first, recursively) followed by
    // the type's own fields. The emitted C++ struct uses this list so its
    // member offsets match the slot grid that LayoutInstanceFields assigned
    // (a derived class's slots continue from the base chain). Value types have
    // no user inheritance (only System.ValueType/System.Enum BCL roots, zero
    // fields), so the base pass is skipped and the list is just the own fields
    // -- the historical behavior.
    public static IEnumerable<FieldDef> InlineLayoutFields(TypeDef t)
    {
        if (!t.IsValueType && t.BaseType != null && t.BaseTypeName != "System.Object")
        {
            foreach (var f in InlineLayoutFields(t.BaseType))
                yield return f;
        }
        foreach (var f in t.InstanceFields)
            yield return f;
    }

    //--------------------------------------------------------------------------
    // EmitClassStruct -- the generated C++ struct for one translated class.
    //--------------------------------------------------------------------------
    public static string EmitClassStruct(TypeDef t, IReadOnlyList<TypeDef> allTypes)
    {
        LayoutInstanceFields(t, allTypes);
        var w = new CppWriter();
        w.Line("//------------------------------------------------------------------------------");
        w.Line($"// {t.FullName} -- translated class layout.");
        w.Line($"// The object header (Il2CppObject, a klass pointer) is the FIRST member so an");
        w.Line($"// Il2CppObject* aliases this struct; reference fields are GC pointers managed by");
        w.Line($"// the mark-sweep collector (zero-initialized at allocation).");
        // GN-20.4 F1: a value type is laid out sequentially (compact byte
        // offsets -- what the native side of a P/Invoke out/ref parameter
        // uses), a reference type keeps the whole-pointer slot grid the
        // collector scans.
        w.Line(t.IsValueType
            ? $"// Sequential value layout (net48): compact byte offsets; refFieldBitmap = 0b{Convert.ToString(t.RefFieldBitmap, 2).PadLeft(Math.Max(t.InstanceSize, 1) / t.PointerSize, '0')}."
            : $"// Slot grid: slot i at byte offset i*{t.PointerSize}; refFieldBitmap = 0b{Convert.ToString(t.RefFieldBitmap, 2).PadLeft(t.InstanceSize / t.PointerSize, '0')}.");
        w.Line("//------------------------------------------------------------------------------");
        w.Line($"struct {TypeCppName(t)}");
        w.Line("{");
        w.Open();
        if (t.IsValueType)
        {
            // M3 Task 11: value type -- NO object header. Instances are plain
            // C++ values (local variables hold them inline).
            w.Line("// value type: no object header (instances are plain C++ values)");
        }
        else
        {
            w.Line("Il2CppObject _obj; // object header: runtime type descriptor pointer (offset 0)");
        }
        // O-1 (RF-N4 audit A): a DERIVED class's slot grid continues from its
        // base chain (LayoutInstanceFields starts the own slots at
        // base.InstanceSize / pointerSize). The C++ struct must therefore
        // embed the base chain's instance fields INLINE, base-first, so the
        // member byte offsets match the slot offsets the GC refFieldBitmap
        // scans -- otherwise a derived class's OWN reference field would land
        // right after _obj (offset ptr) while the collector scans its slot
        // (offset base.InstanceSize), reading the wrong slot / a neighbour.
        // Value types have no user inheritance, so they skip the base pass.
        //
        // Name collision guard: the inline BASE-chain copies are LAYOUT-ONLY
        // (field access always casts to the DECLARING owner's struct -- a
        // base field is read through the base struct, never through the
        // derived one -- see CodeGenerator.FieldAccess), so a base copy whose
        // canonical name is already taken (a derived `new`-hidden field, e.g.
        // reflprop2's PropBase2.BaseProp vs PropDerived2.BaseProp) is renamed
        // with its slot index. Own fields KEEP their canonical name (access
        // sites emit them by name).
        var inlineFields = InlineLayoutFields(t).ToList();
        var ownNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in t.InstanceFields)
        {
            ownNames.Add(Sanitize(f.Name) == "_obj" ? "_obj_field" : Sanitize(f.Name));
        }
        var usedNames = new HashSet<string>(ownNames, StringComparer.Ordinal);
        foreach (var f in inlineFields)
        {
            var canonical = Sanitize(f.Name) == "_obj" ? "_obj_field" : Sanitize(f.Name);
            var isOwn = ReferenceEquals(f.Owner, t);
            var memberName = canonical;
            if (!isOwn && !usedNames.Add(canonical))
            {
                // Collision with an own field or a sibling base copy -- the
                // base copy is layout padding, name it uniquely by slot.
                memberName = $"{canonical}_slot{f.SlotIndex}";
                usedNames.Add(memberName);
            }
            else
            {
                usedNames.Add(canonical);
            }
            // P/Invoke fixed-buffer field: the INLINE native array net48
            // marshals (ByValTStr -> WCHAR[SizeConst]). No GC reference lives
            // here, so the layout pass excluded it from refFieldBitmap; reads
            // and stores are buffer copies (CodeGenerator route).
            if (f.MarshalKind == FieldMarshalKind.ByValTStr)
            {
                w.Line($"char16_t {memberName}[{f.MarshalSizeConst}]; // slot {f.SlotIndex} at offset {f.ByteOffset} (ByValTStr: inline UTF-16 buffer, {f.MarshalSizeConst} units, no GC reference)");
                continue;
            }
            // ByValArray: the INLINE native array net48 marshals (T[SizeConst]).
            // BOOLEAN elements are 4-byte BOOLs, so the declaration is int32_t
            // even though the managed array cell is a 1-byte bool.
            if (f.MarshalKind == FieldMarshalKind.ByValArray)
            {
                var elem = DescribeMarshalArray(f.MarshalArrayElement, t.FullName, f.Name);
                w.Line($"{elem.NativeCpp} {memberName}[{f.MarshalSizeConst}]; // slot {f.SlotIndex} at offset {f.ByteOffset} (ByValArray: inline {elem.NativeCpp}[{f.MarshalSizeConst}], no GC reference)");
                continue;
            }
            // A captured Nullable<T> expands to the underlying T value slot
            // plus a hasValue(bool) slot (a whole pointer slot, padded), so
            // the member offsets match the layout pass exactly.
            if (f.TypeKind == FieldTypeKind.Nullable)
            {
                var shape = ResolveFieldValueShape(f, allTypes, t.PointerSize);
                var valueMember = NullableValueMemberName(memberName);
                var hasMember = NullableHasValueMemberName(memberName);
                var padMember = NullablePadMemberName(memberName);
                w.Line($"{shape.CppType} {valueMember}; // slot {f.SlotIndex} at offset {f.ByteOffset} (Nullable<{f.NullableUnderlyingName}> value)");
                w.Line($"bool {hasMember}; // offset {f.NullableHasValueOffset} (Nullable hasValue)");
                w.Line($"uint8_t {padMember}[{NullablePadBytes(shape.Size, t.PointerSize)}]; // pad to the next pointer slot");
                continue;
            }
            // A captured USER STRUCT is the generated struct VALUE: its real
            // natural alignment / size / inner GC references are the layout
            // pass's output.
            if (f.TypeKind == FieldTypeKind.UserStruct)
            {
                var shape = ResolveFieldValueShape(f, allTypes, t.PointerSize);
                w.Line($"{shape.CppType} {memberName}; // slot {f.SlotIndex} at offset {f.ByteOffset} (user struct value{(shape.RefBitmap != 0 ? ", inner GC references marked in the owner bitmap" : "")})");
                continue;
            }
            var cppType = f.TypeKind switch
            {
                FieldTypeKind.Int32 => "int32_t",
                // nb9 F5: real widths for wide primitive instance fields.
                FieldTypeKind.Int64 => "int64_t",
                FieldTypeKind.UInt64 => "uint64_t",
                FieldTypeKind.Float => "float",
                FieldTypeKind.Double => "double",
                // wfm1 (winforms1): IntPtr/UIntPtr struct fields emit as the
                // full-width intptr_t carrier -- the native pointer type.
                // GN-18 B1 (fnptr fields): a `delegate*` (0x1B) field carries a
                // void* value (FuncPtrExpr -> reinterpret_cast<void*>(&fn), the
                // "p" slot). An intptr_t member would make the stfld store
                // (void* value) and the ldfld load (into a void* local) C++
                // type errors. Reuse the NativeInt KIND (a non-reference value
                // slot; the refFieldBitmap marks only Reference fields, so the
                // GC never scans it) but distinguish from a real IntPtr by the
                // EMPTY FieldTypeName (IntPtr fields carry "System.IntPtr").
                FieldTypeKind.NativeInt when string.IsNullOrEmpty(f.FieldTypeName) => "void*",
                FieldTypeKind.NativeInt => "intptr_t",
                // B3: System.Guid is the 16-byte guid.h value struct.
                FieldTypeKind.Guid => "il2cpp::Il2CppGuid",
                // The runtime value structs (a closure-captured DateTime and
                // friends): the member IS the runtime struct, so the slot
                // holds the whole value (never an int32 truncation).
                FieldTypeKind.DateTime => "il2cpp::runtime::Il2CppDateTime",
                FieldTypeKind.TimeSpan => "il2cpp::runtime::Il2CppTimeSpan",
                FieldTypeKind.Decimal => "il2cpp::runtime::Il2CppDecimal",
                FieldTypeKind.DateTimeOffset => "il2cpp::runtime::Il2CppDateTimeOffset",
                FieldTypeKind.Reference => ReferenceCppType(f.FieldTypeName, allTypes),
                _ => throw new NotSupportedException($"field kind {f.TypeKind} not supported in M2"),
            };
            // _obj guard: the object header member is named _obj; a metadata
            // field that sanitizes to _obj (e.g. SerializationMonkey._obj)
            // would collide -- rename the member (access sites use FieldCppName
            // uniformly, see below).
            w.Line($"{cppType} {memberName}; // slot {f.SlotIndex} at offset {f.ByteOffset} ({(f.TypeKind == FieldTypeKind.Reference ? "GC reference" : "value")})");
        }
        // enumdeep/enumdeep2/enumops (M7R2 cmake build): an ENUM type is
        // translated as a struct whose only instance field is value__ (the
        // underlying int32 backing slot), but the IR represents every enum
        // value as a raw int64 constant (`loc0 = 2LL`, `loc0 == 2LL`,
        // `loc2 | 4LL`). Without conversions the generated C++ does not
        // compile ("no viable overloaded '='", "invalid operands"). The
        // implicit int32 conversion + ctor make the enum struct behave like
        // its underlying int exactly where the IR needs it (assignments,
        // comparisons, arithmetic/bitwise). _obj zero-init keeps the object
        // header valid for any box-style aliasing.
        if (t.BaseTypeName == "System.Enum")
        {
            w.Line();
            w.Line($"// enum value: implicit conversion to/from the underlying int32 backing");
            w.Line($"{TypeCppName(t)}() = default;");
            w.Line($"{TypeCppName(t)}(int32_t v) : _obj{{}}, value__{{v}} {{}}");
            w.Line("operator int32_t() const { return value__; }");
        }
        w.Close();
        w.Line("};");
        w.Line();
        return w.ToString();
    }

    // Backward-compatible overload (unit-test call sites pass only the type;
    // without the all-types list a reference field of a custom delegate type
    // falls back to its Sanitize name -- the tests build the model without a
    // surrounding type list).
    public static string EmitClassStruct(TypeDef t) => EmitClassStruct(t, null!);

    // C++ type for a reference field: concrete generated struct for user types,
    // Il2CppObject* for System.Object / System.String (M2 has no string type yet)
    // and for CUSTOM DELEGATE types (wf5: a delegate TypeDef is expanded as a
    // top-level entry but has NO generated struct -- the runtime stamps every
    // delegate with the single System.Delegate class, so a field of a delegate
    // type is a plain GC reference slot, exactly like a System.Object field).
    // PUBLIC: the FieldAssignStmt emitter casts the stored value to this SAME
    // type (single authority for the field's C++ storage type -- the gccompact
    // regression came from the two sides drifting apart).
    public static string ReferenceCppType(string fullName, IReadOnlyList<TypeDef>? allTypes) => fullName switch
    {
        "System.Object" => "Il2CppObject*",
        "System.String" => "Il2CppObject*",
        // D-GC-1 (newarrdeep): an ARRAY-typed reference field -- the member is
        // a GC pointer to the array OBJECT (the uniform reference shape, same
        // as System.Object). The D-GC-2 element classification (base type from
        // metadata) decides the array's scan stride at ALLOCATION time; the
        // field slot itself is always one GC pointer.
        "System.Array" => "Il2CppObject*",
        // batch 156c (asynccase): the async state machine's GENERICINST
        // fields (AsyncTaskMethodBuilder`1<T> / TaskAwaiter`1<T> inside the
        // <F>d__1 struct) -- the minimal surface models both as a single
        // Il2CppObject* slot (the builder object / the task-as-awaiter).
        // The generic instantiation name has no generated C++ struct.
        _ when fullName.StartsWith("System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1", StringComparison.Ordinal)
            || fullName.StartsWith("System.Runtime.CompilerServices.TaskAwaiter`1", StringComparison.Ordinal) => "Il2CppObject*",
        // batch 162 (eventcase): BCL DELEGATE fields (System.EventHandler --
        // the event backing field) are runtime Il2CppDelegate objects; the
        // concrete generated C++ type does not exist.
        _ when fullName == "System.EventHandler" || fullName == "System.EventHandler`1" => "Il2CppObject*",
        _ when allTypes != null && allTypes.Any(x => TypeLookup.Matches(x, fullName) && IsDelegateType(x)) => "Il2CppObject*",
        // COM stage 17 batch 3 (com17type): an INTERFACE-typed reference field
        // (DragEventArgs.Data : IDataObject). Interfaces have NO generated C++
        // struct (CodeGenerator emits only the extern Il2CppClass descriptor
        // -- instanceSize 0, never allocated), so the member is a plain GC
        // reference slot, exactly like a System.Object field; the concrete
        // implementer's object is what the slot holds at runtime.
        _ when allTypes != null && allTypes.Any(x => TypeLookup.Matches(x, fullName) && x.IsInterface) => "Il2CppObject*",
        // M6 Nullable<T>: a nullable field of any T has no generated C++ struct --
        // the runtime represents it as an Il2CppObject* (null for HasValue=false,
        // boxed T for HasValue=true). Map to Il2CppObject* like other BCL structs.
        _ when fullName.StartsWith("System.Nullable`1", StringComparison.Ordinal) => "Il2CppObject*",
        // r78 KeyValuePair<K,V>: a KeyValuePair<K,V> reference field has no generated
        // C++ struct -- the runtime models every pair as the Il2CppDictKvpSI holder
        // OBJECT (get_Key/get_Value are separate icalls -- no 16-byte pair struct).
        // Map to Il2CppObject* so fields/current-slots declare a GC ref, not a
        // {Sanitize(name)}* pointer to an undefined type.
        _ when fullName.StartsWith("System.Collections.Generic.KeyValuePair`2", StringComparison.Ordinal) => "Il2CppObject*",
        // r78 generic instantiations of BCL interfaces (IEnumerator`1<T>, IEnumerable`1<T>,
        // ICollection`1<T>) and generic classes (ConcurrentSet`2<K,V>) that have no generated
        // C++ struct. The runtime stamps these with the single System.Object / System.Array
        // class, so a field/slot of such a type is a plain GC reference, exactly like a
        // System.Object field. Without this, the fallback emits {Sanitize(name)}* pointing
        // to an undefined type -> cmake build FAIL.
        _ when (fullName.StartsWith("System.Collections.Generic.IEnumerator`1<", StringComparison.Ordinal)
            || fullName.StartsWith("System.Collections.Generic.IEnumerable`1<", StringComparison.Ordinal)
            || fullName.StartsWith("System.Collections.Generic.ICollection`1<", StringComparison.Ordinal)
            || fullName.StartsWith("System.Collections.Concurrent.ConcurrentSet`2<", StringComparison.Ordinal)
            || fullName.StartsWith("System.Collections.Generic.IDictionary`2<", StringComparison.Ordinal)
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyDictionary`2<", StringComparison.Ordinal)
            || fullName.StartsWith("System.Collections.Generic.ISet`1<", StringComparison.Ordinal)) => "Il2CppObject*",
        // r78 fix: generic instantiations of BCL types (e.g. ReadOnlyCollection`1<Exception>,
        // Dictionary`2<String,Object>, Func`1<Int32>) have no generated C++ struct.
        // Map to Il2CppObject* like other BCL reference types. Without this, the
        // fallback emits {Sanitize(fullName)}* pointing to an undefined type -> cmake FAIL.
        _ when fullName.Contains('`') || fullName.Contains('<') => "Il2CppObject*",
        // r78 fix: any other BCL type not in the types list has no generated C++ struct.
        // Map to Il2CppObject* (the runtime stamps these with System.Object/System.Array).
        // Types in the types list get their own struct definition via EmitClassStruct.
        _ when allTypes == null || !allTypes.Any(x => TypeLookup.Matches(x, fullName)) => "Il2CppObject*",
        _ => $"{Sanitize(fullName)}*",
    };

    //--------------------------------------------------------------------------
    // EmitClassDescriptor -- the Il2CppClass table for the runtime.
    //
    // Non-static (external linkage): entry.cpp references the descriptor by
    // extern declaration (generated.h); internal linkage would make the
    // extern declaration a different symbol. The vtable pointer/size are
    // filled by CodeGenerator (it owns the method-name hashing); this method
    // emits the descriptor WITHOUT vtable wiring, and
    // EmitVTable(WireVTable) pairs the static table + descriptor fields.
    //--------------------------------------------------------------------------
    public static string EmitClassDescriptor(TypeDef t, string? vtableArrayName = null, string? vtableName = null, int vtableSize = 0,
        string? ifaceMapArrayName = null, int ifaceCount = 0)
    {
        LayoutInstanceFields(t);
        var w = new CppWriter();
        w.Line($"// Runtime type descriptor for {t.FullName} (registered with the runtime by entry.cpp).");
        // Interface types have no instances (instanceSize=0, never allocated); all other
        // types = the LayoutInstanceFields size (a DERIVED class inherits its base
        // slots -- the generated accessors cast to the base struct, so sizeof(struct)
        // alone undercounts a derived type that adds no own fields; rf-props exposed
        // the miss: instanceSize=8 let the next allocation land inside the instance).
        var sizeExpr = t.IsInterface ? "0" : t.InstanceSize.ToString();
        w.Line($"Il2CppClass {TypeCppName(t)}_class = {{");
        w.Line($"    \"{ReflectionFullName(t)}\",                                        // name");
        w.Line($"    {sizeExpr},                                            // instanceSize");
        w.Line($"    0b{Convert.ToString(t.RefFieldBitmap, 2).PadLeft(Math.Max(t.InstanceSize, 1) / t.PointerSize, '0')}u,          // refFieldBitmap: bit i <=> slot i is a GC reference");
        w.Line($"    {t.RefFieldCount},                                        // refFieldCount");
        // Audit H7: wire the resolved user base class -- il2cpp_is_instance
        // walks baseClass and il2cpp_interface_dispatch scans the base chain
        // for inherited interfaces. System.Object roots stay nullptr.
        // P25b (user struct box): a user value type's descriptor baseClass is
        // &System_ValueType_class (the runtime builtin) so il2cpp_is_instance
        // answers `boxedPoint is object` / `is ValueType` (the base chain
        // walks {T}_class -> System.ValueType -> System.Object) and
        // il2cpp_unbox_user_struct's ValueType-chain check accepts the box.
        // User classes keep their resolved user base; enums (BaseTypeName
        // System.Enum, not IsValueType) keep nullptr -- unchanged.
        var baseClassRef = t.IsValueType
            ? "&System_ValueType_class"
            : t.BaseType != null
                ? $"&{TypeCppName(t.BaseType)}_class"
                : "nullptr";
        w.Line($"    {baseClassRef},                                                // baseClass ({(t.IsValueType ? "System.ValueType (P25b box base)" : t.BaseType != null ? "user inheritance: " + t.BaseType.FullName : "null (System.Object root)")})");
        w.Line(vtableArrayName == null
            ? $"    nullptr,                                                // vtable (no virtual methods in this type)"
            : $"    reinterpret_cast<void**>({vtableArrayName}),            // vtable: {vtableSize} virtual slot(s)");
        w.Line($"    {vtableSize},                                           // vtableSize");
        w.Line(ifaceMapArrayName == null
            ? $"    nullptr,                                                // interfaces (no implemented interfaces)"
            : $"    {ifaceMapArrayName},                                     // interfaces: {ifaceCount} map entr(ies)");
        w.Line($"    {ifaceCount},                                           // interfaceCount");
        // net48 default GetHashCode field model (user VALUE types only; every
        // other descriptor leaves the four trailing members value-initialized
        // to 0 by the aggregate initializer -- no other descriptor edits
        // needed). See il2cpp_metadata.h Il2CppClass.vtFields.
        if (t.IsValueType && !t.IsInterface && t.InstanceFields.Count > 0)
        {
            var vt = ComputeVtHashModel(t);
            if (vt is { } model)
            {
                w.Line($"    {TypeCppName(t)}_vtfields,                                 // vtFields (net48 default-hash field model)");
                w.Line($"    {model.Fields.Count},                                     // vtFieldCount");
                w.Line($"    {model.PackedSize},                                     // vtPackedSize (net48 natural layout)");
                w.Line($"    {(model.FastHashable ? 1 : 0)},                                     // vtFastHashable (tightly packed, no GC refs)");
            }
            else
            {
                w.Line("    nullptr,                                                // vtFields (no default-hash model)");
                w.Line("    0,                                                      // vtFieldCount");
                w.Line("    0,                                                      // vtPackedSize");
                w.Line("    0,                                                      // vtFastHashable");
            }
        }
        else
        {
            w.Line("    nullptr,                                                // vtFields (not a user value type)");
            w.Line("    0,                                                      // vtFieldCount");
            w.Line("    0,                                                      // vtPackedSize");
            w.Line("    0,                                                      // vtFastHashable");
        }
        w.Line($"}};");
        w.Line();
        return w.ToString();
    }

    //--------------------------------------------------------------------------
    // The net48 default-hash field model of one user value type.
    //
    // The generated C++ struct places a `short`/`byte` field in a whole int32_t
    // slot (the emitter widens narrow primitives), while net48 packs them at
    // their true width. The default GetHashCode hashes the NET48 image, so each
    // field carries BOTH offsets: cppOffset (where the generated struct keeps
    // it) and netOffset (where net48 places it).
    //
    // Returns null when a field has no faithful net48 width (the runtime then
    // has no default-hash model for this type and the caller falls back).
    //--------------------------------------------------------------------------
    private readonly record struct VtFieldModel(int CppOffset, int NetOffset, int Size, int Kind);
    private readonly record struct VtHashModel(List<VtFieldModel> Fields, int PackedSize, bool FastHashable);

    //--------------------------------------------------------------------------
    // EmitVtFieldTable -- the net48 default-hash field table of one user value
    // type (empty output for every other type). Must precede the descriptor
    // that references it.
    //--------------------------------------------------------------------------
    public static string EmitVtFieldTable(TypeDef t)
    {
        if (!t.IsValueType || t.IsInterface || t.InstanceFields.Count == 0) return "";
        var model = ComputeVtHashModel(t);
        if (model is not { } m) return "";
        var w = new CppWriter();
        w.Line($"// net48 default-GetHashCode field model for {t.FullName} (packed {m.PackedSize}B, "
               + $"{(m.FastHashable ? "fast path" : "regular path")}).");
        w.Line($"const Il2CppVtField {TypeCppName(t)}_vtfields[] = {{");
        w.Open();
        foreach (var f in m.Fields)
            w.Line($"{{ {f.CppOffset}, {f.NetOffset}, {f.Size}, {f.Kind} }},");
        w.Close();
        w.Line("};");
        w.Line();
        return w.ToString();
    }

    private static VtHashModel? ComputeVtHashModel(TypeDef t)
    {
        LayoutInstanceFields(t);
        var fields = new List<VtFieldModel>();
        int netOffset = 0;
        int maxAlign = 1;
        bool fast = true;
        foreach (var f in t.InstanceFields)
        {
            // [MarshalAs] fixed-buffer fields have native inline semantics
            // (a WCHAR buffer) and no net48 default-hash model here -- keep
            // the type out of the table (the caller falls back) instead of
            // hashing the bytes as if they were a GC pointer.
            if (f.MarshalKind != FieldMarshalKind.None) return null;
            int size, align, kind;
            switch (f.TypeKind)
            {
                // Narrow primitives: net48's TRUE width (the generated struct
                // widens them to an int32 slot). Reported as the integral kind.
                case FieldTypeKind.Int32 when f.FieldTypeName is "System.Byte" or "System.SByte":
                    (size, align, kind) = (1, 1, 1); break;
                case FieldTypeKind.Int32 when f.FieldTypeName is "System.Int16" or "System.UInt16":
                    (size, align, kind) = (2, 2, 1); break;
                case FieldTypeKind.Int32:
                    (size, align, kind) = (4, 4, 1); break;
                // 8-byte integer family.
                case FieldTypeKind.Int64:
                case FieldTypeKind.UInt64:
                case FieldTypeKind.TimeSpan:
                    (size, align, kind) = (8, 8, 3); break;
                case FieldTypeKind.Double:
                    (size, align, kind) = (8, 8, 4); break;
                case FieldTypeKind.Float:
                    (size, align, kind) = (4, 4, 5); break;
                case FieldTypeKind.NativeInt:
                    (size, align, kind) = (t.PointerSize, t.PointerSize, 1); break;
                case FieldTypeKind.Reference:
                    // A GC reference: net48 uses the REGULAR path for any struct
                    // holding one, so the fast flag drops.
                    (size, align, kind) = (t.PointerSize, t.PointerSize, 2); break;
                case FieldTypeKind.Guid:
                    (size, align, kind) = (16, 4, 6); break;
                case FieldTypeKind.Decimal:
                case FieldTypeKind.DateTime:
                case FieldTypeKind.DateTimeOffset:
                case FieldTypeKind.UserStruct:
                case FieldTypeKind.Nullable:
                    // Composite value structs hash through their own default
                    // path in net48; the model keeps them out of the fast path
                    // and reports an opaque 8-byte-aligned block.
                    (size, align, kind) = (f.TypeKind == FieldTypeKind.UserStruct
                        ? Math.Max(1, f.UserStructType?.InstanceSize ?? 16) : 16, 8, 6); break;
                default:
                    return null;
            }
            netOffset = (netOffset + align - 1) / align * align;
            fields.Add(new VtFieldModel(f.ByteOffset, netOffset, size, kind));
            if (align > maxAlign) maxAlign = align;
            if (f.TypeKind == FieldTypeKind.Reference || kind == 6) fast = false;
            netOffset += size;
        }
        int packedSize = (netOffset + maxAlign - 1) / maxAlign * maxAlign;
        // The fast-path gate is "no padding anywhere": the packed size must be
        // exactly the sum of the field widths PLUS the interior alignment gaps
        // the natural layout introduced -- i.e. no alignment was needed because
        // every field already sat at its offset. A simple sufficient test: the
        // densely packed sum equals the natural extent.
        int dense = 0;
        int natural = 0;
        int maxAl = 1;
        foreach (var fm in fields)
        {
            natural = (natural + 1 - 1); // no-op placeholder (kept explicit)
            int a = Math.Min(fm.Size, 8);
            if (a < 1) a = 1;
            natural = (natural + a - 1) / a * a;
            if (natural != fm.NetOffset) fast = false;
            natural += fm.Size;
            dense += fm.Size;
            if (a > maxAl) maxAl = a;
        }
        if (packedSize != dense) fast = false;
        if (packedSize > 512) fast = false; // runtime scratch bound (kMaxVtPackedSize)
        return new VtHashModel(fields, packedSize, fast);
    }

    //--------------------------------------------------------------------------
    // EmitInterfaceMaps -- per-type interface dispatch tables (Task 8).
    //
    //   static int32_t {Type}_iface_{Iface}_map[] = { slot, slot, ... };
    //   static InterfaceMapEntry {Type}_interfaces[] = {
    //       { &{Iface}_class, {Type}_iface_{Iface}_map }, ...
    //   };
    // The maps are emitted in the type's .cpp; the descriptor references the
    // entry array. Each map: interface slot i -> this type's vtable slot.
    //--------------------------------------------------------------------------
    public static string EmitInterfaceMaps(TypeDef t)
    {
        if (t.InterfaceSlotMaps.Count == 0) return "";
        var w = new CppWriter();
        w.Line($"// Interface dispatch maps for {t.FullName} ({t.InterfaceSlotMaps.Count} interface(s)).");
        foreach (var (iface, slotMap) in t.InterfaceSlotMaps)
        {
            var mapName = $"{TypeCppName(t)}_iface_{TypeCppName(iface)}_map";
            w.Line($"// Interface slot -> vtable slot for {iface.FullName}.");
            // C2466 guard (boxstr/boxdeep, msvc): a MARKER interface with ZERO
            // methods yields an empty slot map -- `int32_t m[] = {}` is a
            // zero-size array the C++ standard forbids (g++/clang tolerate as
            // an extension, MSVC rejects: C2466). The runtime never reads the
            // map for a 0-slot interface; emit a single dummy slot.
            if (slotMap.Length == 0)
            {
                w.Line($"int32_t {mapName}[] = {{ 0 }}; // empty marker interface (no slots)");
                w.Line();
                continue;
            }
            w.Line($"int32_t {mapName}[] = {{");
            w.Open();
            for (var i = 0; i < slotMap.Length; i++)
                w.Line($"{slotMap[i]}, // interface slot {i} -> vtable slot {slotMap[i]}");
            w.Close();
            w.Line($"}};");
            w.Line();
        }
        w.Line($"InterfaceMapEntry {TypeCppName(t)}_interfaces[] = {{");
        w.Open();
        foreach (var (iface, _) in t.InterfaceSlotMaps)
        {
            w.Line($"{{ &{TypeCppName(iface)}_class, {TypeCppName(t)}_iface_{TypeCppName(iface)}_map }},");
        }
        w.Close();
        w.Line($"}};");
        w.Line();
        return w.ToString();
    }

    //--------------------------------------------------------------------------
    // EmitVTable -- the static virtual-slot table for one type.
    //
    //   static void* {Type}_vtable[] = {
    //       (void*)&Cases_Rect_Area_<hash>,   // slot 0: virtual dispatch target
    //       ...
    //   };
    // Each entry is the C++ name of the effective virtual method at that slot
    // (base-inherited or overridden -- VTablePlanner already resolved the
    // derived override into the slot). slotMethodNames must have exactly
    // t.VirtualMethods.Count entries. The vtable array lives in the type's
    // .cpp (single definition); the descriptor references it.
    //--------------------------------------------------------------------------
    public static string EmitVTable(TypeDef t, IReadOnlyList<string?> slotMethodNames)
    {
        var w = new CppWriter();
        w.Line($"// Virtual dispatch table for {t.FullName}: {t.VirtualMethods.Count} slot(s) in base-first order.");
        w.Line($"void* {TypeCppName(t)}_vtable[] = {{");
        w.Open();
        for (var i = 0; i < t.VirtualMethods.Count; i++)
        {
            var m = t.VirtualMethods[i];
            // An ABSTRACT slot has no generated function body (abstract methods
            // have RVA=0 and are skipped at emission), so taking its address
            // would reference an undefined symbol. The declaring type is
            // abstract and can never be instantiated, so the slot is
            // unreachable; nullptr keeps the table well-formed and the slot
            // layout intact (a derived type replaces it with its override).
            var entry = slotMethodNames[i] == null
                ? "nullptr"
                : $"reinterpret_cast<void*>(&{slotMethodNames[i]})";
            w.Line($"{entry}, // slot {i}: {m.Name}({m.ParamTypes}){(m.Owner == t ? "" : " (inherited from " + m.Owner.FullName + ")")}");
        }
        w.Close();
        w.Line($"}};");
        w.Line();
        return w.ToString();
    }

    public static string EmitStaticStorage(TypeDef t, MetadataReader? md = null)
    {
        var w = new CppWriter();
        w.Line("// static field storage for " + t.FullName);
        foreach (var f in t.StaticFields.Where(f => f.IsStatic && !f.IsLiteral))
        {
            var name = FieldCppName(t, f.Name);
            var (semantic, _) = StaticFieldSemantics(f, md);
            switch (semantic)
            {
                case StaticFieldSemantic.Reference:
                    // Delegate / object / string / array reference fields: a
                    // GC pointer slot. The IR's uniform reference shape is
                    // Il2CppObject* -- delegate storage is passed to
                    // icall_delegate_combine/remove (Il2CppObject* parameters,
                    // il2cpp_delegate_ext.h:99/105) and dispatched by
                    // icall_multicast_invoke_i4 (Il2CppObject* list), so the
                    // declaration MUST be Il2CppObject*, not int32_t
                    // (delechain cmake FAIL root cause). Non-literal static
                    // reference fields never carry a metadata ConstantValue
                    // (their initializers run in .cctor), so null-init is the
                    // correct zero image.
                    w.Line($"Il2CppObject* {name} = nullptr; // GC reference (delegate / object)");
                    break;
                case StaticFieldSemantic.Decimal:
                    // Static System.Decimal field: a 16-byte Il2CppDecimal
                    // aggregate (decimal.h, transitively included via
                    // il2cpp_boxing_ext2.h -- same type as the BCL Zero/One
                    // storage below). Zero-init; the .cctor performs the value
                    // assignment (decimal stsfld writes the 16-byte slot).
                    w.Line($"il2cpp::runtime::Il2CppDecimal {name} = {{ 0, 0, 0 }}; // 16-byte decimal value");
                    break;
                case StaticFieldSemantic.IntPtr:
                    // wf16 (winforms1): native-int static field -- full-width
                    // intptr_t pointer carrier (a 64-bit WNDPROC/HWND must NOT
                    // fit an int32_t slot). Zero-init; .cctor assigns the value.
                    w.Line($"intptr_t {name} = 0; // native-int pointer carrier");
                    break;
                case StaticFieldSemantic.UIntPtr:
                    w.Line($"uintptr_t {name} = 0; // native unsigned pointer carrier");
                    break;
                // GN-18 B1 (fnptr fields): a static fnptr field stores a void*
                // pointer (the raw fnptr value); int32_t storage would truncate
                // the x64 address. Zero-init (nullptr) matches the non-literal
                // static default; the .cctor assigns the real function pointer.
                // StaticFieldCppType names the same void* so the generated.h
                // extern and this DEFINITION are type-identical.
                case StaticFieldSemantic.FnPtr:
                    w.Line($"void* {name} = nullptr; // fnptr pointer carrier");
                    break;
                case StaticFieldSemantic.Int64:
                    w.Line($"int64_t {name} = 0; // 64-bit signed value");
                    break;
                case StaticFieldSemantic.UInt64:
                    w.Line($"uint64_t {name} = 0; // 64-bit unsigned value");
                    break;
                // probe-sfieldfloat: static float/double fields store a REAL
                // floating value (the historical int32_t slot truncated 3.5 to
                // 3 at every store and load). Same member types the INSTANCE
                // field path emits for FieldTypeKind.Float/Double, so a static
                // read/write is width-identical to an instance one. The
                // initializer image is the metadata ConstantValue when the
                // field carries one (otherwise 0; the .cctor assigns).
                case StaticFieldSemantic.Float:
                    w.Line($"float {name} = {f.ConstantValue ?? "0"}; // 32-bit floating value");
                    break;
                case StaticFieldSemantic.Double:
                    w.Line($"double {name} = {f.ConstantValue ?? "0"}; // 64-bit floating value");
                    break;
                // DateTime/TimeSpan static fields are the 8-byte runtime value
                // structs (StaticFieldCppType names the same C++ type for the
                // generated.h extern). int32_t storage truncated the ticks and
                // made the extern/definition pair type-conflict. Zero-init; the
                // .cctor assigns the value.
                case StaticFieldSemantic.DateTime:
                    w.Line($"il2cpp::runtime::Il2CppDateTime {name} = il2cpp::runtime::Il2CppDateTime(0); // 8-byte DateTime value");
                    break;
                case StaticFieldSemantic.TimeSpan:
                    w.Line($"il2cpp::runtime::Il2CppTimeSpan {name} = il2cpp::runtime::Il2CppTimeSpan(0); // 8-byte TimeSpan value");
                    break;
                default:
                    w.Line($"int32_t {name} = {f.ConstantValue ?? "0"};");
                    break;
            }
        }
        // decimal-domain (blocker B): System.Decimal::Zero / One are BCL
        // static fields -- mscorlib types have no TypeDef, so the loop above
        // never emits their storage, yet Roslyn's decimal-literal lowering
        // (`decimal one = 1m` -> ldsfld System.Decimal::One) renders the
        // reference System_Decimal_sf_One (FieldStorageName). Without a
        // definition the generated .cpp fails with "use of undeclared
        // identifier"; AND the generic int32_t storage above would be a type
        // mismatch (a decimal is a 16-byte Il2CppDecimal, not int32_t).
        //
        // Storage: a 16-byte il2cpp::runtime::Il2CppDecimal aggregate
        // (decimal.h -- transitively included via il2cpp_boxing_ext2.h).
        // Zero = {flags:0, hi32:0, lo64:0}; One = {flags:0, hi32:0, lo64:1}
        // (mantissa 1, scale 0 -- Decimal.One's exact image).
        //
        // C++17 `inline` variable: every user .cpp that emits static storage
        // also emits the SAME inline definition, and the linker merges them
        // into ONE object (ODR-safe, value-identical constants). This is
        // stateless (no once-flag), warning-free (inline variables are never
        // "unused" at -Wall), and multi-TU safe (a reference in ANY generated
        // .cpp resolves without a generated.h extern declaration -- the
        // header's extern loop only covers USER types). Zero/One are immutable
        // readonly constants (user code can never write them), so the merged
        // single definition carries no shared-mutable-state risk.
        w.Line("// System.Decimal::Zero/One BCL static storage (16-byte Il2CppDecimal, inline variable)");
        w.Line("inline il2cpp::runtime::Il2CppDecimal System_Decimal_sf_Zero = { 0, 0, 0 };");
        w.Line("inline il2cpp::runtime::Il2CppDecimal System_Decimal_sf_One = { 0, 0, 1 };");
        // decimal-domain C44 (decgap): System.Decimal::MinusOne -- the C# `-1m`
        // literal lowers to ldsfld System.Decimal::MinusOne (Roslyn folds the
        // unary minus of the 1m constant), so the BCL static must be emitted
        // too or the generated C++ hits "use of undeclared identifier".
        // Image: mantissa 1, scale 0, sign bit set (flags = 0x80000000 -- the
        // same bit icall_decimal_ctor_5 sets for isNegative; decimal.cpp:2308).
        w.Line("inline il2cpp::runtime::Il2CppDecimal System_Decimal_sf_MinusOne = { static_cast<int32_t>(0x80000000), 0, 1 };");
        // icallwide: System.TimeSpan::Zero -- the all-zero ticks slot (the
        // same inline-variable doctrine as the Decimal constants above; the
        // int64 ctor is EXPLICIT so the value form is used, not brace-init).
        w.Line("// System.TimeSpan::Zero BCL static storage (8-byte Il2CppTimeSpan, inline variable)");
        w.Line("inline il2cpp::runtime::Il2CppTimeSpan System_TimeSpan_sf_Zero = il2cpp::runtime::Il2CppTimeSpan(0);");
        // D5 (stsdeep): System.TimeSpan::MaxValue / ::MinValue -- mscorlib
        // static readonly fields (long.MaxValue / long.MinValue ticks). Same
        // inline-variable doctrine as TimeSpan.Zero above.
        w.Line("// System.TimeSpan::MaxValue/MinValue BCL static storage (8-byte Il2CppTimeSpan, inline variable)");
        w.Line("inline il2cpp::runtime::Il2CppTimeSpan System_TimeSpan_sf_MaxValue = il2cpp::runtime::Il2CppTimeSpan(0x7FFFFFFFFFFFFFFFLL);");
        w.Line("inline il2cpp::runtime::Il2CppTimeSpan System_TimeSpan_sf_MinValue = il2cpp::runtime::Il2CppTimeSpan(-0x7FFFFFFFFFFFFFFFLL - 1);");
        // P26 (bcldeep): System.DateTime::MinValue -- a static readonly
        // VALUETYPE field (net8: NOT const, so Roslyn cannot fold the ldsfld
        // into a literal -- the read emits &System_DateTime_sf_MinValue).
        // Image: ticks 0 (0001-01-01T00:00:00 UTC) -- exactly the il2cpp
        // runtime's Il2CppDateTime zero value (il2cpp_time.h).
        w.Line("// System.DateTime::MinValue BCL static storage (8-byte Il2CppDateTime, inline variable)");
        w.Line("inline il2cpp::runtime::Il2CppDateTime System_DateTime_sf_MinValue = il2cpp::runtime::Il2CppDateTime(0);");
        // A3 (arraydtneedle): System.DateTime::MaxValue -- the ticks of
        // 9999-12-31T23:59:59.9999999 (DateTime.MaxTicks), the same value the
        // DateTimeOffset MaxValue storage above uses. Without this definition
        // a `DateTime.MaxValue` read is an undeclared identifier.
        w.Line("// System.DateTime::MaxValue BCL static storage (8-byte Il2CppDateTime, inline variable)");
        w.Line("inline il2cpp::runtime::Il2CppDateTime System_DateTime_sf_MaxValue = il2cpp::runtime::Il2CppDateTime(3155378975999999999LL);");
        // B4 (dtoprobe): System.DateTimeOffset::MinValue / ::MaxValue --
        // mscorlib static readonly VALUETYPE fields (net8: NOT const, so
        // Roslyn cannot fold the ldsfld -- the read references
        // System_DateTimeOffset_sf_MinValue/MaxValue). 16-byte
        // Il2CppDateTimeOffset image {utcTicks, offsetMinutes}:
        // MinValue = 0001-01-01 +00:00 ({0,0}); MaxValue = 9999-12-31
        // 23:59:59.9999999 +00:00 ({DateTime.MaxTicks, 0}).
        w.Line("// System.DateTimeOffset::MinValue/MaxValue BCL static storage (16-byte Il2CppDateTimeOffset, inline variable)");
        w.Line("inline il2cpp::runtime::Il2CppDateTimeOffset System_DateTimeOffset_sf_MinValue = { 0, 0 };");
        w.Line("inline il2cpp::runtime::Il2CppDateTimeOffset System_DateTimeOffset_sf_MaxValue = { 3155378975999999999LL, 0 };");
        // wf2 (winforms1): System.IntPtr::Zero / System.UIntPtr::Zero are BCL
        // static fields (mscorlib types have no TypeDef -- no storage emitted).
        w.Line("// System.IntPtr::Zero BCL static storage (intptr_t pointer carrier, inline variable)");
        w.Line("inline intptr_t System_IntPtr_sf_Zero = 0;");
        w.Line("inline uintptr_t System_UIntPtr_sf_Zero = 0;");
        return w.ToString();
    }

    //--------------------------------------------------------------------------
    // Static-field semantic classification (shared by EmitStaticStorage and
    // the EmitTypeInfo field table).
    //
    //   Reference -- delegate / object / string / array / any managed-
    //       reference static field: the storage slot is a GC pointer, emitted
    //       as the uniform Il2CppObject* (delegate fields flow through the
    //       runtime delegate icalls as Il2CppObject*, never a typed struct
    //       pointer).
    //   Decimal   -- a static System.Decimal field: 16-byte Il2CppDecimal
    //       aggregate storage (int32_t would overrun the value).
    //   IntPtr / UIntPtr -- native-int static fields (0x18/0x19): full-width
    //       intptr_t/uintptr_t pointer carriers (wf16 winforms1 root cause:
    //       the M1 int32_t slot truncated a 64-bit WNDPROC/HWND value).
    //   Int64 / UInt64 -- 64-bit integer statics (0x0A/0x0B): int64_t/uint64_t
    //       storage (an int32_t slot would truncate a long value).
    //   Float / Double -- 32/64-bit floating statics (0x0C/0x0D): float/double
    //       storage (an int32_t slot truncated every store/load -- the
    //       probe-sfieldfloat `static double D = 3.5; D++;` -> 3/4 defect).
    //   DateTime / TimeSpan -- 8-byte runtime value structs (ticks would be
    //       truncated by int32_t; must agree with EmitStaticStorage).
    //   Value     -- everything else (int32_t storage).
    //
    // Resolution order: the FieldDef MODEL first (a loader that populates
    // TypeKind/FieldTypeName -- the unit tests construct the model this way),
    // then -- when the caller supplies the metadata reader -- the field
    // SIGNATURE (mirrors CodeGenerator.StaticFieldIsReference's reference
    // arms + the CG-FIX-4 extern decode, so the DEFINITION emitted here and
    // the generated.h extern stay type-identical). Static FieldDefs carry no
    // TypeKind today (top-level static fields are loaded type-less), so the
    // metadata decode is what activates the delechain fix in real transpiles.
    // A field with neither model type nor a reference/decimal/pointer/int64
    // signature keeps the M1 int32_t emission -- the historical behavior, so
    // classification can only improve, never regress.
    //--------------------------------------------------------------------------
    private enum StaticFieldSemantic { Value, Reference, Decimal, IntPtr, UIntPtr, Int64, UInt64, Float, Double, DateTime, TimeSpan, FnPtr }

    // Full-name -> value semantic map shared by the model and the metadata
    // signature paths (decimalcase pairing; wf16 extends it to the wide
    // integer/pointer statics). The TypeName is the SAME full name, so the
    // reflection field table sees the real type name (not the Int32 fallback).
    private static (StaticFieldSemantic Semantic, string TypeName)? ValueTypeSemantic(string fullName) => fullName switch
    {
        "System.Decimal" => (StaticFieldSemantic.Decimal, "System.Decimal"),
        "System.IntPtr" => (StaticFieldSemantic.IntPtr, "System.IntPtr"),
        "System.UIntPtr" => (StaticFieldSemantic.UIntPtr, "System.UIntPtr"),
        "System.Int64" => (StaticFieldSemantic.Int64, "System.Int64"),
        "System.UInt64" => (StaticFieldSemantic.UInt64, "System.UInt64"),
        // probe-sfieldfloat: the floating statics (0x0C/0x0D) keep their real
        // storage width -- int32_t truncated every store/load.
        "System.Single" => (StaticFieldSemantic.Float, "System.Single"),
        "System.Double" => (StaticFieldSemantic.Double, "System.Double"),
        // DateTime/TimeSpan statics (Zero/MinValue/MaxValue) are 8-byte
        // runtime value structs -- int32_t storage would truncate ticks and
        // disagree with EmitStaticStorage's inline definitions.
        "System.DateTime" => (StaticFieldSemantic.DateTime, "System.DateTime"),
        "System.TimeSpan" => (StaticFieldSemantic.TimeSpan, "System.TimeSpan"),
        _ => null,
    };

    private static (StaticFieldSemantic Semantic, string TypeName) StaticFieldSemantics(FieldDef f, MetadataReader? md)
    {
        if (f.TypeKind == FieldTypeKind.Reference)
            return (StaticFieldSemantic.Reference, string.IsNullOrEmpty(f.FieldTypeName) ? "System.Object" : f.FieldTypeName);
        // GN-18 B1 (fnptr fields): a STATIC `delegate*` (0x1B) field needs a
        // void* pointer carrier (the fnptr value is void*; intptr_t storage
        // makes the stsfld store and the ldsfld load C++ type errors). The
        // EMPTY FieldTypeName distinguishes it from a real IntPtr (0x18/0x19)
        // which carries "System.IntPtr"; both reuse the NativeInt KIND so the
        // model needs no new enum value.
        if (f.TypeKind == FieldTypeKind.NativeInt)
            return string.IsNullOrEmpty(f.FieldTypeName)
                ? (StaticFieldSemantic.FnPtr, "")
                : (StaticFieldSemantic.IntPtr, f.FieldTypeName);
        // Model-driven wide-value classification (wf16 winforms1 static IntPtr
        // truncation): the metadata loader populates FieldTypeName for static
        // fields (DecodeExtendedStaticType maps 0x18/0x19/0x0A/0x0B to the
        // System.IntPtr/UIntPtr/Int64/UInt64 full names), so the model path
        // classifies the wide statics exactly like the Decimal arm below.
        if (ValueTypeSemantic(f.FieldTypeName) is var wide && wide != null)
            return wide.Value;
        if (md == null)
            return (StaticFieldSemantic.Value, "System.Int32");
        try
        {
            foreach (var tdh in md.TypeDefinitions)
            {
                // Nested owners: the expanded model keys nested types by SHORT
                // FullName ("<>c") while metadata GetFullName walks the
                // declaring chain ("Cases.ArrayExt2Demo+<>c") -- match either
                // form or <>c lambda-cache fields (Func<>, reference) fall to
                // the int32 default below (arrayext2 cmake FAIL).
                var metaName = md.GetFullName(tdh);
                if (metaName != f.Owner.FullName && metaName != ReflectionFullName(f.Owner)) continue;
                var td = md.GetTypeDefinition(tdh);
                foreach (var fh in td.GetFields())
                {
                    var fd = md.GetFieldDefinition(fh);
                    if (md.GetString(fd.Name) != f.Name) continue;
                    var sig = md.GetBlobReader(fd.Signature);
                    if (sig.ReadByte() != 0x06) return (StaticFieldSemantic.Value, "System.Int32");
                    // ECMA-335 II.23.1.16: strip custom modifiers
                    // (volatile -> CMOD_REQD(IsVolatile) 0x1F) first.
                    var typeByte = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
                    // Reference kinds (mirrors CodeGenerator.StaticFieldIsReference):
                    // 0x0E String, 0x1C Object, 0x12 Class, 0x15 GenericInst
                    // (Func`N etc.), 0x1D/0x1F SZARRAY, 0x14 ARRAY. 0x15 is
                    // followed by the 0x12 class marker + the TypeDefOrRef.
                    if (typeByte is 0x0E) return (StaticFieldSemantic.Reference, "System.String");
                    if (typeByte is 0x1C) return (StaticFieldSemantic.Reference, "System.Object");
                    if (typeByte is 0x12) return (StaticFieldSemantic.Reference, ResolveNestedTypeName(md, sig.ReadTypeHandle()));
                    if (typeByte is 0x15) { sig.ReadByte(); return (StaticFieldSemantic.Reference, ResolveNestedTypeName(md, sig.ReadTypeHandle())); }
                    if (typeByte is 0x1D or 0x14) return (StaticFieldSemantic.Reference, "System.Array");
                    // wf16 (winforms1): native-int static fields (0x18/0x19)
                    // and 64-bit integer statics (0x0A/0x0B) -- the M1 int32_t
                    // slot truncated the 64-bit WNDPROC/HWND pointer and long
                    // values on x64. Same classification as the model arm.
                    if (typeByte is 0x18) return (StaticFieldSemantic.IntPtr, "System.IntPtr");
                    if (typeByte is 0x19) return (StaticFieldSemantic.UIntPtr, "System.UIntPtr");
                    // GN-18 B1 (fnptr fields): a STATIC fnptr field not in the
                    // model (NESTED types are keyed by short FullName and their
                    // static fields carry no TypeKind) re-scanned here -- the
                    // void* FnPtr carrier (same as the model arm).
                    if (typeByte is 0x1B) return (StaticFieldSemantic.FnPtr, "");
                    if (typeByte is 0x0A) return (StaticFieldSemantic.Int64, "System.Int64");
                    if (typeByte is 0x0B) return (StaticFieldSemantic.UInt64, "System.UInt64");
                    // probe-sfieldfloat: the floating statics (0x0C/0x0D) keep
                    // their real storage width -- the M1 int32_t slot truncated
                    // the value at every store/load.
                    if (typeByte is 0x0C) return (StaticFieldSemantic.Float, "System.Single");
                    if (typeByte is 0x0D) return (StaticFieldSemantic.Double, "System.Double");
                    // Value types: a decimal field needs 16-byte storage; the
                    // wide integer/pointer structs (IntPtr etc., encoded as a
                    // VALUETYPE + TypeDefOrRef by some compilers) need their
                    // full-width carrier; any other value type keeps the M1
                    // int32_t slot.
                    if (typeByte is 0x11)
                    {
                        var vtName = ResolveNestedTypeName(md, sig.ReadTypeHandle());
                        if (ValueTypeSemantic(vtName) is var vtSemantic && vtSemantic != null)
                            return vtSemantic.Value;
                        return (StaticFieldSemantic.Value, "System.Int32");
                    }
                    return (StaticFieldSemantic.Value, "System.Int32");
                }
            }
        }
        catch
        {
            // Best-effort classifier: a signature we cannot decode falls back
            // to the M1 value emission (never newly throws on a field that
            // loaded fine -- the metadata decode is a typing aid, not a gate).
        }
        return (StaticFieldSemantic.Value, "System.Int32");
    }

    //--------------------------------------------------------------------------
    // StaticFieldCppType -- the C++ storage TYPE of a static field, shared by
    // the generated.h extern declaration (CodeGenerator's "static field
    // storage declarations" loop) and the .cpp DEFINITION (EmitStaticStorage)
    // so the pair can never disagree (decimalcase cmake FAIL root cause:
    // the header declared Cases_DecimalCase_sf_Euler as int32_t while the
    // definition emitted a 16-byte Il2CppDecimal -- "redefinition with a
    // different type").
    //--------------------------------------------------------------------------
    public static string StaticFieldCppType(FieldDef f, MetadataReader? md)
    {
        var (semantic, _) = StaticFieldSemantics(f, md);
        return semantic switch
        {
            StaticFieldSemantic.Reference => "Il2CppObject*",
            StaticFieldSemantic.Decimal => "il2cpp::runtime::Il2CppDecimal",
            // wf16 (winforms1): the wide static carrier types -- int32_t
            // truncates a 64-bit WNDPROC/HWND pointer and long values on x64.
            // Keeps the generated.h extern and the EmitStaticStorage
            // DEFINITION type-identical (decimalcase cmake FAIL rule).
            StaticFieldSemantic.IntPtr => "intptr_t",
            StaticFieldSemantic.UIntPtr => "uintptr_t",
            // GN-18 B1 (fnptr fields): a STATIC `delegate*` field stores a
            // void* pointer value -- the C++ storage is void* (matches the
            // instance member emission; a fnptr IS a raw function pointer, not
            // an intptr_t integer carrier). Generated.h extern and the
            // EmitStaticStorage DEFINITION must be type-identical (decimalcase
            // cmake FAIL rule), so both name void*.
            StaticFieldSemantic.FnPtr => "void*",
            StaticFieldSemantic.Int64 => "int64_t",
            StaticFieldSemantic.UInt64 => "uint64_t",
            // probe-sfieldfloat: the floating statics keep their real width --
            // the generated.h extern and the EmitStaticStorage DEFINITION must
            // stay type-identical (decimalcase cmake FAIL rule), so both name
            // float/double (the same types the INSTANCE field path emits).
            StaticFieldSemantic.Float => "float",
            StaticFieldSemantic.Double => "double",
            StaticFieldSemantic.DateTime => "il2cpp::runtime::Il2CppDateTime",
            StaticFieldSemantic.TimeSpan => "il2cpp::runtime::Il2CppTimeSpan",
            _ => "int32_t"
        };
    }

    //--------------------------------------------------------------------------
    // ReferenceStaticFieldCppNames -- the C++ field names of a type's REFERENCE
    // static fields (the Il2CppObject* slots emitted by EmitStaticStorage).
    //
    // FixAV (winforms1 user32 AV root cause): the runtime's collector is a
    // compacting Gen0 GC whose root set is ONLY registered static roots
    // (il2cpp_gc_add_static_root) + root ranges (gc.cpp:51-53); it sweeps
    // unmarked blocks and rewrites ONLY registered roots after a compaction
    // (gc.cpp:580-628). A generated static delegate/object field that is never
    // registered keeps a STALE pointer after a collection (the object is either
    // reclaimed or moved without the field being rewritten). The WndProc
    // delegates live ONLY in such fields -- after a collection,
    // icall_delegate_get_fn_ptr reads a stale methodPtr and USER32's dispatch
    // calls a garbage address (0xC0000005 in the window-message path).
    // entry.cpp's il2cpp_runtime_init registers every returned field via
    // il2cpp::il2cpp_gc_add_static_root(&field) (address-dedup, kMaxStaticRoots).
    //--------------------------------------------------------------------------
    public static IEnumerable<string> ReferenceStaticFieldCppNames(TypeDef t, MetadataReader? md)
    {
        foreach (var f in t.StaticFields.Where(f => f.IsStatic && !f.IsLiteral))
        {
            if (StaticFieldSemantics(f, md).Semantic == StaticFieldSemantic.Reference)
                yield return FieldCppName(t, f.Name);
        }
    }

    //--------------------------------------------------------------------------
    // EmitTypeInfo -- M5.5 reflection metadata table for one REFLECTED type
    // (reflection-runtime-design.md section 2 + typeinfo.h).
    //
    //   static const il2cpp::Il2CppMethodInfo {T}_typeinfo_methods[]  (per method)
    //   static const il2cpp::Il2CppFieldInfo {T}_typeinfo_fields[]    (per field)
    //   static const il2cpp::Il2CppTypeInfo* {T}_typeinfo_interfaces[]
    //   il2cpp::Il2CppTypeInfo {T}_typeinfo = { ... }
    //
    //   LINKAGE CONTRACT: the MAIN table is a NON-CONST namespace-scope global
    //   (external linkage), mirroring Il2CppClass {T}_class exactly. The
    //   registration hook (EmitTypeInfoRegistration) emits
    //   il2cpp_typeinfo_register(&{T}_typeinfo) from entry.cpp -- a DIFFERENT
    //   translation unit. Do NOT use `static const` (internal linkage: the
    //   entry.cpp reference fails to compile/link) and do NOT use `inline`
    //   (a definition is only emitted where it is odr-USED; the defining .cpp
    //   never references its own table, so the symbol is not emitted -> link
    //   error -- probe-verified with MinGW/clang). A non-const global is ALWAYS
    //   emitted, -Wall silent (external linkage is never "unused"), and the
    //   runtime register API takes const Il2CppTypeInfo* so the table is still
    //   read-only at runtime. The sub-tables are referenced ONLY by the main
    //   table in the same .cpp, so they stay static const.
    //
    // Scope: reflectedNames == null emits EVERY type (mode A); otherwise only
    // types whose FullName is in the set (mode B -- the Kept result of
    // FilterReflectionManifest) emit a table; others return "".
    //
    // Layout contract with the runtime (typeinfo.h):
    //   * methods: signature = MethodSignatureCoder return encoding + "(" +
    //     ParamTypes + ")"; flags bit0 public bit1 static bit2 ctor
    //     bit3 virtual; virtualSlot = VTableSlot (virtual only, else -1);
    //     declaringType = own type (inherited methods live under THEIR
    //     declaring type's table); fnPtr = the transpiler-emitted static
    //     function (&{T}_{Name}_{SigHash} -- the SAME symbol call sites
    //     resolve; MethodRefResolver/CodeGenerator naming), or nullptr for
    //     methods that have no emitted function (interface / abstract /
    //     P/Invoke -- trimmed methods, typeinfo.h "null = unreachable").
    //     foldedAccessorNames (refl2 S2/G1): a folded-accessor method
    //     (get_X/set_X rewritten to a field access -- AccessorAnalyzer) ALSO
    //     has no emitted function symbol, so its fnPtr is nullptr instead of
    //     &{MethodSymbolName} (which would fail to link). The set is per-type
    //     method short names, so the table stays intact while folded methods
    //     are unreachable (typeinfo.h "null = unreachable").
    //   * fields: instance -> byte offset (FieldDef.ByteOffset: SlotIndex *
//     pointer size for a REFERENCE type -- the slot grid; the net48 compact
//     sequential offset for a VALUE type -- see LayoutInstanceFields above)
//     + isRef;
    //     static -> offset -1 + staticAddr = &{FieldCppName} (non-literal);
    //     flags bit1 static bit2 literal. FieldDef has NO IsPublic (model
    //     gap, mirror of the M1 static-field classification gap) -- the
    //     public bit stays 0 here.
    //   * baseType: resolved user base -> &{Base}_typeinfo; System.Object
    //     roots -> nullptr. interfaces: resolved InterfaceTypes -> pointer
    //     array. properties: PropertyDef model not landed -- the property
    //     sub-table is emitted EMPTY (nullptr / 0) until properties decode
    //     lands (design section 2.1 structure is ready).
    //   * klass: &{T}_class (the existing descriptor -- never duplicated) ONLY
    //     when the type actually has one emitted (CodeGenerator's descriptor
    //     condition: non-value-type, non-delegate, with an instance surface --
    //     instance fields / .ctor / virtuals / interface). A C# STATIC class
    //     (no instance fields, no .ctor, no virtuals) gets NO descriptor, so
    //     its klass is nullptr (the &{T}_class symbol would be an undeclared
    //     identifier in generated C++). Value types also have no descriptor ->
    //     nullptr.
    //
    // Emitted in the type's .cpp by the caller (CodeGenerator per-type
    // pass), same pass as EmitClassDescriptor.
    //--------------------------------------------------------------------------
    public static string EmitTypeInfo(TypeDef t, IReadOnlyCollection<string>? reflectedNames = null, MetadataReader? md = null,
        IReadOnlySet<string>? foldedAccessorNames = null, IReadOnlyList<TypeDef>? allTypes = null)
    {
        if (reflectedNames != null && !reflectedNames.Contains(t.FullName)) return "";
        LayoutInstanceFields(t, allTypes);
        var w = new CppWriter();
        w.Line($"// Reflection metadata for {t.FullName} (M5.5 typeinfo.h contract).");

        // Method sub-table.
        if (t.Methods.Count > 0)
        {
            w.Line($"static const il2cpp::Il2CppMethodInfo {TypeCppName(t)}_typeinfo_methods[] = {{");
            w.Open();
            foreach (var m in t.Methods)
            {
                var flags = (m.IsPublic ? 1 : 0) | (m.IsStatic ? 2 : 0)
                    | (m.Name is ".ctor" or ".cctor" ? 4 : 0) | (m.IsVirtual ? 8 : 0)
                    // refl5 (item 1): the PRIVATE visibility bit (net48 never
                    // enumerates a private base-chain member; the flags-aware
                    // enumerators skip bit0x10 entries on the base walk).
                    | (m.IsPrivate ? 0x10 : 0)
                    // refl6b (item C): the precise visibility states as TWO
                    // dedicated flag bits -- bit0x20 Internal(0x3), bit0x40
                    // FamORAssem(0x5). Encoded ONLY for those exact vis values
                    // (public/private/protected stay bit-identical to the
                    // pre-refl6b tables -- zero-delta bytes); the precise
                    // IsAssembly / IsFamilyOrAssembly predicates decode them.
                    | (m.AccessMask == 0x3 ? 0x20 : 0)
                    | (m.AccessMask == 0x5 ? 0x40 : 0)
                    // p-refl2-gapmatrix (B08/B11): the MethodBase.IsAbstract /
                    // MethodInfo.IsGenericMethod flag predicates as dedicated
                    // bits (0x80 abstract, 0x100 generic method). The emitter
                    // reads the real MethodAttributes.Abstract and the decoded
                    // generic arity (MethodDef.GenericArity) -- zero-delta for
                    // concrete non-generic methods.
                    | (m.IsAbstract ? 0x80 : 0)
                    | (m.GenericArity > 0 ? 0x100 : 0);
                var slot = m.IsVirtual ? m.VTableSlot : -1;
                // refl2 S2/G1: folded-accessor methods have NO emitted function
                // symbol (AccessorAnalyzer rewrote every call site to a field
                // access), so their fnPtr must be nullptr -- the old whole-type
                // exclusion (foldedAccessorOwners) is removed.
                var folded = foldedAccessorNames != null && foldedAccessorNames.Contains(m.Name);
                // fixdel3 (b23): a custom delegate TypeDef's synthesized methods
                // (.ctor/Invoke/BeginInvoke/EndInvoke -- Roslyn "runtime
                // managed" MethodDefs, RVA=0) are never emitted as C++ function
                // symbols (CodeGenerator folds delegate construction/Invoke
                // through the runtime delegate machinery). Every method entry
                // gets fnPtr=nullptr -- the typeinfo.h "null = runtime-
                // unreachable (trimmed)" contract -- or the TypeInfo table
                // leaves a dangling &Cb_Invoke_... reference (m7final
                // delegatecb cmake FAIL: undeclared identifier).
                var isDelegate = IsDelegateType(t);
                var fnPtr = (t.IsInterface || m.IsAbstract || m.IsPInvoke || folded || isDelegate)
                    ? "nullptr"
                    : $"reinterpret_cast<void*>(&{MethodSymbolName(t, m)})";
                // REFL3 (item 2): the per-parameter metadata (name|outFlag,
                // ';'-joined). paramCount must match ParamNames (the Param rows
                // project onto the signature slots); a mismatch (or no names)
                // emits nullptr -> GetParameters answers an empty array.
                var paramMetaCpp = ParamMetaCppString(m);
                w.Line($"{{ \"{m.Name}\", \"{EncodeReturnType(m.ReturnType, m.ReturnTypeName)}({m.ParamTypes})\", {m.ParamCount}, 0x{flags:x}, {slot}, \"{ReflectionFullName(t)}\", {fnPtr}, {paramMetaCpp}, 0x{m.MetadataToken:x8} }},");
            }
            w.Close();
            w.Line("};");
            w.Line();
        }

        // Field sub-table (instance + static, metadata order).
        // P2 refl audit: a non-reference INSTANCE field's typeName used to be
        // the constant "System.Int32" (FieldInfo.FieldType would report the
        // wrong type for any value-type field). The model carries the real
        // value-type full name in FieldTypeName for VALUETYPE (0x11) and
        // native-int (0x18/0x19) instance fields (DecodeNestedFieldSignature /
        // MetadataLoader.DecodeFieldSignature); use it when present, keeping
        // the "System.Int32" fallback for the widthless-typed value slots
        // (int32 carrier with no name).
        var fieldEntries = t.InstanceFields
            .Select(f => (f.Name, TypeName: f.TypeKind == FieldTypeKind.Reference || !string.IsNullOrEmpty(f.FieldTypeName) ? f.FieldTypeName : "System.Int32",
                // [MarshalAs] fixed buffers are INLINE native arrays, not GC
                // references -- the physical isRef bit must say so (a 1 here
                // would make reflection treat the buffer bytes as a pointer).
                Offset: f.ByteOffset, IsRef: f.TypeKind == FieldTypeKind.Reference && f.MarshalKind == FieldMarshalKind.None ? 1 : 0,
                Addr: "nullptr",
                // p-refl2-gapmatrix (B17): the IsInitOnly predicate is the
                // dedicated field-flag bit 0x80 (the literal const flag is bit2).
                Flags: (f.IsPublic ? 1 : 0) | (f.IsPrivate ? 0x10 : 0)
                    | (f.AccessMask == 0x3 ? 0x20 : 0) | (f.AccessMask == 0x5 ? 0x40 : 0)
                    | (f.IsInitOnly ? 0x80 : 0),
                Token: f.MetadataToken))
            .Concat(t.StaticFields.Select(f =>
            {
                var (semantic, typeName) = StaticFieldSemantics(f, md);
                return (f.Name, TypeName: typeName,
                    Offset: -1, IsRef: semantic == StaticFieldSemantic.Reference ? 1 : 0,
                    Addr: f.IsLiteral ? "nullptr" : $"&{FieldCppName(t, f.Name)}",
                    Flags: (f.IsPublic ? 1 : 0) | (f.IsStatic ? 2 : 0) | (f.IsLiteral ? 4 : 0) | (f.IsPrivate ? 0x10 : 0)
                        | (f.AccessMask == 0x3 ? 0x20 : 0) | (f.AccessMask == 0x5 ? 0x40 : 0)
                        | (f.IsInitOnly ? 0x80 : 0),
                    Token: f.MetadataToken);
            }))
            .ToList();
        if (fieldEntries.Count > 0)
        {
            w.Line($"static const il2cpp::Il2CppFieldInfo {TypeCppName(t)}_typeinfo_fields[] = {{");
            w.Open();
            foreach (var f in fieldEntries)
                w.Line($"{{ \"{f.Name}\", \"{f.TypeName}\", {f.Offset}, {f.IsRef}, {f.Addr}, {f.Flags}, 0x{f.Token:x8} }},");
            w.Close();
            w.Line("};");
            w.Line();
        }

        // Interface pointer array (resolved interfaces only).
        var ifaceArray = $"{TypeCppName(t)}_typeinfo_interfaces";
        if (t.InterfaceTypes.Count > 0)
        {
            w.Line($"static const il2cpp::Il2CppTypeInfo* {ifaceArray}[] = {{");
            w.Open();
            foreach (var iface in t.InterfaceTypes)
                w.Line($"&{TypeCppName(iface)}_typeinfo,");
            w.Close();
            w.Line("};");
            w.Line();
        }

        // Property sub-table (refl3/m7d, d1_prop). Accessors are referenced by
        // METHOD NAME (Il2CppPropertyInfo.getterName/setterName) -- the runtime
        // resolves them by name through the SAME methods table, so no fnPtr is
        // duplicated here. A missing accessor emits nullptr. Folded auto-
        // property accessors (AccessorAnalyzer) stay in the methods table with
        // fnPtr=nullptr; the name references below still resolve.
        var propertyEntries = t.Properties
            .Select(p => (p.Name, p.TypeName,
                Getter: p.GetterName == null ? "nullptr" : $"\"{p.GetterName}\"",
                Setter: p.SetterName == null ? "nullptr" : $"\"{p.SetterName}\"",
                p.IndexParamCount))
            .ToList();
        if (propertyEntries.Count > 0)
        {
            w.Line($"static const il2cpp::Il2CppPropertyInfo {TypeCppName(t)}_typeinfo_properties[] = {{");
            w.Open();
            foreach (var p in propertyEntries)
                w.Line($"{{ \"{p.Name}\", \"{p.TypeName}\", {p.Getter}, {p.Setter}, {p.IndexParamCount} }},");
            w.Close();
            w.Line("};");
            w.Line();
        }

        // Event sub-table (COM-C3 / RF-N1). The eventIID is the folded [Guid]
        // of the event's COM source interface (16 raw bytes -- zeroed = a
        // managed-only event, no COM source). The add/remove ACCESSORS are
        // referenced by METHOD NAME (Il2CppEventInfo.addName/removeName), the
        // same single-source-of-truth convention as the property table. 0
        // events emit NO table (nullptr/0 -- zero delta for event-free types).
        var eventEntries = t.Events
            .Select(e => (e.Name,
                IID: string.Join(", ", e.EventIID.Select(b => $"0x{b:x2}")),
                Add: e.AddName == null ? "nullptr" : $"\"{e.AddName}\"",
                Remove: e.RemoveName == null ? "nullptr" : $"\"{e.RemoveName}\""))
            .ToList();
        if (eventEntries.Count > 0)
        {
            w.Line($"static const il2cpp::Il2CppEventInfo {TypeCppName(t)}_typeinfo_events[] = {{");
            w.Open();
            foreach (var e in eventEntries)
                w.Line($"{{ \"{e.Name}\", {{ {e.IID} }}, {e.Add}, {e.Remove} }},");
            w.Close();
            w.Line("};");
            w.Line();
        }

        // RF-N5: nested-type sub-table (typeinfo.h Il2CppTypeInfo nestedNames/
        // nestedCount -- GetNestedTypes). The nested types were expanded to
        // independent top-level entries by ExpandNestedTypes; this table
        // references each DIRECT nested type's REFLECTION full name (the '+'
        // form, TypeEmitter.ReflectionFullName -- the same key its own
        // typeinfo registers under, so the GetNestedTypes result elements are
        // full-name strings). Metadata declaration order = the types list
        // order (ExpandNestedTypes appends in md.TypeDefinitions order). 0
        // nested types emit NO table (nullptr/0 -- zero delta for
        // nested-free types).
        var nestedDefs = NestedTypesOf(t, allTypes);
        var nestedEntries = nestedDefs.Select(ReflectionFullName).ToList();
        if (nestedEntries.Count > 0)
        {
            w.Line($"static const char* {TypeCppName(t)}_typeinfo_nested[] = {{");
            w.Open();
            foreach (var n in nestedEntries)
                w.Line($"    \"{n}\",");
            w.Close();
            w.Line("};");
            w.Line();
            // refl6b (item A): the parallel VISIBILITY byte column -- one
            // TypeAttributes&0x7 value per nested entry (the expanded nested
            // TypeDef's VisibilityMask). The runtime GetNestedType /
            // GetNestedTypes visibility filter consumes it (net48 default
            // GetNestedTypes() is Public-only; GetNestedType(name) binds
            // Public-only). Emitted whenever the names table is emitted.
            w.Line($"static const uint8_t {TypeCppName(t)}_typeinfo_nestedvis[] = {{");
            w.Open();
            foreach (var nd in nestedDefs)
                w.Line($"    {(byte)(nd.VisibilityMask & 0x7)}, // {ReflectionFullName(nd)}");
            w.Close();
            w.Line("};");
            w.Line();
        }

        // Main table.
        var methodsTable = t.Methods.Count > 0 ? $"{TypeCppName(t)}_typeinfo_methods" : "nullptr";
        var fieldsTable = fieldEntries.Count > 0 ? $"{TypeCppName(t)}_typeinfo_fields" : "nullptr";
        var propertiesTable = propertyEntries.Count > 0 ? $"{TypeCppName(t)}_typeinfo_properties" : "nullptr";
        var eventsTable = eventEntries.Count > 0 ? $"{TypeCppName(t)}_typeinfo_events" : "nullptr";
        var interfacesExpr = t.InterfaceTypes.Count > 0 ? ifaceArray : "nullptr";
        // O4 (boxed user-struct reflection): for a VALUE type WITH an emitted
        // descriptor (field-bearing or .ctor-bearing -- the exact
        // CodeGenerator descriptor-emission condition at CodeGenerator.cs:
        // 1258-1260, mirrored here so &{T}_class never references an undefined
        // symbol), the klass back-pointer rides the P25b box descriptor. The
        // runtime il2cpp_typeinfo_lookup_by_class then joins a BOXED user
        // struct (whose klass IS &{T}_class) to its own typeinfo table, so
        // FieldInfo.GetValue/SetValue can resolve the struct-relative field
        // offset. A fieldless struct has no descriptor and keeps klass
        // nullptr (unboxable edge -- no change). Enums keep the existing
        // HasClassDescriptor path (already non-null).
        var valueHasDescriptor = t.IsValueType
            && (t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor"));
        var klassExpr = valueHasDescriptor
            ? $"&{TypeCppName(t)}_class"
            : !HasClassDescriptor(t) ? "nullptr" : $"&{TypeCppName(t)}_class";
        var baseExpr = t.BaseType != null
            ? $"&{TypeCppName(t.BaseType)}_typeinfo,"
            : "nullptr, // baseType (System.Object)";
        // Main table: NON-CONST namespace-scope global (external linkage) --
        // the registration hook (EmitTypeInfoRegistration) takes
        // &{T}_typeinfo from entry.cpp, a different TU. static const would
        // be internal linkage (compile/link failure); inline is not emitted
        // unless odr-used in its own .cpp (link failure -- probe-verified).
        // Mirrors Il2CppClass {T}_class exactly (see linkage contract above).
        w.Line($"il2cpp::Il2CppTypeInfo {TypeCppName(t)}_typeinfo = {{");
        w.Line($"    \"{ReflectionFullName(t)}\",");
        w.Line($"    {klassExpr},");
        w.Line($"    {baseExpr}");
        w.Line($"    {interfacesExpr}, {t.InterfaceTypes.Count},          // interfaces");
        w.Line($"    {fieldsTable}, {fieldEntries.Count},                  // fields");
        w.Line($"    {methodsTable}, {t.Methods.Count},                    // methods");
        w.Line($"    {propertiesTable}, {t.Properties.Count},              // properties");
        w.Line($"    {eventsTable}, {t.Events.Count},                      // events");
        // RF-N4: genericArity -- the runtime IsGenericType / GetGenericArguments
        // / GetGenericTypeDefinition icalls read this field (typeinfo.h). Both
        // a generic DEFINITION and its closed CLONES emit the definition's
        // arity (TypeEmitter.ReflectionFullName registers both under the same
        // name key; a clone carries the SAME GenericArity as its definition),
        // so the runtime answers IsGenericType for typeof(Box<>) and
        // typeof(Box<int>) alike. 0 = non-generic (the zero-delta default).
        w.Line($"    {t.GenericArity},                                    // genericArity");
        // D / NB-1: baseTypeName -- the base-type terminal answered by the
        // runtime get_BaseType icall. net48 semantics, disambiguated by
        // IsInterface (D conflated the class:object root with the interface):
        //   * USER base (baseType pointer set) -> nullptr (the runtime rides
        //     info->baseType->name).
        //   * INTERFACE (IsInterface)         -> nullptr (net48 interface
        //     BaseType == null; an interface decodes a NIL extends so its
        //     BaseTypeName reads "System.Object" too -- exactly the case D
        //     mis-mapped).
        //   * ROOT CLASS (non-interface, BaseTypeName "System.Object") ->
        //     "System.Object" (net48: typeof(RootClass).BaseType == Object).
        //   * BCL non-Object base (e.g. CustomEx : System.Exception) ->
        //     the BaseTypeName string (baseType pointer null there).
        // null/empty BaseTypeName is unreachable in production (the decoders
        // always return a full name) but maps to nullptr for safety. Trailing
        // field -- mirrors the struct order in typeinfo.h (after genericArity).
        var baseTypeNameExpr = t.BaseType != null || t.IsInterface
            || string.IsNullOrEmpty(t.BaseTypeName)
            ? "nullptr"
            : $"\"{t.BaseTypeName}\"";
        w.Line($"    {baseTypeNameExpr},  // baseTypeName (BCL base terminal)");
        // RF-N5: nested-type sub-table row (main table).
        var nestedExpr = nestedEntries.Count > 0 ? $"{TypeCppName(t)}_typeinfo_nested" : "nullptr";
        w.Line($"    {nestedExpr}, {nestedEntries.Count},                  // nested (RF-N5)");
        w.Line($"    {(nestedEntries.Count > 0 ? $"{TypeCppName(t)}_typeinfo_nestedvis" : "nullptr")},  // nestedVis (refl6b: TypeAttributes&0x7 per entry)");
        // O4 (boxed user-struct reflection): the value-type marker -- 1 for a
        // translated user STRUCT (TypeDef.IsValueType; its Il2CppFieldInfo.
        // offset column is struct-relative, so the runtime GetValue/SetValue
        // adds the box payload offset). 0 for classes AND enums (an enum's
        // value__ slot-grid offset already coincides with the box payload --
        // adding the header again would double-shift). Trailing field,
        // zero-delta default (typeinfo.h).
        w.Line($"    {(t.IsValueType ? 1 : 0)},                            // isValueType (O4: boxed-struct field offset + header)");
        // refl2 (A): the TypeAttributes-derived predicate bits (typeinfo.h
        // typeFlags). Exact ECMA bits, never a shape guess:
        //   0x01 Abstract (0x20)  0x02 Sealed (0x01)
        //   0x04 Public   (VisibilityMask == 0x1 NotPublic? no -- Public is 0x1)
        //   0x08 Nested   (the TypeDef is nested)
        var typeFlags = (t.IsAbstract ? 0x01 : 0)
            | (t.IsSealed ? 0x02 : 0)
            | (t.VisibilityMask == 0x1 ? 0x04 : 0)
            | (t.IsNested ? 0x08 : 0);
        w.Line($"    0x{typeFlags:x},  // typeFlags (refl2: Abstract|Sealed|Public|Nested)");
        // refl2 (A): the ECMA metadata token for Type.MetadataToken on a
        // runtime receiver (the folded typeof chain answers it directly).
        w.Line($"    0x{t.MetadataToken:x8},  // metadataToken (refl2: TypeDef row token)");
        // refl2 (A): the RAW TypeAttributes value (Type.Attributes + the exact
        // nested-visibility predicates).
        w.Line($"    0x{t.RawAttributes:x8},  // attributes (refl2: raw ECMA TypeAttributes)");
        w.Line("};");
        w.Line();
        return w.ToString();
    }

    //--------------------------------------------------------------------------
    // EmitTypeInfoRegistration -- the registry hook line for one type
    // (typeinfo.h: il2cpp_typeinfo_register, mirrors il2cpp_class_register).
    // HOOK RECIPE (wired by CodeGenerator, NOT edited here -- CodeGenerator
    // is a parallel agent domain): in the entry.cpp pass
    // (CodeGenerator.EmitMain -> il2cpp_runtime_init), immediately after the
    // il2cpp_class_register loop, emit one line per REFLECTED type:
    //     TypeEmitter.EmitTypeInfoRegistration(t)   // for each Kept type
    // (mode B: iterate the Kept set; mode A: all non-value types). Types
    // with a .cctor may alternatively register inside ensure_cctor_{T}
    // (before the done flag is set), the typeinfo registry is static and
    // registration is idempotent (typeinfo.cpp). The table must be
    // registered BEFORE any Type.GetType/GetMethod/GetField query runs.
    //--------------------------------------------------------------------------
    public static string EmitTypeInfoRegistration(TypeDef t)
        => $"il2cpp::il2cpp_typeinfo_register(&{TypeCppName(t)}_typeinfo);";

    //--------------------------------------------------------------------------
    // HasClassDescriptor -- does {Type}_class exist in the generated C++?
    //
    // Mirrors CodeGenerator's descriptor-emission condition (the typeinfo
    // klass field references &{T}_class, so the reference is only valid when
    // the descriptor is emitted). Types without a descriptor:
    //   * value types (no GC class),
    //   * custom delegate types (objects are runtime Il2CppDelegate
    //     instances, never il2cpp_object_new(&{T}_class)),
    //   * types with NO instance surface -- a C# static class (no instance
    //     fields, no .ctor, no virtuals, not an interface) is never
    //     instantiated and gets no descriptor.
    // (CodeGenerator.IsDelegateTypeDef + the descriptor `if` at ~line 359;
    // the duplication is deliberate -- the file mirrors read-only emitter
    // logic elsewhere, and TypeEmitter must not depend on a private member.)
    //--------------------------------------------------------------------------
    private static bool HasClassDescriptor(TypeDef t) =>
        !t.IsValueType && !IsDelegateType(t) &&
        (t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor")
         || t.VirtualMethods.Count > 0 || t.IsInterface);

    private static bool IsDelegateType(TypeDef t)
    {
        for (var cur = t; cur != null; cur = cur.BaseType)
            if (cur.BaseTypeName is "System.MulticastDelegate" or "System.Delegate")
                return true;
        return false;
    }

    // The transpiler-emitted C++ function name of a method -- the SAME
    // symbol call sites resolve (CodeGenerator.MethodCppName; the prefix is
    // TypeCppName so generic instantiations get their _int suffix).
    // Hash input is the '+' canonical owner key (ReflectionFullName), exactly
    // like CodeGenerator.MethodCppName, so nested types agree on both sides.
    private static string MethodSymbolName(TypeDef t, MethodDef m)
        => $"{TypeCppName(t)}_{Sanitize(m.Name)}_{SigHash.Hash(ReflectionFullName(t) + "." + m.Name + "(" + m.ParamTypes + ")")}";

    // Return-type encoding for Il2CppMethodInfo.signature (MethodSignatureCoder
    // style, shared with the runtime). Nameful returns carry their FULL encoded
    // name (MethodDef.ReturnTypeName -- refl2 S1/D1: 0x11 VALUETYPE -> "V:...",
    // 0x12 CLASS -> "r:...", 0x15 GENERICINST -> "r:...", SZARRAY/BYREF/PTR/
    // ARRAY/FNPTR analogues), so no reference/value/generic-inst return is
    // unencodable anymore (the G2 reflection guard was removed). Returns with an
    // EMPTY name (void/primitives/generic params) fall to the primitive arms.
    private static string EncodeReturnType(SignatureTypeCode returnType, string returnTypeName)
    {
        if (returnTypeName.Length > 0) return returnTypeName;
        return returnType switch
        {
            SignatureTypeCode.Void => "v",
            SignatureTypeCode.Boolean => "b",
            SignatureTypeCode.Char => "c",
            SignatureTypeCode.SByte => "i1",
            SignatureTypeCode.Byte => "u1",
            SignatureTypeCode.Int16 => "i2",
            SignatureTypeCode.UInt16 => "u2",
            SignatureTypeCode.Int32 or SignatureTypeCode.UInt32 => "i",
            SignatureTypeCode.Int64 => "l",
            SignatureTypeCode.UInt64 => "q",
            SignatureTypeCode.Single => "f",
            SignatureTypeCode.Double => "d",
            SignatureTypeCode.String => "r:System.String",
            SignatureTypeCode.Object => "r:System.Object",
            SignatureTypeCode.IntPtr => "n",
            SignatureTypeCode.UIntPtr => "i",
            // Generic-parameter returns (VAR/MVAR) follow the param-encoding
            // default instantiation (iron rule 2g: every T maps to int32_t).
            SignatureTypeCode.GenericTypeParameter or SignatureTypeCode.GenericMethodParameter => "i",
            // TypedReference (0x16): special internal type -- tolerant fallback to object
            SignatureTypeCode.TypedReference => "r:System.TypedReference",
            _ => throw new NotSupportedException(
                $"typeinfo: method return type {returnType} not representable in Il2CppMethodInfo.signature -- the MethodDef model carries no full type name for this return (model gap, mirror of the M1 static-field classification gap)"),
        };
    }

    //--------------------------------------------------------------------------
    // REFL3 (item 2): the per-parameter metadata string baked into each
    // Il2CppMethodInfo.paramMeta slot (typeinfo.h contract): one "name|outFlag"
    // row per parameter, ';'-joined ("a|0;b|0"), nullptr for parameterless or
    // mismatched metadata. ParamNames/ParamOutFlags come from the Param table
    // (MetadataLoader) and must align with ParamCount; a count mismatch emits
    // nullptr (a defensive constant -- GetParameters then answers an empty
    // array, never memory garbage). Both arrays are in SIGNATURE order.
    //--------------------------------------------------------------------------
    private static string ParamMetaCppString(MethodDef m)
    {
        if (m.ParamCount <= 0 || m.ParamNames is not { Length: > 0 } names) return "nullptr";
        if (names.Length != m.ParamCount) return "nullptr";
        var outFlags = m.ParamOutFlags;
        var rows = new string[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var isOut = outFlags is { Length: > 0 } && i < outFlags.Length && outFlags[i] ? 1 : 0;
            if (names[i].Contains('|') || names[i].Contains(';'))
                throw new NotSupportedException(
                    $"typeinfo method param name '{names[i]}' contains a meta separator ('|' or ';') -- the paramMeta contract cannot encode it (REFL3)");
            rows[i] = $"{names[i]}|{isOut}";
        }
        // C# identifiers are ASCII alphanumeric + '_' (no quote/backslash), but
        // escape defensively so a hand-written .il param name cannot break the
        // emitted C string literal.
        var joined = string.Join(";", rows);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in joined)
        {
            if (ch == '\\') sb.Append("\\\\");
            else if (ch == '"') sb.Append("\\\"");
            else sb.Append(ch);
        }
        return $"\"{sb}\"";
    }

    //--------------------------------------------------------------------------
    // FilterReflectionManifest -- mode B manifest type filtering (reflection-runtime-design.md section 4).
    //
    // Mode B generation rule: user reflection manifest -> generate reflection metadata only
    // for the manifest's types. A manifest entry must first be validated against the
    // type set actually loaded from the assembly set: when an entry names a type that does
    // not exist in the assembly set, the generator cannot produce any metadata for it
    // (Type.GetType returns null for unregistered types) -- keeping it would make
    // reflection-manifest.txt claim a reflection surface that never exists.
    // Therefore entries are filtered by exact full-name match; unmatched entries are
    // dropped (the caller is responsible for the warning).
    //
    // Returns Kept (manifest types that truly exist in the assembly set, in the manifest's
    // original order -- the TypeInfo table emission uses Kept as its generation scope) +
    // Dropped (unmatched entries, also in original order).
    // Comparison is Ordinal exact match (full-name case-sensitive, consistent with the
    // runtime's il2cpp_typeinfo_lookup string key). Note: filtering applies only to the
    // reflection metadata (TypeInfo) scope; the Il2CppClass type descriptors are
    // object-model infrastructure (isinst/castclass/GC) that mode B does not touch
    // (reflection-runtime-design.md section 5.1: Il2CppClass untouched, only TypeInfo is trimmed).
    //--------------------------------------------------------------------------
    public static ReflectionManifestFilterResult FilterReflectionManifest(
        IReadOnlyList<string> manifest, IReadOnlyList<TypeDef> types)
    {
        var typeSet = new HashSet<string>(types.Select(t => t.FullName), StringComparer.Ordinal);
        var kept = new List<string>();
        var dropped = new List<string>();
        foreach (var entry in manifest)
        {
            if (typeSet.Contains(entry))
                kept.Add(entry);
            else
                dropped.Add(entry);
        }
        return new ReflectionManifestFilterResult(kept, dropped);
    }
    //--------------------------------------------------------------------------
    // Windows SDK typedef names -- MEASURED, not guessed (name-clash).
    //
    // A managed type in the GLOBAL namespace sanitizes to its bare name, so a
    // type named LOGFONTW is emitted as `struct LOGFONTW`; <windows.h> has
    // `typedef struct tagLOGFONTW {...} LOGFONTW;`, and C++ forbids an
    // elaborated-type-specifier naming a typedef-name ("using typedef-name
    // 'LOGFONTW' after 'struct'"). The set below is every ordinary identifier
    // that mingw-w64 g++ 16.1.0 rejects in `struct <name>;` after including
    // <windows.h> + <commctrl.h> + <commdlg.h> + <wingdi.h> + <winuser.h>
    // (probe output: .opencode/tmp/impl-name-clash/sdk_typedef_conflicts.txt,
    // 6373 names). Keeping the COMPLETE measured set (not just the LOGFONTW
    // family) means the same failure cannot reappear for TEXTMETRICW,
    // NONCLIENTMETRICSW, MSG, RECT, POINT, SIZE, ... or any other SDK typedef.
    //--------------------------------------------------------------------------
    private static readonly HashSet<string> WindowsSdkTypedefNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ABC", "ABCFLOAT", "ABORTPROC", "ACCEL", "ACCESSTIMEOUT", "ACCESS_ALLOWED_ACE",
            "ACCESS_ALLOWED_CALLBACK_ACE", "ACCESS_ALLOWED_CALLBACK_OBJECT_ACE", "ACCESS_ALLOWED_OBJECT_ACE", "ACCESS_DENIED_ACE", "ACCESS_DENIED_CALLBACK_ACE", "ACCESS_DENIED_CALLBACK_OBJECT_ACE",
            "ACCESS_DENIED_OBJECT_ACE", "ACCESS_MASK", "ACCESS_REASON", "ACCESS_REASONS", "ACCESS_REASON_TYPE", "ACE_HEADER",
            "ACL", "ACL_INFORMATION_CLASS", "ACL_REVISION_INFORMATION", "ACL_SIZE_INFORMATION", "ACTCTX", "ACTCTXA",
            "ACTCTXW", "ACTCTX_COMPATIBILITY_ELEMENT_TYPE", "ACTCTX_REQUESTED_RUN_LEVEL", "ACTCTX_SECTION_KEYED_DATA", "ACTCTX_SECTION_KEYED_DATA_2600", "ACTCTX_SECTION_KEYED_DATA_ASSEMBLY_METADATA",
            "ACTION_HEADER", "ACTIVATION_CONTEXT_ASSEMBLY_DETAILED_INFORMATION", "ACTIVATION_CONTEXT_BASIC_INFORMATION", "ACTIVATION_CONTEXT_COMPATIBILITY_INFORMATION", "ACTIVATION_CONTEXT_DETAILED_INFORMATION", "ACTIVATION_CONTEXT_INFO_CLASS",
            "ACTIVATION_CONTEXT_QUERY_INDEX", "ACTIVATION_CONTEXT_RUN_LEVEL_INFORMATION", "ADAPTER_STATUS", "ADDJOB_INFO_1", "ADDJOB_INFO_1A", "ADDJOB_INFO_1W",
            "ADMINISTRATOR_POWER_POLICY", "ADVF", "ALG_ID", "ALTTABINFO", "ANIMATIONINFO", "APARTMENTID",
            "APC_CALLBACK_FUNCTION", "APPBARDATA", "APPLICATIONLAUNCH_SETTING_VALUE", "APPLICATION_RECOVERY_CALLBACK", "APP_MEMORY_INFORMATION", "APTTYPE",
            "APTTYPEQUALIFIER", "ARM64EC_NT_CONTEXT", "ARM64_NT_CONTEXT", "ARM64_NT_NEON128", "ARM64_RUNTIME_FUNCTION", "ARRAYDESC",
            "ARRAY_INFO", "AR_STATE", "ASSEMBLY_FILE_DETAILED_INFORMATION", "ASYNC_STGMEDIUM", "ATOM", "ATTACH_VIRTUAL_DISK_FLAG",
            "ATTACH_VIRTUAL_DISK_PARAMETERS", "ATTACH_VIRTUAL_DISK_VERSION", "ATTRIBUTES_AND_SID", "AUDIODESCRIPTION", "AUDIT_EVENT_TYPE", "AUTHENTICATEF",
            "AUTHENTICATEINFO", "AUTHENTICODE_EXTRA_CERT_CHAIN_POLICY_PARA", "AUTHENTICODE_EXTRA_CERT_CHAIN_POLICY_STATUS", "AUTHENTICODE_TS_EXTRA_CERT_CHAIN_POLICY_PARA", "AUXCAPS", "AUXCAPS2",
            "AUXCAPS2A", "AUXCAPS2W", "AUXCAPSA", "AUXCAPSW", "AXESLIST", "AXESLISTA",
            "AXESLISTW", "AXISINFO", "AXISINFOA", "AXISINFOW", "ApplicationType", "BAD_TRACK_NUMBER",
            "BATTERY_REPORTING_SCALE", "BCHAR", "BCRYPT_ALGORITHM_IDENTIFIER", "BCRYPT_ALG_HANDLE", "BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO", "BCRYPT_AUTH_TAG_LENGTHS_STRUCT",
            "BCRYPT_DH_KEY_BLOB", "BCRYPT_DH_PARAMETER_HEADER", "BCRYPT_DSA_KEY_BLOB", "BCRYPT_DSA_KEY_BLOB_V2", "BCRYPT_DSA_PARAMETER_HEADER", "BCRYPT_DSA_PARAMETER_HEADER_V2",
            "BCRYPT_ECCKEY_BLOB", "BCRYPT_ECC_CURVE_NAMES", "BCRYPT_HANDLE", "BCRYPT_HASH_HANDLE", "BCRYPT_HASH_OPERATION_TYPE", "BCRYPT_INTERFACE_VERSION",
            "BCRYPT_KEY_BLOB", "BCRYPT_KEY_DATA_BLOB_HEADER", "BCRYPT_KEY_HANDLE", "BCRYPT_KEY_LENGTHS_STRUCT", "BCRYPT_MULTI_HASH_OPERATION", "BCRYPT_MULTI_OBJECT_LENGTH_STRUCT",
            "BCRYPT_MULTI_OPERATION_TYPE", "BCRYPT_OAEP_PADDING_INFO", "BCRYPT_OID", "BCRYPT_OID_LIST", "BCRYPT_PKCS1_PADDING_INFO", "BCRYPT_PROVIDER_NAME",
            "BCRYPT_PSS_PADDING_INFO", "BCRYPT_RSAKEY_BLOB", "BCRYPT_SECRET_HANDLE", "BCryptBuffer", "BCryptBufferDesc", "BEM_FREE_INTERFACE_CALLBACK",
            "BEM_REFERENCE", "BIDI_DATA", "BIDI_REQUEST_CONTAINER", "BIDI_REQUEST_DATA", "BIDI_RESPONSE_CONTAINER", "BIDI_RESPONSE_DATA",
            "BIDI_TYPE", "BINARY_CONTAINER", "BINDF", "BINDF2", "BINDHANDLETYPES", "BINDINFO",
            "BINDINFOF", "BINDINFO_OPTIONS", "BINDPTR", "BINDSPEED", "BINDSTATUS", "BINDSTRING",
            "BINDVERB", "BIND_FLAGS", "BIND_OPTS", "BIND_OPTS2", "BIND_OPTS3", "BIN_COUNT",
            "BIN_RANGE", "BIN_RESULTS", "BIN_TYPES", "BITMAP", "BITMAPCOREHEADER", "BITMAPCOREINFO",
            "BITMAPFILEHEADER", "BITMAPINFO", "BITMAPINFOHEADER", "BITMAPV4HEADER", "BITMAPV5HEADER", "BLENDFUNCTION",
            "BLOB", "BLOBHEADER", "BOOL", "BOOLEAN", "BOOT_AREA_INFO", "BORDERWIDTHS",
            "BSCF", "BSMINFO", "BSTR", "BSTRBLOB", "BULK_SECURITY_TEST_DATA", "BUTTON_IMAGELIST",
            "BUTTON_SPLITINFO", "BYTE", "BYTE_BLOB", "BYTE_SIZEDARR", "BY_HANDLE_FILE_INFORMATION", "BinaryParam",
            "CABOOL", "CABSTR", "CABSTRBLOB", "CAC", "CACHE_DESCRIPTOR", "CACHE_RELATIONSHIP",
            "CACLIPDATA", "CACLSID", "CACY", "CADATE", "CADBL", "CAFILETIME",
            "CAFLT", "CAH", "CAI", "CAL", "CALID", "CALINFO_ENUMPROCA",
            "CALINFO_ENUMPROCEXA", "CALINFO_ENUMPROCEXEX", "CALINFO_ENUMPROCEXW", "CALINFO_ENUMPROCW", "CALLCONV", "CALLTYPE",
            "CALPSTR", "CALPWSTR", "CALTYPE", "CANDIDATEFORM", "CANDIDATELIST", "CAPROPVARIANT",
            "CASCODE", "CAUB", "CAUH", "CAUI", "CAUL", "CBTACTIVATESTRUCT",
            "CBT_CREATEWND", "CBT_CREATEWNDA", "CBT_CREATEWNDW", "CCERT_STORE_PROV_FIND_INFO", "CCHAR", "CCRYPT_OID_INFO",
            "CERT_ACCESS_DESCRIPTION", "CERT_ALT_NAME_ENTRY", "CERT_ALT_NAME_INFO", "CERT_AUTHORITY_INFO_ACCESS", "CERT_AUTHORITY_KEY_ID2_INFO", "CERT_AUTHORITY_KEY_ID_INFO",
            "CERT_BASIC_CONSTRAINTS2_INFO", "CERT_BASIC_CONSTRAINTS_INFO", "CERT_BIOMETRIC_DATA", "CERT_BIOMETRIC_EXT_INFO", "CERT_BLOB", "CERT_CHAIN",
            "CERT_CHAIN_CONTEXT", "CERT_CHAIN_ELEMENT", "CERT_CHAIN_ENGINE_CONFIG", "CERT_CHAIN_FIND_BY_ISSUER_PARA", "CERT_CHAIN_FIND_ISSUER_PARA", "CERT_CHAIN_PARA",
            "CERT_CHAIN_POLICY_PARA", "CERT_CHAIN_POLICY_STATUS", "CERT_CONTEXT", "CERT_CREATE_CONTEXT_PARA", "CERT_CRL_CONTEXT_PAIR", "CERT_DH_PARAMETERS",
            "CERT_DSS_PARAMETERS", "CERT_ECC_SIGNATURE", "CERT_ENHKEY_USAGE", "CERT_EXTENSION", "CERT_EXTENSIONS", "CERT_FORTEZZA_DATA_PROP",
            "CERT_GENERAL_SUBTREE", "CERT_HASHED_URL", "CERT_ID", "CERT_INFO", "CERT_ISSUER_SERIAL_NUMBER", "CERT_KEYGEN_REQUEST_INFO",
            "CERT_KEY_ATTRIBUTES_INFO", "CERT_KEY_CONTEXT", "CERT_KEY_USAGE_RESTRICTION_INFO", "CERT_LDAP_STORE_OPENED_PARA", "CERT_LOGOTYPE_AUDIO", "CERT_LOGOTYPE_AUDIO_INFO",
            "CERT_LOGOTYPE_DATA", "CERT_LOGOTYPE_DETAILS", "CERT_LOGOTYPE_EXT_INFO", "CERT_LOGOTYPE_IMAGE", "CERT_LOGOTYPE_IMAGE_INFO", "CERT_LOGOTYPE_INFO",
            "CERT_LOGOTYPE_REFERENCE", "CERT_NAME_BLOB", "CERT_NAME_CONSTRAINTS_INFO", "CERT_NAME_INFO", "CERT_NAME_VALUE", "CERT_OR_CRL_BLOB",
            "CERT_OR_CRL_BUNDLE", "CERT_OTHER_LOGOTYPE_INFO", "CERT_OTHER_NAME", "CERT_PAIR", "CERT_PHYSICAL_STORE_INFO", "CERT_POLICIES_INFO",
            "CERT_POLICY95_QUALIFIER1", "CERT_POLICY_CONSTRAINTS_INFO", "CERT_POLICY_ID", "CERT_POLICY_INFO", "CERT_POLICY_MAPPING", "CERT_POLICY_MAPPINGS_INFO",
            "CERT_POLICY_QUALIFIER_INFO", "CERT_POLICY_QUALIFIER_NOTICE_REFERENCE", "CERT_POLICY_QUALIFIER_USER_NOTICE", "CERT_PRIVATE_KEY_VALIDITY", "CERT_PUBLIC_KEY_INFO", "CERT_QC_STATEMENT",
            "CERT_QC_STATEMENTS_EXT_INFO", "CERT_RDN", "CERT_RDN_ATTR", "CERT_RDN_VALUE_BLOB", "CERT_REGISTRY_STORE_CLIENT_GPT_PARA", "CERT_REGISTRY_STORE_ROAMING_PARA",
            "CERT_REQUEST_INFO", "CERT_REVOCATION_CHAIN_PARA", "CERT_REVOCATION_CRL_INFO", "CERT_REVOCATION_INFO", "CERT_REVOCATION_PARA", "CERT_REVOCATION_STATUS",
            "CERT_SELECT_CHAIN_PARA", "CERT_SELECT_CRITERIA", "CERT_SERVER_OCSP_RESPONSE_CONTEXT", "CERT_SERVER_OCSP_RESPONSE_OPEN_PARA", "CERT_SIGNED_CONTENT_INFO", "CERT_SIMPLE_CHAIN",
            "CERT_STORE_PROV_FIND_INFO", "CERT_STORE_PROV_INFO", "CERT_STRONG_SIGN_PARA", "CERT_STRONG_SIGN_SERIALIZED_INFO", "CERT_SUBJECT_INFO_ACCESS", "CERT_SUPPORTED_ALGORITHM_INFO",
            "CERT_SYSTEM_STORE_INFO", "CERT_SYSTEM_STORE_RELOCATE_PARA", "CERT_TEMPLATE_EXT", "CERT_TPM_SPECIFICATION_INFO", "CERT_TRUST_LIST_INFO", "CERT_TRUST_STATUS",
            "CERT_USAGE_MATCH", "CERT_X942_DH_PARAMETERS", "CERT_X942_DH_VALIDATION_PARAMS", "CFG_CALL_TARGET_INFO", "CHANGEFILTERSTRUCT", "CHANGEKIND",
            "CHANGER_DEVICE_PROBLEM_TYPE", "CHANGER_ELEMENT", "CHANGER_ELEMENT_LIST", "CHANGER_ELEMENT_STATUS", "CHANGER_ELEMENT_STATUS_EX", "CHANGER_EXCHANGE_MEDIUM",
            "CHANGER_INITIALIZE_ELEMENT_STATUS", "CHANGER_MOVE_MEDIUM", "CHANGER_PRODUCT_DATA", "CHANGER_READ_ELEMENT_STATUS", "CHANGER_SEND_VOLUME_TAG_INFORMATION", "CHANGER_SET_ACCESS",
            "CHANGER_SET_POSITION", "CHAR", "CHARSETINFO", "CHAR_INFO", "CHOOSECOLOR", "CHOOSECOLORA",
            "CHOOSECOLORW", "CHOOSEFONT", "CHOOSEFONTA", "CHOOSEFONTW", "CIEXYZ", "CIEXYZTRIPLE",
            "CIP_STATUS", "CLAIM_SECURITY_ATTRIBUTES_INFORMATION", "CLAIM_SECURITY_ATTRIBUTE_FQBN_VALUE", "CLAIM_SECURITY_ATTRIBUTE_OCTET_STRING_VALUE", "CLAIM_SECURITY_ATTRIBUTE_RELATIVE_V1", "CLAIM_SECURITY_ATTRIBUTE_V1",
            "CLASS_MEDIA_CHANGE_CONTEXT", "CLEANLOCALSTORAGE", "CLIENTCREATESTRUCT", "CLIENT_CALL_RETURN", "CLIPDATA", "CLIPFORMAT",
            "CLSCTX", "CLSID", "CMC_ADD_ATTRIBUTES_INFO", "CMC_ADD_EXTENSIONS_INFO", "CMC_DATA_INFO", "CMC_PEND_INFO",
            "CMC_RESPONSE_INFO", "CMC_STATUS_INFO", "CMC_TAGGED_ATTRIBUTE", "CMC_TAGGED_CERT_REQUEST", "CMC_TAGGED_CONTENT_INFO", "CMC_TAGGED_OTHER_MSG",
            "CMC_TAGGED_REQUEST", "CMSG_ATTR", "CMSG_CMS_RECIPIENT_INFO", "CMSG_CMS_SIGNER_INFO", "CMSG_CNG_CONTENT_DECRYPT_INFO", "CMSG_CONTENT_ENCRYPT_INFO",
            "CMSG_CTRL_ADD_SIGNER_UNAUTH_ATTR_PARA", "CMSG_CTRL_DECRYPT_PARA", "CMSG_CTRL_DEL_SIGNER_UNAUTH_ATTR_PARA", "CMSG_CTRL_KEY_AGREE_DECRYPT_PARA", "CMSG_CTRL_KEY_TRANS_DECRYPT_PARA", "CMSG_CTRL_MAIL_LIST_DECRYPT_PARA",
            "CMSG_CTRL_VERIFY_SIGNATURE_EX_PARA", "CMSG_ENCRYPTED_ENCODE_INFO", "CMSG_ENVELOPED_ENCODE_INFO", "CMSG_HASHED_ENCODE_INFO", "CMSG_KEY_AGREE_ENCRYPT_INFO", "CMSG_KEY_AGREE_KEY_ENCRYPT_INFO",
            "CMSG_KEY_AGREE_RECIPIENT_ENCODE_INFO", "CMSG_KEY_AGREE_RECIPIENT_INFO", "CMSG_KEY_TRANS_ENCRYPT_INFO", "CMSG_KEY_TRANS_RECIPIENT_ENCODE_INFO", "CMSG_KEY_TRANS_RECIPIENT_INFO", "CMSG_MAIL_LIST_ENCRYPT_INFO",
            "CMSG_MAIL_LIST_RECIPIENT_ENCODE_INFO", "CMSG_MAIL_LIST_RECIPIENT_INFO", "CMSG_RC2_AUX_INFO", "CMSG_RC4_AUX_INFO", "CMSG_RECIPIENT_ENCODE_INFO", "CMSG_RECIPIENT_ENCRYPTED_KEY_ENCODE_INFO",
            "CMSG_RECIPIENT_ENCRYPTED_KEY_INFO", "CMSG_SIGNED_AND_ENVELOPED_ENCODE_INFO", "CMSG_SIGNED_ENCODE_INFO", "CMSG_SIGNER_ENCODE_INFO", "CMSG_SIGNER_INFO", "CMSG_SP3_COMPATIBLE_AUX_INFO",
            "CMSG_STREAM_INFO", "CMS_DH_KEY_INFO", "CMS_KEY_INFO", "CM_POWER_DATA", "COAUTHIDENTITY", "COAUTHINFO",
            "CODEBASEHOLD", "CODEPAGE_ENUMPROCA", "CODEPAGE_ENUMPROCW", "COINIT", "COINITBASE", "COLOR16",
            "COLORADJUSTMENT", "COLORMAP", "COLORREF", "COLORSCHEME", "COMBOBOXEXITEMA", "COMBOBOXEXITEMW",
            "COMBOBOXINFO", "COMMCONFIG", "COMMPROP", "COMMTIMEOUTS", "COMM_FAULT_OFFSETS", "COMPACT_VIRTUAL_DISK_FLAG",
            "COMPACT_VIRTUAL_DISK_PARAMETERS", "COMPACT_VIRTUAL_DISK_VERSION", "COMPAREITEMSTRUCT", "COMPARTMENT_ID", "COMPATIBILITY_CONTEXT_ELEMENT", "COMPONENT_FILTER",
            "COMPOSITIONFORM", "COMPUTER_NAME_FORMAT", "COMSD", "COMSTAT", "CONDITION_VARIABLE", "CONNECTDLGSTRUCT",
            "CONNECTDLGSTRUCTA", "CONNECTDLGSTRUCTW", "CONSOLE_CURSOR_INFO", "CONSOLE_FONT_INFO", "CONSOLE_FONT_INFOEX", "CONSOLE_HISTORY_INFO",
            "CONSOLE_READCONSOLE_CONTROL", "CONSOLE_SCREEN_BUFFER_INFO", "CONSOLE_SCREEN_BUFFER_INFOEX", "CONSOLE_SELECTION_INFO", "CONTEXT", "CONTRACT_DESCRIPTION",
            "CONVCONTEXT", "CONVINFO", "COORD", "COPYDATASTRUCT", "COPYFILE2_COPY_PHASE", "COPYFILE2_MESSAGE_ACTION",
            "COPYFILE2_MESSAGE_TYPE", "CORE_PRINTER_DRIVER", "CORE_PRINTER_DRIVERA", "CORE_PRINTER_DRIVERW", "COSERVERINFO", "COWAIT_FLAGS",
            "CPINFO", "CPINFOEX", "CPINFOEXA", "CPINFOEXW", "CPS_URLS", "CPU_SET_INFORMATION_TYPE",
            "CREATESTRUCT", "CREATESTRUCTA", "CREATESTRUCTW", "CREATE_DISK", "CREATE_DISK_GPT", "CREATE_DISK_MBR",
            "CREATE_PROCESS_DEBUG_INFO", "CREATE_THREAD_DEBUG_INFO", "CREATE_USN_JOURNAL_DATA", "CREATE_VIRTUAL_DISK_FLAG", "CREATE_VIRTUAL_DISK_PARAMETERS", "CREATE_VIRTUAL_DISK_VERSION",
            "CRGB", "CRITICAL_SECTION", "CRITICAL_SECTION_DEBUG", "CRL_BLOB", "CRL_CONTEXT", "CRL_DIST_POINT",
            "CRL_DIST_POINTS_INFO", "CRL_DIST_POINT_NAME", "CRL_ENTRY", "CRL_FIND_ISSUED_FOR_PARA", "CRL_INFO", "CRL_ISSUING_DIST_POINT",
            "CRL_REVOCATION_INFO", "CRM_PROTOCOL_ID", "CROSS_CERT_DIST_POINTS_INFO", "CRYPTNET_URL_CACHE_FLUSH_INFO", "CRYPTNET_URL_CACHE_PRE_FETCH_INFO", "CRYPTNET_URL_CACHE_RESPONSE_INFO",
            "CRYPTPROTECT_PROMPTSTRUCT", "CRYPT_3DES_KEY_STATE", "CRYPT_AES_128_KEY_STATE", "CRYPT_AES_256_KEY_STATE", "CRYPT_ALGORITHM_IDENTIFIER", "CRYPT_ASYNC_RETRIEVAL_COMPLETION",
            "CRYPT_ATTRIBUTE", "CRYPT_ATTRIBUTES", "CRYPT_ATTRIBUTE_TYPE_VALUE", "CRYPT_ATTR_BLOB", "CRYPT_BIT_BLOB", "CRYPT_BLOB_ARRAY",
            "CRYPT_CONTENT_INFO", "CRYPT_CONTENT_INFO_SEQUENCE_OF_ANY", "CRYPT_CONTEXTS", "CRYPT_CONTEXT_CONFIG", "CRYPT_CONTEXT_FUNCTIONS", "CRYPT_CONTEXT_FUNCTION_CONFIG",
            "CRYPT_CONTEXT_FUNCTION_PROVIDERS", "CRYPT_CREDENTIALS", "CRYPT_CSP_PROVIDER", "CRYPT_DATA_BLOB", "CRYPT_DECODE_PARA", "CRYPT_DECRYPT_MESSAGE_PARA",
            "CRYPT_DEFAULT_CONTEXT_MULTI_OID_PARA", "CRYPT_DER_BLOB", "CRYPT_DES_KEY_STATE", "CRYPT_DIGEST_BLOB", "CRYPT_ECC_CMS_SHARED_INFO", "CRYPT_ECC_PRIVATE_KEY_INFO",
            "CRYPT_ENCODE_PARA", "CRYPT_ENCRYPTED_PRIVATE_KEY_INFO", "CRYPT_ENCRYPT_MESSAGE_PARA", "CRYPT_ENROLLMENT_NAME_VALUE_PAIR", "CRYPT_GET_TIME_VALID_OBJECT_EXTRA_INFO", "CRYPT_HASH_BLOB",
            "CRYPT_HASH_INFO", "CRYPT_HASH_MESSAGE_PARA", "CRYPT_IMAGE_REF", "CRYPT_IMAGE_REG", "CRYPT_INTEGER_BLOB", "CRYPT_INTERFACE_REG",
            "CRYPT_KEY_PROV_INFO", "CRYPT_KEY_PROV_PARAM", "CRYPT_KEY_SIGN_MESSAGE_PARA", "CRYPT_KEY_VERIFY_MESSAGE_PARA", "CRYPT_MASK_GEN_ALGORITHM", "CRYPT_OBJID_BLOB",
            "CRYPT_OBJID_TABLE", "CRYPT_OID_FUNC_ENTRY", "CRYPT_OID_INFO", "CRYPT_PASSWORD_CREDENTIALS", "CRYPT_PASSWORD_CREDENTIALSA", "CRYPT_PASSWORD_CREDENTIALSW",
            "CRYPT_PKCS12_PBE_PARAMS", "CRYPT_PKCS8_EXPORT_PARAMS", "CRYPT_PKCS8_IMPORT_PARAMS", "CRYPT_PRIVATE_KEY_BLOB_AND_PARAMS", "CRYPT_PRIVATE_KEY_INFO", "CRYPT_PROPERTY_REF",
            "CRYPT_PROVIDERS", "CRYPT_PROVIDER_REF", "CRYPT_PROVIDER_REFS", "CRYPT_PROVIDER_REG", "CRYPT_PSOURCE_ALGORITHM", "CRYPT_RC2_CBC_PARAMETERS",
            "CRYPT_RC4_KEY_STATE", "CRYPT_RETRIEVE_AUX_INFO", "CRYPT_RSAES_OAEP_PARAMETERS", "CRYPT_RSA_SSA_PSS_PARAMETERS", "CRYPT_SEQUENCE_OF_ANY", "CRYPT_SIGN_MESSAGE_PARA",
            "CRYPT_SMART_CARD_ROOT_INFO", "CRYPT_SMIME_CAPABILITIES", "CRYPT_SMIME_CAPABILITY", "CRYPT_TIMESTAMP_ACCURACY", "CRYPT_TIMESTAMP_CONTEXT", "CRYPT_TIMESTAMP_INFO",
            "CRYPT_TIMESTAMP_PARA", "CRYPT_TIMESTAMP_REQUEST", "CRYPT_TIMESTAMP_RESPONSE", "CRYPT_TIME_STAMP_REQUEST_INFO", "CRYPT_UINT_BLOB", "CRYPT_URL_ARRAY",
            "CRYPT_URL_INFO", "CRYPT_VERIFY_CERT_SIGN_STRONG_PROPERTIES_INFO", "CRYPT_VERIFY_MESSAGE_PARA", "CRYPT_X942_OTHER_INFO", "CSPLATFORM", "CSV_NAMESPACE_INFO",
            "CS_STUB_INFO", "CS_TAG_GETTING_ROUTINE", "CS_TYPE_FROM_NETCS_ROUTINE", "CS_TYPE_LOCAL_SIZE_ROUTINE", "CS_TYPE_NET_SIZE_ROUTINE", "CS_TYPE_TO_NETCS_ROUTINE",
            "CTL_ANY_SUBJECT_INFO", "CTL_CONTEXT", "CTL_ENTRY", "CTL_FIND_SUBJECT_PARA", "CTL_FIND_USAGE_PARA", "CTL_INFO",
            "CTL_USAGE", "CTL_USAGE_MATCH", "CTL_VERIFY_USAGE_PARA", "CTL_VERIFY_USAGE_STATUS", "CURRENCY", "CURRENCYFMT",
            "CURRENCYFMTA", "CURRENCYFMTW", "CURSORINFO", "CURSORSHAPE", "CUSTDATA", "CUSTDATAITEM",
            "CWPRETSTRUCT", "CWPSTRUCT", "CY", "DATADIR", "DATAINFO", "DATATYPES_INFO_1",
            "DATATYPES_INFO_1A", "DATATYPES_INFO_1W", "DATA_BLOB", "DATE", "DATEFMT_ENUMPROCA", "DATEFMT_ENUMPROCEXA",
            "DATEFMT_ENUMPROCEXEX", "DATEFMT_ENUMPROCEXW", "DATEFMT_ENUMPROCW", "DATETIMEPICKERINFO", "DCB", "DCOM_CALL_STATE",
            "DDEACK", "DDEADVISE", "DDEDATA", "DDELN", "DDEML_MSG_HOOK_DATA", "DDEPOKE",
            "DDEUP", "DEBUGHOOKINFO", "DEBUG_EVENT", "DECIMAL", "DECRYPTION_STATUS_BUFFER", "DELETEITEMSTRUCT",
            "DELETE_USN_JOURNAL_DATA", "DEPENDENT_DISK_FLAG", "DEP_SYSTEM_POLICY_TYPE", "DESCKIND", "DESIGNVECTOR", "DESKTOPENUMPROC",
            "DESKTOPENUMPROCA", "DESKTOPENUMPROCW", "DETACH_VIRTUAL_DISK_FLAG", "DETECTION_TYPE", "DEVICE_COPY_OFFLOAD_DESCRIPTOR", "DEVICE_DATA_MANAGEMENT_SET_ACTION",
            "DEVICE_DATA_SET_RANGE", "DEVICE_DSM_NOTIFICATION_PARAMETERS", "DEVICE_LB_PROVISIONING_DESCRIPTOR", "DEVICE_MANAGE_DATA_SET_ATTRIBUTES", "DEVICE_MEDIA_INFO", "DEVICE_POWER_DESCRIPTOR",
            "DEVICE_POWER_STATE", "DEVICE_SEEK_PENALTY_DESCRIPTOR", "DEVICE_TRIM_DESCRIPTOR", "DEVICE_WRITE_AGGREGATION_DESCRIPTOR", "DEVMODE", "DEVMODEA",
            "DEVMODEW", "DEVNAMES", "DHPRIVKEY_VER3", "DHPUBKEY", "DHPUBKEY_VER3", "DIBSECTION",
            "DISCARDCACHE", "DISCDLGSTRUCT", "DISCDLGSTRUCTA", "DISCDLGSTRUCTW", "DISK_CACHE_INFORMATION", "DISK_CACHE_RETENTION_PRIORITY",
            "DISK_CONTROLLER_NUMBER", "DISK_DETECTION_INFO", "DISK_EXTENT", "DISK_EX_INT13_INFO", "DISK_GEOMETRY", "DISK_GEOMETRY_EX",
            "DISK_GROW_PARTITION", "DISK_HISTOGRAM", "DISK_INT13_INFO", "DISK_LOGGING", "DISK_PARTITION_INFO", "DISK_PERFORMANCE",
            "DISK_RECORD", "DISPATCHER_CONTEXT", "DISPATCHER_CONTEXT_ARM64", "DISPATCHER_CONTEXT_NONVOLREG_ARM64", "DISPID", "DISPLAYCONFIG_COLOR_ENCODING",
            "DISPLAYCONFIG_DEVICE_INFO_TYPE", "DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO", "DISPLAYCONFIG_GET_MONITOR_SPECIALIZATION", "DISPLAYCONFIG_MODE_INFO_TYPE", "DISPLAYCONFIG_PIXELFORMAT", "DISPLAYCONFIG_ROTATION",
            "DISPLAYCONFIG_SCALING", "DISPLAYCONFIG_SCANLINE_ORDERING", "DISPLAYCONFIG_SDR_WHITE_LEVEL", "DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE", "DISPLAYCONFIG_SET_MONITOR_SPECIALIZATION", "DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY",
            "DISPLAY_DEVICE", "DISPLAY_DEVICEA", "DISPLAY_DEVICEW", "DISPPARAMS", "DLGITEMTEMPLATE", "DLGPROC",
            "DLGTEMPLATE", "DLL_DIRECTORY_COOKIE", "DOCINFO", "DOCINFOA", "DOCINFOW", "DOC_INFO_1",
            "DOC_INFO_1A", "DOC_INFO_1W", "DOC_INFO_2", "DOC_INFO_2A", "DOC_INFO_2W", "DOC_INFO_3",
            "DOC_INFO_3A", "DOC_INFO_3W", "DOMNodeType", "DOUBLE", "DPASTREAMINFO", "DPI_AWARENESS_CONTEXT",
            "DRAGINFO", "DRAGINFOA", "DRAGINFOW", "DRAGLISTINFO", "DRAWITEMSTRUCT", "DRAWPATRECT",
            "DRAWSTATEPROC", "DRAWTEXTPARAMS", "DRIVERMSGPROC", "DRIVERPROC", "DRIVERSTATUS", "DRIVER_INFO_1",
            "DRIVER_INFO_1A", "DRIVER_INFO_1W", "DRIVER_INFO_2", "DRIVER_INFO_2A", "DRIVER_INFO_2W", "DRIVER_INFO_3",
            "DRIVER_INFO_3A", "DRIVER_INFO_3W", "DRIVER_INFO_4", "DRIVER_INFO_4A", "DRIVER_INFO_4W", "DRIVER_INFO_5",
            "DRIVER_INFO_5A", "DRIVER_INFO_5W", "DRIVER_INFO_6", "DRIVER_INFO_6A", "DRIVER_INFO_6W", "DRIVER_INFO_8",
            "DRIVER_INFO_8A", "DRIVER_INFO_8W", "DRIVE_LAYOUT_INFORMATION", "DRIVE_LAYOUT_INFORMATION_EX", "DRIVE_LAYOUT_INFORMATION_GPT", "DRIVE_LAYOUT_INFORMATION_MBR",
            "DROPSTRUCT", "DRVCALLBACK", "DRVCONFIGINFO", "DSAFIPSVERSION_ENUM", "DSSPRIVKEY_VER3", "DSSPUBKEY",
            "DSSPUBKEY_VER3", "DSSSEED", "DVASPECT", "DVTARGETDEVICE", "DWORD", "DWORD32",
            "DWORD64", "DWORDLONG", "DWORD_BLOB", "DWORD_PTR", "DWORD_SIZEDARR", "DYNAMIC_TIME_ZONE_INFORMATION",
            "EDITBALLOONTIP", "EDITWORDBREAKPROC", "EDITWORDBREAKPROCA", "EDITWORDBREAKPROCW", "EFS_CERTIFICATE_BLOB", "EFS_HASH_BLOB",
            "EFS_KEY_INFO", "EFS_RPC_BLOB", "ELEMDESC", "ELEMENT_TYPE", "EMR", "EMRABORTPATH",
            "EMRALPHABLEND", "EMRANGLEARC", "EMRARC", "EMRARCTO", "EMRBEGINPATH", "EMRBITBLT",
            "EMRCHORD", "EMRCLOSEFIGURE", "EMRCOLORCORRECTPALETTE", "EMRCOLORMATCHTOTARGET", "EMRCREATEBRUSHINDIRECT", "EMRCREATECOLORSPACE",
            "EMRCREATECOLORSPACEW", "EMRCREATEDIBPATTERNBRUSHPT", "EMRCREATEMONOBRUSH", "EMRCREATEPALETTE", "EMRCREATEPEN", "EMRDELETECOLORSPACE",
            "EMRDELETEOBJECT", "EMRDRAWESCAPE", "EMRELLIPSE", "EMRENDPATH", "EMREOF", "EMREXCLUDECLIPRECT",
            "EMREXTCREATEFONTINDIRECTW", "EMREXTCREATEPEN", "EMREXTESCAPE", "EMREXTFLOODFILL", "EMREXTSELECTCLIPRGN", "EMREXTTEXTOUTA",
            "EMREXTTEXTOUTW", "EMRFILLPATH", "EMRFILLRGN", "EMRFLATTENPATH", "EMRFORMAT", "EMRFRAMERGN",
            "EMRGDICOMMENT", "EMRGLSBOUNDEDRECORD", "EMRGLSRECORD", "EMRGRADIENTFILL", "EMRINTERSECTCLIPRECT", "EMRINVERTRGN",
            "EMRLINETO", "EMRMASKBLT", "EMRMODIFYWORLDTRANSFORM", "EMRMOVETOEX", "EMRNAMEDESCAPE", "EMROFFSETCLIPRGN",
            "EMRPAINTRGN", "EMRPIE", "EMRPIXELFORMAT", "EMRPLGBLT", "EMRPOLYBEZIER", "EMRPOLYBEZIER16",
            "EMRPOLYBEZIERTO", "EMRPOLYBEZIERTO16", "EMRPOLYDRAW", "EMRPOLYDRAW16", "EMRPOLYGON", "EMRPOLYGON16",
            "EMRPOLYLINE", "EMRPOLYLINE16", "EMRPOLYLINETO", "EMRPOLYLINETO16", "EMRPOLYPOLYGON", "EMRPOLYPOLYGON16",
            "EMRPOLYPOLYLINE", "EMRPOLYPOLYLINE16", "EMRPOLYTEXTOUTA", "EMRPOLYTEXTOUTW", "EMRREALIZEPALETTE", "EMRRECTANGLE",
            "EMRRESIZEPALETTE", "EMRRESTOREDC", "EMRROUNDRECT", "EMRSAVEDC", "EMRSCALEVIEWPORTEXTEX", "EMRSCALEWINDOWEXTEX",
            "EMRSELECTCLIPPATH", "EMRSELECTCOLORSPACE", "EMRSELECTOBJECT", "EMRSELECTPALETTE", "EMRSETARCDIRECTION", "EMRSETBKCOLOR",
            "EMRSETBKMODE", "EMRSETBRUSHORGEX", "EMRSETCOLORADJUSTMENT", "EMRSETCOLORSPACE", "EMRSETDIBITSTODEVICE", "EMRSETICMMODE",
            "EMRSETICMPROFILE", "EMRSETICMPROFILEA", "EMRSETICMPROFILEW", "EMRSETLAYOUT", "EMRSETMAPMODE", "EMRSETMAPPERFLAGS",
            "EMRSETMETARGN", "EMRSETMITERLIMIT", "EMRSETPALETTEENTRIES", "EMRSETPIXELV", "EMRSETPOLYFILLMODE", "EMRSETROP2",
            "EMRSETSTRETCHBLTMODE", "EMRSETTEXTALIGN", "EMRSETTEXTCOLOR", "EMRSETVIEWPORTEXTEX", "EMRSETVIEWPORTORGEX", "EMRSETWINDOWEXTEX",
            "EMRSETWINDOWORGEX", "EMRSETWORLDTRANSFORM", "EMRSTRETCHBLT", "EMRSTRETCHDIBITS", "EMRSTROKEANDFILLPATH", "EMRSTROKEPATH",
            "EMRTEXT", "EMRTRANSPARENTBLT", "EMRWIDENPATH", "ENCLAVE_CREATE_INFO_SGX", "ENCLAVE_CREATE_INFO_VBS", "ENCLAVE_CREATE_INFO_VBS_BASIC",
            "ENCLAVE_INIT_INFO_SGX", "ENCLAVE_INIT_INFO_VBS", "ENCLAVE_INIT_INFO_VBS_BASIC", "ENCLAVE_LOAD_DATA_VBS_BASIC", "ENCLAVE_TARGET_FUNCTION", "ENCRYPTED_DATA_INFO",
            "ENCRYPTION_BUFFER", "ENCRYPTION_CERTIFICATE", "ENCRYPTION_CERTIFICATE_HASH", "ENCRYPTION_CERTIFICATE_HASH_LIST", "ENCRYPTION_CERTIFICATE_LIST", "ENHMETAHEADER",
            "ENHMETARECORD", "ENHMFENUMPROC", "ENLISTMENT_BASIC_INFORMATION", "ENLISTMENT_CRM_INFORMATION", "ENLISTMENT_INFORMATION_CLASS", "ENUMLOGFONT",
            "ENUMLOGFONTA", "ENUMLOGFONTEX", "ENUMLOGFONTEXA", "ENUMLOGFONTEXDV", "ENUMLOGFONTEXDVA", "ENUMLOGFONTEXDVW",
            "ENUMLOGFONTEXW", "ENUMLOGFONTW", "ENUMRESLANGPROCA", "ENUMRESLANGPROCW", "ENUMRESNAMEPROCA", "ENUMRESNAMEPROCW",
            "ENUMRESTYPEPROCA", "ENUMRESTYPEPROCW", "ENUMTEXTMETRIC", "ENUMTEXTMETRICA", "ENUMTEXTMETRICW", "ENUMUILANG",
            "ENUM_SERVICE_STATUS", "ENUM_SERVICE_STATUSA", "ENUM_SERVICE_STATUSW", "ENUM_SERVICE_STATUS_PROCESS", "ENUM_SERVICE_STATUS_PROCESSA", "ENUM_SERVICE_STATUS_PROCESSW",
            "EOLE_AUTHENTICATION_CAPABILITIES", "EPrintXPSJobOperation", "EPrintXPSJobProgress", "EVENTLOGRECORD", "EVENTLOG_FULL_INFORMATION", "EVENTMSG",
            "EVENTSFORLOGFILE", "EV_EXTRA_CERT_CHAIN_POLICY_PARA", "EV_EXTRA_CERT_CHAIN_POLICY_STATUS", "EXCEPINFO", "EXCEPTION_DEBUG_INFO", "EXCEPTION_DISPOSITION",
            "EXCEPTION_POINTERS", "EXCEPTION_RECORD", "EXCEPTION_RECORD32", "EXCEPTION_RECORD64", "EXCEPTION_REGISTRATION", "EXCEPTION_REGISTRATION_RECORD",
            "EXCEPTION_ROUTINE", "EXECUTION_STATE", "EXFAT_STATISTICS", "EXIT_PROCESS_DEBUG_INFO", "EXIT_THREAD_DEBUG_INFO", "EXPAND_VIRTUAL_DISK_FLAG",
            "EXPAND_VIRTUAL_DISK_PARAMETERS", "EXPAND_VIRTUAL_DISK_VERSION", "EXPR_EVAL", "EXTCONN", "EXTENDED_ENCRYPTED_DATA_INFO", "EXTLOGFONT",
            "EXTLOGFONTA", "EXTLOGFONTW", "EXTLOGPEN", "EXTLOGPEN32", "ExtendedErrorParamTypes", "FARPROC",
            "FAT_STATISTICS", "FCHAR", "FD_SET", "FILEMUIINFO", "FILEOP_FLAGS", "FILESYSTEM_STATISTICS",
            "FILETIME", "FILE_ALLOCATED_RANGE_BUFFER", "FILE_ALLOCATION_INFO", "FILE_ATTRIBUTE_TAG_INFO", "FILE_BASIC_INFO", "FILE_CASE_SENSITIVE_INFO",
            "FILE_CASE_SENSITIVE_INFORMATION", "FILE_COMPRESSION_INFO", "FILE_DISPOSITION_INFO", "FILE_DISPOSITION_INFO_EX", "FILE_END_OF_FILE_INFO", "FILE_FS_PERSISTENT_VOLUME_INFORMATION",
            "FILE_FULL_DIR_INFO", "FILE_ID_128", "FILE_ID_BOTH_DIR_INFO", "FILE_ID_TYPE", "FILE_INFO_BY_HANDLE_CLASS", "FILE_IO_PRIORITY_HINT_INFO",
            "FILE_MAKE_COMPATIBLE_BUFFER", "FILE_NAME_INFO", "FILE_NOTIFY_EXTENDED_INFORMATION", "FILE_NOTIFY_FULL_INFORMATION", "FILE_NOTIFY_INFORMATION", "FILE_OBJECTID_BUFFER",
            "FILE_PREFETCH", "FILE_PREFETCH_EX", "FILE_QUERY_ON_DISK_VOL_INFO_BUFFER", "FILE_QUERY_SPARING_BUFFER", "FILE_REMOTE_PROTOCOL_INFO", "FILE_RENAME_INFO",
            "FILE_SEGMENT_ELEMENT", "FILE_SET_DEFECT_MGMT_BUFFER", "FILE_SET_SPARSE_BUFFER", "FILE_STANDARD_INFO", "FILE_STAT_INFORMATION", "FILE_STAT_LX_INFORMATION",
            "FILE_STREAM_INFO", "FILE_SYSTEM_RECOGNITION_INFORMATION", "FILE_SYSTEM_RECOGNITION_STRUCTURE", "FILE_TYPE_NOTIFICATION_INPUT", "FILE_ZERO_DATA_INFORMATION", "FILTERKEYS",
            "FINDEX_INFO_LEVELS", "FINDEX_SEARCH_OPS", "FINDREPLACE", "FINDREPLACEA", "FINDREPLACEW", "FIND_BY_SID_DATA",
            "FIND_BY_SID_OUTPUT", "FIND_NAME_BUFFER", "FIND_NAME_HEADER", "FIRMWARE_TYPE", "FIXED", "FLAGGED_BYTE_BLOB",
            "FLAGGED_WORD_BLOB", "FLAG_STGMEDIUM", "FLASHWINFO", "FLOAT", "FLOAT128", "FLONG",
            "FMTID", "FNCALLBACK", "FOCUS_EVENT_RECORD", "FONTENUMPROC", "FONTENUMPROCA", "FONTENUMPROCW",
            "FONTSIGNATURE", "FORMATETC", "FORMAT_EX_PARAMETERS", "FORMAT_PARAMETERS", "FORM_INFO_1", "FORM_INFO_1A",
            "FORM_INFO_1W", "FORM_INFO_2", "FORM_INFO_2A", "FORM_INFO_2W", "FOURCC", "FPO_DATA",
            "FSCTL_QUERY_FAT_BPB_BUFFER", "FSHORT", "FULL_PTR_TO_REFID_ELEMENT", "FULL_PTR_XLAT_TABLES", "FUNCDESC", "FUNCFLAGS",
            "FUNCKIND", "FXPT16DOT16", "FXPT2DOT30", "GCP_RESULTS", "GCP_RESULTSA", "GCP_RESULTSW",
            "GDI_NONREMOTE", "GDI_OBJECT", "GENERIC_BINDING_INFO", "GENERIC_BINDING_ROUTINE", "GENERIC_BINDING_ROUTINE_PAIR", "GENERIC_MAPPING",
            "GENERIC_UNBIND_ROUTINE", "GEOCLASS", "GEOID", "GEOTYPE", "GEO_ENUMPROC", "GESTURECONFIG",
            "GESTUREINFO", "GESTURENOTIFYSTRUCT", "GETVERSIONINPARAMS", "GET_CHANGER_PARAMETERS", "GET_FILEEX_INFO_LEVELS", "GET_LENGTH_INFORMATION",
            "GET_MEDIA_TYPES", "GET_STORAGE_DEPENDENCY_FLAG", "GET_VIRTUAL_DISK_INFO", "GET_VIRTUAL_DISK_INFO_VERSION", "GLOBALHANDLE", "GLOBALOPT_EH_VALUES",
            "GLOBALOPT_PROPERTIES", "GLOBALOPT_RO_FLAGS", "GLOBALOPT_RPCTP_VALUES", "GLOBALOPT_UNMARSHALING_POLICY_VALUES", "GLYPHMETRICS", "GLYPHMETRICSFLOAT",
            "GLYPHSET", "GOBJENUMPROC", "GRADIENT_RECT", "GRADIENT_TRIANGLE", "GRAYSTRINGPROC", "GROUP_AFFINITY",
            "GROUP_AFFINITY32", "GROUP_AFFINITY64", "GROUP_RELATIONSHIP", "GUID", "GUITHREADINFO", "HACCEL",
            "HALF_PTR", "HANDLE", "HANDLETABLE", "HANDLE_PTR", "HARDWAREHOOKSTRUCT", "HARDWAREINPUT",
            "HARDWARE_COUNTER_DATA", "HARDWARE_COUNTER_TYPE", "HASHALGORITHM_ENUM", "HBITMAP", "HBRUSH", "HCERTCHAINENGINE",
            "HCERTSTORE", "HCERTSTOREPROV", "HCERT_SERVER_OCSP_RESPONSE", "HCOLORSPACE", "HCONTEXT", "HCONV",
            "HCONVLIST", "HCRYPTASYNC", "HCRYPTDEFAULTCONTEXT", "HCRYPTHASH", "HCRYPTKEY", "HCRYPTMSG",
            "HCRYPTOIDFUNCADDR", "HCRYPTOIDFUNCSET", "HCRYPTPROV", "HCRYPTPROV_LEGACY", "HCRYPTPROV_OR_NCRYPT_KEY_HANDLE", "HCURSOR",
            "HDC", "HDDEDATA", "HDESK", "HDEVNOTIFY", "HDHITTESTINFO", "HDITEMA",
            "HDITEMW", "HDLAYOUT", "HDPA", "HDROP", "HDRVR", "HDSA",
            "HDWP", "HD_TEXTFILTERA", "HD_TEXTFILTERW", "HEAP_INFORMATION_CLASS", "HEAP_SUMMARY", "HELPINFO",
            "HELPPOLY", "HELPWININFO", "HELPWININFOA", "HELPWININFOW", "HENHMETAFILE", "HFILE",
            "HFONT", "HGDIOBJ", "HGESTUREINFO", "HGLOBAL", "HGLRC", "HHOOK",
            "HICON", "HIGHCONTRAST", "HIGHCONTRASTA", "HIGHCONTRASTW", "HIMAGELIST", "HIMC",
            "HIMCC", "HINSTANCE", "HISTOGRAM_BUCKET", "HIT_LOGGING_INFO", "HKEY", "HKL",
            "HLOCAL", "HLSURF", "HMAC_INFO", "HMENU", "HMETAFILE", "HMETAFILEPICT",
            "HMIDI", "HMIDIIN", "HMIDIOUT", "HMIDISTRM", "HMIXER", "HMIXEROBJ",
            "HMMIO", "HMODULE", "HMONITOR", "HOLEMENU", "HOOKPROC", "HOSTENT",
            "HPALETTE", "HPCON", "HPEN", "HPOWERNOTIFY", "HPROPSHEETPAGE", "HPSTR",
            "HRAWINPUT", "HREFTYPE", "HRESULT", "HRGN", "HRSRC", "HSPRITE",
            "HSTR", "HSZ", "HSZPAIR", "HTASK", "HTOUCHINPUT", "HTREEITEM",
            "HTTPSPolicyCallbackData", "HUMPD", "HWAVE", "HWAVEIN", "HWAVEOUT", "HWINEVENTHOOK",
            "HWINSTA", "HWND", "HW_PROFILE_INFO", "HW_PROFILE_INFOA", "HW_PROFILE_INFOW", "HYPER_SIZEDARR",
            "ICMENUMPROCA", "ICMENUMPROCW", "ICONINFO", "ICONINFOEX", "ICONINFOEXA", "ICONINFOEXW",
            "ICONMETRICS", "ICONMETRICSA", "ICONMETRICSW", "IDEREGS", "IDLDESC", "IDL_CS_CONVERT",
            "IID", "IMAGEINFO", "IMAGELISTDRAWPARAMS", "IMAGE_ALPHA64_RUNTIME_FUNCTION_ENTRY", "IMAGE_ALPHA_RUNTIME_FUNCTION_ENTRY", "IMAGE_ARCHITECTURE_ENTRY",
            "IMAGE_ARCHITECTURE_HEADER", "IMAGE_ARCHIVE_MEMBER_HEADER", "IMAGE_ARM64_RUNTIME_FUNCTION_ENTRY", "IMAGE_ARM_RUNTIME_FUNCTION_ENTRY", "IMAGE_AUX_SYMBOL", "IMAGE_AUX_SYMBOL_EX",
            "IMAGE_BASE_RELOCATION", "IMAGE_BOUND_FORWARDER_REF", "IMAGE_BOUND_IMPORT_DESCRIPTOR", "IMAGE_CE_RUNTIME_FUNCTION_ENTRY", "IMAGE_COFF_SYMBOLS_HEADER", "IMAGE_DATA_DIRECTORY",
            "IMAGE_DEBUG_DIRECTORY", "IMAGE_DEBUG_MISC", "IMAGE_DELAYLOAD_DESCRIPTOR", "IMAGE_DOS_HEADER", "IMAGE_EXPORT_DIRECTORY", "IMAGE_FILE_HEADER",
            "IMAGE_FUNCTION_ENTRY", "IMAGE_FUNCTION_ENTRY64", "IMAGE_IA64_RUNTIME_FUNCTION_ENTRY", "IMAGE_IMPORT_BY_NAME", "IMAGE_IMPORT_DESCRIPTOR", "IMAGE_LINENUMBER",
            "IMAGE_LOAD_CONFIG_CODE_INTEGRITY", "IMAGE_LOAD_CONFIG_DIRECTORY", "IMAGE_LOAD_CONFIG_DIRECTORY32", "IMAGE_LOAD_CONFIG_DIRECTORY64", "IMAGE_NT_HEADERS", "IMAGE_NT_HEADERS32",
            "IMAGE_NT_HEADERS64", "IMAGE_OPTIONAL_HEADER", "IMAGE_OPTIONAL_HEADER32", "IMAGE_OPTIONAL_HEADER64", "IMAGE_OS2_HEADER", "IMAGE_RELOCATION",
            "IMAGE_RESOURCE_DATA_ENTRY", "IMAGE_RESOURCE_DIRECTORY", "IMAGE_RESOURCE_DIRECTORY_ENTRY", "IMAGE_RESOURCE_DIRECTORY_STRING", "IMAGE_RESOURCE_DIR_STRING_U", "IMAGE_ROM_HEADERS",
            "IMAGE_ROM_OPTIONAL_HEADER", "IMAGE_RUNTIME_FUNCTION_ENTRY", "IMAGE_SECTION_HEADER", "IMAGE_SEPARATE_DEBUG_HEADER", "IMAGE_SYMBOL", "IMAGE_SYMBOL_EX",
            "IMAGE_THUNK_DATA", "IMAGE_THUNK_DATA32", "IMAGE_THUNK_DATA64", "IMAGE_TLS_DIRECTORY", "IMAGE_TLS_DIRECTORY32", "IMAGE_TLS_DIRECTORY64",
            "IMAGE_VXD_HEADER", "IMCENUMPROC", "IMECHARPOSITION", "IMEMENUITEMINFO", "IMEMENUITEMINFOA", "IMEMENUITEMINFOW",
            "INITCOMMONCONTROLSEX", "INIT_ONCE", "INPUT", "INPUT_MESSAGE_DEVICE_TYPE", "INPUT_MESSAGE_ORIGIN_ID", "INPUT_MESSAGE_SOURCE",
            "INPUT_RECORD", "INT", "INT16", "INT32", "INT64", "INT8",
            "INTERFACEDATA", "INTERFACEINFO", "INTERNETFEATURELIST", "INT_PTR", "INVOKEKIND", "IN_ADDR",
            "IO_COUNTERS", "IP_MREQ", "I_RPC_HANDLE", "I_RPC_MUTEX", "I_RpcFreeCalloutStateFn", "I_RpcPerformCalloutFn",
            "I_RpcProxyCallbackInterface", "I_RpcProxyGetClientAddressFn", "I_RpcProxyGetConnectionTimeoutFn", "I_RpcProxyIsValidMachineFn", "JIT_DEBUG_INFO", "JIT_DEBUG_INFO32",
            "JIT_DEBUG_INFO64", "JOBOBJECTINFOCLASS", "JOBOBJECT_ASSOCIATE_COMPLETION_PORT", "JOBOBJECT_BASIC_ACCOUNTING_INFORMATION", "JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION", "JOBOBJECT_BASIC_LIMIT_INFORMATION",
            "JOBOBJECT_BASIC_PROCESS_ID_LIST", "JOBOBJECT_BASIC_UI_RESTRICTIONS", "JOBOBJECT_CPU_RATE_CONTROL_INFORMATION", "JOBOBJECT_END_OF_JOB_TIME_INFORMATION", "JOBOBJECT_EXTENDED_LIMIT_INFORMATION", "JOBOBJECT_IO_ATTRIBUTION_INFORMATION",
            "JOBOBJECT_IO_ATTRIBUTION_STATS", "JOBOBJECT_IO_RATE_CONTROL_INFORMATION_NATIVE_V1", "JOBOBJECT_JOBSET_INFORMATION", "JOBOBJECT_LIMIT_VIOLATION_INFORMATION", "JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION", "JOBOBJECT_RATE_CONTROL_TOLERANCE",
            "JOBOBJECT_RATE_CONTROL_TOLERANCE_INTERVAL", "JOBOBJECT_SECURITY_LIMIT_INFORMATION", "JOB_INFO_1", "JOB_INFO_1A", "JOB_INFO_1W", "JOB_INFO_2",
            "JOB_INFO_2A", "JOB_INFO_2W", "JOB_INFO_3", "JOB_SET_ARRAY", "JOYCAPS", "JOYCAPS2",
            "JOYCAPS2A", "JOYCAPS2W", "JOYCAPSA", "JOYCAPSW", "JOYINFO", "JOYINFOEX",
            "KAFFINITY", "KBDLLHOOKSTRUCT", "KCRM_MARSHAL_HEADER", "KCRM_PROTOCOL_BLOB", "KCRM_TRANSACTION_BLOB", "KEAPUBKEY",
            "KERNEL_CET_CONTEXT", "KERNINGPAIR", "KEYARRAY", "KEYBDINPUT", "KEY_EVENT_RECORD", "KEY_TYPE_SUBTYPE",
            "KNONVOLATILE_CONTEXT_POINTERS", "KNONVOLATILE_CONTEXT_POINTERS_ARM64", "KSPIN_LOCK", "KTMOBJECT_CURSOR", "KTMOBJECT_TYPE", "LANA_ENUM",
            "LANGGROUPLOCALE_ENUMPROCA", "LANGGROUPLOCALE_ENUMPROCW", "LANGID", "LANGUAGEGROUP_ENUMPROCA", "LANGUAGEGROUP_ENUMPROCW", "LARGE_INTEGER",
            "LASTINPUTINFO", "LATENCY_TIME", "LAYERPLANEDESCRIPTOR", "LCID", "LCSCSTYPE", "LCSGAMUTMATCH",
            "LCTYPE", "LC_ID", "LDT_ENTRY", "LGRPID", "LHITTESTINFO", "LIBFLAGS",
            "LINEDDAPROC", "LINGER", "LINKSRCDESCRIPTOR", "LIST_ENTRY", "LITEM", "LOAD_DLL_DEBUG_INFO",
            "LOCALESIGNATURE", "LOCALE_ENUMPROCA", "LOCALE_ENUMPROCEX", "LOCALE_ENUMPROCW", "LOCALHANDLE", "LOCKTYPE",
            "LOGBRUSH", "LOGBRUSH32", "LOGCOLORSPACE", "LOGCOLORSPACEA", "LOGCOLORSPACEW", "LOGFONT",
            "LOGFONTA", "LOGFONTW", "LOGICAL_PROCESSOR_RELATIONSHIP", "LOGPALETTE", "LOGPEN", "LONG",
            "LONG32", "LONG64", "LONGLONG", "LONG_PTR", "LOOKUP_STREAM_FROM_CLUSTER_ENTRY", "LOOKUP_STREAM_FROM_CLUSTER_INPUT",
            "LOOKUP_STREAM_FROM_CLUSTER_OUTPUT", "LPABC", "LPABCFLOAT", "LPACCEL", "LPACCESSTIMEOUT", "LPADDJOB_INFO_1",
            "LPADDJOB_INFO_1A", "LPADDJOB_INFO_1W", "LPADDREXCLUSIONCONTROL", "LPADDRTRACKINGCONTROL", "LPADVISESINK", "LPADVISESINK2",
            "LPALTTABINFO", "LPANIMATIONINFO", "LPARAM", "LPAUDIODESCRIPTION", "LPAUTHENTICATION", "LPAUTHENTICATIONEX",
            "LPAUXCAPS", "LPAUXCAPS2", "LPAUXCAPS2A", "LPAUXCAPS2W", "LPAUXCAPSA", "LPAUXCAPSW",
            "LPAXESLIST", "LPAXESLISTA", "LPAXESLISTW", "LPAXISINFO", "LPAXISINFOA", "LPAXISINFOW",
            "LPBC", "LPBIDI_DATA", "LPBIDI_REQUEST_CONTAINER", "LPBIDI_REQUEST_DATA", "LPBIDI_RESPONSE_CONTAINER", "LPBIDI_RESPONSE_DATA",
            "LPBINDCALLBACKREDIRECT", "LPBINDCTX", "LPBINDHOST", "LPBINDING", "LPBINDPROTOCOL", "LPBINDPTR",
            "LPBINDSTATUSCALLBACK", "LPBINDSTATUSCALLBACKEX", "LPBIND_OPTS", "LPBIND_OPTS2", "LPBIND_OPTS3", "LPBITMAP",
            "LPBITMAPCOREHEADER", "LPBITMAPCOREINFO", "LPBITMAPFILEHEADER", "LPBITMAPINFO", "LPBITMAPINFOHEADER", "LPBITMAPV4HEADER",
            "LPBITMAPV5HEADER", "LPBLOB", "LPBOOL", "LPBORDERWIDTHS", "LPBSTR", "LPBSTRBLOB",
            "LPBYTE", "LPBY_HANDLE_FILE_INFORMATION", "LPCANCELMETHODCALLS", "LPCANDIDATEFORM", "LPCANDIDATELIST", "LPCATALOGFILEINFO",
            "LPCBORDERWIDTHS", "LPCBTACTIVATESTRUCT", "LPCBT_CREATEWND", "LPCBT_CREATEWNDA", "LPCBT_CREATEWNDW", "LPCBYTE",
            "LPCCH", "LPCCHOOKPROC", "LPCDLGTEMPLATE", "LPCDLGTEMPLATEA", "LPCDLGTEMPLATEW", "LPCFHOOKPROC",
            "LPCGUID", "LPCH", "LPCHARSETINFO", "LPCHOOSECOLOR", "LPCHOOSECOLORA", "LPCHOOSECOLORW",
            "LPCHOOSEFONT", "LPCHOOSEFONTA", "LPCHOOSEFONTW", "LPCIEXYZ", "LPCIEXYZTRIPLE", "LPCLASSFACTORY",
            "LPCLIENTCREATESTRUCT", "LPCLIPFORMAT", "LPCLSID", "LPCMENUINFO", "LPCMENUITEMINFO", "LPCMENUITEMINFOA",
            "LPCMENUITEMINFOW", "LPCMMCKINFO", "LPCMMIOINFO", "LPCODEBASEHOLD", "LPCODEINSTALL", "LPCOLESTR",
            "LPCOLORADJUSTMENT", "LPCOLORMAP", "LPCOLORREF", "LPCOLORSCHEME", "LPCOMBOBOXINFO", "LPCOMMCONFIG",
            "LPCOMMPROP", "LPCOMMTIMEOUTS", "LPCOMPAREITEMSTRUCT", "LPCOMPOSITIONFORM", "LPCOMSTAT", "LPCONNECTDLGSTRUCT",
            "LPCONNECTDLGSTRUCTA", "LPCONNECTDLGSTRUCTW", "LPCONTEXT", "LPCPINFO", "LPCPINFOEX", "LPCPINFOEXA",
            "LPCPINFOEXW", "LPCPROPSHEETHEADERA", "LPCPROPSHEETHEADERW", "LPCPROPSHEETPAGEA", "LPCPROPSHEETPAGEA_LATEST", "LPCPROPSHEETPAGEA_V1",
            "LPCPROPSHEETPAGEA_V2", "LPCPROPSHEETPAGEA_V3", "LPCPROPSHEETPAGEW", "LPCPROPSHEETPAGEW_LATEST", "LPCPROPSHEETPAGEW_V1", "LPCPROPSHEETPAGEW_V2",
            "LPCPROPSHEETPAGEW_V3", "LPCREATEERRORINFO", "LPCREATESTRUCT", "LPCREATESTRUCTA", "LPCREATESTRUCTW", "LPCREATETYPEINFO",
            "LPCREATETYPEINFO2", "LPCREATETYPELIB", "LPCREATETYPELIB2", "LPCREATE_PROCESS_DEBUG_INFO", "LPCREATE_THREAD_DEBUG_INFO", "LPCREBARBANDINFOA",
            "LPCREBARBANDINFOW", "LPCRECT", "LPCRECTL", "LPCRITICAL_SECTION", "LPCRITICAL_SECTION_DEBUG", "LPCSCARD_IO_REQUEST",
            "LPCSCROLLINFO", "LPCSTR", "LPCTBBUTTON", "LPCTSTR", "LPCURRENCYFMT", "LPCURRENCYFMTA",
            "LPCURRENCYFMTW", "LPCURSORINFO", "LPCURSORSHAPE", "LPCUSTDATA", "LPCUSTDATAITEM", "LPCUTSTR",
            "LPCUWCHAR", "LPCUWSTR", "LPCVOID", "LPCWAVEFORMATEX", "LPCWCH", "LPCWCHAR",
            "LPCWPRETSTRUCT", "LPCWPSTRUCT", "LPCWSTR", "LPCY", "LPDATAADVISEHOLDER", "LPDATAFILTER",
            "LPDATAOBJECT", "LPDATATYPES_INFO_1", "LPDATATYPES_INFO_1A", "LPDATATYPES_INFO_1W", "LPDATETIMEPICKERINFO", "LPDCB",
            "LPDEBUGHOOKINFO", "LPDEBUG_EVENT", "LPDECIMAL", "LPDELETEITEMSTRUCT", "LPDESIGNVECTOR", "LPDEVMODE",
            "LPDEVMODEA", "LPDEVMODEW", "LPDEVNAMES", "LPDIBSECTION", "LPDISCDLGSTRUCT", "LPDISCDLGSTRUCTA",
            "LPDISCDLGSTRUCTW", "LPDISPATCH", "LPDISPLAY_DEVICE", "LPDISPLAY_DEVICEA", "LPDISPLAY_DEVICEW", "LPDLGITEMTEMPLATE",
            "LPDLGITEMTEMPLATEA", "LPDLGITEMTEMPLATEW", "LPDLGTEMPLATE", "LPDLGTEMPLATEA", "LPDLGTEMPLATEW", "LPDOCINFO",
            "LPDOCINFOA", "LPDOCINFOW", "LPDOC_INFO_1", "LPDOC_INFO_1A", "LPDOC_INFO_1W", "LPDOC_INFO_2",
            "LPDOC_INFO_2A", "LPDOC_INFO_2W", "LPDOC_INFO_3", "LPDOC_INFO_3A", "LPDOC_INFO_3W", "LPDRAGINFO",
            "LPDRAGINFOA", "LPDRAGINFOW", "LPDRAGLISTINFO", "LPDRAWITEMSTRUCT", "LPDRAWTEXTPARAMS", "LPDRIVERSTATUS",
            "LPDRIVER_INFO_1", "LPDRIVER_INFO_1A", "LPDRIVER_INFO_1W", "LPDRIVER_INFO_2", "LPDRIVER_INFO_2A", "LPDRIVER_INFO_2W",
            "LPDRIVER_INFO_3", "LPDRIVER_INFO_3A", "LPDRIVER_INFO_3W", "LPDRIVER_INFO_4", "LPDRIVER_INFO_4A", "LPDRIVER_INFO_4W",
            "LPDRIVER_INFO_5", "LPDRIVER_INFO_5A", "LPDRIVER_INFO_5W", "LPDRIVER_INFO_6", "LPDRIVER_INFO_6A", "LPDRIVER_INFO_6W",
            "LPDRIVER_INFO_8", "LPDRIVER_INFO_8A", "LPDRIVER_INFO_8W", "LPDROPSOURCE", "LPDROPSTRUCT", "LPDROPTARGET",
            "LPDRVCALLBACK", "LPDRVCONFIGINFO", "LPDRVCONFIGINFOEX", "LPDWORD", "LPELEMDESC", "LPENCLAVE_ROUTINE",
            "LPENCLAVE_TARGET_FUNCTION", "LPENCODINGFILTERFACTORY", "LPENHMETAHEADER", "LPENHMETARECORD", "LPENUMFORMATETC", "LPENUMLOGFONT",
            "LPENUMLOGFONTA", "LPENUMLOGFONTEX", "LPENUMLOGFONTEXA", "LPENUMLOGFONTEXDV", "LPENUMLOGFONTEXDVA", "LPENUMLOGFONTEXDVW",
            "LPENUMLOGFONTEXW", "LPENUMLOGFONTW", "LPENUMMONIKER", "LPENUMOLEVERB", "LPENUMSTATDATA", "LPENUMSTATPROPSETSTG",
            "LPENUMSTATPROPSTG", "LPENUMSTATSTG", "LPENUMSTRING", "LPENUMTEXTMETRIC", "LPENUMTEXTMETRICA", "LPENUMTEXTMETRICW",
            "LPENUMUNKNOWN", "LPENUMVARIANT", "LPENUM_SERVICE_STATUS", "LPENUM_SERVICE_STATUSA", "LPENUM_SERVICE_STATUSW", "LPENUM_SERVICE_STATUS_PROCESS",
            "LPENUM_SERVICE_STATUS_PROCESSA", "LPENUM_SERVICE_STATUS_PROCESSW", "LPERRORINFO", "LPERRORLOG", "LPEVENTLOG_FULL_INFORMATION", "LPEVENTMSG",
            "LPEVENTMSGMSG", "LPEXCEPINFO", "LPEXCEPTION_DEBUG_INFO", "LPEXCEPTION_POINTERS", "LPEXCEPTION_RECORD", "LPEXIT_PROCESS_DEBUG_INFO",
            "LPEXIT_THREAD_DEBUG_INFO", "LPEXTERNALCONNECTION", "LPEXTLOGFONT", "LPEXTLOGFONTA", "LPEXTLOGFONTW", "LPEXTLOGPEN",
            "LPEXTLOGPEN32", "LPFD_SET", "LPFIBER_START_ROUTINE", "LPFILETIME", "LPFILE_ID_DESCRIPTOR", "LPFILTERKEYS",
            "LPFINDINFOA", "LPFINDINFOW", "LPFINDREPLACE", "LPFINDREPLACEA", "LPFINDREPLACEW", "LPFMTID",
            "LPFNADDPROPSHEETPAGE", "LPFNADDPROPSHEETPAGES", "LPFNCANUNLOADNOW", "LPFNDEVCAPS", "LPFNDEVMODE", "LPFNGETCLASSOBJECT",
            "LPFNPSPCALLBACKA", "LPFNPSPCALLBACKW", "LPFONTSIGNATURE", "LPFORMATETC", "LPFORM_INFO_1", "LPFORM_INFO_1A",
            "LPFORM_INFO_1W", "LPFRHOOKPROC", "LPFUNCDESC", "LPFXPT16DOT16", "LPFXPT2DOT30", "LPGCP_RESULTS",
            "LPGCP_RESULTSA", "LPGCP_RESULTSW", "LPGETBINDHANDLE", "LPGETVERSIONINPARAMS", "LPGLOBALINTERFACETABLE", "LPGLYPHMETRICS",
            "LPGLYPHMETRICSFLOAT", "LPGLYPHSET", "LPGRADIENT_RECT", "LPGRADIENT_TRIANGLE", "LPGUID", "LPGUITHREADINFO",
            "LPHANDLE", "LPHANDLER_FUNCTION", "LPHANDLER_FUNCTION_EX", "LPHANDLETABLE", "LPHARDWAREHOOKSTRUCT", "LPHARDWAREINPUT",
            "LPHDHITTESTINFO", "LPHDITEMA", "LPHDITEMW", "LPHDLAYOUT", "LPHD_TEXTFILTERA", "LPHD_TEXTFILTERW",
            "LPHEAP_SUMMARY", "LPHELPINFO", "LPHELPWININFO", "LPHELPWININFOA", "LPHELPWININFOW", "LPHIGHCONTRAST",
            "LPHIGHCONTRASTA", "LPHIGHCONTRASTW", "LPHIT_LOGGING_INFO", "LPHKL", "LPHMIDI", "LPHMIDIIN",
            "LPHMIDIOUT", "LPHMIDISTRM", "LPHMIXER", "LPHMIXEROBJ", "LPHOSTENT", "LPHTTPNEGOTIATE",
            "LPHTTPNEGOTIATE2", "LPHTTPNEGOTIATE3", "LPHTTPSECURITY", "LPHWAVEIN", "LPHWAVEOUT", "LPHW_PROFILE_INFO",
            "LPHW_PROFILE_INFOA", "LPHW_PROFILE_INFOW", "LPICONMETRICS", "LPICONMETRICSA", "LPICONMETRICSW", "LPIDEREGS",
            "LPIDLDESC", "LPIID", "LPIINTERNET", "LPIINTERNETBINDINFO", "LPIINTERNETBINDINFOEX", "LPIINTERNETPRIORITY",
            "LPIINTERNETPROTOCOL", "LPIINTERNETPROTOCOLINFO", "LPIINTERNETPROTOCOLROOT", "LPIINTERNETPROTOCOLSINK", "LPIINTERNETPROTOCOLSINKStackable", "LPIINTERNETSESSION",
            "LPIINTERNETTHREADSWITCH", "LPIMAGEINFO", "LPIMAGELISTDRAWPARAMS", "LPIMECHARPOSITION", "LPIMEMENUITEMINFO", "LPIMEMENUITEMINFOA",
            "LPIMEMENUITEMINFOW", "LPINITCOMMONCONTROLSEX", "LPINITIALIZESPY", "LPINIT_ONCE", "LPINPUT", "LPINT",
            "LPINTERFACEDATA", "LPINTERFACEINFO", "LPIN_ADDR", "LPIWRAPPEDPROTOCOL", "LPJIT_DEBUG_INFO", "LPJIT_DEBUG_INFO32",
            "LPJIT_DEBUG_INFO64", "LPJOB_INFO_1", "LPJOB_INFO_1A", "LPJOB_INFO_1W", "LPJOB_INFO_2", "LPJOB_INFO_2A",
            "LPJOB_INFO_2W", "LPJOB_INFO_3", "LPJOYCAPS", "LPJOYCAPS2", "LPJOYCAPS2A", "LPJOYCAPS2W",
            "LPJOYCAPSA", "LPJOYCAPSW", "LPJOYINFO", "LPJOYINFOEX", "LPKBDLLHOOKSTRUCT", "LPKERNINGPAIR",
            "LPKEYARRAY", "LPKEYBDINPUT", "LPLAYERPLANEDESCRIPTOR", "LPLC_ID", "LPLDT_ENTRY", "LPLINGER",
            "LPLINKSRCDESCRIPTOR", "LPLOAD_DLL_DEBUG_INFO", "LPLOCALESIGNATURE", "LPLOCKBYTES", "LPLOGBRUSH", "LPLOGBRUSH32",
            "LPLOGCOLORSPACE", "LPLOGCOLORSPACEA", "LPLOGCOLORSPACEW", "LPLOGFONT", "LPLOGFONTA", "LPLOGFONTW",
            "LPLOGPALETTE", "LPLOGPEN", "LPLONG", "LPLVBKIMAGEA", "LPLVBKIMAGEW", "LPLVCOLUMNA",
            "LPLVCOLUMNW", "LPLVFOOTERINFO", "LPLVFOOTERITEM", "LPLVHITTESTINFO", "LPLVINSERTMARK", "LPLVITEMA",
            "LPLVITEMW", "LPMALLOC", "LPMALLOCSPY", "LPMARSHAL", "LPMARSHAL2", "LPMAT2",
            "LPMCI_ANIM_OPEN_PARMS", "LPMCI_ANIM_OPEN_PARMSA", "LPMCI_ANIM_OPEN_PARMSW", "LPMCI_ANIM_PLAY_PARMS", "LPMCI_ANIM_RECT_PARMS", "LPMCI_ANIM_STEP_PARMS",
            "LPMCI_ANIM_UPDATE_PARMS", "LPMCI_ANIM_WINDOW_PARMS", "LPMCI_ANIM_WINDOW_PARMSA", "LPMCI_ANIM_WINDOW_PARMSW", "LPMCI_BREAK_PARMS", "LPMCI_GENERIC_PARMS",
            "LPMCI_GETDEVCAPS_PARMS", "LPMCI_INFO_PARMS", "LPMCI_INFO_PARMSA", "LPMCI_INFO_PARMSW", "LPMCI_LOAD_PARMS", "LPMCI_LOAD_PARMSA",
            "LPMCI_LOAD_PARMSW", "LPMCI_OPEN_PARMS", "LPMCI_OPEN_PARMSA", "LPMCI_OPEN_PARMSW", "LPMCI_OVLY_LOAD_PARMS", "LPMCI_OVLY_LOAD_PARMSA",
            "LPMCI_OVLY_LOAD_PARMSW", "LPMCI_OVLY_OPEN_PARMS", "LPMCI_OVLY_OPEN_PARMSA", "LPMCI_OVLY_OPEN_PARMSW", "LPMCI_OVLY_RECT_PARMS", "LPMCI_OVLY_SAVE_PARMS",
            "LPMCI_OVLY_SAVE_PARMSA", "LPMCI_OVLY_SAVE_PARMSW", "LPMCI_OVLY_WINDOW_PARMS", "LPMCI_OVLY_WINDOW_PARMSA", "LPMCI_OVLY_WINDOW_PARMSW", "LPMCI_PLAY_PARMS",
            "LPMCI_RECORD_PARMS", "LPMCI_SAVE_PARMS", "LPMCI_SAVE_PARMSA", "LPMCI_SAVE_PARMSW", "LPMCI_SEEK_PARMS", "LPMCI_SEQ_SET_PARMS",
            "LPMCI_SET_PARMS", "LPMCI_STATUS_PARMS", "LPMCI_SYSINFO_PARMS", "LPMCI_SYSINFO_PARMSA", "LPMCI_SYSINFO_PARMSW", "LPMCI_VD_ESCAPE_PARMS",
            "LPMCI_VD_ESCAPE_PARMSA", "LPMCI_VD_ESCAPE_PARMSW", "LPMCI_VD_PLAY_PARMS", "LPMCI_VD_STEP_PARMS", "LPMCI_WAVE_DELETE_PARMS", "LPMCI_WAVE_OPEN_PARMS",
            "LPMCI_WAVE_OPEN_PARMSA", "LPMCI_WAVE_OPEN_PARMSW", "LPMCI_WAVE_SET_PARMS", "LPMDICREATESTRUCT", "LPMDICREATESTRUCTA", "LPMDICREATESTRUCTW",
            "LPMDINEXTMENU", "LPMEASUREITEMSTRUCT", "LPMEMORYSTATUS", "LPMEMORYSTATUSEX", "LPMENUBARINFO", "LPMENUINFO",
            "LPMENUITEMINFO", "LPMENUITEMINFOA", "LPMENUITEMINFOW", "LPMENUTEMPLATE", "LPMENUTEMPLATEA", "LPMENUTEMPLATEW",
            "LPMESSAGEFILTER", "LPMETAFILEPICT", "LPMETAHEADER", "LPMETARECORD", "LPMETHODDATA", "LPMIDICALLBACK",
            "LPMIDIHDR", "LPMIDIINCAPS", "LPMIDIINCAPS2", "LPMIDIINCAPS2A", "LPMIDIINCAPS2W", "LPMIDIINCAPSA",
            "LPMIDIINCAPSW", "LPMIDIOUTCAPS", "LPMIDIOUTCAPS2", "LPMIDIOUTCAPS2A", "LPMIDIOUTCAPS2W", "LPMIDIOUTCAPSA",
            "LPMIDIOUTCAPSW", "LPMIDIPROPTEMPO", "LPMIDIPROPTIMEDIV", "LPMINIMIZEDMETRICS", "LPMINMAXINFO", "LPMIXERCAPS",
            "LPMIXERCAPS2", "LPMIXERCAPS2A", "LPMIXERCAPS2W", "LPMIXERCAPSA", "LPMIXERCAPSW", "LPMIXERCONTROL",
            "LPMIXERCONTROLA", "LPMIXERCONTROLDETAILS", "LPMIXERCONTROLDETAILS_BOOLEAN", "LPMIXERCONTROLDETAILS_LISTTEXT", "LPMIXERCONTROLDETAILS_LISTTEXTA", "LPMIXERCONTROLDETAILS_LISTTEXTW",
            "LPMIXERCONTROLDETAILS_SIGNED", "LPMIXERCONTROLDETAILS_UNSIGNED", "LPMIXERCONTROLW", "LPMIXERLINE", "LPMIXERLINEA", "LPMIXERLINECONTROLS",
            "LPMIXERLINECONTROLSA", "LPMIXERLINECONTROLSW", "LPMIXERLINEW", "LPMMCKINFO", "LPMMIOINFO", "LPMMIOPROC",
            "LPMMTIME", "LPMODEMDEVCAPS", "LPMODEMSETTINGS", "LPMONIKER", "LPMONIKERPROP", "LPMONITORINFO",
            "LPMONITORINFOEX", "LPMONITORINFOEXA", "LPMONITORINFOEXW", "LPMONITOR_INFO_1", "LPMONITOR_INFO_1A", "LPMONITOR_INFO_1W",
            "LPMONITOR_INFO_2", "LPMONITOR_INFO_2A", "LPMONITOR_INFO_2W", "LPMONTHDAYSTATE", "LPMOUSEHOOKSTRUCT", "LPMOUSEHOOKSTRUCTEX",
            "LPMOUSEINPUT", "LPMOUSEKEYS", "LPMOUSEMOVEPOINT", "LPMSG", "LPMSGBOXPARAMS", "LPMSGBOXPARAMSA",
            "LPMSGBOXPARAMSW", "LPMSLLHOOKSTRUCT", "LPMULTIKEYHELP", "LPMULTIKEYHELPA", "LPMULTIKEYHELPW", "LPMULTIQI",
            "LPNCCALCSIZE_PARAMS", "LPNETCONNECTINFOSTRUCT", "LPNETINFOSTRUCT", "LPNETRESOURCE", "LPNETRESOURCEA", "LPNETRESOURCEW",
            "LPNEWTEXTMETRIC", "LPNEWTEXTMETRICA", "LPNEWTEXTMETRICW", "LPNLSVERSIONINFO", "LPNLSVERSIONINFOEX", "LPNMBCDROPDOWN",
            "LPNMBCHOTITEM", "LPNMCBEDRAGBEGINA", "LPNMCBEDRAGBEGINW", "LPNMCBEENDEDITA", "LPNMCBEENDEDITW", "LPNMCHAR",
            "LPNMCLICK", "LPNMCUSTOMDRAW", "LPNMCUSTOMSPLITRECTINFO", "LPNMCUSTOMTEXT", "LPNMDATETIMECHANGE", "LPNMDATETIMEFORMATA",
            "LPNMDATETIMEFORMATQUERYA", "LPNMDATETIMEFORMATQUERYW", "LPNMDATETIMEFORMATW", "LPNMDATETIMESTRINGA", "LPNMDATETIMESTRINGW", "LPNMDATETIMEWMKEYDOWNA",
            "LPNMDATETIMEWMKEYDOWNW", "LPNMDAYSTATE", "LPNMHDDISPINFOA", "LPNMHDDISPINFOW", "LPNMHDFILTERBTNCLICK", "LPNMHDR",
            "LPNMHEADERA", "LPNMHEADERW", "LPNMIPADDRESS", "LPNMITEMACTIVATE", "LPNMKEY", "LPNMLISTVIEW",
            "LPNMLVCACHEHINT", "LPNMLVCUSTOMDRAW", "LPNMLVDISPINFOA", "LPNMLVDISPINFOW", "LPNMLVFINDITEMA", "LPNMLVFINDITEMW",
            "LPNMLVGETINFOTIPA", "LPNMLVGETINFOTIPW", "LPNMLVKEYDOWN", "LPNMLVODSTATECHANGE", "LPNMLVSCROLL", "LPNMMOUSE",
            "LPNMOBJECTNOTIFY", "LPNMPGCALCSIZE", "LPNMPGHOTITEM", "LPNMPGSCROLL", "LPNMRBAUTOSIZE", "LPNMREBAR",
            "LPNMREBARAUTOBREAK", "LPNMREBARCHEVRON", "LPNMREBARCHILDSIZE", "LPNMREBARSPLITTER", "LPNMSELCHANGE", "LPNMSELECT",
            "LPNMTBCUSTOMDRAW", "LPNMTBDISPINFOA", "LPNMTBDISPINFOW", "LPNMTBGETINFOTIPA", "LPNMTBGETINFOTIPW", "LPNMTBHOTITEM",
            "LPNMTBRESTORE", "LPNMTBSAVE", "LPNMTOOLBARA", "LPNMTOOLBARW", "LPNMTOOLTIPSCREATED", "LPNMTREEVIEWA",
            "LPNMTREEVIEWW", "LPNMTTCUSTOMDRAW", "LPNMTTDISPINFOA", "LPNMTTDISPINFOW", "LPNMTVCUSTOMDRAW", "LPNMTVDISPINFOA",
            "LPNMTVDISPINFOEXA", "LPNMTVDISPINFOEXW", "LPNMTVDISPINFOW", "LPNMTVGETINFOTIPA", "LPNMTVGETINFOTIPW", "LPNMTVKEYDOWN",
            "LPNMTVSTATEIMAGECHANGING", "LPNMUPDOWN", "LPNMVIEWCHANGE", "LPNONCLIENTMETRICS", "LPNONCLIENTMETRICSA", "LPNONCLIENTMETRICSW",
            "LPNUMBERFMT", "LPNUMBERFMTA", "LPNUMBERFMTW", "LPOBJECTDESCRIPTOR", "LPOCNCHKPROC", "LPOCNCONNPROCA",
            "LPOCNCONNPROCW", "LPOCNDSCPROC", "LPOFNHOOKPROC", "LPOFNOTIFY", "LPOFNOTIFYA", "LPOFNOTIFYEX",
            "LPOFNOTIFYEXA", "LPOFNOTIFYEXW", "LPOFNOTIFYW", "LPOFSTRUCT", "LPOLEADVISEHOLDER", "LPOLECACHE",
            "LPOLECACHE2", "LPOLECACHECONTROL", "LPOLECLIENTSITE", "LPOLECONTAINER", "LPOLEINPLACEACTIVEOBJECT", "LPOLEINPLACEFRAME",
            "LPOLEINPLACEFRAMEINFO", "LPOLEINPLACEOBJECT", "LPOLEINPLACESITE", "LPOLEINPLACEUIWINDOW", "LPOLEITEMCONTAINER", "LPOLELINK",
            "LPOLEMENUGROUPWIDTHS", "LPOLEOBJECT", "LPOLERENDER", "LPOLESTR", "LPOLESTREAM", "LPOLESTREAMVTBL",
            "LPOLEUPDATE", "LPOLEVERB", "LPOLEWINDOW", "LPOPENCARDNAME", "LPOPENCARDNAMEA", "LPOPENCARDNAMEW",
            "LPOPENCARDNAME_EX", "LPOPENCARDNAME_EXA", "LPOPENCARDNAME_EXW", "LPOPENCARD_SEARCH_CRITERIA", "LPOPENCARD_SEARCH_CRITERIAA", "LPOPENCARD_SEARCH_CRITERIAW",
            "LPOPENFILENAME", "LPOPENFILENAMEA", "LPOPENFILENAMEW", "LPOPENFILENAME_NT4", "LPOPENFILENAME_NT4A", "LPOPENFILENAME_NT4W",
            "LPOSVERSIONINFO", "LPOSVERSIONINFOA", "LPOSVERSIONINFOEX", "LPOSVERSIONINFOEXA", "LPOSVERSIONINFOEXW", "LPOSVERSIONINFOW",
            "LPOUTLINETEXTMETRIC", "LPOUTLINETEXTMETRICA", "LPOUTLINETEXTMETRICW", "LPOUTPUT_DEBUG_STRING_INFO", "LPOVERLAPPED", "LPOVERLAPPED_COMPLETION_ROUTINE",
            "LPOVERLAPPED_ENTRY", "LPPAGEPAINTHOOK", "LPPAGESETUPDLG", "LPPAGESETUPDLGA", "LPPAGESETUPDLGW", "LPPAGESETUPHOOK",
            "LPPAINTSTRUCT", "LPPALETTEENTRY", "LPPANOSE", "LPPARAMDATA", "LPPARAMDESC", "LPPARAMDESCEX",
            "LPPARSEDISPLAYNAME", "LPPATCHARRAY", "LPPATTERN", "LPPCMWAVEFORMAT", "LPPELARRAY", "LPPERSIST",
            "LPPERSISTFILE", "LPPERSISTMONIKER", "LPPERSISTSTORAGE", "LPPERSISTSTREAM", "LPPIXELFORMATDESCRIPTOR", "LPPOINT",
            "LPPOINTFX", "LPPOINTS", "LPPOLYTEXT", "LPPOLYTEXTA", "LPPOLYTEXTW", "LPPORT_INFO_1",
            "LPPORT_INFO_1A", "LPPORT_INFO_1W", "LPPORT_INFO_2", "LPPORT_INFO_2A", "LPPORT_INFO_2W", "LPPORT_INFO_3",
            "LPPORT_INFO_3A", "LPPORT_INFO_3W", "LPPOWER_REQUEST_CONTEXT", "LPPRINTDLG", "LPPRINTDLGA", "LPPRINTDLGEX",
            "LPPRINTDLGEXA", "LPPRINTDLGEXW", "LPPRINTDLGW", "LPPRINTER_DEFAULTS", "LPPRINTER_DEFAULTSA", "LPPRINTER_DEFAULTSW",
            "LPPRINTER_ENUM_VALUES", "LPPRINTER_ENUM_VALUESA", "LPPRINTER_ENUM_VALUESW", "LPPRINTER_INFO_1", "LPPRINTER_INFO_1A", "LPPRINTER_INFO_1W",
            "LPPRINTER_INFO_2", "LPPRINTER_INFO_2A", "LPPRINTER_INFO_2W", "LPPRINTER_INFO_3", "LPPRINTER_INFO_4", "LPPRINTER_INFO_4A",
            "LPPRINTER_INFO_4W", "LPPRINTER_INFO_5", "LPPRINTER_INFO_5A", "LPPRINTER_INFO_5W", "LPPRINTER_INFO_6", "LPPRINTER_INFO_7",
            "LPPRINTER_INFO_7A", "LPPRINTER_INFO_7W", "LPPRINTER_INFO_8", "LPPRINTER_INFO_8A", "LPPRINTER_INFO_8W", "LPPRINTER_INFO_9",
            "LPPRINTER_INFO_9A", "LPPRINTER_INFO_9W", "LPPRINTER_NOTIFY_INFO", "LPPRINTER_NOTIFY_INFO_DATA", "LPPRINTER_NOTIFY_OPTIONS", "LPPRINTER_NOTIFY_OPTIONS_TYPE",
            "LPPRINTHOOKPROC", "LPPRINTPAGERANGE", "LPPRINTPROCESSOR_INFO_1", "LPPRINTPROCESSOR_INFO_1A", "LPPRINTPROCESSOR_INFO_1W", "LPPROCESS_HEAP_ENTRY",
            "LPPROCESS_INFORMATION", "LPPROC_THREAD_ATTRIBUTE_LIST", "LPPROGRESS_ROUTINE", "LPPROPERTYBAG", "LPPROPERTYSETSTORAGE", "LPPROPERTYSTORAGE",
            "LPPROPSHEETHEADERA", "LPPROPSHEETHEADERW", "LPPROPSHEETPAGEA", "LPPROPSHEETPAGEA_LATEST", "LPPROPSHEETPAGEA_V1", "LPPROPSHEETPAGEA_V2",
            "LPPROPSHEETPAGEA_V3", "LPPROPSHEETPAGEW", "LPPROPSHEETPAGEW_LATEST", "LPPROPSHEETPAGEW_V1", "LPPROPSHEETPAGEW_V2", "LPPROPSHEETPAGEW_V3",
            "LPPROPVARIANT", "LPPROTOCOL_ARGUMENT", "LPPROTOENT", "LPPROVIDOR_INFO_1", "LPPROVIDOR_INFO_1A", "LPPROVIDOR_INFO_1W",
            "LPPROVIDOR_INFO_2", "LPPROVIDOR_INFO_2A", "LPPROVIDOR_INFO_2W", "LPPSHNOTIFY", "LPQUERY_SERVICE_CONFIG", "LPQUERY_SERVICE_CONFIGA",
            "LPQUERY_SERVICE_CONFIGW", "LPQUERY_SERVICE_LOCK_STATUS", "LPQUERY_SERVICE_LOCK_STATUSA", "LPQUERY_SERVICE_LOCK_STATUSW", "LPRASTERIZER_STATUS", "LPRAWHID",
            "LPRAWINPUT", "LPRAWINPUTDEVICE", "LPRAWINPUTHEADER", "LPRAWKEYBOARD", "LPRAWMOUSE", "LPRBHITTESTINFO",
            "LPREBARBANDINFOA", "LPREBARBANDINFOW", "LPREBARINFO", "LPRECONVERTSTRING", "LPRECORDINFO", "LPRECT",
            "LPRECTL", "LPREGISTERWORD", "LPREGISTERWORDA", "LPREGISTERWORDW", "LPREMFORMATETC", "LPREMOTE_NAME_INFO",
            "LPREMOTE_NAME_INFOA", "LPREMOTE_NAME_INFOW", "LPREMSECURITY_ATTRIBUTES", "LPRGBQUAD", "LPRGBTRIPLE", "LPRGNDATA",
            "LPRID_DEVICE_INFO", "LPRIP_INFO", "LPROOTSTORAGE", "LPRUNNABLEOBJECT", "LPRUNNINGOBJECTTABLE", "LPSAFEARRAY",
            "LPSAFEARRAYBOUND", "LPSCARDCONTEXT", "LPSCARDHANDLE", "LPSCARD_ATRMASK", "LPSCARD_IO_REQUEST", "LPSCARD_READERSTATE",
            "LPSCARD_READERSTATEA", "LPSCARD_READERSTATEW", "LPSCARD_T0_COMMAND", "LPSCARD_T0_REQUEST", "LPSCARD_T1_REQUEST", "LPSCROLLBARINFO",
            "LPSCROLLINFO", "LPSC_ACTION", "LPSC_HANDLE", "LPSECURITY_ATTRIBUTES", "LPSECURITY_CAPABILITIES", "LPSENDCMDINPARAMS",
            "LPSENDCMDOUTPARAMS", "LPSERIALKEYS", "LPSERIALKEYSA", "LPSERIALKEYSW", "LPSERVENT", "LPSERVICEPROVIDER",
            "LPSERVICE_DELAYED_AUTO_START_INFO", "LPSERVICE_DESCRIPTION", "LPSERVICE_DESCRIPTIONA", "LPSERVICE_DESCRIPTIONW", "LPSERVICE_FAILURE_ACTIONS", "LPSERVICE_FAILURE_ACTIONSA",
            "LPSERVICE_FAILURE_ACTIONSW", "LPSERVICE_FAILURE_ACTIONS_FLAG", "LPSERVICE_MAIN_FUNCTIONA", "LPSERVICE_MAIN_FUNCTIONW", "LPSERVICE_PRESHUTDOWN_INFO", "LPSERVICE_REQUIRED_PRIVILEGES_INFOA",
            "LPSERVICE_REQUIRED_PRIVILEGES_INFOW", "LPSERVICE_SID_INFO", "LPSERVICE_STATUS", "LPSERVICE_STATUS_PROCESS", "LPSERVICE_TABLE_ENTRY", "LPSERVICE_TABLE_ENTRYA",
            "LPSERVICE_TABLE_ENTRYW", "LPSETUPHOOKPROC", "LPSHELLEXECUTEINFO", "LPSHELLEXECUTEINFOA", "LPSHELLEXECUTEINFOW", "LPSHELLHOOKINFO",
            "LPSHFILEOPSTRUCT", "LPSHFILEOPSTRUCTA", "LPSHFILEOPSTRUCTW", "LPSHNAMEMAPPING", "LPSHNAMEMAPPINGA", "LPSHNAMEMAPPINGW",
            "LPSHQUERYRBINFO", "LPSIZE", "LPSIZEL", "LPSOCKADDR", "LPSOCKADDR_IN", "LPSOFTDISTINFO",
            "LPSOUNDSENTRY", "LPSOUNDSENTRYA", "LPSOUNDSENTRYW", "LPSTARTUPINFO", "LPSTARTUPINFOA", "LPSTARTUPINFOEX",
            "LPSTARTUPINFOEXA", "LPSTARTUPINFOEXW", "LPSTARTUPINFOW", "LPSTATDATA", "LPSTDMARSHALINFO", "LPSTGMEDIUM",
            "LPSTICKYKEYS", "LPSTORAGE", "LPSTR", "LPSTREAM", "LPSTYLEBUF", "LPSTYLEBUFA",
            "LPSTYLEBUFW", "LPSTYLESTRUCT", "LPSUPPORTERRORINFO", "LPSURROGATE", "LPSYNCHRONIZATION_BARRIER", "LPSYSTEMTIME",
            "LPSYSTEM_INFO", "LPSYSTEM_POWER_STATUS", "LPTBADDBITMAP", "LPTBBUTTON", "LPTBBUTTONINFOA", "LPTBBUTTONINFOW",
            "LPTBINSERTMARK", "LPTBMETRICS", "LPTBREPLACEBITMAP", "LPTBSAVEPARAMSA", "LPTBSAVEPARAMW", "LPTCH",
            "LPTCHITTESTINFO", "LPTCITEMA", "LPTCITEMHEADERA", "LPTCITEMHEADERW", "LPTCITEMW", "LPTEXTMETRIC",
            "LPTEXTMETRICA", "LPTEXTMETRICW", "LPTHREAD_START_ROUTINE", "LPTIMECALLBACK", "LPTIMECAPS", "LPTIMEVAL",
            "LPTIME_ZONE_INFORMATION", "LPTITLEBARINFO", "LPTITLEBARINFOEX", "LPTLIBATTR", "LPTOGGLEKEYS", "LPTOP_LEVEL_EXCEPTION_FILTER",
            "LPTPMPARAMS", "LPTRACKMOUSEEVENT", "LPTRANSMIT_FILE_BUFFERS", "LPTRIVERTEX", "LPTSTR", "LPTTHITTESTINFOA",
            "LPTTHITTESTINFOW", "LPTTPOLYCURVE", "LPTTPOLYGONHEADER", "LPTTTOOLINFOA", "LPTTTOOLINFOW", "LPTVHITTESTINFO",
            "LPTVINSERTSTRUCTA", "LPTVINSERTSTRUCTW", "LPTVITEMA", "LPTVITEMEX", "LPTVITEMEXA", "LPTVITEMEXW",
            "LPTVITEMW", "LPTVSORTCB", "LPTYPEATTR", "LPTYPECHANGEEVENTS", "LPTYPECOMP", "LPTYPEINFO",
            "LPTYPEINFO2", "LPTYPELIB", "LPTYPELIB2", "LPUDACCEL", "LPUINT", "LPUNIVERSAL_NAME_INFO",
            "LPUNIVERSAL_NAME_INFOA", "LPUNIVERSAL_NAME_INFOW", "LPUNKNOWN", "LPUNLOAD_DLL_DEBUG_INFO", "LPURLZONEMANAGER", "LPUTSTR",
            "LPUWSTR", "LPVARDESC", "LPVARIANT", "LPVARIANTARG", "LPVERSIONEDSTREAM", "LPVIDEOPARAMETERS",
            "LPVIEWOBJECT", "LPVIEWOBJECT2", "LPVOID", "LPWAVECALLBACK", "LPWAVEFORMAT", "LPWAVEFORMATEX",
            "LPWAVEHDR", "LPWAVEINCAPS", "LPWAVEINCAPS2", "LPWAVEINCAPS2A", "LPWAVEINCAPS2W", "LPWAVEINCAPSA",
            "LPWAVEINCAPSW", "LPWAVEOUTCAPS", "LPWAVEOUTCAPS2", "LPWAVEOUTCAPS2A", "LPWAVEOUTCAPS2W", "LPWAVEOUTCAPSA",
            "LPWAVEOUTCAPSW", "LPWCH", "LPWCRANGE", "LPWGLSWAP", "LPWIN32_FILE_ATTRIBUTE_DATA", "LPWIN32_FIND_DATA",
            "LPWIN32_FIND_DATAA", "LPWIN32_FIND_DATAW", "LPWIN32_STREAM_ID", "LPWINDOWFORBINDINGUI", "LPWINDOWINFO", "LPWINDOWPLACEMENT",
            "LPWINDOWPOS", "LPWININETCACHEHINTS", "LPWININETCACHEHINTS2", "LPWININETFILESTREAM", "LPWININETHTTPINFO", "LPWININETINFO",
            "LPWNDCLASS", "LPWNDCLASSA", "LPWNDCLASSEX", "LPWNDCLASSEXA", "LPWNDCLASSEXW", "LPWNDCLASSW",
            "LPWORD", "LPWSADATA", "LPWSTR", "LPXFORM", "LPZONEATTRIBUTES", "LRESULT",
            "LSTATUS", "LUID", "LUID_AND_ATTRIBUTES", "LUID_AND_ATTRIBUTES_ARRAY", "LVBKIMAGEA", "LVBKIMAGEW",
            "LVCOLUMNA", "LVCOLUMNW", "LVFINDINFOA", "LVFINDINFOW", "LVFOOTERINFO", "LVFOOTERITEM",
            "LVGROUP", "LVGROUPMETRICS", "LVHITTESTINFO", "LVINSERTGROUPSORTED", "LVINSERTMARK", "LVITEMA",
            "LVITEMINDEX", "LVITEMW", "LVSETINFOTIP", "LVTILEINFO", "LVTILEVIEWINFO", "M128A",
            "MACHINE_ATTRIBUTES", "MALLOC_FREE_STRUCT", "MANDATORY_LEVEL", "MARK_HANDLE_INFO", "MARK_HANDLE_INFO32", "MAT2",
            "MCGRIDINFO", "MCHITTESTINFO", "MCIDEVICEID", "MCIERROR", "MCI_ANIM_OPEN_PARMS", "MCI_ANIM_OPEN_PARMSA",
            "MCI_ANIM_OPEN_PARMSW", "MCI_ANIM_PLAY_PARMS", "MCI_ANIM_RECT_PARMS", "MCI_ANIM_STEP_PARMS", "MCI_ANIM_UPDATE_PARMS", "MCI_ANIM_WINDOW_PARMS",
            "MCI_ANIM_WINDOW_PARMSA", "MCI_ANIM_WINDOW_PARMSW", "MCI_BREAK_PARMS", "MCI_GENERIC_PARMS", "MCI_GETDEVCAPS_PARMS", "MCI_INFO_PARMS",
            "MCI_INFO_PARMSA", "MCI_INFO_PARMSW", "MCI_LOAD_PARMS", "MCI_LOAD_PARMSA", "MCI_LOAD_PARMSW", "MCI_OPEN_PARMS",
            "MCI_OPEN_PARMSA", "MCI_OPEN_PARMSW", "MCI_OVLY_LOAD_PARMS", "MCI_OVLY_LOAD_PARMSA", "MCI_OVLY_LOAD_PARMSW", "MCI_OVLY_OPEN_PARMS",
            "MCI_OVLY_OPEN_PARMSA", "MCI_OVLY_OPEN_PARMSW", "MCI_OVLY_RECT_PARMS", "MCI_OVLY_SAVE_PARMS", "MCI_OVLY_SAVE_PARMSA", "MCI_OVLY_SAVE_PARMSW",
            "MCI_OVLY_WINDOW_PARMS", "MCI_OVLY_WINDOW_PARMSA", "MCI_OVLY_WINDOW_PARMSW", "MCI_PLAY_PARMS", "MCI_RECORD_PARMS", "MCI_SAVE_PARMS",
            "MCI_SAVE_PARMSA", "MCI_SAVE_PARMSW", "MCI_SEEK_PARMS", "MCI_SEQ_SET_PARMS", "MCI_SET_PARMS", "MCI_STATUS_PARMS",
            "MCI_SYSINFO_PARMS", "MCI_SYSINFO_PARMSA", "MCI_SYSINFO_PARMSW", "MCI_VD_ESCAPE_PARMS", "MCI_VD_ESCAPE_PARMSA", "MCI_VD_ESCAPE_PARMSW",
            "MCI_VD_PLAY_PARMS", "MCI_VD_STEP_PARMS", "MCI_WAVE_DELETE_PARMS", "MCI_WAVE_OPEN_PARMS", "MCI_WAVE_OPEN_PARMSA", "MCI_WAVE_OPEN_PARMSW",
            "MCI_WAVE_SET_PARMS", "MDICREATESTRUCT", "MDICREATESTRUCTA", "MDICREATESTRUCTW", "MDINEXTMENU", "MEASUREITEMSTRUCT",
            "MEDIA_TYPE", "MEMBERID", "MEMCTX", "MEMORYSTATUS", "MEMORYSTATUSEX", "MEMORY_BASIC_INFORMATION",
            "MEMORY_BASIC_INFORMATION32", "MEMORY_BASIC_INFORMATION64", "MEMORY_PARTITION_DEDICATED_MEMORY_ATTRIBUTE", "MEMORY_PARTITION_DEDICATED_MEMORY_INFORMATION", "MEMORY_RESOURCE_NOTIFICATION_TYPE", "MEM_ADDRESS_REQUIREMENTS",
            "MEM_DEDICATED_ATTRIBUTE_TYPE", "MENUBARINFO", "MENUGETOBJECTINFO", "MENUINFO", "MENUITEMINFO", "MENUITEMINFOA",
            "MENUITEMINFOW", "MENUITEMTEMPLATE", "MENUITEMTEMPLATEHEADER", "MENUTEMPLATE", "MENUTEMPLATEA", "MENUTEMPLATEW",
            "MENU_EVENT_RECORD", "MERGE_VIRTUAL_DISK_FLAG", "MERGE_VIRTUAL_DISK_PARAMETERS", "MERGE_VIRTUAL_DISK_VERSION", "MESSAGE_RESOURCE_BLOCK", "MESSAGE_RESOURCE_DATA",
            "MESSAGE_RESOURCE_ENTRY", "METAFILEPICT", "METAHEADER", "METARECORD", "METHODDATA", "MFENUMPROC",
            "MFT_ENUM_DATA", "MIDICALLBACK", "MIDIEVENT", "MIDIHDR", "MIDIINCAPS", "MIDIINCAPS2",
            "MIDIINCAPS2A", "MIDIINCAPS2W", "MIDIINCAPSA", "MIDIINCAPSW", "MIDIOUTCAPS", "MIDIOUTCAPS2",
            "MIDIOUTCAPS2A", "MIDIOUTCAPS2W", "MIDIOUTCAPSA", "MIDIOUTCAPSW", "MIDIPROPTEMPO", "MIDIPROPTIMEDIV",
            "MIDISTRMBUFFVER", "MIDL_FORMAT_STRING", "MIDL_INTERCEPTION_INFO", "MIDL_INTERFACE_METHOD_PROPERTIES", "MIDL_METHOD_PROPERTY", "MIDL_METHOD_PROPERTY_MAP",
            "MIDL_SERVER_INFO", "MIDL_STUBLESS_PROXY_INFO", "MIDL_STUB_DESC", "MIDL_STUB_MESSAGE", "MIDL_SYNTAX_INFO", "MIDL_WINRT_TYPE_SERIALIZATION_INFO",
            "MINIMIZEDMETRICS", "MINMAXINFO", "MIRROR_VIRTUAL_DISK_FLAG", "MIRROR_VIRTUAL_DISK_VERSION", "MIXERCAPS", "MIXERCAPS2",
            "MIXERCAPS2A", "MIXERCAPS2W", "MIXERCAPSA", "MIXERCAPSW", "MIXERCONTROL", "MIXERCONTROLA",
            "MIXERCONTROLDETAILS", "MIXERCONTROLDETAILS_BOOLEAN", "MIXERCONTROLDETAILS_LISTTEXT", "MIXERCONTROLDETAILS_LISTTEXTA", "MIXERCONTROLDETAILS_LISTTEXTW", "MIXERCONTROLDETAILS_SIGNED",
            "MIXERCONTROLDETAILS_UNSIGNED", "MIXERCONTROLW", "MIXERLINE", "MIXERLINEA", "MIXERLINECONTROLS", "MIXERLINECONTROLSA",
            "MIXERLINECONTROLSW", "MIXERLINEW", "MKRREDUCE", "MKSYS", "MMCKINFO", "MMIOINFO",
            "MMIOPROC", "MMRESULT", "MMTIME", "MMVERSION", "MODEMDEVCAPS", "MODEMSETTINGS",
            "MONCBSTRUCT", "MONCONVSTRUCT", "MONERRSTRUCT", "MONHSZSTRUCT", "MONHSZSTRUCTA", "MONHSZSTRUCTW",
            "MONIKERPROPERTY", "MONITORENUMPROC", "MONITORINFO", "MONITORINFOEX", "MONITORINFOEXA", "MONITORINFOEXW",
            "MONITOR_DISPLAY_STATE", "MONITOR_INFO_1", "MONITOR_INFO_1A", "MONITOR_INFO_1W", "MONITOR_INFO_2", "MONITOR_INFO_2A",
            "MONITOR_INFO_2W", "MONLINKSTRUCT", "MONMSGSTRUCT", "MONTHDAYSTATE", "MOUSEHOOKSTRUCT", "MOUSEHOOKSTRUCTEX",
            "MOUSEINPUT", "MOUSEKEYS", "MOUSEMOVEPOINT", "MOUSE_EVENT_RECORD", "MOVE_FILE_DATA", "MOVE_FILE_DATA32",
            "MOVE_FILE_RECORD_DATA", "MSG", "MSGBOXCALLBACK", "MSGBOXPARAMS", "MSGBOXPARAMSA", "MSGBOXPARAMSW",
            "MSHCTX", "MSHLFLAGS", "MSLLHOOKSTRUCT", "MULTIKEYHELP", "MULTIKEYHELPA", "MULTIKEYHELPW",
            "MULTI_QI", "NAMEENUMPROCA", "NAMEENUMPROCW", "NAME_BUFFER", "NCB", "NCCALCSIZE_PARAMS",
            "NCRYPT_HANDLE", "NCRYPT_HASH_HANDLE", "NCRYPT_KEY_HANDLE", "NCRYPT_PROV_HANDLE", "NCRYPT_SECRET_HANDLE", "NCRYPT_SUPPORTED_LENGTHS",
            "NCRYPT_UI_POLICY", "NC_ADDRESS", "NCryptAlgorithmName", "NCryptBuffer", "NCryptBufferDesc", "NDR_CCONTEXT",
            "NDR_CS_ROUTINES", "NDR_CS_SIZE_CONVERT_ROUTINES", "NDR_EXPR_DESC", "NDR_NOTIFY2_ROUTINE", "NDR_NOTIFY_ROUTINE", "NDR_RUNDOWN",
            "NDR_SCONTEXT", "NDR_USER_MARSHAL_INFO", "NDR_USER_MARSHAL_INFO_LEVEL1", "NEARPROC", "NETCONNECTINFOSTRUCT", "NETINFOSTRUCT",
            "NETRESOURCE", "NETRESOURCEA", "NETRESOURCEW", "NEWTEXTMETRIC", "NEWTEXTMETRICA", "NEWTEXTMETRICEX",
            "NEWTEXTMETRICEXA", "NEWTEXTMETRICEXW", "NEWTEXTMETRICW", "NLSVERSIONINFO", "NLSVERSIONINFOEX", "NLS_FUNCTION",
            "NMBCDROPDOWN", "NMBCHOTITEM", "NMCBEDRAGBEGINA", "NMCBEDRAGBEGINW", "NMCBEENDEDITA", "NMCBEENDEDITW",
            "NMCHAR", "NMCLICK", "NMCOMBOBOXEXA", "NMCOMBOBOXEXW", "NMCUSTOMDRAW", "NMCUSTOMSPLITRECTINFO",
            "NMCUSTOMTEXT", "NMDATETIMECHANGE", "NMDATETIMEFORMATA", "NMDATETIMEFORMATQUERYA", "NMDATETIMEFORMATQUERYW", "NMDATETIMEFORMATW",
            "NMDATETIMESTRINGA", "NMDATETIMESTRINGW", "NMDATETIMEWMKEYDOWNA", "NMDATETIMEWMKEYDOWNW", "NMDAYSTATE", "NMHDDISPINFOA",
            "NMHDDISPINFOW", "NMHDFILTERBTNCLICK", "NMHDR", "NMHEADERA", "NMHEADERW", "NMIPADDRESS",
            "NMITEMACTIVATE", "NMKEY", "NMLINK", "NMLISTVIEW", "NMLVCACHEHINT", "NMLVCUSTOMDRAW",
            "NMLVDISPINFOA", "NMLVDISPINFOW", "NMLVEMPTYMARKUP", "NMLVFINDITEMA", "NMLVFINDITEMW", "NMLVGETINFOTIPA",
            "NMLVGETINFOTIPW", "NMLVKEYDOWN", "NMLVLINK", "NMLVODSTATECHANGE", "NMLVSCROLL", "NMMOUSE",
            "NMOBJECTNOTIFY", "NMPGCALCSIZE", "NMPGHOTITEM", "NMPGSCROLL", "NMRBAUTOSIZE", "NMREBAR",
            "NMREBARAUTOBREAK", "NMREBARCHEVRON", "NMREBARCHILDSIZE", "NMREBARSPLITTER", "NMSELCHANGE", "NMSELECT",
            "NMTBCUSTOMDRAW", "NMTBDISPINFOA", "NMTBDISPINFOW", "NMTBGETINFOTIPA", "NMTBGETINFOTIPW", "NMTBHOTITEM",
            "NMTBRESTORE", "NMTBSAVE", "NMTCKEYDOWN", "NMTOOLBARA", "NMTOOLBARW", "NMTOOLTIPSCREATED",
            "NMTRBTHUMBPOSCHANGING", "NMTREEVIEWA", "NMTREEVIEWW", "NMTTCUSTOMDRAW", "NMTTDISPINFOA", "NMTTDISPINFOW",
            "NMTVASYNCDRAW", "NMTVCUSTOMDRAW", "NMTVDISPINFOA", "NMTVDISPINFOEXA", "NMTVDISPINFOEXW", "NMTVDISPINFOW",
            "NMTVGETINFOTIPA", "NMTVGETINFOTIPW", "NMTVITEMCHANGE", "NMTVKEYDOWN", "NMTVSTATEIMAGECHANGING", "NMUPDOWN",
            "NMVIEWCHANGE", "NONCLIENTMETRICS", "NONCLIENTMETRICSA", "NONCLIENTMETRICSW", "NON_PAGED_DEBUG_INFO", "NORM_FORM",
            "NOTIFICATION_MASK", "NOTIFYICONDATA", "NOTIFYICONDATAA", "NOTIFYICONDATAW", "NOTIFYICONIDENTIFIER", "NOTIFY_USER_POWER_SETTING",
            "NPABC", "NPABCFLOAT", "NPAUXCAPS", "NPAUXCAPS2", "NPAUXCAPS2A", "NPAUXCAPS2W",
            "NPAUXCAPSA", "NPAUXCAPSW", "NPBITMAP", "NPCANDIDATEFORM", "NPCANDIDATELIST", "NPCHARSETINFO",
            "NPCOMPOSITIONFORM", "NPCWPRETSTRUCT", "NPCWPSTRUCT", "NPDEBUGHOOKINFO", "NPDEVMODE", "NPDEVMODEA",
            "NPDEVMODEW", "NPDRVCONFIGINFO", "NPDRVCONFIGINFOEX", "NPEVENTMSG", "NPEVENTMSGMSG", "NPEXTLOGFONT",
            "NPEXTLOGFONTA", "NPEXTLOGFONTW", "NPEXTLOGPEN", "NPEXTLOGPEN32", "NPIMECHARPOSITION", "NPIMEMENUITEMINFO",
            "NPIMEMENUITEMINFOA", "NPIMEMENUITEMINFOW", "NPJOYCAPS", "NPJOYCAPS2", "NPJOYCAPS2A", "NPJOYCAPS2W",
            "NPJOYCAPSA", "NPJOYCAPSW", "NPJOYINFO", "NPJOYINFOEX", "NPLOGBRUSH", "NPLOGBRUSH32",
            "NPLOGFONT", "NPLOGFONTA", "NPLOGFONTW", "NPLOGPALETTE", "NPLOGPEN", "NPMIDIHDR",
            "NPMIDIINCAPS", "NPMIDIINCAPS2", "NPMIDIINCAPS2A", "NPMIDIINCAPS2W", "NPMIDIINCAPSA", "NPMIDIINCAPSW",
            "NPMIDIOUTCAPS", "NPMIDIOUTCAPS2", "NPMIDIOUTCAPS2A", "NPMIDIOUTCAPS2W", "NPMIDIOUTCAPSA", "NPMIDIOUTCAPSW",
            "NPMMCKINFO", "NPMMIOINFO", "NPMMTIME", "NPMSG", "NPNEWTEXTMETRIC", "NPNEWTEXTMETRICA",
            "NPNEWTEXTMETRICW", "NPOUTLINETEXTMETRIC", "NPOUTLINETEXTMETRICA", "NPOUTLINETEXTMETRICW", "NPPAINTSTRUCT", "NPPATTERN",
            "NPPCMWAVEFORMAT", "NPPELARRAY", "NPPOINT", "NPPOLYTEXT", "NPPOLYTEXTA", "NPPOLYTEXTW",
            "NPRECONVERTSTRING", "NPRECT", "NPREGISTERWORD", "NPREGISTERWORDA", "NPREGISTERWORDW", "NPRGBTRIPLE",
            "NPRGNDATA", "NPSTR", "NPSTYLEBUF", "NPSTYLEBUFA", "NPSTYLEBUFW", "NPTEXTMETRIC",
            "NPTEXTMETRICA", "NPTEXTMETRICW", "NPTIMECAPS", "NPWAVEFORMAT", "NPWAVEFORMATEX", "NPWAVEHDR",
            "NPWAVEINCAPS", "NPWAVEINCAPS2", "NPWAVEINCAPS2A", "NPWAVEINCAPS2W", "NPWAVEINCAPSA", "NPWAVEINCAPSW",
            "NPWAVEOUTCAPS", "NPWAVEOUTCAPS2", "NPWAVEOUTCAPS2A", "NPWAVEOUTCAPS2W", "NPWAVEOUTCAPSA", "NPWAVEOUTCAPSW",
            "NPWNDCLASS", "NPWNDCLASSA", "NPWNDCLASSEX", "NPWNDCLASSEXA", "NPWNDCLASSEXW", "NPWNDCLASSW",
            "NTFS_EXTENDED_VOLUME_DATA", "NTFS_FILE_RECORD_INPUT_BUFFER", "NTFS_FILE_RECORD_OUTPUT_BUFFER", "NTFS_STATISTICS", "NTFS_VOLUME_DATA_BUFFER", "NTSTATUS",
            "NT_TIB", "NT_TIB32", "NT_TIB64", "NUMA_NODE_RELATIONSHIP", "NUMBERFMT", "NUMBERFMTA",
            "NUMBERFMTW", "NUMPARSE", "NWPSTR", "OBJECTDESCRIPTOR", "OBJECTID", "OBJECT_TYPE_LIST",
            "OCSP_BASIC_RESPONSE_ENTRY", "OCSP_BASIC_RESPONSE_INFO", "OCSP_BASIC_REVOKED_INFO", "OCSP_BASIC_SIGNED_RESPONSE_INFO", "OCSP_CERT_ID", "OCSP_REQUEST_ENTRY",
            "OCSP_REQUEST_INFO", "OCSP_RESPONSE_INFO", "OCSP_SIGNATURE_INFO", "OCSP_SIGNED_REQUEST_INFO", "OFNOTIFY", "OFNOTIFYA",
            "OFNOTIFYEX", "OFNOTIFYEXA", "OFNOTIFYEXW", "OFNOTIFYW", "OFSTRUCT", "OIBDG_FLAGS",
            "OLDFONTENUMPROCA", "OLDFONTENUMPROCW", "OLECHAR", "OLECLOSE", "OLECONTF", "OLEGETMONIKER",
            "OLEINPLACEFRAMEINFO", "OLELINKBIND", "OLEMENUGROUPWIDTHS", "OLEMISC", "OLERENDER", "OLESTREAM",
            "OLESTREAMVTBL", "OLEUPDATE", "OLEVERB", "OLEVERBATTRIB", "OLEWHICHMK", "OPENCARDNAME",
            "OPENCARDNAMEA", "OPENCARDNAMEW", "OPENCARDNAME_EX", "OPENCARDNAME_EXA", "OPENCARDNAME_EXW", "OPENCARD_SEARCH_CRITERIA",
            "OPENCARD_SEARCH_CRITERIAA", "OPENCARD_SEARCH_CRITERIAW", "OPENFILENAME", "OPENFILENAMEA", "OPENFILENAMEW", "OPENFILENAME_NT4",
            "OPENFILENAME_NT4A", "OPENFILENAME_NT4W", "OPEN_PRINTER_PROPS_INFO", "OPEN_PRINTER_PROPS_INFOA", "OPEN_PRINTER_PROPS_INFOW", "OPEN_VIRTUAL_DISK_FLAG",
            "OPEN_VIRTUAL_DISK_PARAMETERS", "OPEN_VIRTUAL_DISK_VERSION", "OSVERSIONINFO", "OSVERSIONINFOA", "OSVERSIONINFOEX", "OSVERSIONINFOEXA",
            "OSVERSIONINFOEXW", "OSVERSIONINFOW", "OUTLINETEXTMETRIC", "OUTLINETEXTMETRICA", "OUTLINETEXTMETRICW", "OUTPUT_DEBUG_STRING_INFO",
            "OVERLAPPED", "OVERLAPPED_ENTRY", "PABC", "PABCFLOAT", "PACCESS_ALLOWED_ACE", "PACCESS_ALLOWED_CALLBACK_ACE",
            "PACCESS_ALLOWED_CALLBACK_OBJECT_ACE", "PACCESS_ALLOWED_OBJECT_ACE", "PACCESS_DENIED_ACE", "PACCESS_DENIED_CALLBACK_ACE", "PACCESS_DENIED_CALLBACK_OBJECT_ACE", "PACCESS_DENIED_OBJECT_ACE",
            "PACCESS_MASK", "PACCESS_REASONS", "PACCESS_TOKEN", "PACE_HEADER", "PACKEDEVENTINFO", "PACL",
            "PACL_REVISION_INFORMATION", "PACL_SIZE_INFORMATION", "PACTCTX", "PACTCTXA", "PACTCTXW", "PACTCTX_SECTION_KEYED_DATA",
            "PACTCTX_SECTION_KEYED_DATA_2600", "PACTCTX_SECTION_KEYED_DATA_ASSEMBLY_METADATA", "PACTION_HEADER", "PACTIVATION_CONTEXT_ASSEMBLY_DETAILED_INFORMATION", "PACTIVATION_CONTEXT_BASIC_INFORMATION", "PACTIVATION_CONTEXT_COMPATIBILITY_INFORMATION",
            "PACTIVATION_CONTEXT_DETAILED_INFORMATION", "PACTIVATION_CONTEXT_QUERY_INDEX", "PACTIVATION_CONTEXT_RUN_LEVEL_INFORMATION", "PADAPTER_STATUS", "PADDJOB_INFO_1", "PADDJOB_INFO_1A",
            "PADDJOB_INFO_1W", "PADMINISTRATOR_POWER_POLICY", "PAGESETUPDLG", "PAGESETUPDLGA", "PAGESETUPDLGW", "PAINTSTRUCT",
            "PALETTEENTRY", "PALTTABINFO", "PANOSE", "PAPCFUNC", "PAPPBARDATA", "PAPPLICATIONLAUNCH_SETTING_VALUE",
            "PAPP_MEMORY_INFORMATION", "PARAMDATA", "PARAMDESC", "PARAMDESCEX", "PARAM_OFFSETTABLE", "PARM64EC_NT_CONTEXT",
            "PARM64_NT_CONTEXT", "PARM64_NT_NEON128", "PARM64_RUNTIME_FUNCTION", "PARRAY_INFO", "PARSEACTION", "PARTITION_INFORMATION",
            "PARTITION_INFORMATION_EX", "PARTITION_INFORMATION_GPT", "PARTITION_INFORMATION_MBR", "PARTITION_STYLE", "PAR_STATE", "PASSEMBLY_FILE_DETAILED_INFORMATION",
            "PATCHARRAY", "PATHNAME_BUFFER", "PATTACH_VIRTUAL_DISK_PARAMETERS", "PATTERN", "PATTRIBUTES_AND_SID", "PAUDIT_EVENT_TYPE",
            "PAUTHENTICODE_EXTRA_CERT_CHAIN_POLICY_PARA", "PAUTHENTICODE_EXTRA_CERT_CHAIN_POLICY_STATUS", "PAUTHENTICODE_TS_EXTRA_CERT_CHAIN_POLICY_PARA", "PAUXCAPS", "PAUXCAPS2", "PAUXCAPS2A",
            "PAUXCAPS2W", "PAUXCAPSA", "PAUXCAPSW", "PAXESLIST", "PAXESLISTA", "PAXESLISTW",
            "PAXISINFO", "PAXISINFOA", "PAXISINFOW", "PBAD_TRACK_NUMBER", "PBATTERY_REPORTING_SCALE", "PBCRYPT_AUTHENTICATED_CIPHER_MODE_INFO",
            "PBCRYPT_DH_KEY_BLOB", "PBCRYPT_DSA_KEY_BLOB", "PBCRYPT_DSA_KEY_BLOB_V2", "PBCRYPT_ECCKEY_BLOB", "PBCRYPT_INTERFACE_VERSION", "PBCRYPT_KEY_DATA_BLOB_HEADER",
            "PBCryptBuffer", "PBCryptBufferDesc", "PBIDI_DATA", "PBIDI_REQUEST_CONTAINER", "PBIDI_REQUEST_DATA", "PBIDI_RESPONSE_CONTAINER",
            "PBIDI_RESPONSE_DATA", "PBINARY_CONTAINER", "PBIN_COUNT", "PBIN_RANGE", "PBIN_RESULTS", "PBITMAP",
            "PBITMAPCOREHEADER", "PBITMAPCOREINFO", "PBITMAPFILEHEADER", "PBITMAPINFO", "PBITMAPINFOHEADER", "PBITMAPV4HEADER",
            "PBITMAPV5HEADER", "PBLENDFUNCTION", "PBOOL", "PBOOLEAN", "PBOOT_AREA_INFO", "PBRANGE",
            "PBSMINFO", "PBULK_SECURITY_TEST_DATA", "PBUTTON_IMAGELIST", "PBUTTON_SPLITINFO", "PBYTE", "PBY_HANDLE_FILE_INFORMATION",
            "PCACHE_DESCRIPTOR", "PCACHE_RELATIONSHIP", "PCACTCTX", "PCACTCTXA", "PCACTCTXW", "PCACTCTX_SECTION_KEYED_DATA",
            "PCACTCTX_SECTION_KEYED_DATA_2600", "PCACTCTX_SECTION_KEYED_DATA_ASSEMBLY_METADATA", "PCACTIVATION_CONTEXT_ASSEMBLY_DETAILED_INFORMATION", "PCACTIVATION_CONTEXT_BASIC_INFORMATION", "PCACTIVATION_CONTEXT_COMPATIBILITY_INFORMATION", "PCACTIVATION_CONTEXT_DETAILED_INFORMATION",
            "PCACTIVATION_CONTEXT_QUERY_INDEX", "PCACTIVATION_CONTEXT_RUN_LEVEL_INFORMATION", "PCANDIDATEFORM", "PCANDIDATELIST", "PCASSEMBLY_FILE_DETAILED_INFORMATION", "PCCERT_CHAIN_CONTEXT",
            "PCCERT_CHAIN_ELEMENT", "PCCERT_CONTEXT", "PCCERT_CRL_CONTEXT_PAIR", "PCCERT_ENHKEY_USAGE", "PCCERT_EXTENSION", "PCCERT_SELECT_CHAIN_PARA",
            "PCCERT_SELECT_CRITERIA", "PCCERT_SERVER_OCSP_RESPONSE_CONTEXT", "PCCERT_SIMPLE_CHAIN", "PCCERT_STORE_PROV_FIND_INFO", "PCCERT_STRONG_SIGN_PARA", "PCCH",
            "PCCOMBOEXITEMA", "PCCOMBOEXITEMW", "PCCOMPATIBILITY_CONTEXT_ELEMENT", "PCCRL_CONTEXT", "PCCRYPT_OID_INFO", "PCCTL_CONTEXT",
            "PCCTL_USAGE", "PCERT_ACCESS_DESCRIPTION", "PCERT_ALT_NAME_ENTRY", "PCERT_ALT_NAME_INFO", "PCERT_AUTHORITY_INFO_ACCESS", "PCERT_AUTHORITY_KEY_ID2_INFO",
            "PCERT_AUTHORITY_KEY_ID_INFO", "PCERT_BASIC_CONSTRAINTS2_INFO", "PCERT_BASIC_CONSTRAINTS_INFO", "PCERT_BIOMETRIC_DATA", "PCERT_BIOMETRIC_EXT_INFO", "PCERT_BLOB",
            "PCERT_CHAIN", "PCERT_CHAIN_CONTEXT", "PCERT_CHAIN_ELEMENT", "PCERT_CHAIN_ENGINE_CONFIG", "PCERT_CHAIN_FIND_BY_ISSUER_PARA", "PCERT_CHAIN_FIND_ISSUER_PARA",
            "PCERT_CHAIN_PARA", "PCERT_CHAIN_POLICY_PARA", "PCERT_CHAIN_POLICY_STATUS", "PCERT_CONTEXT", "PCERT_CREATE_CONTEXT_PARA", "PCERT_CRL_CONTEXT_PAIR",
            "PCERT_DH_PARAMETERS", "PCERT_DSS_PARAMETERS", "PCERT_ECC_SIGNATURE", "PCERT_ENHKEY_USAGE", "PCERT_EXTENSION", "PCERT_EXTENSIONS",
            "PCERT_GENERAL_SUBTREE", "PCERT_HASHED_URL", "PCERT_ID", "PCERT_INFO", "PCERT_ISSUER_SERIAL_NUMBER", "PCERT_KEYGEN_REQUEST_INFO",
            "PCERT_KEY_ATTRIBUTES_INFO", "PCERT_KEY_CONTEXT", "PCERT_KEY_USAGE_RESTRICTION_INFO", "PCERT_LDAP_STORE_OPENED_PARA", "PCERT_LOGOTYPE_AUDIO", "PCERT_LOGOTYPE_AUDIO_INFO",
            "PCERT_LOGOTYPE_DATA", "PCERT_LOGOTYPE_DETAILS", "PCERT_LOGOTYPE_EXT_INFO", "PCERT_LOGOTYPE_IMAGE", "PCERT_LOGOTYPE_IMAGE_INFO", "PCERT_LOGOTYPE_INFO",
            "PCERT_LOGOTYPE_REFERENCE", "PCERT_NAME_BLOB", "PCERT_NAME_CONSTRAINTS_INFO", "PCERT_NAME_INFO", "PCERT_NAME_VALUE", "PCERT_OR_CRL_BLOB",
            "PCERT_OR_CRL_BUNDLE", "PCERT_OTHER_LOGOTYPE_INFO", "PCERT_OTHER_NAME", "PCERT_PAIR", "PCERT_PHYSICAL_STORE_INFO", "PCERT_POLICIES_INFO",
            "PCERT_POLICY95_QUALIFIER1", "PCERT_POLICY_CONSTRAINTS_INFO", "PCERT_POLICY_ID", "PCERT_POLICY_INFO", "PCERT_POLICY_MAPPING", "PCERT_POLICY_MAPPINGS_INFO",
            "PCERT_POLICY_QUALIFIER_INFO", "PCERT_POLICY_QUALIFIER_NOTICE_REFERENCE", "PCERT_POLICY_QUALIFIER_USER_NOTICE", "PCERT_PRIVATE_KEY_VALIDITY", "PCERT_PUBLIC_KEY_INFO", "PCERT_QC_STATEMENT",
            "PCERT_QC_STATEMENTS_EXT_INFO", "PCERT_RDN", "PCERT_RDN_ATTR", "PCERT_RDN_VALUE_BLOB", "PCERT_REGISTRY_STORE_CLIENT_GPT_PARA", "PCERT_REGISTRY_STORE_ROAMING_PARA",
            "PCERT_REQUEST_INFO", "PCERT_REVOCATION_CHAIN_PARA", "PCERT_REVOCATION_CRL_INFO", "PCERT_REVOCATION_INFO", "PCERT_REVOCATION_PARA", "PCERT_REVOCATION_STATUS",
            "PCERT_SELECT_CHAIN_PARA", "PCERT_SELECT_CRITERIA", "PCERT_SERVER_OCSP_RESPONSE_CONTEXT", "PCERT_SERVER_OCSP_RESPONSE_OPEN_PARA", "PCERT_SIGNED_CONTENT_INFO", "PCERT_SIMPLE_CHAIN",
            "PCERT_STORE_PROV_FIND_INFO", "PCERT_STORE_PROV_INFO", "PCERT_STRONG_SIGN_PARA", "PCERT_STRONG_SIGN_SERIALIZED_INFO", "PCERT_SUBJECT_INFO_ACCESS", "PCERT_SUPPORTED_ALGORITHM_INFO",
            "PCERT_SYSTEM_STORE_INFO", "PCERT_SYSTEM_STORE_RELOCATE_PARA", "PCERT_TEMPLATE_EXT", "PCERT_TPM_SPECIFICATION_INFO", "PCERT_TRUST_LIST_INFO", "PCERT_TRUST_STATUS",
            "PCERT_USAGE_MATCH", "PCERT_X942_DH_PARAMETERS", "PCERT_X942_DH_VALIDATION_PARAMS", "PCFG_CALL_TARGET_INFO", "PCGESTUREINFO", "PCH",
            "PCHANGEFILTERSTRUCT", "PCHANGER_DEVICE_PROBLEM_TYPE", "PCHANGER_ELEMENT", "PCHANGER_ELEMENT_LIST", "PCHANGER_ELEMENT_STATUS", "PCHANGER_ELEMENT_STATUS_EX",
            "PCHANGER_EXCHANGE_MEDIUM", "PCHANGER_INITIALIZE_ELEMENT_STATUS", "PCHANGER_MOVE_MEDIUM", "PCHANGER_PRODUCT_DATA", "PCHANGER_READ_ELEMENT_STATUS", "PCHANGER_SEND_VOLUME_TAG_INFORMATION",
            "PCHANGER_SET_ACCESS", "PCHANGER_SET_POSITION", "PCHAR", "PCHARSETINFO", "PCHAR_INFO", "PCIMAGE_DELAYLOAD_DESCRIPTOR",
            "PCLAIMS_BLOB", "PCLAIM_SECURITY_ATTRIBUTES_INFORMATION", "PCLAIM_SECURITY_ATTRIBUTE_FQBN_VALUE", "PCLAIM_SECURITY_ATTRIBUTE_OCTET_STRING_VALUE", "PCLAIM_SECURITY_ATTRIBUTE_RELATIVE_V1", "PCLAIM_SECURITY_ATTRIBUTE_V1",
            "PCLASS_MEDIA_CHANGE_CONTEXT", "PCMC_ADD_ATTRIBUTES_INFO", "PCMC_ADD_EXTENSIONS_INFO", "PCMC_DATA_INFO", "PCMC_PEND_INFO", "PCMC_RESPONSE_INFO",
            "PCMC_STATUS_INFO", "PCMC_TAGGED_ATTRIBUTE", "PCMC_TAGGED_CERT_REQUEST", "PCMC_TAGGED_CONTENT_INFO", "PCMC_TAGGED_OTHER_MSG", "PCMC_TAGGED_REQUEST",
            "PCMSG_ATTR", "PCMSG_CMS_RECIPIENT_INFO", "PCMSG_CMS_SIGNER_INFO", "PCMSG_CNG_CONTENT_DECRYPT_INFO", "PCMSG_CONTENT_ENCRYPT_INFO", "PCMSG_CTRL_ADD_SIGNER_UNAUTH_ATTR_PARA",
            "PCMSG_CTRL_DECRYPT_PARA", "PCMSG_CTRL_DEL_SIGNER_UNAUTH_ATTR_PARA", "PCMSG_CTRL_KEY_AGREE_DECRYPT_PARA", "PCMSG_CTRL_KEY_TRANS_DECRYPT_PARA", "PCMSG_CTRL_MAIL_LIST_DECRYPT_PARA", "PCMSG_CTRL_VERIFY_SIGNATURE_EX_PARA",
            "PCMSG_ENCRYPTED_ENCODE_INFO", "PCMSG_ENVELOPED_ENCODE_INFO", "PCMSG_HASHED_ENCODE_INFO", "PCMSG_KEY_AGREE_ENCRYPT_INFO", "PCMSG_KEY_AGREE_KEY_ENCRYPT_INFO", "PCMSG_KEY_AGREE_RECIPIENT_ENCODE_INFO",
            "PCMSG_KEY_AGREE_RECIPIENT_INFO", "PCMSG_KEY_TRANS_ENCRYPT_INFO", "PCMSG_KEY_TRANS_RECIPIENT_ENCODE_INFO", "PCMSG_KEY_TRANS_RECIPIENT_INFO", "PCMSG_MAIL_LIST_ENCRYPT_INFO", "PCMSG_MAIL_LIST_RECIPIENT_ENCODE_INFO",
            "PCMSG_MAIL_LIST_RECIPIENT_INFO", "PCMSG_RC2_AUX_INFO", "PCMSG_RC4_AUX_INFO", "PCMSG_RECIPIENT_ENCODE_INFO", "PCMSG_RECIPIENT_ENCRYPTED_KEY_ENCODE_INFO", "PCMSG_RECIPIENT_ENCRYPTED_KEY_INFO",
            "PCMSG_SIGNED_AND_ENVELOPED_ENCODE_INFO", "PCMSG_SIGNED_ENCODE_INFO", "PCMSG_SIGNER_ENCODE_INFO", "PCMSG_SIGNER_INFO", "PCMSG_SP3_COMPATIBLE_AUX_INFO", "PCMSG_STREAM_INFO",
            "PCMS_DH_KEY_INFO", "PCMS_KEY_INFO", "PCMWAVEFORMAT", "PCM_POWER_DATA", "PCNZCH", "PCNZTCH",
            "PCNZWCH", "PCOLORADJUSTMENT", "PCOMBOBOXEXITEMA", "PCOMBOBOXEXITEMW", "PCOMBOBOXINFO", "PCOMPACT_VIRTUAL_DISK_PARAMETERS",
            "PCOMPAREITEMSTRUCT", "PCOMPARTMENT_ID", "PCOMPATIBILITY_CONTEXT_ELEMENT", "PCOMPONENT_FILTER", "PCOMPOSITIONFORM", "PCONDITION_VARIABLE",
            "PCONSOLE_CURSOR_INFO", "PCONSOLE_FONT_INFO", "PCONSOLE_FONT_INFOEX", "PCONSOLE_HISTORY_INFO", "PCONSOLE_READCONSOLE_CONTROL", "PCONSOLE_SCREEN_BUFFER_INFO",
            "PCONSOLE_SCREEN_BUFFER_INFOEX", "PCONSOLE_SELECTION_INFO", "PCONTEXT", "PCONVCONTEXT", "PCONVINFO", "PCOORD",
            "PCOPYDATASTRUCT", "PCOPYFILE2_PROGRESS_ROUTINE", "PCORE_PRINTER_DRIVER", "PCORE_PRINTER_DRIVERA", "PCORE_PRINTER_DRIVERW", "PCPS_URLS",
            "PCPU_SET_INFORMATION_TYPE", "PCRAWINPUTDEVICE", "PCREATE_DISK", "PCREATE_DISK_GPT", "PCREATE_DISK_MBR", "PCREATE_USN_JOURNAL_DATA",
            "PCREATE_VIRTUAL_DISK_PARAMETERS", "PCREDIRECTION_DESCRIPTOR", "PCREDIRECTION_FUNCTION_DESCRIPTOR", "PCRITICAL_SECTION", "PCRITICAL_SECTION_DEBUG", "PCRL_BLOB",
            "PCRL_CONTEXT", "PCRL_DIST_POINT", "PCRL_DIST_POINTS_INFO", "PCRL_DIST_POINT_NAME", "PCRL_ENTRY", "PCRL_FIND_ISSUED_FOR_PARA",
            "PCRL_INFO", "PCRL_ISSUING_DIST_POINT", "PCRL_REVOCATION_INFO", "PCRM_PROTOCOL_ID", "PCROSS_CERT_DIST_POINTS_INFO", "PCRYPTNET_URL_CACHE_FLUSH_INFO",
            "PCRYPTNET_URL_CACHE_PRE_FETCH_INFO", "PCRYPTNET_URL_CACHE_RESPONSE_INFO", "PCRYPTPROTECT_PROMPTSTRUCT", "PCRYPT_3DES_KEY_STATE", "PCRYPT_AES_128_KEY_STATE", "PCRYPT_AES_256_KEY_STATE",
            "PCRYPT_ALGORITHM_IDENTIFIER", "PCRYPT_ASYNC_RETRIEVAL_COMPLETION", "PCRYPT_ATTRIBUTE", "PCRYPT_ATTRIBUTES", "PCRYPT_ATTRIBUTE_TYPE_VALUE", "PCRYPT_ATTR_BLOB",
            "PCRYPT_BIT_BLOB", "PCRYPT_BLOB_ARRAY", "PCRYPT_CONTENT_INFO", "PCRYPT_CONTENT_INFO_SEQUENCE_OF_ANY", "PCRYPT_CONTEXTS", "PCRYPT_CONTEXT_CONFIG",
            "PCRYPT_CONTEXT_FUNCTIONS", "PCRYPT_CONTEXT_FUNCTION_CONFIG", "PCRYPT_CONTEXT_FUNCTION_PROVIDERS", "PCRYPT_CREDENTIALS", "PCRYPT_CSP_PROVIDER", "PCRYPT_DATA_BLOB",
            "PCRYPT_DECODE_PARA", "PCRYPT_DECRYPT_MESSAGE_PARA", "PCRYPT_DECRYPT_PRIVATE_KEY_FUNC", "PCRYPT_DEFAULT_CONTEXT_MULTI_OID_PARA", "PCRYPT_DER_BLOB", "PCRYPT_DES_KEY_STATE",
            "PCRYPT_DIGEST_BLOB", "PCRYPT_ECC_CMS_SHARED_INFO", "PCRYPT_ECC_PRIVATE_KEY_INFO", "PCRYPT_ENCODE_PARA", "PCRYPT_ENCRYPTED_PRIVATE_KEY_INFO", "PCRYPT_ENCRYPT_MESSAGE_PARA",
            "PCRYPT_ENCRYPT_PRIVATE_KEY_FUNC", "PCRYPT_ENROLLMENT_NAME_VALUE_PAIR", "PCRYPT_GET_TIME_VALID_OBJECT_EXTRA_INFO", "PCRYPT_HASH_BLOB", "PCRYPT_HASH_INFO", "PCRYPT_HASH_MESSAGE_PARA",
            "PCRYPT_IMAGE_REF", "PCRYPT_IMAGE_REG", "PCRYPT_INTEGER_BLOB", "PCRYPT_INTERFACE_REG", "PCRYPT_KEY_PROV_INFO", "PCRYPT_KEY_PROV_PARAM",
            "PCRYPT_KEY_SIGN_MESSAGE_PARA", "PCRYPT_KEY_VERIFY_MESSAGE_PARA", "PCRYPT_MASK_GEN_ALGORITHM", "PCRYPT_OBJID_BLOB", "PCRYPT_OBJID_TABLE", "PCRYPT_OID_FUNC_ENTRY",
            "PCRYPT_OID_INFO", "PCRYPT_PASSWORD_CREDENTIALS", "PCRYPT_PASSWORD_CREDENTIALSA", "PCRYPT_PASSWORD_CREDENTIALSW", "PCRYPT_PKCS8_EXPORT_PARAMS", "PCRYPT_PKCS8_IMPORT_PARAMS",
            "PCRYPT_PRIVATE_KEY_BLOB_AND_PARAMS", "PCRYPT_PRIVATE_KEY_INFO", "PCRYPT_PROPERTY_REF", "PCRYPT_PROVIDERS", "PCRYPT_PROVIDER_REF", "PCRYPT_PROVIDER_REFS",
            "PCRYPT_PROVIDER_REG", "PCRYPT_PSOURCE_ALGORITHM", "PCRYPT_RC2_CBC_PARAMETERS", "PCRYPT_RC4_KEY_STATE", "PCRYPT_RESOLVE_HCRYPTPROV_FUNC", "PCRYPT_RETRIEVE_AUX_INFO",
            "PCRYPT_RSAES_OAEP_PARAMETERS", "PCRYPT_RSA_SSA_PSS_PARAMETERS", "PCRYPT_SEQUENCE_OF_ANY", "PCRYPT_SIGN_MESSAGE_PARA", "PCRYPT_SMART_CARD_ROOT_INFO", "PCRYPT_SMIME_CAPABILITIES",
            "PCRYPT_SMIME_CAPABILITY", "PCRYPT_TIMESTAMP_ACCURACY", "PCRYPT_TIMESTAMP_CONTEXT", "PCRYPT_TIMESTAMP_INFO", "PCRYPT_TIMESTAMP_PARA", "PCRYPT_TIMESTAMP_REQUEST",
            "PCRYPT_TIMESTAMP_RESPONSE", "PCRYPT_TIME_STAMP_REQUEST_INFO", "PCRYPT_UINT_BLOB", "PCRYPT_URL_ARRAY", "PCRYPT_URL_INFO", "PCRYPT_VERIFY_CERT_SIGN_STRONG_PROPERTIES_INFO",
            "PCRYPT_VERIFY_MESSAGE_PARA", "PCRYPT_X942_OTHER_INFO", "PCSTR", "PCSV_NAMESPACE_INFO", "PCTL_ANY_SUBJECT_INFO", "PCTL_CONTEXT",
            "PCTL_ENTRY", "PCTL_FIND_SUBJECT_PARA", "PCTL_FIND_USAGE_PARA", "PCTL_INFO", "PCTL_USAGE", "PCTL_USAGE_MATCH",
            "PCTL_VERIFY_USAGE_PARA", "PCTL_VERIFY_USAGE_STATUS", "PCTOUCHINPUT", "PCTSTR", "PCUCSCHAR", "PCUCSSTR",
            "PCUNZTCH", "PCUNZWCH", "PCURSORINFO", "PCUTSTR", "PCUUCSCHAR", "PCUUCSSTR",
            "PCUWCHAR", "PCUWSTR", "PCUZZTSTR", "PCUZZWSTR", "PCWCH", "PCWCHAR",
            "PCWPRETSTRUCT", "PCWPSTRUCT", "PCWSTR", "PCZPSTR", "PCZPWSTR", "PCZZSTR",
            "PCZZTSTR", "PCZZWSTR", "PDATATYPES_INFO_1", "PDATATYPES_INFO_1A", "PDATATYPES_INFO_1W", "PDATA_BLOB",
            "PDDEML_MSG_HOOK_DATA", "PDEBUGHOOKINFO", "PDECRYPTION_STATUS_BUFFER", "PDELETEITEMSTRUCT", "PDELETE_USN_JOURNAL_DATA", "PDESIGNVECTOR",
            "PDEVICE_COPY_OFFLOAD_DESCRIPTOR", "PDEVICE_DATA_SET_RANGE", "PDEVICE_DSM_NOTIFICATION_PARAMETERS", "PDEVICE_LB_PROVISIONING_DESCRIPTOR", "PDEVICE_MANAGE_DATA_SET_ATTRIBUTES", "PDEVICE_MEDIA_INFO",
            "PDEVICE_POWER_DESCRIPTOR", "PDEVICE_POWER_STATE", "PDEVICE_SEEK_PENALTY_DESCRIPTOR", "PDEVICE_TRIM_DESCRIPTOR", "PDEVICE_WRITE_AGGREGATION_DESCRIPTOR", "PDEVMODE",
            "PDEVMODEA", "PDEVMODEW", "PDIBSECTION", "PDISK_CACHE_INFORMATION", "PDISK_CONTROLLER_NUMBER", "PDISK_DETECTION_INFO",
            "PDISK_EXTENT", "PDISK_EX_INT13_INFO", "PDISK_GEOMETRY", "PDISK_GEOMETRY_EX", "PDISK_GROW_PARTITION", "PDISK_HISTOGRAM",
            "PDISK_INT13_INFO", "PDISK_LOGGING", "PDISK_PARTITION_INFO", "PDISK_PERFORMANCE", "PDISK_RECORD", "PDISPATCHER_CONTEXT",
            "PDISPATCHER_CONTEXT_ARM64", "PDISPLAY_DEVICE", "PDISPLAY_DEVICEA", "PDISPLAY_DEVICEW", "PDLGITEMTEMPLATE", "PDLGITEMTEMPLATEA",
            "PDLGITEMTEMPLATEW", "PDLL_DIRECTORY_COOKIE", "PDOC_INFO_1", "PDOC_INFO_1A", "PDOC_INFO_1W", "PDOC_INFO_2",
            "PDOC_INFO_2A", "PDOC_INFO_2W", "PDOC_INFO_3", "PDOC_INFO_3A", "PDOC_INFO_3W", "PDRAWITEMSTRUCT",
            "PDRAWPATRECT", "PDRIVERSTATUS", "PDRIVER_INFO_1", "PDRIVER_INFO_1A", "PDRIVER_INFO_1W", "PDRIVER_INFO_2",
            "PDRIVER_INFO_2A", "PDRIVER_INFO_2W", "PDRIVER_INFO_3", "PDRIVER_INFO_3A", "PDRIVER_INFO_3W", "PDRIVER_INFO_4",
            "PDRIVER_INFO_4A", "PDRIVER_INFO_4W", "PDRIVER_INFO_5", "PDRIVER_INFO_5A", "PDRIVER_INFO_5W", "PDRIVER_INFO_6",
            "PDRIVER_INFO_6A", "PDRIVER_INFO_6W", "PDRIVER_INFO_8", "PDRIVER_INFO_8A", "PDRIVER_INFO_8W", "PDRIVE_LAYOUT_INFORMATION",
            "PDRIVE_LAYOUT_INFORMATION_EX", "PDRIVE_LAYOUT_INFORMATION_GPT", "PDRIVE_LAYOUT_INFORMATION_MBR", "PDROPSTRUCT", "PDRVCALLBACK", "PDRVCONFIGINFO",
            "PDRVCONFIGINFOEX", "PDWORD", "PDWORD32", "PDWORD64", "PDWORDLONG", "PDWORD_PTR",
            "PDYNAMIC_TIME_ZONE_INFORMATION", "PEDITBALLOONTIP", "PEFS_CERTIFICATE_BLOB", "PEFS_HASH_BLOB", "PEFS_KEY_INFO", "PEFS_RPC_BLOB",
            "PELARRAY", "PELEMENT_TYPE", "PEMR", "PEMRABORTPATH", "PEMRALPHABLEND", "PEMRANGLEARC",
            "PEMRARC", "PEMRARCTO", "PEMRBEGINPATH", "PEMRBITBLT", "PEMRCHORD", "PEMRCLOSEFIGURE",
            "PEMRCOLORCORRECTPALETTE", "PEMRCOLORMATCHTOTARGET", "PEMRCREATEBRUSHINDIRECT", "PEMRCREATECOLORSPACE", "PEMRCREATECOLORSPACEW", "PEMRCREATEDIBPATTERNBRUSHPT",
            "PEMRCREATEMONOBRUSH", "PEMRCREATEPALETTE", "PEMRCREATEPEN", "PEMRDELETECOLORSPACE", "PEMRDELETEOBJECT", "PEMRDRAWESCAPE",
            "PEMRELLIPSE", "PEMRENDPATH", "PEMREOF", "PEMREXCLUDECLIPRECT", "PEMREXTCREATEFONTINDIRECTW", "PEMREXTCREATEPEN",
            "PEMREXTESCAPE", "PEMREXTFLOODFILL", "PEMREXTSELECTCLIPRGN", "PEMREXTTEXTOUTA", "PEMREXTTEXTOUTW", "PEMRFILLPATH",
            "PEMRFILLRGN", "PEMRFLATTENPATH", "PEMRFORMAT", "PEMRFRAMERGN", "PEMRGDICOMMENT", "PEMRGLSBOUNDEDRECORD",
            "PEMRGLSRECORD", "PEMRGRADIENTFILL", "PEMRINTERSECTCLIPRECT", "PEMRINVERTRGN", "PEMRLINETO", "PEMRMASKBLT",
            "PEMRMODIFYWORLDTRANSFORM", "PEMRMOVETOEX", "PEMRNAMEDESCAPE", "PEMROFFSETCLIPRGN", "PEMRPAINTRGN", "PEMRPIE",
            "PEMRPIXELFORMAT", "PEMRPLGBLT", "PEMRPOLYBEZIER", "PEMRPOLYBEZIER16", "PEMRPOLYBEZIERTO", "PEMRPOLYBEZIERTO16",
            "PEMRPOLYDRAW", "PEMRPOLYDRAW16", "PEMRPOLYGON", "PEMRPOLYGON16", "PEMRPOLYLINE", "PEMRPOLYLINE16",
            "PEMRPOLYLINETO", "PEMRPOLYLINETO16", "PEMRPOLYPOLYGON", "PEMRPOLYPOLYGON16", "PEMRPOLYPOLYLINE", "PEMRPOLYPOLYLINE16",
            "PEMRPOLYTEXTOUTA", "PEMRPOLYTEXTOUTW", "PEMRREALIZEPALETTE", "PEMRRECTANGLE", "PEMRRESIZEPALETTE", "PEMRRESTOREDC",
            "PEMRROUNDRECT", "PEMRSAVEDC", "PEMRSCALEVIEWPORTEXTEX", "PEMRSCALEWINDOWEXTEX", "PEMRSELECTCLIPPATH", "PEMRSELECTCOLORSPACE",
            "PEMRSELECTOBJECT", "PEMRSELECTPALETTE", "PEMRSETARCDIRECTION", "PEMRSETBKCOLOR", "PEMRSETBKMODE", "PEMRSETBRUSHORGEX",
            "PEMRSETCOLORADJUSTMENT", "PEMRSETCOLORSPACE", "PEMRSETDIBITSTODEVICE", "PEMRSETICMMODE", "PEMRSETICMPROFILE", "PEMRSETICMPROFILEA",
            "PEMRSETICMPROFILEW", "PEMRSETLAYOUT", "PEMRSETMAPMODE", "PEMRSETMAPPERFLAGS", "PEMRSETMETARGN", "PEMRSETMITERLIMIT",
            "PEMRSETPALETTEENTRIES", "PEMRSETPIXELV", "PEMRSETPOLYFILLMODE", "PEMRSETROP2", "PEMRSETSTRETCHBLTMODE", "PEMRSETTEXTALIGN",
            "PEMRSETTEXTCOLOR", "PEMRSETVIEWPORTEXTEX", "PEMRSETVIEWPORTORGEX", "PEMRSETWINDOWEXTEX", "PEMRSETWINDOWORGEX", "PEMRSETWORLDTRANSFORM",
            "PEMRSTRETCHBLT", "PEMRSTRETCHDIBITS", "PEMRSTROKEANDFILLPATH", "PEMRSTROKEPATH", "PEMRTEXT", "PEMRTRANSPARENTBLT",
            "PEMRWIDENPATH", "PENCLAVE_CREATE_INFO_SGX", "PENCLAVE_CREATE_INFO_VBS", "PENCLAVE_CREATE_INFO_VBS_BASIC", "PENCLAVE_INIT_INFO_SGX", "PENCLAVE_INIT_INFO_VBS",
            "PENCLAVE_INIT_INFO_VBS_BASIC", "PENCLAVE_LOAD_DATA_VBS_BASIC", "PENCLAVE_ROUTINE", "PENCLAVE_TARGET_FUNCTION", "PENCRYPTED_DATA_INFO", "PENCRYPTION_BUFFER",
            "PENCRYPTION_CERTIFICATE", "PENCRYPTION_CERTIFICATE_HASH", "PENCRYPTION_CERTIFICATE_HASH_LIST", "PENCRYPTION_CERTIFICATE_LIST", "PENDINGMSG", "PENDINGTYPE",
            "PENHMETAHEADER", "PENHMETARECORD", "PENLISTMENT_BASIC_INFORMATION", "PENLISTMENT_CRM_INFORMATION", "PENUMLOGFONTEXDV", "PENUMLOGFONTEXDVA",
            "PENUMLOGFONTEXDVW", "PENUMTEXTMETRIC", "PENUMTEXTMETRICA", "PENUMTEXTMETRICW", "PENUMUILANG", "PERFORMANCE_DATA",
            "PERF_BIN", "PERF_COUNTER_BLOCK", "PERF_COUNTER_DEFINITION", "PERF_DATA_BLOCK", "PERF_INSTANCE_DEFINITION", "PERF_OBJECT_TYPE",
            "PERSISTENT_RESERVE_COMMAND", "PEVENTLOGRECORD", "PEVENTMSG", "PEVENTMSGMSG", "PEVENTSFORLOGFILE", "PEV_EXTRA_CERT_CHAIN_POLICY_PARA",
            "PEV_EXTRA_CERT_CHAIN_POLICY_STATUS", "PEXCEPTION_HANDLER", "PEXCEPTION_POINTERS", "PEXCEPTION_RECORD", "PEXCEPTION_RECORD32", "PEXCEPTION_RECORD64",
            "PEXCEPTION_REGISTRATION", "PEXCEPTION_REGISTRATION_RECORD", "PEXCEPTION_ROUTINE", "PEXECUTION_STATE", "PEXFAT_STATISTICS", "PEXPAND_VIRTUAL_DISK_PARAMETERS",
            "PEXTENDED_ENCRYPTED_DATA_INFO", "PEXTLOGFONT", "PEXTLOGFONTA", "PEXTLOGFONTW", "PEXTLOGPEN", "PEXTLOGPEN32",
            "PFAT_STATISTICS", "PFD_SET", "PFE_EXPORT_FUNC", "PFE_IMPORT_FUNC", "PFIBER_CALLOUT_ROUTINE", "PFIBER_START_ROUTINE",
            "PFILEMUIINFO", "PFILESYSTEM_STATISTICS", "PFILETIME", "PFILE_ALLOCATED_RANGE_BUFFER", "PFILE_ALLOCATION_INFO", "PFILE_ATTRIBUTE_TAG_INFO",
            "PFILE_BASIC_INFO", "PFILE_CASE_SENSITIVE_INFO", "PFILE_CASE_SENSITIVE_INFORMATION", "PFILE_COMPRESSION_INFO", "PFILE_DISPOSITION_INFO", "PFILE_DISPOSITION_INFO_EX",
            "PFILE_END_OF_FILE_INFO", "PFILE_FS_PERSISTENT_VOLUME_INFORMATION", "PFILE_FULL_DIR_INFO", "PFILE_ID_128", "PFILE_ID_BOTH_DIR_INFO", "PFILE_ID_TYPE",
            "PFILE_INFO_BY_HANDLE_CLASS", "PFILE_IO_PRIORITY_HINT_INFO", "PFILE_MAKE_COMPATIBLE_BUFFER", "PFILE_NAME_INFO", "PFILE_NOTIFY_EXTENDED_INFORMATION", "PFILE_NOTIFY_FULL_INFORMATION",
            "PFILE_NOTIFY_INFORMATION", "PFILE_OBJECTID_BUFFER", "PFILE_PREFETCH", "PFILE_PREFETCH_EX", "PFILE_QUERY_ON_DISK_VOL_INFO_BUFFER", "PFILE_QUERY_SPARING_BUFFER",
            "PFILE_REMOTE_PROTOCOL_INFO", "PFILE_RENAME_INFO", "PFILE_SEGMENT_ELEMENT", "PFILE_SET_DEFECT_MGMT_BUFFER", "PFILE_SET_SPARSE_BUFFER", "PFILE_STANDARD_INFO",
            "PFILE_STAT_INFORMATION", "PFILE_STAT_LX_INFORMATION", "PFILE_STREAM_INFO", "PFILE_SYSTEM_RECOGNITION_INFORMATION", "PFILE_TYPE_NOTIFICATION_INPUT", "PFILE_ZERO_DATA_INFORMATION",
            "PFIND_BY_SID_DATA", "PFIND_BY_SID_OUTPUT", "PFIND_NAME_BUFFER", "PFIND_NAME_HEADER", "PFIRMWARE_TYPE", "PFLASHWINFO",
            "PFLOAT", "PFLOAT128", "PFLS_CALLBACK_FUNCTION", "PFNCALLBACK", "PFNCANSHAREFOLDERW", "PFNDACOMPARE",
            "PFNDACOMPARECONST", "PFNDAENUMCALLBACK", "PFNDAENUMCALLBACKCONST", "PFNDPAMERGE", "PFNDPAMERGECONST", "PFNDPASTREAM",
            "PFNGETPROFILEPATHA", "PFNGETPROFILEPATHW", "PFNLVCOMPARE", "PFNLVGROUPCOMPARE", "PFNPROCESSPOLICIESA", "PFNPROCESSPOLICIESW",
            "PFNPROPSHEETCALLBACK", "PFNRECONCILEPROFILEA", "PFNRECONCILEPROFILEW", "PFNSHOWSHAREFOLDERUIW", "PFNTVCOMPARE", "PFN_CANCEL_ASYNC_RETRIEVAL_FUNC",
            "PFN_CERT_CHAIN_FIND_BY_ISSUER_CALLBACK", "PFN_CERT_CREATE_CONTEXT_SORT_FUNC", "PFN_CERT_DLL_OPEN_STORE_PROV_FUNC", "PFN_CERT_ENUM_PHYSICAL_STORE", "PFN_CERT_ENUM_SYSTEM_STORE", "PFN_CERT_ENUM_SYSTEM_STORE_LOCATION",
            "PFN_CERT_SERVER_OCSP_RESPONSE_UPDATE_CALLBACK", "PFN_CERT_STORE_PROV_CLOSE", "PFN_CERT_STORE_PROV_CONTROL", "PFN_CERT_STORE_PROV_DELETE_CERT", "PFN_CERT_STORE_PROV_DELETE_CRL", "PFN_CERT_STORE_PROV_DELETE_CTL",
            "PFN_CERT_STORE_PROV_FIND_CERT", "PFN_CERT_STORE_PROV_FIND_CRL", "PFN_CERT_STORE_PROV_FIND_CTL", "PFN_CERT_STORE_PROV_FREE_FIND_CERT", "PFN_CERT_STORE_PROV_FREE_FIND_CRL", "PFN_CERT_STORE_PROV_FREE_FIND_CTL",
            "PFN_CERT_STORE_PROV_GET_CERT_PROPERTY", "PFN_CERT_STORE_PROV_GET_CRL_PROPERTY", "PFN_CERT_STORE_PROV_GET_CTL_PROPERTY", "PFN_CERT_STORE_PROV_READ_CERT", "PFN_CERT_STORE_PROV_READ_CRL", "PFN_CERT_STORE_PROV_READ_CTL",
            "PFN_CERT_STORE_PROV_SET_CERT_PROPERTY", "PFN_CERT_STORE_PROV_SET_CRL_PROPERTY", "PFN_CERT_STORE_PROV_SET_CTL_PROPERTY", "PFN_CERT_STORE_PROV_WRITE_CERT", "PFN_CERT_STORE_PROV_WRITE_CRL", "PFN_CERT_STORE_PROV_WRITE_CTL",
            "PFN_CMSG_ALLOC", "PFN_CMSG_CNG_IMPORT_CONTENT_ENCRYPT_KEY", "PFN_CMSG_CNG_IMPORT_KEY_AGREE", "PFN_CMSG_CNG_IMPORT_KEY_TRANS", "PFN_CMSG_EXPORT_ENCRYPT_KEY", "PFN_CMSG_EXPORT_KEY_AGREE",
            "PFN_CMSG_EXPORT_KEY_TRANS", "PFN_CMSG_EXPORT_MAIL_LIST", "PFN_CMSG_FREE", "PFN_CMSG_GEN_CONTENT_ENCRYPT_KEY", "PFN_CMSG_GEN_ENCRYPT_KEY", "PFN_CMSG_IMPORT_ENCRYPT_KEY",
            "PFN_CMSG_IMPORT_KEY_AGREE", "PFN_CMSG_IMPORT_KEY_TRANS", "PFN_CMSG_IMPORT_MAIL_LIST", "PFN_CMSG_STREAM_OUTPUT", "PFN_CRYPT_ALLOC", "PFN_CRYPT_ASYNC_PARAM_FREE_FUNC",
            "PFN_CRYPT_ASYNC_RETRIEVAL_COMPLETION_FUNC", "PFN_CRYPT_CANCEL_RETRIEVAL", "PFN_CRYPT_ENUM_KEYID_PROP", "PFN_CRYPT_ENUM_OID_FUNC", "PFN_CRYPT_ENUM_OID_INFO", "PFN_CRYPT_EXPORT_PUBLIC_KEY_INFO_EX2_FUNC",
            "PFN_CRYPT_EXPORT_PUBLIC_KEY_INFO_FROM_BCRYPT_HANDLE_FUNC", "PFN_CRYPT_EXTRACT_ENCODED_SIGNATURE_PARAMETERS_FUNC", "PFN_CRYPT_FREE", "PFN_CRYPT_GET_SIGNER_CERTIFICATE", "PFN_CRYPT_SIGN_AND_ENCODE_HASH_FUNC", "PFN_CRYPT_VERIFY_ENCODED_SIGNATURE_FUNC",
            "PFN_EXPORT_PRIV_KEY_FUNC", "PFN_FREE_ENCODED_OBJECT_FUNC", "PFN_IMPORT_PRIV_KEY_FUNC", "PFN_IMPORT_PUBLIC_KEY_INFO_EX2_FUNC", "PFN_NCRYPT_ALLOC", "PFN_NCRYPT_FREE",
            "PFN_RPCNOTIFICATION_ROUTINE", "PFN_SC_NOTIFY_CALLBACK", "PFOCUS_EVENT_RECORD", "PFONTSIGNATURE", "PFORMAT_EX_PARAMETERS", "PFORMAT_PARAMETERS",
            "PFORMAT_STRING", "PFORM_INFO_1", "PFORM_INFO_1A", "PFORM_INFO_1W", "PFORM_INFO_2", "PFORM_INFO_2A",
            "PFORM_INFO_2W", "PFPO_DATA", "PFSCTL_QUERY_FAT_BPB_BUFFER", "PFTASKDIALOGCALLBACK", "PFULL_PTR_TO_REFID_ELEMENT", "PFULL_PTR_XLAT_TABLES",
            "PGENERIC_BINDING_INFO", "PGENERIC_BINDING_ROUTINE_PAIR", "PGENERIC_MAPPING", "PGESTURECONFIG", "PGESTUREINFO", "PGESTURENOTIFYSTRUCT",
            "PGETVERSIONINPARAMS", "PGET_CHANGER_PARAMETERS", "PGET_LENGTH_INFORMATION", "PGET_MEDIA_TYPES", "PGET_MODULE_HANDLE_EXA", "PGET_MODULE_HANDLE_EXW",
            "PGET_RUNTIME_FUNCTION_CALLBACK", "PGET_SYSTEM_WOW64_DIRECTORY_A", "PGET_SYSTEM_WOW64_DIRECTORY_W", "PGET_VIRTUAL_DISK_INFO", "PGLYPHMETRICSFLOAT", "PGLYPHSET",
            "PGRADIENT_RECT", "PGRADIENT_TRIANGLE", "PGROUP_AFFINITY", "PGROUP_AFFINITY32", "PGROUP_AFFINITY64", "PGROUP_RELATIONSHIP",
            "PGUITHREADINFO", "PHALF_PTR", "PHANDLE", "PHANDLER_ROUTINE", "PHANDLETABLE", "PHARDWAREHOOKSTRUCT",
            "PHARDWAREINPUT", "PHARDWARE_COUNTER_DATA", "PHARDWARE_COUNTER_TYPE", "PHCRYPTASYNC", "PHDEVNOTIFY", "PHEAP_SUMMARY",
            "PHELPWININFO", "PHELPWININFOA", "PHELPWININFOW", "PHISTOGRAM_BUCKET", "PHKEY", "PHMAC_INFO",
            "PHOSTENT", "PHPOWERNOTIFY", "PHSZPAIR", "PHTTPSPolicyCallbackData", "PICONINFO", "PICONINFOEX",
            "PICONINFOEXA", "PICONINFOEXW", "PICONMETRICS", "PICONMETRICSA", "PICONMETRICSW", "PIDEREGS",
            "PIMAGE_ALPHA64_RUNTIME_FUNCTION_ENTRY", "PIMAGE_ALPHA_RUNTIME_FUNCTION_ENTRY", "PIMAGE_ARCHITECTURE_ENTRY", "PIMAGE_ARCHITECTURE_HEADER", "PIMAGE_ARCHIVE_MEMBER_HEADER", "PIMAGE_ARM64_RUNTIME_FUNCTION_ENTRY",
            "PIMAGE_ARM_RUNTIME_FUNCTION_ENTRY", "PIMAGE_AUX_SYMBOL", "PIMAGE_AUX_SYMBOL_EX", "PIMAGE_AUX_SYMBOL_TOKEN_DEF", "PIMAGE_BASE_RELOCATION", "PIMAGE_BOUND_FORWARDER_REF",
            "PIMAGE_BOUND_IMPORT_DESCRIPTOR", "PIMAGE_CE_RUNTIME_FUNCTION_ENTRY", "PIMAGE_COFF_SYMBOLS_HEADER", "PIMAGE_COR20_HEADER", "PIMAGE_DATA_DIRECTORY", "PIMAGE_DEBUG_DIRECTORY",
            "PIMAGE_DEBUG_MISC", "PIMAGE_DELAYLOAD_DESCRIPTOR", "PIMAGE_DOS_HEADER", "PIMAGE_EXPORT_DIRECTORY", "PIMAGE_FILE_HEADER", "PIMAGE_FUNCTION_ENTRY",
            "PIMAGE_FUNCTION_ENTRY64", "PIMAGE_IA64_RUNTIME_FUNCTION_ENTRY", "PIMAGE_IMPORT_BY_NAME", "PIMAGE_IMPORT_DESCRIPTOR", "PIMAGE_LINENUMBER", "PIMAGE_LOAD_CONFIG_CODE_INTEGRITY",
            "PIMAGE_LOAD_CONFIG_DIRECTORY", "PIMAGE_LOAD_CONFIG_DIRECTORY32", "PIMAGE_LOAD_CONFIG_DIRECTORY64", "PIMAGE_NT_HEADERS", "PIMAGE_NT_HEADERS32", "PIMAGE_NT_HEADERS64",
            "PIMAGE_OPTIONAL_HEADER", "PIMAGE_OPTIONAL_HEADER32", "PIMAGE_OPTIONAL_HEADER64", "PIMAGE_OS2_HEADER", "PIMAGE_RELOCATION", "PIMAGE_RESOURCE_DATA_ENTRY",
            "PIMAGE_RESOURCE_DIRECTORY", "PIMAGE_RESOURCE_DIRECTORY_ENTRY", "PIMAGE_RESOURCE_DIRECTORY_STRING", "PIMAGE_RESOURCE_DIR_STRING_U", "PIMAGE_ROM_HEADERS", "PIMAGE_ROM_OPTIONAL_HEADER",
            "PIMAGE_RUNTIME_FUNCTION_ENTRY", "PIMAGE_SECTION_HEADER", "PIMAGE_SEPARATE_DEBUG_HEADER", "PIMAGE_SYMBOL", "PIMAGE_SYMBOL_EX", "PIMAGE_THUNK_DATA",
            "PIMAGE_THUNK_DATA32", "PIMAGE_THUNK_DATA64", "PIMAGE_TLS_CALLBACK", "PIMAGE_TLS_DIRECTORY", "PIMAGE_TLS_DIRECTORY32", "PIMAGE_TLS_DIRECTORY64",
            "PIMAGE_VXD_HEADER", "PIMECHARPOSITION", "PIMEMENUITEMINFO", "PIMEMENUITEMINFOA", "PIMEMENUITEMINFOW", "PINIT_ONCE",
            "PINIT_ONCE_FN", "PINPUT", "PINPUT_RECORD", "PINT", "PINT16", "PINT32",
            "PINT64", "PINT8", "PINT_PTR", "PIN_ADDR", "PIO_COUNTERS", "PIP_MREQ",
            "PISECURITY_DESCRIPTOR", "PISECURITY_DESCRIPTOR_RELATIVE", "PISID", "PIXELFORMATDESCRIPTOR", "PI_FLAGS", "PJOBOBJECT_ASSOCIATE_COMPLETION_PORT",
            "PJOBOBJECT_BASIC_ACCOUNTING_INFORMATION", "PJOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION", "PJOBOBJECT_BASIC_LIMIT_INFORMATION", "PJOBOBJECT_BASIC_PROCESS_ID_LIST", "PJOBOBJECT_BASIC_UI_RESTRICTIONS", "PJOBOBJECT_CPU_RATE_CONTROL_INFORMATION",
            "PJOBOBJECT_END_OF_JOB_TIME_INFORMATION", "PJOBOBJECT_EXTENDED_LIMIT_INFORMATION", "PJOBOBJECT_IO_ATTRIBUTION_INFORMATION", "PJOBOBJECT_IO_ATTRIBUTION_STATS", "PJOBOBJECT_JOBSET_INFORMATION", "PJOBOBJECT_LIMIT_VIOLATION_INFORMATION",
            "PJOBOBJECT_NOTIFICATION_LIMIT_INFORMATION", "PJOBOBJECT_SECURITY_LIMIT_INFORMATION", "PJOB_INFO_1", "PJOB_INFO_1A", "PJOB_INFO_1W", "PJOB_INFO_2",
            "PJOB_INFO_2A", "PJOB_INFO_2W", "PJOB_INFO_3", "PJOB_SET_ARRAY", "PJOYCAPS", "PJOYCAPS2",
            "PJOYCAPS2A", "PJOYCAPS2W", "PJOYCAPSA", "PJOYCAPSW", "PJOYINFO", "PJOYINFOEX",
            "PKAFFINITY", "PKBDLLHOOKSTRUCT", "PKCRM_MARSHAL_HEADER", "PKCRM_PROTOCOL_BLOB", "PKCRM_TRANSACTION_BLOB", "PKCS12_PBES2_EXPORT_PARAMS",
            "PKERNEL_CET_CONTEXT", "PKEYBDINPUT", "PKEY_EVENT_RECORD", "PKEY_TYPE_SUBTYPE", "PKNONVOLATILE_CONTEXT_POINTERS", "PKNONVOLATILE_CONTEXT_POINTERS_ARM64",
            "PKSPIN_LOCK", "PKTMOBJECT_CURSOR", "PKTMOBJECT_TYPE", "PLANA_ENUM", "PLARGE_INTEGER", "PLASTINPUTINFO",
            "PLAYERPLANEDESCRIPTOR", "PLCID", "PLDT_ENTRY", "PLEX_READ_DATA_REQUEST", "PLHITTESTINFO", "PLINGER",
            "PLINKSRCDESCRIPTOR", "PLIST_ENTRY", "PLIST_ENTRY32", "PLIST_ENTRY64", "PLITEM", "PLOCALESIGNATURE",
            "PLOGBRUSH", "PLOGBRUSH32", "PLOGFONT", "PLOGFONTA", "PLOGFONTW", "PLOGPALETTE",
            "PLOGPEN", "PLONG", "PLONG32", "PLONG64", "PLONGLONG", "PLONG_PTR",
            "PLOOKUP_STREAM_FROM_CLUSTER_ENTRY", "PLOOKUP_STREAM_FROM_CLUSTER_INPUT", "PLOOKUP_STREAM_FROM_CLUSTER_OUTPUT", "PLUID", "PLUID_AND_ATTRIBUTES", "PLUID_AND_ATTRIBUTES_ARRAY",
            "PLVGROUP", "PLVGROUPMETRICS", "PLVINSERTGROUPSORTED", "PLVITEMINDEX", "PLVSETINFOTIP", "PLVTILEINFO",
            "PLVTILEVIEWINFO", "PM128A", "PMANDATORY_LEVEL", "PMARK_HANDLE_INFO", "PMARK_HANDLE_INFO32", "PMCGRIDINFO",
            "PMCHITTESTINFO", "PMCI_ANIM_OPEN_PARMS", "PMCI_ANIM_OPEN_PARMSA", "PMCI_ANIM_OPEN_PARMSW", "PMCI_ANIM_PLAY_PARMS", "PMCI_ANIM_RECT_PARMS",
            "PMCI_ANIM_STEP_PARMS", "PMCI_ANIM_UPDATE_PARMS", "PMCI_ANIM_WINDOW_PARMS", "PMCI_ANIM_WINDOW_PARMSA", "PMCI_ANIM_WINDOW_PARMSW", "PMCI_BREAK_PARMS",
            "PMCI_GENERIC_PARMS", "PMCI_GETDEVCAPS_PARMS", "PMCI_LOAD_PARMS", "PMCI_LOAD_PARMSA", "PMCI_LOAD_PARMSW", "PMCI_OPEN_PARMS",
            "PMCI_OPEN_PARMSA", "PMCI_OPEN_PARMSW", "PMCI_OVLY_LOAD_PARMS", "PMCI_OVLY_LOAD_PARMSA", "PMCI_OVLY_LOAD_PARMSW", "PMCI_OVLY_OPEN_PARMS",
            "PMCI_OVLY_OPEN_PARMSA", "PMCI_OVLY_OPEN_PARMSW", "PMCI_OVLY_RECT_PARMS", "PMCI_OVLY_SAVE_PARMS", "PMCI_OVLY_SAVE_PARMSA", "PMCI_OVLY_SAVE_PARMSW",
            "PMCI_OVLY_WINDOW_PARMS", "PMCI_OVLY_WINDOW_PARMSA", "PMCI_OVLY_WINDOW_PARMSW", "PMCI_PLAY_PARMS", "PMCI_SAVE_PARMS", "PMCI_SAVE_PARMSA",
            "PMCI_SAVE_PARMSW", "PMCI_SEEK_PARMS", "PMCI_SEQ_SET_PARMS", "PMCI_SET_PARMS", "PMCI_STATUS_PARMS", "PMCI_SYSINFO_PARMS",
            "PMCI_SYSINFO_PARMSA", "PMCI_SYSINFO_PARMSW", "PMCI_VD_ESCAPE_PARMS", "PMCI_VD_ESCAPE_PARMSA", "PMCI_VD_ESCAPE_PARMSW", "PMCI_VD_PLAY_PARMS",
            "PMCI_VD_STEP_PARMS", "PMCI_WAVE_DELETE_PARMS", "PMCI_WAVE_OPEN_PARMS", "PMCI_WAVE_OPEN_PARMSA", "PMCI_WAVE_OPEN_PARMSW", "PMCI_WAVE_SET_PARMS",
            "PMDINEXTMENU", "PMEASUREITEMSTRUCT", "PMEDIA_TYPE", "PMEMORY_BASIC_INFORMATION", "PMEMORY_BASIC_INFORMATION32", "PMEMORY_BASIC_INFORMATION64",
            "PMEMORY_PARTITION_DEDICATED_MEMORY_ATTRIBUTE", "PMEMORY_PARTITION_DEDICATED_MEMORY_INFORMATION", "PMEM_ADDRESS_REQUIREMENTS", "PMEM_DEDICATED_ATTRIBUTE_TYPE", "PMEM_EXTENDED_PARAMETER", "PMEM_EXTENDED_PARAMETER_TYPE",
            "PMEM_SECTION_EXTENDED_PARAMETER_TYPE", "PMENUBARINFO", "PMENUGETOBJECTINFO", "PMENUITEMTEMPLATE", "PMENUITEMTEMPLATEHEADER", "PMENU_EVENT_RECORD",
            "PMERGE_VIRTUAL_DISK_PARAMETERS", "PMESSAGE_RESOURCE_BLOCK", "PMESSAGE_RESOURCE_DATA", "PMESSAGE_RESOURCE_ENTRY", "PMETAHEADER", "PMETARECORD",
            "PMFT_ENUM_DATA", "PMIDIHDR", "PMIDIINCAPS", "PMIDIINCAPS2", "PMIDIINCAPS2A", "PMIDIINCAPS2W",
            "PMIDIINCAPSA", "PMIDIINCAPSW", "PMIDIOUTCAPS", "PMIDIOUTCAPS2", "PMIDIOUTCAPS2A", "PMIDIOUTCAPS2W",
            "PMIDIOUTCAPSA", "PMIDIOUTCAPSW", "PMIDL_INTERCEPTION_INFO", "PMIDL_METHOD_PROPERTY", "PMIDL_METHOD_PROPERTY_MAP", "PMIDL_SERVER_INFO",
            "PMIDL_STUBLESS_PROXY_INFO", "PMIDL_STUB_DESC", "PMIDL_STUB_MESSAGE", "PMIDL_SYNTAX_INFO", "PMIDL_WINRT_TYPE_SERIALIZATION_INFO", "PMIDL_XMIT_TYPE",
            "PMINIMIZEDMETRICS", "PMINMAXINFO", "PMIXERCAPS", "PMIXERCAPS2", "PMIXERCAPS2A", "PMIXERCAPS2W",
            "PMIXERCAPSA", "PMIXERCAPSW", "PMIXERCONTROL", "PMIXERCONTROLA", "PMIXERCONTROLDETAILS", "PMIXERCONTROLDETAILS_BOOLEAN",
            "PMIXERCONTROLDETAILS_LISTTEXT", "PMIXERCONTROLDETAILS_LISTTEXTA", "PMIXERCONTROLDETAILS_LISTTEXTW", "PMIXERCONTROLDETAILS_SIGNED", "PMIXERCONTROLDETAILS_UNSIGNED", "PMIXERCONTROLW",
            "PMIXERLINE", "PMIXERLINEA", "PMIXERLINECONTROLS", "PMIXERLINECONTROLSA", "PMIXERLINECONTROLSW", "PMIXERLINEW",
            "PMMCKINFO", "PMMIOINFO", "PMMTIME", "PMODEMDEVCAPS", "PMODEMSETTINGS", "PMONCBSTRUCT",
            "PMONCONVSTRUCT", "PMONERRSTRUCT", "PMONHSZSTRUCT", "PMONHSZSTRUCTA", "PMONHSZSTRUCTW", "PMONITOR_DISPLAY_STATE",
            "PMONITOR_INFO_1", "PMONITOR_INFO_1A", "PMONITOR_INFO_1W", "PMONITOR_INFO_2", "PMONITOR_INFO_2A", "PMONITOR_INFO_2W",
            "PMONLINKSTRUCT", "PMONMSGSTRUCT", "PMOUSEHOOKSTRUCT", "PMOUSEHOOKSTRUCTEX", "PMOUSEINPUT", "PMOUSEMOVEPOINT",
            "PMOUSE_EVENT_RECORD", "PMOVE_FILE_DATA", "PMOVE_FILE_DATA32", "PMOVE_FILE_RECORD_DATA", "PMSG", "PMSGBOXPARAMS",
            "PMSGBOXPARAMSA", "PMSGBOXPARAMSW", "PMSLLHOOKSTRUCT", "PMULTIKEYHELP", "PMULTIKEYHELPA", "PMULTIKEYHELPW",
            "PM_CLOSE_PROC", "PM_COLLECT_PROC", "PM_OPEN_PROC", "PM_QUERY_PROC", "PNAME_BUFFER", "PNCB",
            "PNC_ADDRESS", "PNCryptBuffer", "PNCryptBufferDesc", "PNDR_ASYNC_MESSAGE", "PNDR_CORRELATION_INFO", "PNEWTEXTMETRIC",
            "PNEWTEXTMETRICA", "PNEWTEXTMETRICW", "PNMCBEDRAGBEGINA", "PNMCBEDRAGBEGINW", "PNMCBEENDEDITA", "PNMCBEENDEDITW",
            "PNMCOMBOBOXEXA", "PNMCOMBOBOXEXW", "PNMLINK", "PNMLVLINK", "PNONCLIENTMETRICS", "PNONCLIENTMETRICSA",
            "PNONCLIENTMETRICSW", "PNON_PAGED_DEBUG_INFO", "PNOTIFYICONDATA", "PNOTIFYICONDATAA", "PNOTIFYICONDATAW", "PNOTIFYICONIDENTIFIER",
            "PNOTIFY_USER_POWER_SETTING", "PNTFS_EXTENDED_VOLUME_DATA", "PNTFS_FILE_RECORD_INPUT_BUFFER", "PNTFS_FILE_RECORD_OUTPUT_BUFFER", "PNTFS_STATISTICS", "PNTFS_VOLUME_DATA_BUFFER",
            "PNTSTATUS", "PNT_TIB", "PNT_TIB32", "PNT_TIB64", "PNUMA_NODE_RELATIONSHIP", "PNZCH",
            "PNZTCH", "PNZWCH", "POBJECTDESCRIPTOR", "POBJECT_TYPE_LIST", "POCSP_BASIC_RESPONSE_ENTRY", "POCSP_BASIC_RESPONSE_INFO",
            "POCSP_BASIC_REVOKED_INFO", "POCSP_BASIC_SIGNED_RESPONSE_INFO", "POCSP_CERT_ID", "POCSP_REQUEST_ENTRY", "POCSP_REQUEST_INFO", "POCSP_RESPONSE_INFO",
            "POCSP_SIGNATURE_INFO", "POCSP_SIGNED_REQUEST_INFO", "POFSTRUCT", "POINT", "POINTER_64_INT", "POINTFLOAT",
            "POINTFX", "POINTL", "POINTS", "POLEUPDATE", "POLYTEXT", "POLYTEXTA",
            "POLYTEXTW", "POPENCARDNAME", "POPENCARDNAMEA", "POPENCARDNAMEW", "POPENCARDNAME_EX", "POPENCARDNAME_EXA",
            "POPENCARDNAME_EXW", "POPENCARD_SEARCH_CRITERIA", "POPENCARD_SEARCH_CRITERIAA", "POPENCARD_SEARCH_CRITERIAW", "POPEN_PRINTER_PROPS_INFO", "POPEN_PRINTER_PROPS_INFOA",
            "POPEN_PRINTER_PROPS_INFOW", "POPEN_VIRTUAL_DISK_PARAMETERS", "PORT_INFO_1", "PORT_INFO_1A", "PORT_INFO_1W", "PORT_INFO_2",
            "PORT_INFO_2A", "PORT_INFO_2W", "PORT_INFO_3", "PORT_INFO_3A", "PORT_INFO_3W", "POSVERSIONINFO",
            "POSVERSIONINFOA", "POSVERSIONINFOEX", "POSVERSIONINFOEXA", "POSVERSIONINFOEXW", "POSVERSIONINFOW", "POUTLINETEXTMETRIC",
            "POUTLINETEXTMETRICA", "POUTLINETEXTMETRICW", "POUT_OF_PROCESS_FUNCTION_TABLE_CALLBACK", "POWERBROADCAST_SETTING", "POWER_ACTION", "POWER_ACTION_POLICY",
            "POWER_IDLE_RESILIENCY", "POWER_INFORMATION_LEVEL", "POWER_MONITOR_INVOCATION", "POWER_MONITOR_REQUEST_REASON", "POWER_PLATFORM_INFORMATION", "POWER_PLATFORM_ROLE",
            "POWER_REQUEST_CONTEXT", "POWER_REQUEST_TYPE", "POWER_SESSION_CONNECT", "POWER_SESSION_RIT_STATE", "POWER_SESSION_TIMEOUTS", "POWER_SESSION_WINLOGON",
            "POWER_USER_PRESENCE", "POWER_USER_PRESENCE_TYPE", "PPACKEDEVENTINFO", "PPAINTSTRUCT", "PPALETTEENTRY", "PPARAM_OFFSETTABLE",
            "PPARTITION_INFORMATION", "PPARTITION_INFORMATION_EX", "PPARTITION_INFORMATION_GPT", "PPARTITION_INFORMATION_MBR", "PPATHNAME_BUFFER", "PPATTERN",
            "PPBRANGE", "PPCMWAVEFORMAT", "PPELARRAY", "PPERFORMANCE_DATA", "PPERF_BIN", "PPERF_COUNTER_BLOCK",
            "PPERF_COUNTER_DEFINITION", "PPERF_DATA_BLOCK", "PPERF_INSTANCE_DEFINITION", "PPERF_OBJECT_TYPE", "PPERSISTENT_RESERVE_COMMAND", "PPIXELFORMATDESCRIPTOR",
            "PPKCS12_PBES2_EXPORT_PARAMS", "PPLEX_READ_DATA_REQUEST", "PPM_IDLESTATE_EVENT", "PPM_IDLE_ACCOUNTING", "PPM_IDLE_ACCOUNTING_EX", "PPM_IDLE_STATE_ACCOUNTING",
            "PPM_IDLE_STATE_ACCOUNTING_EX", "PPM_IDLE_STATE_BUCKET_EX", "PPM_PERFSTATE_DOMAIN_EVENT", "PPM_PERFSTATE_EVENT", "PPM_THERMALCHANGE_EVENT", "PPM_THERMAL_POLICY_EVENT",
            "PPM_WMI_IDLE_STATE", "PPM_WMI_IDLE_STATES", "PPM_WMI_IDLE_STATES_EX", "PPM_WMI_LEGACY_PERFSTATE", "PPM_WMI_PERF_STATE", "PPM_WMI_PERF_STATES",
            "PPM_WMI_PERF_STATES_EX", "PPOINT", "PPOINTFLOAT", "PPOINTL", "PPOINTS", "PPOLYTEXT",
            "PPOLYTEXTA", "PPOLYTEXTW", "PPORT_INFO_1", "PPORT_INFO_1A", "PPORT_INFO_1W", "PPORT_INFO_2",
            "PPORT_INFO_2A", "PPORT_INFO_2W", "PPORT_INFO_3", "PPORT_INFO_3A", "PPORT_INFO_3W", "PPOWERBROADCAST_SETTING",
            "PPOWER_ACTION", "PPOWER_ACTION_POLICY", "PPOWER_IDLE_RESILIENCY", "PPOWER_MONITOR_INVOCATION", "PPOWER_PLATFORM_INFORMATION", "PPOWER_PLATFORM_ROLE",
            "PPOWER_REQUEST_CONTEXT", "PPOWER_REQUEST_TYPE", "PPOWER_SESSION_CONNECT", "PPOWER_SESSION_RIT_STATE", "PPOWER_SESSION_TIMEOUTS", "PPOWER_SESSION_WINLOGON",
            "PPOWER_USER_PRESENCE", "PPOWER_USER_PRESENCE_TYPE", "PPPM_IDLESTATE_EVENT", "PPPM_IDLE_ACCOUNTING", "PPPM_IDLE_ACCOUNTING_EX", "PPPM_IDLE_STATE_ACCOUNTING",
            "PPPM_IDLE_STATE_ACCOUNTING_EX", "PPPM_IDLE_STATE_BUCKET_EX", "PPPM_PERFSTATE_DOMAIN_EVENT", "PPPM_PERFSTATE_EVENT", "PPPM_THERMALCHANGE_EVENT", "PPPM_THERMAL_POLICY_EVENT",
            "PPPM_WMI_IDLE_STATE", "PPPM_WMI_IDLE_STATES", "PPPM_WMI_IDLE_STATES_EX", "PPPM_WMI_LEGACY_PERFSTATE", "PPPM_WMI_PERF_STATE", "PPPM_WMI_PERF_STATES",
            "PPPM_WMI_PERF_STATES_EX", "PPREVENT_MEDIA_REMOVAL", "PPRINTER_CONNECTION_INFO_1", "PPRINTER_DEFAULTS", "PPRINTER_DEFAULTSA", "PPRINTER_DEFAULTSW",
            "PPRINTER_ENUM_VALUES", "PPRINTER_ENUM_VALUESA", "PPRINTER_ENUM_VALUESW", "PPRINTER_INFO_1", "PPRINTER_INFO_1A", "PPRINTER_INFO_1W",
            "PPRINTER_INFO_2", "PPRINTER_INFO_2A", "PPRINTER_INFO_2W", "PPRINTER_INFO_3", "PPRINTER_INFO_4", "PPRINTER_INFO_4A",
            "PPRINTER_INFO_4W", "PPRINTER_INFO_5", "PPRINTER_INFO_5A", "PPRINTER_INFO_5W", "PPRINTER_INFO_6", "PPRINTER_INFO_7",
            "PPRINTER_INFO_7A", "PPRINTER_INFO_7W", "PPRINTER_INFO_8", "PPRINTER_INFO_8A", "PPRINTER_INFO_8W", "PPRINTER_INFO_9",
            "PPRINTER_INFO_9A", "PPRINTER_INFO_9W", "PPRINTER_NOTIFY_INFO", "PPRINTER_NOTIFY_INFO_DATA", "PPRINTER_NOTIFY_OPTIONS", "PPRINTER_NOTIFY_OPTIONS_TYPE",
            "PPRINTER_OPTIONS", "PPRINTPROCESSOR_CAPS_1", "PPRINTPROCESSOR_CAPS_2", "PPRINTPROCESSOR_INFO_1", "PPRINTPROCESSOR_INFO_1A", "PPRINTPROCESSOR_INFO_1W",
            "PPRIVILEGE_SET", "PPROCESSOR_GROUP_INFO", "PPROCESSOR_IDLESTATE_INFO", "PPROCESSOR_IDLESTATE_POLICY", "PPROCESSOR_NUMBER", "PPROCESSOR_PERFSTATE_POLICY",
            "PPROCESSOR_POWER_POLICY", "PPROCESSOR_POWER_POLICY_INFO", "PPROCESSOR_RELATIONSHIP", "PPROCESS_DYNAMIC_EH_CONTINUATION_TARGET", "PPROCESS_DYNAMIC_EH_CONTINUATION_TARGETS_INFORMATION", "PPROCESS_DYNAMIC_ENFORCED_ADDRESS_RANGE",
            "PPROCESS_DYNAMIC_ENFORCED_ADDRESS_RANGES_INFORMATION", "PPROCESS_HEAP_ENTRY", "PPROCESS_INFORMATION", "PPROCESS_LEAP_SECOND_INFO", "PPROCESS_MEMORY_EXHAUSTION_INFO", "PPROCESS_MEMORY_EXHAUSTION_TYPE",
            "PPROCESS_MITIGATION_ASLR_POLICY", "PPROCESS_MITIGATION_BINARY_SIGNATURE_POLICY", "PPROCESS_MITIGATION_CHILD_PROCESS_POLICY", "PPROCESS_MITIGATION_CONTROL_FLOW_GUARD_POLICY", "PPROCESS_MITIGATION_DEP_POLICY", "PPROCESS_MITIGATION_DYNAMIC_CODE_POLICY",
            "PPROCESS_MITIGATION_EXTENSION_POINT_DISABLE_POLICY", "PPROCESS_MITIGATION_FONT_DISABLE_POLICY", "PPROCESS_MITIGATION_IMAGE_LOAD_POLICY", "PPROCESS_MITIGATION_PAYLOAD_RESTRICTION_POLICY", "PPROCESS_MITIGATION_POLICY", "PPROCESS_MITIGATION_REDIRECTION_TRUST_POLICY",
            "PPROCESS_MITIGATION_SEHOP_POLICY", "PPROCESS_MITIGATION_SIDE_CHANNEL_ISOLATION_POLICY", "PPROCESS_MITIGATION_STRICT_HANDLE_CHECK_POLICY", "PPROCESS_MITIGATION_SYSTEM_CALL_DISABLE_POLICY", "PPROCESS_MITIGATION_SYSTEM_CALL_FILTER_POLICY", "PPROCESS_MITIGATION_USER_POINTER_AUTH_POLICY",
            "PPROCESS_MITIGATION_USER_SHADOW_STACK_POLICY", "PPROCESS_POWER_THROTTLING_STATE", "PPROC_THREAD_ATTRIBUTE_LIST", "PPROTOENT", "PPROVIDER", "PPROVIDOR_INFO_1",
            "PPROVIDOR_INFO_1A", "PPROVIDOR_INFO_1W", "PPROVIDOR_INFO_2", "PPROVIDOR_INFO_2A", "PPROVIDOR_INFO_2W", "PPSFEATURE_CUSTPAPER",
            "PPSFEATURE_OUTPUT", "PPSINJECTDATA", "PPVALUE", "PPVALUEA", "PPVALUEW", "PQUERYACTCTXW_FUNC",
            "PQUERYHANDLER", "PQUOTA_LIMITS", "PQUOTA_LIMITS_EX", "PRATE_QUOTA_LIMIT", "PRAWHID", "PRAWINPUT",
            "PRAWINPUTDEVICE", "PRAWINPUTDEVICELIST", "PRAWINPUTHEADER", "PRAWKEYBOARD", "PRAWMOUSE", "PREAD_ELEMENT_ADDRESS_INFO",
            "PREAD_USN_JOURNAL_DATA", "PREASON_CONTEXT", "PREASSIGN_BLOCKS", "PREASSIGN_BLOCKS_EX", "PRECONVERTSTRING", "PRECT",
            "PRECTL", "PREDIRECTION_DESCRIPTOR", "PREDIRECTION_FUNCTION_DESCRIPTOR", "PREGISTERCLASSNAMEW", "PREGISTERWORD", "PREGISTERWORDA",
            "PREGISTERWORDW", "PREMSECURITY_ATTRIBUTES", "PREPARSE_GUID_DATA_BUFFER", "PREQUEST_OPLOCK_INPUT_BUFFER", "PREQUEST_OPLOCK_OUTPUT_BUFFER", "PREQUEST_RAW_ENCRYPTED_DATA",
            "PRESOURCEMANAGER_BASIC_INFORMATION", "PRESOURCEMANAGER_COMPLETION_INFORMATION", "PRESUME_PERFORMANCE", "PRETRIEVAL_POINTERS_BUFFER", "PRETRIEVAL_POINTER_BASE", "PREVENT_MEDIA_REMOVAL",
            "PRGBTRIPLE", "PRGNDATA", "PRGNDATAHEADER", "PRID_DEVICE_INFO", "PRID_DEVICE_INFO_HID", "PRID_DEVICE_INFO_KEYBOARD",
            "PRID_DEVICE_INFO_MOUSE", "PRINTDLG", "PRINTDLGA", "PRINTDLGEX", "PRINTDLGEXA", "PRINTDLGEXW",
            "PRINTDLGW", "PRINTEROP_FLAGS", "PRINTER_CONNECTION_INFO_1", "PRINTER_DEFAULTS", "PRINTER_DEFAULTSA", "PRINTER_DEFAULTSW",
            "PRINTER_ENUM_VALUES", "PRINTER_ENUM_VALUESA", "PRINTER_ENUM_VALUESW", "PRINTER_INFO_1", "PRINTER_INFO_1A", "PRINTER_INFO_1W",
            "PRINTER_INFO_2", "PRINTER_INFO_2A", "PRINTER_INFO_2W", "PRINTER_INFO_3", "PRINTER_INFO_4", "PRINTER_INFO_4A",
            "PRINTER_INFO_4W", "PRINTER_INFO_5", "PRINTER_INFO_5A", "PRINTER_INFO_5W", "PRINTER_INFO_6", "PRINTER_INFO_7",
            "PRINTER_INFO_7A", "PRINTER_INFO_7W", "PRINTER_INFO_8", "PRINTER_INFO_8A", "PRINTER_INFO_8W", "PRINTER_INFO_9",
            "PRINTER_INFO_9A", "PRINTER_INFO_9W", "PRINTER_NOTIFY_INFO", "PRINTER_NOTIFY_INFO_DATA", "PRINTER_NOTIFY_OPTIONS", "PRINTER_NOTIFY_OPTIONS_TYPE",
            "PRINTER_OPTIONS", "PRINTER_OPTION_FLAGS", "PRINTPAGERANGE", "PRINTPROCESSOR_CAPS_1", "PRINTPROCESSOR_CAPS_2", "PRINTPROCESSOR_INFO_1",
            "PRINTPROCESSOR_INFO_1A", "PRINTPROCESSOR_INFO_1W", "PRIORITY_HINT", "PRIVILEGE_SET", "PRKCRM_MARSHAL_HEADER", "PRKCRM_PROTOCOL_BLOB",
            "PRKCRM_TRANSACTION_BLOB", "PRLIST_ENTRY", "PROC", "PROCESSOR_CACHE_TYPE", "PROCESSOR_GROUP_INFO", "PROCESSOR_IDLESTATE_INFO",
            "PROCESSOR_IDLESTATE_POLICY", "PROCESSOR_NUMBER", "PROCESSOR_PERFSTATE_POLICY", "PROCESSOR_POWER_POLICY", "PROCESSOR_POWER_POLICY_INFO", "PROCESSOR_RELATIONSHIP",
            "PROCESS_DYNAMIC_EH_CONTINUATION_TARGET", "PROCESS_DYNAMIC_EH_CONTINUATION_TARGETS_INFORMATION", "PROCESS_DYNAMIC_ENFORCED_ADDRESS_RANGE", "PROCESS_DYNAMIC_ENFORCED_ADDRESS_RANGES_INFORMATION", "PROCESS_HEAP_ENTRY", "PROCESS_INFORMATION",
            "PROCESS_INFORMATION_CLASS", "PROCESS_LEAP_SECOND_INFO", "PROCESS_MACHINE_INFORMATION", "PROCESS_MEMORY_EXHAUSTION_INFO", "PROCESS_MEMORY_EXHAUSTION_TYPE", "PROCESS_MITIGATION_ASLR_POLICY",
            "PROCESS_MITIGATION_BINARY_SIGNATURE_POLICY", "PROCESS_MITIGATION_CHILD_PROCESS_POLICY", "PROCESS_MITIGATION_CONTROL_FLOW_GUARD_POLICY", "PROCESS_MITIGATION_DEP_POLICY", "PROCESS_MITIGATION_DYNAMIC_CODE_POLICY", "PROCESS_MITIGATION_EXTENSION_POINT_DISABLE_POLICY",
            "PROCESS_MITIGATION_FONT_DISABLE_POLICY", "PROCESS_MITIGATION_IMAGE_LOAD_POLICY", "PROCESS_MITIGATION_PAYLOAD_RESTRICTION_POLICY", "PROCESS_MITIGATION_POLICY", "PROCESS_MITIGATION_REDIRECTION_TRUST_POLICY", "PROCESS_MITIGATION_SEHOP_POLICY",
            "PROCESS_MITIGATION_SIDE_CHANNEL_ISOLATION_POLICY", "PROCESS_MITIGATION_STRICT_HANDLE_CHECK_POLICY", "PROCESS_MITIGATION_SYSTEM_CALL_DISABLE_POLICY", "PROCESS_MITIGATION_SYSTEM_CALL_FILTER_POLICY", "PROCESS_MITIGATION_USER_POINTER_AUTH_POLICY", "PROCESS_MITIGATION_USER_SHADOW_STACK_POLICY",
            "PROCESS_POWER_THROTTLING_STATE", "PROC_THREAD_ATTRIBUTE_NUM", "PROOT_INFO_LUID", "PROPENUMPROC", "PROPENUMPROCA", "PROPENUMPROCEX",
            "PROPENUMPROCEXA", "PROPENUMPROCEXW", "PROPENUMPROCW", "PROPERTYKEY", "PROPID", "PROPSHEETHEADERA",
            "PROPSHEETHEADERW", "PROPSHEETPAGEA", "PROPSHEETPAGEA_LATEST", "PROPSHEETPAGEA_V1", "PROPSHEETPAGEA_V2", "PROPSHEETPAGEA_V3",
            "PROPSHEETPAGEW", "PROPSHEETPAGEW_LATEST", "PROPSHEETPAGEW_V1", "PROPSHEETPAGEW_V2", "PROPSHEETPAGEW_V3", "PROPSHEETPAGE_RESOURCE",
            "PROPSPEC", "PROPVARIANT", "PROPVAR_PAD1", "PROPVAR_PAD2", "PROPVAR_PAD3", "PROTOCOLDATA",
            "PROTOCOLFILTERDATA", "PROTOCOL_ARGUMENT", "PROTOENT", "PROVIDOR_INFO_1", "PROVIDOR_INFO_1A", "PROVIDOR_INFO_1W",
            "PROVIDOR_INFO_2", "PROVIDOR_INFO_2A", "PROVIDOR_INFO_2W", "PROV_ENUMALGS", "PROV_ENUMALGS_EX", "PROXY_PHASE",
            "PRPCOLEMESSAGE", "PRPC_ASYNC_NOTIFICATION_INFO", "PRPC_ASYNC_STATE", "PRPC_CLIENT_INFORMATION1", "PRPC_CLIENT_INTERFACE", "PRPC_DISPATCH_TABLE",
            "PRPC_HTTP_TRANSPORT_CREDENTIALS_A", "PRPC_HTTP_TRANSPORT_CREDENTIALS_W", "PRPC_IMPORT_CONTEXT_P", "PRPC_MESSAGE", "PRPC_POLICY", "PRPC_PROTSEQ_ENDPOINT",
            "PRPC_RUNDOWN", "PRPC_SECURITY_QOS", "PRPC_SECURITY_QOS_V2_A", "PRPC_SECURITY_QOS_V2_W", "PRPC_SECURITY_QOS_V3_A", "PRPC_SECURITY_QOS_V3_W",
            "PRPC_SERVER_INTERFACE", "PRPC_SYNTAX_IDENTIFIER", "PRTL_BARRIER", "PRTL_CONDITION_VARIABLE", "PRTL_CRITICAL_SECTION", "PRTL_CRITICAL_SECTION_DEBUG",
            "PRTL_OSVERSIONINFOEXW", "PRTL_OSVERSIONINFOW", "PRTL_REFERENCE_COUNT", "PRTL_REFERENCE_COUNT32", "PRTL_RESOURCE_DEBUG", "PRTL_RUN_ONCE",
            "PRTL_RUN_ONCE_INIT_FN", "PRTL_SRWLOCK", "PRTL_UMS_SCHEDULER_ENTRY_POINT", "PRTL_UMS_SCHEDULER_REASON", "PRTL_UMS_THREAD_INFO_CLASS", "PRTL_VERIFIER_DLL_DESCRIPTOR",
            "PRTL_VERIFIER_PROVIDER_DESCRIPTOR", "PRTL_VERIFIER_THUNK_DESCRIPTOR", "PRUNTIME_FUNCTION", "PSAVEPOINT_ID", "PSCARDCONTEXT", "PSCARDHANDLE",
            "PSCARD_ATRMASK", "PSCARD_IO_REQUEST", "PSCARD_READERSTATE", "PSCARD_READERSTATEA", "PSCARD_READERSTATEW", "PSCARD_T0_REQUEST",
            "PSCARD_T1_REQUEST", "PSCHANNEL_ALG", "PSCODE", "PSCONTEXT_QUEUE", "PSCOPE_TABLE_AMD64", "PSCROLLBARINFO",
            "PSD_CHANGE_MACHINE_SID_INPUT", "PSD_CHANGE_MACHINE_SID_OUTPUT", "PSECURE_MEMORY_CACHE_CALLBACK", "PSECURITY_ATTRIBUTES", "PSECURITY_ATTRIBUTES_OPAQUE", "PSECURITY_CAPABILITIES",
            "PSECURITY_CONTEXT_TRACKING_MODE", "PSECURITY_DESCRIPTOR", "PSECURITY_DESCRIPTOR_CONTROL", "PSECURITY_IMPERSONATION_LEVEL", "PSECURITY_INFORMATION", "PSECURITY_OBJECT_AI_PARAMS",
            "PSECURITY_QUALITY_OF_SERVICE", "PSEC_WINNT_AUTH_IDENTITY_A", "PSEC_WINNT_AUTH_IDENTITY_W", "PSENDCMDINPARAMS", "PSENDCMDOUTPARAMS", "PSERVENT",
            "PSERVERSILO_BASIC_INFORMATION", "PSERVERSILO_STATE", "PSERVICE_CONTROL_STATUS_REASON_PARAMS", "PSERVICE_CONTROL_STATUS_REASON_PARAMSA", "PSERVICE_CONTROL_STATUS_REASON_PARAMSW", "PSERVICE_NOTIFY",
            "PSERVICE_NOTIFYA", "PSERVICE_NOTIFYW", "PSESSION_BUFFER", "PSESSION_HEADER", "PSET_PARTITION_INFORMATION", "PSET_PARTITION_INFORMATION_EX",
            "PSET_POWER_SETTING_VALUE", "PSET_VIRTUAL_DISK_INFO", "PSE_ACCESS_REPLY", "PSE_ACCESS_REQUEST", "PSE_IMAGE_SIGNATURE_TYPE", "PSE_IMPERSONATION_STATE",
            "PSE_SECURITY_DESCRIPTOR", "PSE_SID", "PSE_SIGNING_LEVEL", "PSE_TOKEN_USER", "PSFEATURE_CUSTPAPER", "PSFEATURE_OUTPUT",
            "PSHCREATEPROCESSINFOW", "PSHNOTIFY", "PSHORT", "PSHRINK_VOLUME_INFORMATION", "PSID", "PSID_AND_ATTRIBUTES",
            "PSID_AND_ATTRIBUTES_ARRAY", "PSID_AND_ATTRIBUTES_HASH", "PSID_HASH_ENTRY", "PSID_IDENTIFIER_AUTHORITY", "PSID_NAME_USE", "PSILOOBJECT_BASIC_INFORMATION",
            "PSINGLE_LIST_ENTRY", "PSINJECTDATA", "PSIZE", "PSIZEL", "PSIZE_T", "PSI_COPYFILE",
            "PSLIST_ENTRY", "PSLIST_HEADER", "PSMALL_RECT", "PSOCKADDR", "PSOCKADDR_IN", "PSOLE_AUTHENTICATION_INFO",
            "PSOLE_AUTHENTICATION_LIST", "PSOLE_AUTHENTICATION_SERVICE", "PSRWLOCK", "PSSIZE_T", "PSSL_EXTRA_CERT_CHAIN_POLICY_PARA", "PSSL_F12_EXTRA_CERT_CHAIN_POLICY_STATUS",
            "PSSL_HPKP_HEADER_EXTRA_CERT_CHAIN_POLICY_PARA", "PSSL_KEY_PIN_EXTRA_CERT_CHAIN_POLICY_PARA", "PSSL_KEY_PIN_EXTRA_CERT_CHAIN_POLICY_STATUS", "PSTARTING_LCN_INPUT_BUFFER", "PSTARTING_VCN_INPUT_BUFFER", "PSTORAGE_ACCESS_ALIGNMENT_DESCRIPTOR",
            "PSTORAGE_ADAPTER_DESCRIPTOR", "PSTORAGE_ALLOCATE_BC_STREAM_INPUT", "PSTORAGE_ALLOCATE_BC_STREAM_OUTPUT", "PSTORAGE_ASSOCIATION_TYPE", "PSTORAGE_BREAK_RESERVATION_REQUEST", "PSTORAGE_BUS_RESET_REQUEST",
            "PSTORAGE_BUS_TYPE", "PSTORAGE_CRYPTO_ALGORITHM_ID", "PSTORAGE_CRYPTO_CAPABILITY", "PSTORAGE_CRYPTO_DESCRIPTOR", "PSTORAGE_CRYPTO_KEY_SIZE", "PSTORAGE_DEPENDENCY_INFO",
            "PSTORAGE_DEPENDENCY_INFO_TYPE_1", "PSTORAGE_DEPENDENCY_INFO_TYPE_2", "PSTORAGE_DESCRIPTOR_HEADER", "PSTORAGE_DEVICE_DESCRIPTOR", "PSTORAGE_DEVICE_FAULT_DOMAIN_DESCRIPTOR", "PSTORAGE_DEVICE_ID_DESCRIPTOR",
            "PSTORAGE_DEVICE_NUMBER", "PSTORAGE_DEVICE_NUMBERS", "PSTORAGE_DEVICE_NUMBER_EX", "PSTORAGE_DEVICE_RESILIENCY_DESCRIPTOR", "PSTORAGE_DEVICE_TIERING_DESCRIPTOR", "PSTORAGE_FAILURE_PREDICTION_CONFIG",
            "PSTORAGE_GET_BC_PROPERTIES_OUTPUT", "PSTORAGE_HOTPLUG_INFO", "PSTORAGE_IDENTIFIER", "PSTORAGE_IDENTIFIER_CODE_SET", "PSTORAGE_IDENTIFIER_TYPE", "PSTORAGE_ID_NAA_FORMAT",
            "PSTORAGE_LB_PROVISIONING_MAP_RESOURCES", "PSTORAGE_MEDIA_SERIAL_NUMBER_DATA", "PSTORAGE_MEDIA_TYPE", "PSTORAGE_MEDIUM_PRODUCT_TYPE_DESCRIPTOR", "PSTORAGE_MINIPORT_DESCRIPTOR", "PSTORAGE_PORT_CODE_SET",
            "PSTORAGE_PREDICT_FAILURE", "PSTORAGE_PRIORITY_HINT_SUPPORT", "PSTORAGE_PROPERTY_ID", "PSTORAGE_PROPERTY_QUERY", "PSTORAGE_PROPERTY_SET", "PSTORAGE_PROTOCOL_ATA_DATA_TYPE",
            "PSTORAGE_PROTOCOL_COMMAND", "PSTORAGE_PROTOCOL_DATA_DESCRIPTOR", "PSTORAGE_PROTOCOL_DATA_DESCRIPTOR_EXT", "PSTORAGE_PROTOCOL_DATA_SUBVALUE_GET_LOG_PAGE", "PSTORAGE_PROTOCOL_NVME_DATA_TYPE", "PSTORAGE_PROTOCOL_SPECIFIC_DATA",
            "PSTORAGE_PROTOCOL_SPECIFIC_DATA_EXT", "PSTORAGE_PROTOCOL_TYPE", "PSTORAGE_PROTOCOL_UFS_DATA_TYPE", "PSTORAGE_QUERY_TYPE", "PSTORAGE_READ_CAPACITY", "PSTORAGE_RPMB_DESCRIPTOR",
            "PSTORAGE_RPMB_FRAME_TYPE", "PSTORAGE_SET_TYPE", "PSTORAGE_TEMPERATURE_DATA_DESCRIPTOR", "PSTORAGE_TEMPERATURE_INFO", "PSTORAGE_TEMPERATURE_THRESHOLD", "PSTORAGE_TIER",
            "PSTORAGE_TIER_CLASS", "PSTORAGE_TIER_MEDIA_TYPE", "PSTORAGE_WRITE_CACHE_PROPERTY", "PSTR", "PSTYLEBUF", "PSTYLEBUFA",
            "PSTYLEBUFW", "PSUACTION", "PSUPPORTED_OS_INFO", "PSYNCHRONIZATION_BARRIER", "PSYSTEMTIME", "PSYSTEM_ACCESS_FILTER_ACE",
            "PSYSTEM_ALARM_ACE", "PSYSTEM_ALARM_CALLBACK_ACE", "PSYSTEM_ALARM_CALLBACK_OBJECT_ACE", "PSYSTEM_ALARM_OBJECT_ACE", "PSYSTEM_AUDIT_ACE", "PSYSTEM_AUDIT_CALLBACK_ACE",
            "PSYSTEM_AUDIT_CALLBACK_OBJECT_ACE", "PSYSTEM_AUDIT_OBJECT_ACE", "PSYSTEM_BATTERY_STATE", "PSYSTEM_CPU_SET_INFORMATION", "PSYSTEM_LOGICAL_PROCESSOR_INFORMATION", "PSYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX",
            "PSYSTEM_MANDATORY_LABEL_ACE", "PSYSTEM_POOL_ZEROING_INFORMATION", "PSYSTEM_POWER_CAPABILITIES", "PSYSTEM_POWER_LEVEL", "PSYSTEM_POWER_POLICY", "PSYSTEM_POWER_STATE",
            "PSYSTEM_PROCESSOR_CYCLE_TIME_INFORMATION", "PSYSTEM_PROCESS_TRUST_LABEL_ACE", "PSYSTEM_RESOURCE_ATTRIBUTE_ACE", "PSYSTEM_SCOPED_POLICY_ID_ACE", "PSYSTEM_SUPPORTED_PROCESSOR_ARCHITECTURES_INFORMATION", "PSZ",
            "PTAPE_CREATE_PARTITION", "PTAPE_ERASE", "PTAPE_GET_DRIVE_PARAMETERS", "PTAPE_GET_MEDIA_PARAMETERS", "PTAPE_GET_POSITION", "PTAPE_GET_STATISTICS",
            "PTAPE_PREPARE", "PTAPE_SET_DRIVE_PARAMETERS", "PTAPE_SET_MEDIA_PARAMETERS", "PTAPE_SET_POSITION", "PTAPE_STATISTICS", "PTAPE_WMI_OPERATIONS",
            "PTAPE_WRITE_MARKS", "PTBBUTTON", "PTBYTE", "PTCH", "PTCHAR", "PTEXTMETRIC",
            "PTEXTMETRICA", "PTEXTMETRICW", "PTHREAD_START_ROUTINE", "PTIMECAPS", "PTIMERAPCROUTINE", "PTIMEVAL",
            "PTIME_ZONE_INFORMATION", "PTITLEBARINFO", "PTITLEBARINFOEX", "PTOKEN_ACCESS_INFORMATION", "PTOKEN_APPCONTAINER_INFORMATION", "PTOKEN_AUDIT_POLICY",
            "PTOKEN_BNO_ISOLATION_INFORMATION", "PTOKEN_CONTROL", "PTOKEN_DEFAULT_DACL", "PTOKEN_DEVICE_CLAIMS", "PTOKEN_ELEVATION", "PTOKEN_ELEVATION_TYPE",
            "PTOKEN_GROUPS", "PTOKEN_GROUPS_AND_PRIVILEGES", "PTOKEN_INFORMATION_CLASS", "PTOKEN_LINKED_TOKEN", "PTOKEN_MANDATORY_LABEL", "PTOKEN_MANDATORY_POLICY",
            "PTOKEN_ORIGIN", "PTOKEN_OWNER", "PTOKEN_PRIMARY_GROUP", "PTOKEN_PRIVILEGES", "PTOKEN_SID_INFORMATION", "PTOKEN_SOURCE",
            "PTOKEN_STATISTICS", "PTOKEN_TYPE", "PTOKEN_USER", "PTOKEN_USER_CLAIMS", "PTOOLINFOA", "PTOOLINFOW",
            "PTOP_LEVEL_EXCEPTION_FILTER", "PTOUCHINPUT", "PTP_CALLBACK_ENVIRON", "PTP_CALLBACK_INSTANCE", "PTP_CLEANUP_GROUP", "PTP_CLEANUP_GROUP_CANCEL_CALLBACK",
            "PTP_IO", "PTP_POOL", "PTP_POOL_STACK_INFORMATION", "PTP_SIMPLE_CALLBACK", "PTP_TIMER", "PTP_TIMER_CALLBACK",
            "PTP_VERSION", "PTP_WAIT", "PTP_WAIT_CALLBACK", "PTP_WIN32_IO_CALLBACK", "PTP_WORK", "PTP_WORK_CALLBACK",
            "PTRANSACTIONMANAGER_BASIC_INFORMATION", "PTRANSACTIONMANAGER_LOGPATH_INFORMATION", "PTRANSACTIONMANAGER_LOG_INFORMATION", "PTRANSACTIONMANAGER_OLDEST_INFORMATION", "PTRANSACTIONMANAGER_RECOVERY_INFORMATION", "PTRANSACTION_BASIC_INFORMATION",
            "PTRANSACTION_BIND_INFORMATION", "PTRANSACTION_ENLISTMENTS_INFORMATION", "PTRANSACTION_ENLISTMENT_PAIR", "PTRANSACTION_LIST_ENTRY", "PTRANSACTION_LIST_INFORMATION", "PTRANSACTION_NOTIFICATION",
            "PTRANSACTION_NOTIFICATION_MARSHAL_ARGUMENT", "PTRANSACTION_NOTIFICATION_PROMOTE_ARGUMENT", "PTRANSACTION_NOTIFICATION_PROPAGATE_ARGUMENT", "PTRANSACTION_NOTIFICATION_RECOVERY_ARGUMENT", "PTRANSACTION_NOTIFICATION_SAVEPOINT_ARGUMENT", "PTRANSACTION_NOTIFICATION_TM_ONLINE_ARGUMENT",
            "PTRANSACTION_PROPERTIES_INFORMATION", "PTRANSACTION_SUPERIOR_ENLISTMENT_INFORMATION", "PTRANSMIT_FILE_BUFFERS", "PTRIVERTEX", "PTSTR", "PTTGETTITLE",
            "PTXFS_CREATE_MINIVERSION_INFO", "PTXFS_GET_METADATA_INFO_OUT", "PTXFS_GET_TRANSACTED_VERSION", "PTXFS_LIST_TRANSACTIONS", "PTXFS_LIST_TRANSACTIONS_ENTRY", "PTXFS_LIST_TRANSACTION_LOCKED_FILES",
            "PTXFS_LIST_TRANSACTION_LOCKED_FILES_ENTRY", "PTXFS_MODIFY_RM", "PTXFS_QUERY_RM_INFORMATION", "PTXFS_READ_BACKUP_INFORMATION_OUT", "PTXFS_ROLLFORWARD_REDO_INFORMATION", "PTXFS_SAVEPOINT_INFORMATION",
            "PTXFS_START_RM_INFORMATION", "PTXFS_TRANSACTION_ACTIVE_INFO", "PTXFS_WRITE_BACKUP_INFORMATION", "PUAF", "PUAFOUT", "PUBLICKEYSTRUC",
            "PUCHAR", "PUCSCHAR", "PUCSSTR", "PUHALF_PTR", "PUINT", "PUINT16",
            "PUINT32", "PUINT64", "PUINT8", "PUINT_PTR", "PULARGE_INTEGER", "PULONG",
            "PULONG32", "PULONG64", "PULONGLONG", "PULONG_PTR", "PUMS_COMPLETION_LIST", "PUMS_CONTEXT",
            "PUMS_CREATE_THREAD_ATTRIBUTES", "PUMS_SCHEDULER_ENTRY_POINT", "PUMS_SCHEDULER_STARTUP_INFO", "PUMS_SYSTEM_THREAD_INFORMATION", "PUMS_THREAD_INFO_CLASS", "PUNWIND_HISTORY_TABLE",
            "PUNWIND_HISTORY_TABLE_ENTRY", "PUNZTCH", "PUNZWCH", "PUPDATELAYEREDWINDOWINFO", "PUSEROBJECTFLAGS", "PUSER_ACTIVITY_PRESENCE",
            "PUSHORT", "PUSN_JOURNAL_DATA", "PUSN_RECORD", "PUTSTR", "PUUCSCHAR", "PUUCSSTR",
            "PUWSTR", "PUWSTR_C", "PUZZTSTR", "PUZZWSTR", "PVALCONTEXT", "PVALENT",
            "PVALENTA", "PVALENTW", "PVALUE", "PVALUEA", "PVALUEW", "PVECTORED_EXCEPTION_HANDLER",
            "PVERIFY_INFORMATION", "PVIDEOPARAMETERS", "PVIRTUAL_DISK_PROGRESS", "PVIRTUAL_STORAGE_TYPE", "PVOID", "PVOID64",
            "PVOLUME_BITMAP_BUFFER", "PVOLUME_DISK_EXTENTS", "PVOLUME_GET_GPT_ATTRIBUTES_INFORMATION", "PWAVEFORMAT", "PWAVEFORMATEX", "PWAVEHDR",
            "PWAVEINCAPS", "PWAVEINCAPS2", "PWAVEINCAPS2A", "PWAVEINCAPS2W", "PWAVEINCAPSA", "PWAVEINCAPSW",
            "PWAVEOUTCAPS", "PWAVEOUTCAPS2", "PWAVEOUTCAPS2A", "PWAVEOUTCAPS2W", "PWAVEOUTCAPSA", "PWAVEOUTCAPSW",
            "PWCH", "PWCHAR", "PWCRANGE", "PWGLSWAP", "PWIN32_FIND_DATA", "PWIN32_FIND_DATAA",
            "PWIN32_FIND_DATAW", "PWIN32_FIND_STREAM_DATA", "PWINDOWINFO", "PWINDOWPLACEMENT", "PWINDOWPOS", "PWINDOW_BUFFER_SIZE_RECORD",
            "PWNDCLASS", "PWNDCLASSA", "PWNDCLASSEX", "PWNDCLASSEXA", "PWNDCLASSEXW", "PWNDCLASSW",
            "PWORD", "PWOW64_CONTEXT", "PWOW64_DESCRIPTOR_TABLE_ENTRY", "PWOW64_FLOATING_SAVE_AREA", "PWOW64_LDT_ENTRY", "PWSTR",
            "PWTSSESSION_NOTIFICATION", "PXFORM", "PXMIT_ROUTINE_QUINTUPLE", "PXMM_SAVE_AREA32", "PXSAVE_AREA", "PXSAVE_AREA_HEADER",
            "PXSAVE_ARM64_SVE_HEADER", "PXSAVE_CET_U_FORMAT", "PXSAVE_FORMAT", "PXSTATE_CONFIGURATION", "PXSTATE_CONTEXT", "PXSTATE_FEATURE",
            "PZPCSTR", "PZPCWSTR", "PZPSTR", "PZPTSTR", "PZPWSTR", "PZZSTR",
            "PZZTSTR", "PZZWSTR", "QUERYCONTEXT", "QUERYHANDLER", "QUERYOPTION", "QUERY_SERVICE_CONFIG",
            "QUERY_SERVICE_CONFIGA", "QUERY_SERVICE_CONFIGW", "QUERY_SERVICE_LOCK_STATUS", "QUERY_SERVICE_LOCK_STATUSA", "QUERY_SERVICE_LOCK_STATUSW", "QUERY_USER_NOTIFICATION_STATE",
            "QUOTA_LIMITS", "QUOTA_LIMITS_EX", "RASTERIZER_STATUS", "RATE_QUOTA_LIMIT", "RAWHID", "RAWINPUT",
            "RAWINPUTDEVICE", "RAWINPUTDEVICELIST", "RAWINPUTHEADER", "RAWKEYBOARD", "RAWMOUSE", "RBHITTESTINFO",
            "RDR_CALLOUT_STATE", "READ_ELEMENT_ADDRESS_INFO", "READ_USN_JOURNAL_DATA", "REASON_CONTEXT", "REASSIGN_BLOCKS", "REASSIGN_BLOCKS_EX",
            "REBARBANDINFOA", "REBARBANDINFOW", "REBARINFO", "RECONVERTSTRING", "RECT", "RECTL",
            "REDIRECTION_DESCRIPTOR", "REDIRECTION_FUNCTION_DESCRIPTOR", "REGCLS", "REGISTERWORD", "REGISTERWORDA", "REGISTERWORDENUMPROCA",
            "REGISTERWORDENUMPROCW", "REGISTERWORDW", "REGKIND", "REGSAM", "REG_PROVIDER", "REMOTE_NAME_INFO",
            "REMOTE_NAME_INFOA", "REMOTE_NAME_INFOW", "REMSECURITY_ATTRIBUTES", "REPARSE_GUID_DATA_BUFFER", "REQUEST_OPLOCK_INPUT_BUFFER", "REQUEST_OPLOCK_OUTPUT_BUFFER",
            "REQUEST_RAW_ENCRYPTED_DATA", "RESOURCEMANAGER_BASIC_INFORMATION", "RESOURCEMANAGER_COMPLETION_INFORMATION", "RESOURCEMANAGER_INFORMATION_CLASS", "RESUME_PERFORMANCE", "RETRIEVAL_POINTER_BASE",
            "RGBQUAD", "RGBTRIPLE", "RGNDATA", "RGNDATAHEADER", "RID_DEVICE_INFO", "RID_DEVICE_INFO_HID",
            "RID_DEVICE_INFO_KEYBOARD", "RID_DEVICE_INFO_MOUSE", "RIP_INFO", "ROOT_INFO_LUID", "RPCLT_PDU_FILTER_FUNC", "RPCNOTIFICATION_ROUTINE",
            "RPCOLEDATAREP", "RPCOLEMESSAGE", "RPCOPT_PROPERTIES", "RPCOPT_SERVER_LOCALITY_VALUES", "RPC_ADDRESS_CHANGE_FN", "RPC_ASYNC_EVENT",
            "RPC_ASYNC_NOTIFICATION_INFO", "RPC_ASYNC_STATE", "RPC_AUTHZ_HANDLE", "RPC_AUTH_IDENTITY_HANDLE", "RPC_AUTH_KEY_RETRIEVAL_FN", "RPC_BINDING_HANDLE",
            "RPC_BINDING_HANDLE_OPTIONS", "RPC_BINDING_HANDLE_OPTIONS_V1", "RPC_BINDING_HANDLE_SECURITY", "RPC_BINDING_HANDLE_SECURITY_V1", "RPC_BINDING_HANDLE_TEMPLATE", "RPC_BINDING_HANDLE_TEMPLATE_V1",
            "RPC_BINDING_VECTOR", "RPC_BLOCKING_FN", "RPC_BUFPTR", "RPC_CALL_ATTRIBUTES", "RPC_CALL_ATTRIBUTES_A", "RPC_CALL_ATTRIBUTES_V1_A",
            "RPC_CALL_ATTRIBUTES_V1_W", "RPC_CALL_ATTRIBUTES_V2_A", "RPC_CALL_ATTRIBUTES_V2_W", "RPC_CALL_ATTRIBUTES_W", "RPC_CALL_LOCAL_ADDRESS_A", "RPC_CALL_LOCAL_ADDRESS_V1_A",
            "RPC_CALL_LOCAL_ADDRESS_V1_W", "RPC_CALL_LOCAL_ADDRESS_W", "RPC_CLIENT_ALLOC", "RPC_CLIENT_FREE", "RPC_CLIENT_INFORMATION1", "RPC_CLIENT_INTERFACE",
            "RPC_CSTR", "RPC_C_OPT_METADATA_DESCRIPTOR", "RPC_DISPATCH_FUNCTION", "RPC_DISPATCH_TABLE", "RPC_EE_INFO_PARAM", "RPC_EP_INQ_HANDLE",
            "RPC_ERROR_ENUM_HANDLE", "RPC_EXTENDED_ERROR_INFO", "RPC_FORWARD_FUNCTION", "RPC_HTTP_PROXY_FREE_STRING", "RPC_HTTP_REDIRECTOR_STAGE", "RPC_HTTP_TRANSPORT_CREDENTIALS_A",
            "RPC_HTTP_TRANSPORT_CREDENTIALS_W", "RPC_IF_CALLBACK_FN", "RPC_IF_HANDLE", "RPC_IF_ID", "RPC_IF_ID_VECTOR", "RPC_IMPORT_CONTEXT_P",
            "RPC_LENGTH", "RPC_MESSAGE", "RPC_MGMT_AUTHORIZATION_FN", "RPC_NEW_HTTP_PROXY_CHANNEL", "RPC_NOTIFICATIONS", "RPC_NOTIFICATION_TYPES",
            "RPC_NS_HANDLE", "RPC_OBJECT_INQ_FN", "RPC_POLICY", "RPC_PROTSEQ_ENDPOINT", "RPC_PROTSEQ_VECTORA", "RPC_PROTSEQ_VECTORW",
            "RPC_SECURITY_CALLBACK_FN", "RPC_SECURITY_QOS", "RPC_SECURITY_QOS_V2_A", "RPC_SECURITY_QOS_V2_W", "RPC_SECURITY_QOS_V3_A", "RPC_SECURITY_QOS_V3_W",
            "RPC_SERVER_INTERFACE", "RPC_SETFILTER_FUNC", "RPC_SS_THREAD_HANDLE", "RPC_STATS_VECTOR", "RPC_STATUS", "RPC_SYNTAX_IDENTIFIER",
            "RPC_TRANSFER_SYNTAX", "RPC_VERSION", "RPC_WSTR", "RSAPUBKEY", "RTL_BARRIER", "RTL_CONDITION_VARIABLE",
            "RTL_CRITICAL_SECTION", "RTL_CRITICAL_SECTION_DEBUG", "RTL_OSVERSIONINFOEXW", "RTL_OSVERSIONINFOW", "RTL_REFERENCE_COUNT", "RTL_REFERENCE_COUNT32",
            "RTL_RESOURCE_DEBUG", "RTL_RUN_ONCE", "RTL_SRWLOCK", "RTL_UMS_SCHEDULER_ENTRY_POINT", "RTL_UMS_SCHEDULER_REASON", "RTL_UMS_THREAD_INFO_CLASS",
            "RTL_VERIFIER_DLL_DESCRIPTOR", "RTL_VERIFIER_DLL_LOAD_CALLBACK", "RTL_VERIFIER_DLL_UNLOAD_CALLBACK", "RTL_VERIFIER_NTDLLHEAPFREE_CALLBACK", "RTL_VERIFIER_PROVIDER_DESCRIPTOR", "RTL_VERIFIER_THUNK_DESCRIPTOR",
            "RUNTIME_FUNCTION", "RemBINDINFO", "RemFORMATETC", "RemHBITMAP", "RemHBRUSH", "RemHENHMETAFILE",
            "RemHGLOBAL", "RemHMETAFILEPICT", "RemHPALETTE", "RemSNB", "RemSTGMEDIUM", "RemotableHandle",
            "RpcCallClientLocality", "RpcCallType", "RpcLocalAddressFormat", "SAFEARRAY", "SAFEARRAYBOUND", "SAFEARRAYUNION",
            "SAFEARR_BRECORD", "SAFEARR_BSTR", "SAFEARR_DISPATCH", "SAFEARR_HAVEIID", "SAFEARR_UNKNOWN", "SAFEARR_VARIANT",
            "SAVEPOINT_ID", "SCARDCONTEXT", "SCARDHANDLE", "SCARD_ATRMASK", "SCARD_IO_REQUEST", "SCARD_READERSTATE",
            "SCARD_READERSTATEA", "SCARD_READERSTATEW", "SCARD_T0_COMMAND", "SCARD_T0_REQUEST", "SCARD_T1_REQUEST", "SCHANNEL_ALG",
            "SCODE", "SCONTEXT_QUEUE", "SCOPE_TABLE_AMD64", "SCROLLBARINFO", "SCROLLINFO", "SC_ACTION",
            "SC_ACTION_TYPE", "SC_ENUM_TYPE", "SC_HANDLE", "SC_LOCK", "SC_STATUS_TYPE", "SD_CHANGE_MACHINE_SID_INPUT",
            "SD_CHANGE_MACHINE_SID_OUTPUT", "SECURITY_ATTRIBUTES", "SECURITY_CAPABILITIES", "SECURITY_CONTEXT_TRACKING_MODE", "SECURITY_DESCRIPTOR", "SECURITY_DESCRIPTOR_CONTROL",
            "SECURITY_DESCRIPTOR_RELATIVE", "SECURITY_IMPERSONATION_LEVEL", "SECURITY_INFORMATION", "SECURITY_OBJECT_AI_PARAMS", "SECURITY_QUALITY_OF_SERVICE", "SECURITY_STATUS",
            "SEC_WINNT_AUTH_IDENTITY_A", "SEC_WINNT_AUTH_IDENTITY_W", "SENDASYNCPROC", "SENDCMDINPARAMS", "SENDCMDOUTPARAMS", "SERIALIZEDPROPERTYVALUE",
            "SERIALKEYS", "SERIALKEYSA", "SERIALKEYSW", "SERVENT", "SERVERCALL", "SERVERSILO_BASIC_INFORMATION",
            "SERVERSILO_STATE", "SERVER_ROUTINE", "SERVICE_CONTROL_STATUS_REASON_PARAMS", "SERVICE_CONTROL_STATUS_REASON_PARAMSA", "SERVICE_CONTROL_STATUS_REASON_PARAMSW", "SERVICE_DELAYED_AUTO_START_INFO",
            "SERVICE_DESCRIPTION", "SERVICE_DESCRIPTIONA", "SERVICE_DESCRIPTIONW", "SERVICE_ERROR_TYPE", "SERVICE_FAILURE_ACTIONS", "SERVICE_FAILURE_ACTIONSA",
            "SERVICE_FAILURE_ACTIONSW", "SERVICE_FAILURE_ACTIONS_FLAG", "SERVICE_LOAD_TYPE", "SERVICE_NODE_TYPE", "SERVICE_NOTIFY", "SERVICE_NOTIFYA",
            "SERVICE_NOTIFYW", "SERVICE_PRESHUTDOWN_INFO", "SERVICE_REQUIRED_PRIVILEGES_INFO", "SERVICE_REQUIRED_PRIVILEGES_INFOA", "SERVICE_REQUIRED_PRIVILEGES_INFOW", "SERVICE_SID_INFO",
            "SERVICE_STATUS", "SERVICE_STATUS_HANDLE", "SERVICE_STATUS_PROCESS", "SERVICE_TABLE_ENTRY", "SERVICE_TABLE_ENTRYA", "SERVICE_TABLE_ENTRYW",
            "SESSION_BUFFER", "SESSION_HEADER", "SET_PARTITION_INFORMATION", "SET_PARTITION_INFORMATION_EX", "SET_PARTITION_INFORMATION_GPT", "SET_PARTITION_INFORMATION_MBR",
            "SET_POWER_SETTING_VALUE", "SET_VIRTUAL_DISK_INFO", "SET_VIRTUAL_DISK_INFO_VERSION", "SE_ACCESS_REPLY", "SE_ACCESS_REQUEST", "SE_IMAGE_SIGNATURE_TYPE",
            "SE_IMPERSONATION_STATE", "SE_LEARNING_MODE_DATA_TYPE", "SE_SECURITY_DESCRIPTOR", "SE_SID", "SE_SIGNING_LEVEL", "SE_TOKEN_USER",
            "SF_TYPE", "SHANDLE_PTR", "SHCREATEPROCESSINFOW", "SHELLEXECUTEINFO", "SHELLEXECUTEINFOA", "SHELLEXECUTEINFOW",
            "SHELLHOOKINFO", "SHFILEINFO", "SHFILEINFOA", "SHFILEINFOW", "SHFILEOPSTRUCT", "SHFILEOPSTRUCTA",
            "SHFILEOPSTRUCTW", "SHNAMEMAPPING", "SHNAMEMAPPINGA", "SHNAMEMAPPINGW", "SHORT", "SHQUERYRBINFO",
            "SHRINK_VOLUME_INFORMATION", "SHRINK_VOLUME_REQUEST_TYPES", "SHSTOCKICONINFO", "SID", "SID_AND_ATTRIBUTES", "SID_AND_ATTRIBUTES_ARRAY",
            "SID_AND_ATTRIBUTES_HASH", "SID_HASH_ENTRY", "SID_IDENTIFIER_AUTHORITY", "SID_NAME_USE", "SILOOBJECT_BASIC_INFORMATION", "SINGLE_LIST_ENTRY",
            "SIZE", "SIZEL", "SIZE_T", "SI_COPYFILE", "SLIST_ENTRY", "SLIST_HEADER",
            "SMALL_RECT", "SNB", "SOCKADDR", "SOCKADDR_IN", "SOCKET", "SOFTDISTINFO",
            "SOLE_AUTHENTICATION_INFO", "SOLE_AUTHENTICATION_LIST", "SOLE_AUTHENTICATION_SERVICE", "SOUNDSENTRY", "SOUNDSENTRYA", "SOUNDSENTRYW",
            "SPHANDLE", "SRWLOCK", "SSIZE_T", "SSL_EXTRA_CERT_CHAIN_POLICY_PARA", "SSL_F12_EXTRA_CERT_CHAIN_POLICY_STATUS", "SSL_HPKP_HEADER_EXTRA_CERT_CHAIN_POLICY_PARA",
            "SSL_KEY_PIN_EXTRA_CERT_CHAIN_POLICY_PARA", "SSL_KEY_PIN_EXTRA_CERT_CHAIN_POLICY_STATUS", "STARTING_LCN_INPUT_BUFFER", "STARTING_VCN_INPUT_BUFFER", "STARTUPINFO", "STARTUPINFOA",
            "STARTUPINFOEX", "STARTUPINFOEXA", "STARTUPINFOEXW", "STARTUPINFOW", "STATDATA", "STATFLAG",
            "STATPROPSETSTG", "STATPROPSTG", "STATSTG", "STDMSHLFLAGS", "STGC", "STGFMT",
            "STGMEDIUM", "STGMOVE", "STGOPTIONS", "STGTY", "STICKYKEYS", "STORAGE_ACCESS_ALIGNMENT_DESCRIPTOR",
            "STORAGE_ADAPTER_DESCRIPTOR", "STORAGE_ALLOCATE_BC_STREAM_INPUT", "STORAGE_ALLOCATE_BC_STREAM_OUTPUT", "STORAGE_ASSOCIATION_TYPE", "STORAGE_BUS_RESET_REQUEST", "STORAGE_BUS_TYPE",
            "STORAGE_CRYPTO_ALGORITHM_ID", "STORAGE_CRYPTO_CAPABILITY", "STORAGE_CRYPTO_DESCRIPTOR", "STORAGE_CRYPTO_KEY_SIZE", "STORAGE_DEPENDENCY_INFO", "STORAGE_DEPENDENCY_INFO_TYPE_1",
            "STORAGE_DEPENDENCY_INFO_TYPE_2", "STORAGE_DEPENDENCY_INFO_VERSION", "STORAGE_DESCRIPTOR_HEADER", "STORAGE_DEVICE_DESCRIPTOR", "STORAGE_DEVICE_FAULT_DOMAIN_DESCRIPTOR", "STORAGE_DEVICE_ID_DESCRIPTOR",
            "STORAGE_DEVICE_NUMBER", "STORAGE_DEVICE_NUMBERS", "STORAGE_DEVICE_NUMBER_EX", "STORAGE_DEVICE_RESILIENCY_DESCRIPTOR", "STORAGE_DEVICE_TIERING_DESCRIPTOR", "STORAGE_FAILURE_PREDICTION_CONFIG",
            "STORAGE_GET_BC_PROPERTIES_OUTPUT", "STORAGE_HOTPLUG_INFO", "STORAGE_IDENTIFIER", "STORAGE_IDENTIFIER_CODE_SET", "STORAGE_IDENTIFIER_TYPE", "STORAGE_ID_NAA_FORMAT",
            "STORAGE_LB_PROVISIONING_MAP_RESOURCES", "STORAGE_MEDIA_SERIAL_NUMBER_DATA", "STORAGE_MEDIA_TYPE", "STORAGE_MEDIUM_PRODUCT_TYPE_DESCRIPTOR", "STORAGE_MINIPORT_DESCRIPTOR", "STORAGE_PORT_CODE_SET",
            "STORAGE_PREDICT_FAILURE", "STORAGE_PRIORITY_HINT_SUPPORT", "STORAGE_PROPERTY_ID", "STORAGE_PROPERTY_QUERY", "STORAGE_PROPERTY_SET", "STORAGE_PROTOCOL_ATA_DATA_TYPE",
            "STORAGE_PROTOCOL_COMMAND", "STORAGE_PROTOCOL_DATA_DESCRIPTOR", "STORAGE_PROTOCOL_DATA_DESCRIPTOR_EXT", "STORAGE_PROTOCOL_DATA_SUBVALUE_GET_LOG_PAGE", "STORAGE_PROTOCOL_NVME_DATA_TYPE", "STORAGE_PROTOCOL_SPECIFIC_DATA",
            "STORAGE_PROTOCOL_SPECIFIC_DATA_EXT", "STORAGE_PROTOCOL_TYPE", "STORAGE_PROTOCOL_UFS_DATA_TYPE", "STORAGE_QUERY_TYPE", "STORAGE_READ_CAPACITY", "STORAGE_RPMB_DESCRIPTOR",
            "STORAGE_RPMB_FRAME_TYPE", "STORAGE_SET_TYPE", "STORAGE_TEMPERATURE_DATA_DESCRIPTOR", "STORAGE_TEMPERATURE_INFO", "STORAGE_TEMPERATURE_THRESHOLD", "STORAGE_TIER",
            "STORAGE_TIER_CLASS", "STORAGE_TIER_MEDIA_TYPE", "STORAGE_WRITE_CACHE_PROPERTY", "STREAM_INFO_LEVELS", "STREAM_SEEK", "STUB_PHASE",
            "STUB_THUNK", "STYLEBUF", "STYLEBUFA", "STYLEBUFW", "STYLESTRUCT", "SUBCLASSPROC",
            "SUPPORTED_OS_INFO", "SYNCHRONIZATION_BARRIER", "SYSKIND", "SYSTEMTIME", "SYSTEM_ACCESS_FILTER_ACE", "SYSTEM_ALARM_ACE",
            "SYSTEM_ALARM_CALLBACK_ACE", "SYSTEM_ALARM_CALLBACK_OBJECT_ACE", "SYSTEM_ALARM_OBJECT_ACE", "SYSTEM_AUDIT_ACE", "SYSTEM_AUDIT_CALLBACK_ACE", "SYSTEM_AUDIT_CALLBACK_OBJECT_ACE",
            "SYSTEM_AUDIT_OBJECT_ACE", "SYSTEM_BATTERY_STATE", "SYSTEM_CPU_SET_INFORMATION", "SYSTEM_INFO", "SYSTEM_LOGICAL_PROCESSOR_INFORMATION", "SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX",
            "SYSTEM_MANDATORY_LABEL_ACE", "SYSTEM_POOL_ZEROING_INFORMATION", "SYSTEM_POWER_CAPABILITIES", "SYSTEM_POWER_CONDITION", "SYSTEM_POWER_LEVEL", "SYSTEM_POWER_POLICY",
            "SYSTEM_POWER_STATE", "SYSTEM_POWER_STATUS", "SYSTEM_PROCESSOR_CYCLE_TIME_INFORMATION", "SYSTEM_PROCESS_TRUST_LABEL_ACE", "SYSTEM_RESOURCE_ATTRIBUTE_ACE", "SYSTEM_SCOPED_POLICY_ID_ACE",
            "SYSTEM_SUPPORTED_PROCESSOR_ARCHITECTURES_INFORMATION", "SZM_FLAGS", "ShutdownType", "StartParam", "StorageLayout", "TAPE_CREATE_PARTITION",
            "TAPE_DRIVE_PROBLEM_TYPE", "TAPE_ERASE", "TAPE_GET_DRIVE_PARAMETERS", "TAPE_GET_MEDIA_PARAMETERS", "TAPE_GET_POSITION", "TAPE_GET_STATISTICS",
            "TAPE_PREPARE", "TAPE_SET_DRIVE_PARAMETERS", "TAPE_SET_MEDIA_PARAMETERS", "TAPE_SET_POSITION", "TAPE_STATISTICS", "TAPE_WMI_OPERATIONS",
            "TAPE_WRITE_MARKS", "TASKDIALOGCONFIG", "TASKDIALOG_BUTTON", "TASKDIALOG_COMMON_BUTTON_FLAGS", "TASKDIALOG_ELEMENTS", "TASKDIALOG_FLAGS",
            "TASKDIALOG_ICON_ELEMENTS", "TASKDIALOG_MESSAGES", "TASKDIALOG_NOTIFICATIONS", "TBADDBITMAP", "TBBUTTON", "TBBUTTONINFOA",
            "TBBUTTONINFOW", "TBINSERTMARK", "TBMETRICS", "TBREPLACEBITMAP", "TBSAVEPARAMSA", "TBSAVEPARAMSW",
            "TBYTE", "TCHAR", "TCHITTESTINFO", "TCITEMA", "TCITEMHEADERA", "TCITEMHEADERW",
            "TCITEMW", "TEKPUBKEY", "TEXTMETRIC", "TEXTMETRICA", "TEXTMETRICW", "THDTYPE",
            "THREAD_INFORMATION_CLASS", "THREAD_POWER_THROTTLING_STATE", "TIMECALLBACK", "TIMECAPS", "TIMEFMT_ENUMPROCA", "TIMEFMT_ENUMPROCEX",
            "TIMEFMT_ENUMPROCW", "TIMERPROC", "TIMEVAL", "TIME_ZONE_INFORMATION", "TITLEBARINFO", "TITLEBARINFOEX",
            "TLIBATTR", "TOGGLEKEYS", "TOKEN_ACCESS_INFORMATION", "TOKEN_APPCONTAINER_INFORMATION", "TOKEN_AUDIT_POLICY", "TOKEN_BNO_ISOLATION_INFORMATION",
            "TOKEN_CONTROL", "TOKEN_DEFAULT_DACL", "TOKEN_DEVICE_CLAIMS", "TOKEN_ELEVATION", "TOKEN_ELEVATION_TYPE", "TOKEN_GROUPS",
            "TOKEN_GROUPS_AND_PRIVILEGES", "TOKEN_INFORMATION_CLASS", "TOKEN_LINKED_TOKEN", "TOKEN_MANDATORY_LABEL", "TOKEN_MANDATORY_POLICY", "TOKEN_ORIGIN",
            "TOKEN_OWNER", "TOKEN_PRIMARY_GROUP", "TOKEN_PRIVILEGES", "TOKEN_SID_INFORMATION", "TOKEN_SOURCE", "TOKEN_STATISTICS",
            "TOKEN_TYPE", "TOKEN_USER", "TOKEN_USER_CLAIMS", "TOUCHINPUT", "TPMPARAMS", "TP_CALLBACK_ENVIRON",
            "TP_CALLBACK_ENVIRON_V3", "TP_CALLBACK_INSTANCE", "TP_CALLBACK_PRIORITY", "TP_CLEANUP_GROUP", "TP_IO", "TP_POOL",
            "TP_POOL_STACK_INFORMATION", "TP_TIMER", "TP_VERSION", "TP_WAIT", "TP_WAIT_RESULT", "TP_WORK",
            "TRACKMOUSEEVENT", "TRANSACTIONMANAGER_BASIC_INFORMATION", "TRANSACTIONMANAGER_INFORMATION_CLASS", "TRANSACTIONMANAGER_LOGPATH_INFORMATION", "TRANSACTIONMANAGER_LOG_INFORMATION", "TRANSACTIONMANAGER_OLDEST_INFORMATION",
            "TRANSACTIONMANAGER_RECOVERY_INFORMATION", "TRANSACTION_BASIC_INFORMATION", "TRANSACTION_BIND_INFORMATION", "TRANSACTION_ENLISTMENTS_INFORMATION", "TRANSACTION_ENLISTMENT_PAIR", "TRANSACTION_INFORMATION_CLASS",
            "TRANSACTION_LIST_ENTRY", "TRANSACTION_LIST_INFORMATION", "TRANSACTION_NOTIFICATION", "TRANSACTION_NOTIFICATION_MARSHAL_ARGUMENT", "TRANSACTION_NOTIFICATION_PROMOTE_ARGUMENT", "TRANSACTION_NOTIFICATION_PROPAGATE_ARGUMENT",
            "TRANSACTION_NOTIFICATION_RECOVERY_ARGUMENT", "TRANSACTION_NOTIFICATION_SAVEPOINT_ARGUMENT", "TRANSACTION_NOTIFICATION_TM_ONLINE_ARGUMENT", "TRANSACTION_OUTCOME", "TRANSACTION_PROPERTIES_INFORMATION", "TRANSACTION_STATE",
            "TRANSACTION_SUPERIOR_ENLISTMENT_INFORMATION", "TRANSMIT_FILE_BUFFERS", "TRIVERTEX", "TTGETTITLE", "TTHITTESTINFOA", "TTHITTESTINFOW",
            "TTPOLYCURVE", "TTPOLYGONHEADER", "TTTOOLINFOA", "TTTOOLINFOW", "TVGETITEMPARTRECTINFO", "TVHITTESTINFO",
            "TVINSERTSTRUCTA", "TVINSERTSTRUCTW", "TVITEMA", "TVITEMEX", "TVITEMEXA", "TVITEMEXW",
            "TVITEMPART", "TVITEMW", "TVSORTCB", "TXFS_CREATE_MINIVERSION_INFO", "TXFS_GET_METADATA_INFO_OUT", "TXFS_GET_TRANSACTED_VERSION",
            "TXFS_LIST_TRANSACTIONS", "TXFS_LIST_TRANSACTIONS_ENTRY", "TXFS_LIST_TRANSACTION_LOCKED_FILES", "TXFS_LIST_TRANSACTION_LOCKED_FILES_ENTRY", "TXFS_MODIFY_RM", "TXFS_QUERY_RM_INFORMATION",
            "TXFS_READ_BACKUP_INFORMATION_OUT", "TXFS_ROLLFORWARD_REDO_INFORMATION", "TXFS_SAVEPOINT_INFORMATION", "TXFS_START_RM_INFORMATION", "TXFS_TRANSACTION_ACTIVE_INFO", "TXFS_WRITE_BACKUP_INFORMATION",
            "TYMED", "TYPEATTR", "TYPEDESC", "TYPEFLAGS", "TYPEKIND", "TYSPEC",
            "UCHAR", "UCSCHAR", "UDACCEL", "UDATE", "UHALF_PTR", "UILANGUAGE_ENUMPROCA",
            "UILANGUAGE_ENUMPROCW", "UINT", "UINT16", "UINT32", "UINT64", "UINT8",
            "UINT_PTR", "ULARGE_INTEGER", "ULONG", "ULONG32", "ULONG64", "ULONGLONG",
            "ULONG_PTR", "UMS_CREATE_THREAD_ATTRIBUTES", "UMS_SCHEDULER_REASON", "UMS_SCHEDULER_STARTUP_INFO", "UMS_SYSTEM_THREAD_INFORMATION", "UMS_THREAD_INFO_CLASS",
            "UNIVERSAL_NAME_INFO", "UNIVERSAL_NAME_INFOA", "UNIVERSAL_NAME_INFOW", "UNLOAD_DLL_DEBUG_INFO", "UNWIND_HISTORY_TABLE", "UNWIND_HISTORY_TABLE_ENTRY",
            "UPDATELAYEREDWINDOWINFO", "UP_BYTE_BLOB", "UP_DWORD_BLOB", "UP_FLAGGED_BYTE_BLOB", "UP_FLAGGED_WORD_BLOB", "UP_WORD_BLOB",
            "URLTEMPLATE", "URLZONE", "URLZONEREG", "URL_ENCODING", "USERCLASSTYPE", "USEROBJECTFLAGS",
            "USER_ACTIVITY_PRESENCE", "USER_MARSHAL_CB", "USER_MARSHAL_CB_TYPE", "USER_MARSHAL_FREEING_ROUTINE", "USER_MARSHAL_MARSHALLING_ROUTINE", "USER_MARSHAL_ROUTINE_QUADRUPLE",
            "USER_MARSHAL_SIZING_ROUTINE", "USER_MARSHAL_UNMARSHALLING_ROUTINE", "USHORT", "USN", "USN_JOURNAL_DATA", "USN_RECORD",
            "UUID", "UUID_VECTOR", "UWORD", "Uri_HOST_TYPE", "Uri_PROPERTY", "VALENT",
            "VALENTA", "VALENTW", "VARDESC", "VARFLAGS", "VARIANT", "VARIANTARG",
            "VARIANT_BOOL", "VARKIND", "VARTYPE", "VERIFY_INFORMATION", "VERSIONEDSTREAM", "VIDEOPARAMETERS",
            "VIRTUAL_DISK_ACCESS_MASK", "VIRTUAL_DISK_PROGRESS", "VIRTUAL_STORAGE_TYPE", "VOLUME_BITMAP_BUFFER", "VOLUME_DISK_EXTENTS", "VOLUME_GET_GPT_ATTRIBUTES_INFORMATION",
            "VS_FIXEDFILEINFO", "WAITORTIMERCALLBACK", "WAITORTIMERCALLBACKFUNC", "WAVECALLBACK", "WAVEFORMAT", "WAVEFORMATEX",
            "WAVEHDR", "WAVEINCAPS", "WAVEINCAPS2", "WAVEINCAPS2A", "WAVEINCAPS2W", "WAVEINCAPSA",
            "WAVEINCAPSW", "WAVEOUTCAPS", "WAVEOUTCAPS2", "WAVEOUTCAPS2A", "WAVEOUTCAPS2W", "WAVEOUTCAPSA",
            "WAVEOUTCAPSW", "WCHAR", "WCRANGE", "WELL_KNOWN_SID_TYPE", "WGLSWAP", "WIN32_FILE_ATTRIBUTE_DATA",
            "WIN32_FIND_DATA", "WIN32_FIND_DATAA", "WIN32_FIND_DATAW", "WIN32_FIND_STREAM_DATA", "WIN32_STREAM_ID", "WINBOOL",
            "WINDOWINFO", "WINDOWPLACEMENT", "WINDOWPOS", "WINDOW_BUFFER_SIZE_RECORD", "WINEVENTPROC", "WINSTAENUMPROC",
            "WINSTAENUMPROCA", "WINSTAENUMPROCW", "WNDCLASS", "WNDCLASSA", "WNDCLASSEX", "WNDCLASSEXA",
            "WNDCLASSEXW", "WNDCLASSW", "WNDENUMPROC", "WNDPROC", "WORD", "WORD_BLOB",
            "WORD_SIZEDARR", "WORKERCALLBACKFUNC", "WOW64_CONTEXT", "WOW64_DESCRIPTOR_TABLE_ENTRY", "WOW64_FLOATING_SAVE_AREA", "WOW64_LDT_ENTRY",
            "WPARAM", "WRITE_CACHE_CHANGE", "WRITE_CACHE_ENABLE", "WRITE_CACHE_TYPE", "WRITE_THROUGH", "WSADATA",
            "WTSSESSION_NOTIFICATION", "XFORM", "XLAT_SIDE", "XMIT_HELPER_ROUTINE", "XMIT_ROUTINE_QUINTUPLE", "XMLELEM_TYPE",
            "XML_ERROR", "XMM_SAVE_AREA32", "XSAVE_AREA", "XSAVE_AREA_HEADER", "XSAVE_ARM64_SVE_HEADER", "XSAVE_CET_U_FORMAT",
            "XSAVE_FORMAT", "XSTATE_CONFIGURATION", "XSTATE_CONTEXT", "XSTATE_FEATURE", "YIELDPROC", "ZAFLAGS",
            "ZONEATTRIBUTES", "_CMPCCX_ENUM", "_CRT_DOUBLE", "_CRT_FLOAT", "_HEAPINFO", "_LDBL12",
            "_LDOUBLE", "_LONGDOUBLE", "_MM_MANTISSA_NORM_ENUM", "_MM_MANTISSA_SIGN_ENUM", "_MM_PERM_ENUM", "_MM_TERNLOG_ENUM",
            "_PHNDLR", "_PIMAGE_RUNTIME_FUNCTION_ENTRY", "_invalid_parameter_handler", "_locale_t", "_locale_tstruct", "_onexit_t",
            "_purecall_handler", "boolean", "byte", "cs_byte", "div_t", "errno_t",
            "error_status_t", "handle_t", "intptr_t", "ldiv_t", "lldiv_t", "max_align_t",
            "pthreadlocinfo", "pthreadmbcinfo", "ptrdiff_t", "remoteMETAFILEPICT", "rsize_t", "size_t",
            "ssize_t", "system_handle_t", "time_t", "uCLSSPEC", "uSTGMEDIUM", "u_char",
            "u_int", "u_int64", "u_long", "u_short", "uintptr_t", "userBITMAP",
            "userCLIPFORMAT", "userFLAG_STGMEDIUM", "userHBITMAP", "userHENHMETAFILE", "userHGLOBAL", "userHMETAFILE",
            "userHMETAFILEPICT", "userHPALETTE", "userSTGMEDIUM", "va_list", "wctype_t", "wint_t",
            "wireASYNC_STGMEDIUM", "wireBRECORD", "wireBSTR", "wireCLIPFORMAT", "wireFLAG_STGMEDIUM", "wireHACCEL",
            "wireHBITMAP", "wireHBRUSH", "wireHDC", "wireHENHMETAFILE", "wireHFONT", "wireHGLOBAL",
            "wireHICON", "wireHMENU", "wireHMETAFILE", "wireHMETAFILEPICT", "wireHMONITOR", "wireHPALETTE",
            "wireHRGN", "wireHWND", "wirePSAFEARRAY", "wireSAFEARRAY", "wireSNB", "wireSTGMEDIUM",
            "wireVARIANT",
        };
}

// Mode B manifest filtering result: Kept = types that truly exist in the assembly set
// (the TypeInfo generation scope), Dropped = manifest entries that missed the assembly
// set (the caller warns).
public sealed class ReflectionManifestFilterResult
{
    public ReflectionManifestFilterResult(IReadOnlyList<string> kept, IReadOnlyList<string> dropped)
    {
        Kept = kept;
        Dropped = dropped;
    }

    public IReadOnlyList<string> Kept { get; }
    public IReadOnlyList<string> Dropped { get; }
}


