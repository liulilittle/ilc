namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Random -- System.Random icall mappings.
// Owners: System.Random
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapRandom(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                if (count == 0) return I("il2cpp::runtime::icall_random_ctor_default", 0, false, false, true);
                return I("il2cpp::runtime::icall_random_ctor", 1, false, false, true, new[] { "", "i" });
            case "Next":
                if (count == 0) return I("il2cpp::runtime::icall_random_next", 0, true, false, true);
                if (count == 1) return I("il2cpp::runtime::icall_random_next_max", 1, true, false, true);
                return I("il2cpp::runtime::icall_random_next_minmax", 2, true, false, true);
            case "NextDouble": return I("il2cpp::runtime::icall_random_next_double", 0, true, false, true, null, "d");
            case "NextBytes": return I("il2cpp::runtime::icall_random_next_bytes", 1, false, false, true);
            default: return null;
        }
    }
}
