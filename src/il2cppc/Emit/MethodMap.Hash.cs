namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Hash -- the ORIGINAL T1 System.Security.Cryptography hash mapping.
// Owners: System.Security.Cryptography.MD5 / SHA1 / SHA256 / HashAlgorithm.
//
// This mapper is SUPERSEDED and is no longer on the dispatch path: every hash
// owner routes to TryMapCrypto (the T4 object layer, MethodMap.Crypto.cs),
// which implements the full net48 surface -- Create, ComputeHash(byte[]),
// ComputeHash(byte[],int,int) [hashalgorithm.cs:91] and ComputeHash(Stream)
// [hashalgorithm.cs:57] -- for MD5/SHA1/SHA256/SHA384/SHA512/HMAC. Delegating
// here keeps the superseded entry point from LOUD-rejecting overloads the
// object layer supports, so the two mappings can never drift.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapHash(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        return TryMapCrypto(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
    }
}
