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
//     The instance is a flat grid of 8-byte pointer slots. Slot 0 is the
//     object header (Il2CppObject._klass, the type descriptor pointer). Each
//     instance field occupies exactly one slot in metadata declaration order
//     (slots 1..N). An Int32 field uses only the low 4 bytes of its slot (the
//     high 4 stay zero from allocation zero-init); a Reference field uses the
//     whole slot. This is deliberately simpler than the CLR's alignment rules
//     (M3) and keeps the reference-field bitmap exact: bit i is set exactly
//     when slot i is a GC reference, so the collector scans slot-wise with no
//     ambiguity. InstanceSize = (slotCount) * 8, always >= sizeof(Il2CppObject).
//
//   Struct emission (EmitClassStruct)
//     struct {Ns}_{Type} {           // C++ name via TypeCppName
//         Il2CppObject _obj;         // embedded object header, offset 0
//         int32_t     field;         // value fields: int32_t at slot n*8
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
    // ExpandNestedTypes -- nested types → independent top-level emission entries.
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
    // (throw_MyException2 → "MyException2,MyException2,MyException,...") is
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
            var fullName = name;
            if (types.Concat(added).Any(t => t.FullName == fullName))
            {
                var enclosing = td.GetDeclaringType().IsNil
                    ? "?"
                    : md.GetFullName(td.GetDeclaringType());
                throw new NotSupportedException(
                    $"nested type '{enclosing}+{name}' expands to '{fullName}', which already exists in the translated set -- nested types emit under their SHORT metadata name (reference-point convention), so '{fullName}' would produce duplicate C++ symbols");
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
        var byName = types.ToDictionary(t => t.FullName);
        foreach (var t in added) byName[t.FullName] = t;
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
            _ => throw new NotSupportedException($"base type handle kind {baseType.Kind} not supported in M2"),
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
        var typeByte = sig.ReadByte();
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
            0x11 => throw new NotSupportedException("value-type instance fields not supported in M2"),
            // batch 156a (asynccase): GENERICINST (0x15) VALUETYPE fields on
            // a NESTED state-machine struct -- AsyncTaskMethodBuilder`1<T>
            // and TaskAwaiter`1<T> ride as a SINGLE reference slot
            // (Il2CppObject*): the minimal async surface models the builder
            // and the awaiter as runtime objects (the builder wraps the
            // task+result; the awaiter IS the task). Mirrors the top-level
            // static decode (MetadataLoader 0x15 -> Reference).
            0x15 => (FieldTypeKind.Reference, GenericNestedInstantiationName(md, ref sig)),
            var b => throw new NotSupportedException($"field type byte 0x{b:X2} not supported in M2"),
        };
    }

    private static string ResolveNestedTypeName(MetadataReader md, EntityHandle handle) =>
        handle.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)handle),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)handle),
            _ => throw new NotSupportedException($"type handle kind {handle.Kind} not supported in M2"),
        };

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
            0x11 or 0x12 => ResolveNestedTypeName(md, sig.ReadTypeHandle()),
            // batch 165b: SZARRAY generic argument (Task<int[]> -- the
            // <AF>d__3 awaiter field signature carries TaskAwaiter`1<int[]>):
            // the element type follows the 0x1D/0x1F byte (recursive).
            0x1D or 0x1F => NestedTypeArgName(md, ref sig) + "[]",
            var other => throw new NotSupportedException($"generic instantiation argument byte 0x{other:X2} not supported in M2"),
        };
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
        var baseName = Sanitize(t.FullName);
        // M3 generics (Task 7): a generic definition emits ONE instantiation
        // with T -> int32_t. The instantiation name appends "_int" (the
        // value-type specialization marker; iron rule 2g -- no C++ templates).
        return t.GenericArity > 0 ? baseName + "_int" : baseName;
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

    // Public for CodeGenerator reuse (the field path → C++ storage name needs the
    // same sanitize rule). refl2 S2/G3: '=' is an invalid C++ identifier
    // character that the compiler-generated array-initializer struct
    // (<PrivateImplementationDetails>.__StaticArrayInitTypeSize=N) carries in
    // its short metadata name -- replacing it with '_' makes such types emit a
    // VALID C++ name (their TypeInfo table/extern/registration now compile;
    // the old whole-type exclusion is removed). No valid C# identifier contains
    // '=', so no other generated name changes.
    public static string Sanitize(string s) => string.Join("_", s.Split('.', '+', '`', '<', '>', ',', '[', ']', '='));

    // Static field C++ file-scope name: {TypeCppName}_sf_{fieldName}. The file-scope
    // static guarantees uniqueness within the type.
    public static string FieldCppName(TypeDef t, string fieldName) => $"{TypeCppName(t)}_sf_{Sanitize(fieldName)}";

    //--------------------------------------------------------------------------
    // LayoutInstanceFields -- assign slots / instanceSize / refFieldBitmap.
    // Slot 0 is the object header; each instance field gets the next slot in
    // declaration order. Bit i of the bitmap marks slot i when the field is a
    // Reference. InstanceSize = slots * sizeof(void*) (8-byte grid).
    //--------------------------------------------------------------------------
    public static void LayoutInstanceFields(TypeDef t)
    {
        if (t.InstanceSize != 0) return; // idempotent: already laid out
        uint bitmap = 0;
        int refCount = 0;
        // Slot 0 = object header for reference types; value types have NO
        // header (M3 Task 11) -- their slots start at 0.
        int slot = t.IsValueType ? 0 : 1;
        foreach (var f in t.InstanceFields)
        {
            f.SlotIndex = slot;
            if (f.TypeKind == FieldTypeKind.Reference)
            {
                // Audit H6: the 32-bit refFieldBitmap can only express slots
                // 0..31 (1u << slot is UB for slot >= 32 and the collector
                // scans slot < 32). A reference field beyond that would be
                // silently omitted from the GC scan -- objects reachable only
                // through it would be collected. Reject loudly instead.
                if (slot >= 32)
                    throw new NotSupportedException(
                        $"type {t.FullName}: reference field '{f.Name}' lands on slot {slot} -- the 32-bit refFieldBitmap cannot represent slots >= 32 (audit H6)");
                bitmap |= 1u << slot;
                ++refCount;
            }
            ++slot;
        }
        t.RefFieldBitmap = bitmap;
        t.RefFieldCount = refCount;
        t.InstanceSize = slot * IntPtr.Size; // whole 8-byte grid (IntPtr.Size = pointer size)
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
        w.Line($"// Slot grid: slot i at byte offset i*8; refFieldBitmap = 0b{Convert.ToString(t.RefFieldBitmap, 2).PadLeft(t.InstanceSize / 8, '0')}.");
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
        foreach (var f in t.InstanceFields)
        {
            var cppType = f.TypeKind switch
            {
                FieldTypeKind.Int32 => "int32_t",
                // wfm1 (winforms1): IntPtr/UIntPtr struct fields emit as the
                // full-width intptr_t carrier -- the native pointer type.
                FieldTypeKind.NativeInt => "intptr_t",
                FieldTypeKind.Reference => ReferenceCppType(f.FieldTypeName, allTypes),
                _ => throw new NotSupportedException($"field kind {f.TypeKind} not supported in M2"),
            };
            w.Line($"{cppType} {Sanitize(f.Name)}; // slot {f.SlotIndex} at offset {f.SlotIndex * IntPtr.Size} ({(f.TypeKind == FieldTypeKind.Reference ? "GC reference" : "value")})");
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
    private static string ReferenceCppType(string fullName, IReadOnlyList<TypeDef> allTypes) => fullName switch
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
        // types = sizeof(struct).
        var sizeExpr = t.IsInterface ? "0" : $"static_cast<int32_t>(sizeof({TypeCppName(t)}))";
        w.Line($"Il2CppClass {TypeCppName(t)}_class = {{");
        w.Line($"    \"{t.FullName}\",                                        // name");
        w.Line($"    {sizeExpr},                                            // instanceSize");
        w.Line($"    0b{Convert.ToString(t.RefFieldBitmap, 2).PadLeft(Math.Max(t.InstanceSize, 1) / IntPtr.Size, '0')}u,          // refFieldBitmap: bit i <=> slot i is a GC reference");
        w.Line($"    {t.RefFieldCount},                                        // refFieldCount");
        // Audit H7: wire the resolved user base class -- il2cpp_is_instance
        // walks baseClass and il2cpp_interface_dispatch scans the base chain
        // for inherited interfaces. System.Object roots stay nullptr.
        var baseClassRef = t.BaseType != null
            ? $"&{TypeCppName(t.BaseType)}_class"
            : "nullptr";
        w.Line($"    {baseClassRef},                                                // baseClass ({(t.BaseType != null ? "user inheritance: " + t.BaseType.FullName : "null (System.Object root)")})");
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
    private enum StaticFieldSemantic { Value, Reference, Decimal, IntPtr, UIntPtr, Int64, UInt64 }

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
        _ => null,
    };

    private static (StaticFieldSemantic Semantic, string TypeName) StaticFieldSemantics(FieldDef f, MetadataReader? md)
    {
        if (f.TypeKind == FieldTypeKind.Reference)
            return (StaticFieldSemantic.Reference, string.IsNullOrEmpty(f.FieldTypeName) ? "System.Object" : f.FieldTypeName);
        if (f.TypeKind == FieldTypeKind.NativeInt)
            return (StaticFieldSemantic.IntPtr, string.IsNullOrEmpty(f.FieldTypeName) ? "System.IntPtr" : f.FieldTypeName);
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
                if (md.GetFullName(tdh) != f.Owner.FullName) continue;
                var td = md.GetTypeDefinition(tdh);
                foreach (var fh in td.GetFields())
                {
                    var fd = md.GetFieldDefinition(fh);
                    if (md.GetString(fd.Name) != f.Name) continue;
                    var sig = md.GetBlobReader(fd.Signature);
                    if (sig.ReadByte() != 0x06) return (StaticFieldSemantic.Value, "System.Int32");
                    var typeByte = sig.ReadByte();
                    // Reference kinds (mirrors CodeGenerator.StaticFieldIsReference):
                    // 0x0E String, 0x1C Object, 0x12 Class, 0x15 GenericInst
                    // (Func`N etc.), 0x1D/0x1F SZARRAY, 0x14 ARRAY. 0x15 is
                    // followed by the 0x12 class marker + the TypeDefOrRef.
                    if (typeByte is 0x0E) return (StaticFieldSemantic.Reference, "System.String");
                    if (typeByte is 0x1C) return (StaticFieldSemantic.Reference, "System.Object");
                    if (typeByte is 0x12) return (StaticFieldSemantic.Reference, ResolveNestedTypeName(md, sig.ReadTypeHandle()));
                    if (typeByte is 0x15) { sig.ReadByte(); return (StaticFieldSemantic.Reference, ResolveNestedTypeName(md, sig.ReadTypeHandle())); }
                    if (typeByte is 0x1D or 0x1F or 0x14) return (StaticFieldSemantic.Reference, "System.Array");
                    // wf16 (winforms1): native-int static fields (0x18/0x19)
                    // and 64-bit integer statics (0x0A/0x0B) -- the M1 int32_t
                    // slot truncated the 64-bit WNDPROC/HWND pointer and long
                    // values on x64. Same classification as the model arm.
                    if (typeByte is 0x18) return (StaticFieldSemantic.IntPtr, "System.IntPtr");
                    if (typeByte is 0x19) return (StaticFieldSemantic.UIntPtr, "System.UIntPtr");
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
            StaticFieldSemantic.Int64 => "int64_t",
            StaticFieldSemantic.UInt64 => "uint64_t",
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
    // (reflection-runtime-design.md §2 + typeinfo.h).
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
    //   * fields: instance -> byte offset (SlotIndex * pointer size) + isRef;
    //     static -> offset -1 + staticAddr = &{FieldCppName} (non-literal);
    //     flags bit1 static bit2 literal. FieldDef has NO IsPublic (model
    //     gap, mirror of the M1 static-field classification gap) -- the
    //     public bit stays 0 here.
    //   * baseType: resolved user base -> &{Base}_typeinfo; System.Object
    //     roots -> nullptr. interfaces: resolved InterfaceTypes -> pointer
    //     array. properties: PropertyDef model not landed -- the property
    //     sub-table is emitted EMPTY (nullptr / 0) until properties decode
    //     lands (design §2.1 structure is ready).
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
        IReadOnlySet<string>? foldedAccessorNames = null)
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
        var fieldEntries = t.InstanceFields
            .Select(f => (f.Name, TypeName: f.TypeKind == FieldTypeKind.Reference ? f.FieldTypeName : "System.Int32",
                Offset: f.SlotIndex * IntPtr.Size, IsRef: f.TypeKind == FieldTypeKind.Reference ? 1 : 0,
                Addr: "nullptr", Flags: 0))
            .Concat(t.StaticFields.Select(f =>
            {
                var (semantic, typeName) = StaticFieldSemantics(f, md);
                return (f.Name, TypeName: typeName,
                    Offset: -1, IsRef: semantic == StaticFieldSemantic.Reference ? 1 : 0,
                    Addr: f.IsLiteral ? "nullptr" : $"&{FieldCppName(t, f.Name)}",
                    Flags: (f.IsStatic ? 2 : 0) | (f.IsLiteral ? 4 : 0));
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

        // Main table.
        var methodsTable = t.Methods.Count > 0 ? $"{TypeCppName(t)}_typeinfo_methods" : "nullptr";
        var fieldsTable = fieldEntries.Count > 0 ? $"{TypeCppName(t)}_typeinfo_fields" : "nullptr";
        var propertiesTable = propertyEntries.Count > 0 ? $"{TypeCppName(t)}_typeinfo_properties" : "nullptr";
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
    private static string MethodSymbolName(TypeDef t, MethodDef m)
        => $"{TypeCppName(t)}_{Sanitize(m.Name)}_{SigHash.Hash(t.FullName + "." + m.Name + "(" + m.ParamTypes + ")")}";

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
            _ => throw new NotSupportedException(
                $"typeinfo: method return type {returnType} not representable in Il2CppMethodInfo.signature -- the MethodDef model carries no full type name for this return (model gap, mirror of the M1 static-field classification gap)"),
        };
    }

    //--------------------------------------------------------------------------
    // FilterReflectionManifest -- mode B manifest type filtering (reflection-runtime-design.md §4).
    //
    // Mode B generation rule: user reflection manifest → generate reflection metadata only
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
    // (reflection-runtime-design.md §5.1: Il2CppClass untouched, only TypeInfo is trimmed).
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
