namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.RandomExtra -- System.Random gap gate (randomextra gap-fill).
// Owners: System.Random (randomextra gap-fill)
//
// The runtime surface (il2cpp_random.h) is FULLY consumed by
// MethodMap.Random.cs (7/7 icalls wired: both ctors, all three Next
// overloads, NextDouble, NextBytes). This file carries NO new icall
// mappings -- it is the LOUD rejection gate for the remaining net48
// System.Random members that have no runtime implementation. Only
// Sample() is reachable from user code (a derived Random subclass calls
// the protected sample hook); the private helpers (InternalSample /
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
                throw new NotSupportedException(
                    $"System.Random.Sample not wired: the runtime has no icall_random_sample -- derived Random subclasses overriding the sample hook are unsupported");
            default: return null;
        }
    }
}
