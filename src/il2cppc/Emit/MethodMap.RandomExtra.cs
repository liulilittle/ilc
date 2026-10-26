namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.RandomExtra -- System.Random gap gate (randomextra gap-fill).
// Owners: System.Random (randomextra gap-fill)
//
// The runtime surface (il2cpp_random.h) is FULLY consumed by
// MethodMap.Random.cs (7/7 icalls wired: both ctors, all three Next
// overloads, NextDouble, NextBytes). Sample() is the one remaining net48
// member reachable from user code (a derived Random subclass calls the
// protected sample hook); it maps to the same icall as NextDouble, which
// IS net48's Sample. The private helpers (InternalSample /
// GetSampleForLargeRange) live entirely inside mscorlib and never reach
// the transpiler. Unknown members return null (the documented fallback to
// the generated-name path).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapRandomExtra(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "Sample":
                // net48 Random.Sample() is InternalSample() * (1 / Int32.MaxValue)
                // -- exactly the runtime's Sample helper, which
                // icall_random_next_double returns. A derived-class override is
                // not honored (the runtime calls the base Sample directly -- the
                // documented NextDouble limitation).
                return I("il2cpp::runtime::icall_random_next_double", 0, true, false, true, null, "d");
            default: return null;
        }
    }
}
