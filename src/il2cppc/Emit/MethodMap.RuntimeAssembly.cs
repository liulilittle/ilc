namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.RuntimeAssembly -- System.Reflection.RuntimeAssembly QCall stubs
// (stubs.cpp PNSE icalls; dynamic module emission and the assembly version
// table have no runtime model -- AOT static compilation).
// Owners: System.Reflection.RuntimeAssembly
//
//   LoadModule (7-arg QCall shape) -> icall_assembly_loadmodule_unsupported
//     (all args dropped; the call is a statement -- callers do not consume
//     a return).
//   GetVersion (5-arg QCall shape) -> icall_assembly_get_version_unsupported
//     (all args dropped; statement position).
// Managed overloads (GetVersion(int), LoadModule 4-param, ...) keep their
// bodies, which route into these stubs and throw transitively.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapRuntimeAssembly(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (owner != "System.Reflection.RuntimeAssembly") return null;
        switch (name)
        {
            case "LoadModule":
                if (count == 7)
                    return I("il2cpp::runtime::icall_assembly_loadmodule_unsupported", 7, false, false, false,
                        new[] { "drop", "drop", "drop", "drop", "drop", "drop", "drop" });
                return null;
            case "GetVersion":
                if (count == 5)
                    return I("il2cpp::runtime::icall_assembly_get_version_unsupported", 5, false, false, false,
                        new[] { "drop", "drop", "drop", "drop", "drop" });
                return null;
            default: return null;
        }
    }
}
