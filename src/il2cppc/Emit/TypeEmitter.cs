using System.Reflection;
using System.Reflection.Metadata;
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
                        IsPublic = (fd.Attributes & FieldAttributes.Public) != 0,
                        ConstantValue = TryReadNestedConstant(md, fd),
                    });
                }
                else
                {
                    var (kind, typeName) = DecodeNestedFieldSignature(md, fd.Signature);
                    nested.InstanceFields.Add(new FieldDef
                    {
                        Name = fName,
                        Owner = nested,
                        IsStatic = false,
                        IsPublic = (fd.Attributes & FieldAttributes.Public) != 0,
                        TypeKind = kind,
                        FieldTypeName = typeName,
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
                    IsPublic = (attrs & MethodAttributes.Public) != 0,
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
    private static (FieldTypeKind Kind, string TypeName) DecodeNestedFieldSignature(MetadataReader md, BlobHandle sigHandle)
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
            0x08 => (FieldTypeKind.Int32, ""),
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
            0x15 => (FieldTypeKind.Reference, GenericNestedInstantiationName(md, ref sig)),
            0x11 => (FieldTypeKind.Int32, ResolveNestedTypeName(md, sig.ReadTypeHandle())),
            // GN-18 B1 (fnptr fields): a `delegate*` (0x1B) NESTED-type field
            // rides the same full-width void* pointer carrier as top-level
            // fields (NativeInt kind + empty TypeName marker -- see
            // EmitClassStruct; the refFieldBitmap never scans it).
            0x1B => (FieldTypeKind.NativeInt, ""),
            // Primitive and exotic field types not covered above: treat as Int32.
            var b => (FieldTypeKind.Int32, ""),
        };
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

    public static string TypeCppName(TypeDef t)
    {
        var core = TypeCppNameCore(t);
        // GN-N5: append the deterministic disambiguation suffix when THIS type's
        // C++ name collides with another type's (Sanitize maps '.' and '_' both
        // to '_', so e.g. 'Cases.A_B' and nested 'Cases.A.B' both become
        // 'Cases_A_B'). The suffix is keyed by the type identity
        // (ResolveTypeCppNameCollisions); null/absent = no collision.
        if (_cppNameOverrides != null
            && _cppNameOverrides.TryGetValue(TypeCppIdentity(t), out var sfx))
            return core + sfx;
        return core;
    }

    static string TypeCppNameCore(TypeDef t)
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

    private static string TypeCppIdentity(TypeDef t)
        => (t.IsNested && t.DeclaringTypeName.Length > 0 ? t.DeclaringTypeName + "+" + t.Name : t.FullName)
           + "|" + (t.InstantiationSuffix ?? "");

    // Public for CodeGenerator (called once per Generate, after all clones are
    // created -- the session map is deterministic on the final type list).
    public static void ResolveTypeCppNameCollisions(IReadOnlyList<TypeDef> types)
    {
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
        _cppNameOverrides = overrides;
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
        return result;
    }

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
    public static void LayoutInstanceFields(TypeDef t)
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
            LayoutInstanceFields(t.BaseType); // base chain laid out first (multi-level)
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
            ++slot;
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
    private static void LayoutValueTypeFields(TypeDef t)
    {
        uint bitmap = 0;
        int refCount = 0;
        int offset = 0;
        int maxAlign = 1;
        foreach (var f in t.InstanceFields)
        {
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
        // C++ struct size: the extent rounded up to the struct's own alignment
        // (the largest member alignment). An int-only struct therefore reports
        // exactly its compact size (15 int32 -> 60), which is what the native
        // callee writes through.
        t.InstanceSize = (offset + maxAlign - 1) / maxAlign * maxAlign;
    }

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
            FieldTypeKind.Reference => (pointerSize, pointerSize),
            _ => (pointerSize, pointerSize),
        };

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
        LayoutInstanceFields(t);
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
            var cppType = f.TypeKind switch
            {
                FieldTypeKind.Int32 => "int32_t",
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
    public static string ReferenceCppType(string fullName, IReadOnlyList<TypeDef> allTypes) => fullName switch
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
        _ when allTypes != null && allTypes.Any(x => x.FullName == fullName && IsDelegateType(x)) => "Il2CppObject*",
        // COM stage 17 batch 3 (com17type): an INTERFACE-typed reference field
        // (DragEventArgs.Data : IDataObject). Interfaces have NO generated C++
        // struct (CodeGenerator emits only the extern Il2CppClass descriptor
        // -- instanceSize 0, never allocated), so the member is a plain GC
        // reference slot, exactly like a System.Object field; the concrete
        // implementer's object is what the slot holds at runtime.
        _ when allTypes != null && allTypes.Any(x => x.FullName == fullName && x.IsInterface) => "Il2CppObject*",
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
        _ when allTypes == null || !allTypes.Any(x => x.FullName == fullName) => "Il2CppObject*",
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
        w.Line($"}};");
        w.Line();
        return w.ToString();
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
    public static string EmitVTable(TypeDef t, IReadOnlyList<string> slotMethodNames)
    {
        var w = new CppWriter();
        w.Line($"// Virtual dispatch table for {t.FullName}: {t.VirtualMethods.Count} slot(s) in base-first order.");
        w.Line($"void* {TypeCppName(t)}_vtable[] = {{");
        w.Open();
        for (var i = 0; i < t.VirtualMethods.Count; i++)
        {
            var m = t.VirtualMethods[i];
            w.Line($"reinterpret_cast<void*>(&{slotMethodNames[i]}), // slot {i}: {m.Name}({m.ParamTypes}){(m.Owner == t ? "" : " (inherited from " + m.Owner.FullName + ")")}");
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
    private enum StaticFieldSemantic { Value, Reference, Decimal, IntPtr, UIntPtr, Int64, UInt64, DateTime, TimeSpan, FnPtr }

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
        LayoutInstanceFields(t);
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
                    | (m.Name is ".ctor" or ".cctor" ? 4 : 0) | (m.IsVirtual ? 8 : 0);
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
                w.Line($"{{ \"{m.Name}\", \"{EncodeReturnType(m.ReturnType, m.ReturnTypeName)}({m.ParamTypes})\", {m.ParamCount}, 0x{flags:x}, {slot}, \"{ReflectionFullName(t)}\", {fnPtr} }},");
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
                Offset: f.ByteOffset, IsRef: f.TypeKind == FieldTypeKind.Reference ? 1 : 0,
                Addr: "nullptr", Flags: f.IsPublic ? 1 : 0))
            .Concat(t.StaticFields.Select(f =>
            {
                var (semantic, typeName) = StaticFieldSemantics(f, md);
                return (f.Name, TypeName: typeName,
                    Offset: -1, IsRef: semantic == StaticFieldSemantic.Reference ? 1 : 0,
                    Addr: f.IsLiteral ? "nullptr" : $"&{FieldCppName(t, f.Name)}",
                    Flags: (f.IsPublic ? 1 : 0) | (f.IsStatic ? 2 : 0) | (f.IsLiteral ? 4 : 0));
            }))
            .ToList();
        if (fieldEntries.Count > 0)
        {
            w.Line($"static const il2cpp::Il2CppFieldInfo {TypeCppName(t)}_typeinfo_fields[] = {{");
            w.Open();
            foreach (var f in fieldEntries)
                w.Line($"{{ \"{f.Name}\", \"{f.TypeName}\", {f.Offset}, {f.IsRef}, {f.Addr}, {f.Flags} }},");
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
        var nestedEntries = (allTypes ?? Enumerable.Empty<TypeDef>())
            .Where(x => x.IsNested && x.DeclaringTypeName == ReflectionFullName(t))
            .Select(ReflectionFullName)
            .ToList();
        if (nestedEntries.Count > 0)
        {
            w.Line($"static const char* {TypeCppName(t)}_typeinfo_nested[] = {{");
            w.Open();
            foreach (var n in nestedEntries)
                w.Line($"    \"{n}\",");
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
        var klassExpr = !HasClassDescriptor(t) ? "nullptr" : $"&{TypeCppName(t)}_class";
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
