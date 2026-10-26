namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Version -- System.Version mappings
// (src/runtime/include/internal/versionuri.h -- il2cpp_version_* entry points).
// Owners: System.Version
//
// RUNTIME OBJECT CLOSURE (M6 G6): System.Version is now a managed object --
// the runtime defines the System_Version_class descriptor (Il2CppVersionObject:
// header + component count + 4 int32 components, versionuri.h) and the object
// icalls below own the component storage. Generated newobj allocates through
// il2cpp_object_new(&System_Version_class) -- the BclAllocExpr fallback in
// CodeGenerator.cs already emits exactly this mangled global symbol for
// System.Version, so NO BclAllocExpr entry is required for the object to
// allocate (the "System.Version" BclAllocExpr entry is a consistency
// convenience only).
//
//   .ctor(string)            -> icall_version_ctor(Il2CppObject*, Il2CppObject*)
//                               (parse + store; malformed -> ArgumentException)
//   .ctor()                  -> icall_version_ctor_default (0.0)
//   .ctor(int,int[,int[,int]])-> icall_version_ctor_2/3/4 (negative -> AOOoR)
//   get_Major/Minor/Build/Revision -> icall_version_get_*(Il2CppObject*) -> int32
//                               (absent component -> -1)
//   ToString()               -> icall_version_to_string(Il2CppObject*) -> string
//   CompareTo(object)        -> icall_version_compare(Il2CppObject*, Il2CppObject*)
//   Equals(object)           -> icall_version_equals (bool)
//   op_Equality/Inequality   -> icall_version_op_equality / op_inequality (bool)
//   Parse(string)            -> icall_version_parse (static, returns Version)
//
// Members without an object icall (GetHashCode, the int-string ctor form,
// ...) stay LOUD -- wiring manual convention, MethodRefResolver.cs: "known
// but unwireable members MUST throw".
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapVersion(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // Version() -> 0.0 default; Version(string) parses+stores.
                if (count == 0) return I("il2cpp::runtime::icall_version_ctor_default", 0, false, false, true);
                if (count == 1 && ps[0] == 0x0E) return I("il2cpp::runtime::icall_version_ctor", 1, false, false, true);
                // Version(int, int[, int, int]) -> component ctors.
                if (count == 2 && ps[0] == 0x08 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_version_ctor_2", 2, false, false, true, new[] { "", "i", "i" });
                if (count == 3 && ps[0] == 0x08 && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_version_ctor_3", 3, false, false, true, new[] { "", "i", "i", "i" });
                if (count == 4)
                    return I("il2cpp::runtime::icall_version_ctor_4", 4, false, false, true, new[] { "", "i", "i", "i", "i" });
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): Version ctor arity {count} -- net48 has only Version(), " +
                    "Version(string), Version(int,int), Version(int,int,int) and Version(int,int,int,int) " +
                    "[Reference Source version.cs:103,91,79,62,42]");
            case "get_Major":
                return I("il2cpp::runtime::icall_version_get_major", 0, true, false, true);
            case "get_Minor":
                return I("il2cpp::runtime::icall_version_get_minor", 0, true, false, true);
            case "get_Build":
                return I("il2cpp::runtime::icall_version_get_build", 0, true, false, true);
            case "get_Revision":
                return I("il2cpp::runtime::icall_version_get_revision", 0, true, false, true);
            case "ToString":
                if (count == 0) return I("il2cpp::runtime::icall_version_to_string", 0, true, true, true);
                // Version.ToString(int fieldCount) (net48 version.cs:284):
                // rebuilds the first 'fieldCount' components; the runtime icall
                // throws ArgumentException when a requested component is absent
                // (build/revision == -1) or the count is out of range.
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_version_to_string_field", 1, true, true, true,
                        new[] { "", "i" });
                return null;
            case "CompareTo":
                if (count == 1 && ps[0] == 0x12)
                    return I("il2cpp::runtime::icall_version_compare", 1, true, false, true);
                return null;
            case "Equals":
                if (count == 1 && ps[0] == 0x12)
                    return I("il2cpp::runtime::icall_version_equals", 1, true, false, true);
                return null;
            case "Parse":
                // Static: allocates a Version object through System_Version_class.
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_version_parse", 1, true, true, false,
                        returnTypeName: "System.Version");
                return null;
            case "op_Equality":
                if (count == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_version_op_equality", 2, true, false, false);
                return null;
            case "op_Inequality":
                if (count == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_version_op_inequality", 2, true, false, false);
                return null;
            case "op_LessThan":
                if (count == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_version_op_less_than", 2, true, false, false);
                return null;
            case "op_GreaterThan":
                if (count == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_version_op_greater_than", 2, true, false, false);
                return null;
            case "op_LessThanOrEqual":
                if (count == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_version_op_less_or_equal", 2, true, false, false);
                return null;
            case "op_GreaterThanOrEqual":
                if (count == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_version_op_greater_or_equal", 2, true, false, false);
                return null;
            case "get_MajorRevision":
                return I("il2cpp::runtime::icall_version_get_major_revision", 0, true, false, true);
            case "get_MinorRevision":
                return I("il2cpp::runtime::icall_version_get_minor_revision", 0, true, false, true);
            case "GetHashCode":
                return I("il2cpp::runtime::icall_version_get_hash_code", 0, true, false, true);
            default:
                throw new NotSupportedException(
                    "LOUD whitelist (no runtime descriptor/model): System.Version member not wired -- the versionuri.h object " +
                    "icalls cover .ctor/get_Major/get_Minor/get_Build/get_Revision/ToString/ToString(int)/CompareTo/Equals/Parse/" +
                    "op_Equality/op_Inequality only [Reference Source version.cs:42-263]");
        }
    }
}
