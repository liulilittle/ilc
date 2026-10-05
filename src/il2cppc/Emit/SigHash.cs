namespace Il2Cpp.Transpiler.Emit;

public static class SigHash
{
    public static string Hash(string signature)
    {
        // FNV-1a 64-bit, stable across processes/versions (avoids string.GetHashCode's randomization)
        ulong hash = 14695981039346656037UL;
        foreach (var c in signature)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16");
    }

    // BUG-2 (batch 177b): the same FNV-1a digest rendered as a uint64_t C++
    // literal ("<decimal>ULL") -- the delegate-type ID the transpiler stamps
    // onto every delegate at creation (il2cpp_delegate_new's typeId arg). The
    // runtime compares these IDs at Combine time; two delegates of the SAME
    // type always hash identically, DIFFERENT types (Func<int,int> vs
    // Action<int>) collide only at 2^-64 probability.
    public static string UInt64Literal(string s)
    {
        ulong hash = 14695981039346656037UL;
        foreach (var c in s)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash.ToString() + "ULL";
    }
}
