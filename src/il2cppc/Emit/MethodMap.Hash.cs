namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Hash -- System.Security.Cryptography MD5/SHA1/SHA256 wiring
// (md5.h / sha1.h / sha256.h).
// Owners: System.Security.Cryptography.MD5 / SHA1 / SHA256 / HashAlgorithm
//
// OBJECT-GAP SIMPLIFICATION (crypto-domain.md §2.1, T1 decision): the
// runtime has NO hash OBJECT layer -- only the pure three-phase contexts
// plus the one-shot hash_bytes family. So:
//   * Create()  -- static factory -- folds to NULL (::null::) carrying the
//     concrete algorithm type on the NullExpr (ReturnTypeName). Same
//     null-singleton pattern as the Encoding domain getters (get_UTF8
//     etc.); the next callvirt (MD5.Create().ComputeHash(...)) re-resolves
//     with the receiver hint and dispatches the algorithm. The explicit
//     static mapping also FIXES the StackFold static/instance
//     misclassification that used to hit the generated-name fallback
//     (MD5.Create() -> INSTANCE, "pops this but the stack is empty").
//   * ComputeHash(byte[]) -- instance one-shot -- maps DIRECTLY to the
//     hash_bytes icall with the RECEIVER DROPPED ("drop" cast): the
//     mapping ignores the instance (no per-instance state exists in the
//     carrier model). The byte[] argument carries the "a" cast
//     (Il2CppObject* -> Il2CppArray*) and the ARRAY-TAKING runtime
//     overload extracts data/length itself -- the emitter has no cast code
//     for an array data pointer + length, so the raw-buffer (ptr, len)
//     overloads stay for the C++ tests and the array forms are the icall
//     shapes. Returns byte[] (ReturnTypeName "System.Byte[]" -- the
//     ldlen/ldelem folds on the result resolve downstream).
//
// Callvirt shape: csc emits ComputeHash with the DECLARED parent
// System.Security.Cryptography.HashAlgorithm (virtual, inherited), so the
// BCL dispatch reaches this map under the HashAlgorithm owner with the
// algorithm arriving in receiverType (the folded Create() null's TypeName).
// The first resolution (receiverType == null) defers (returns null) to the
// receiver-aware re-resolve -- the Encoding.GetBytes deferral precedent.
//
// LOUD rejections (known-but-unwireable members must not be silent):
//   * ComputeHash(byte[], int, int) -- the range overload needs an
//     (array, offset, count) runtime wrapper that does not ship (T1 scope:
//     single-array form only).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapHash(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "Create":
                // Static factory (0 params) -> null-singleton fold. The
                // runtime has no hash object to allocate and the
                // ComputeHash mapping drops the receiver -- the result is
                // never dereferenced. ReturnTypeName carries the concrete
                // algorithm (owner IS the algorithm for Create).
                return new ResolvedMethod(ResolvedMethod.NullMarker, 0, false, true, true, Array.Empty<bool>(), false,
                    ReturnTypeName: owner);
            case "ComputeHash":
                if (count != 1 || ps[0] != 0x1D)
                    throw new NotSupportedException(
                        $"ComputeHash overload (arity {count}, param 0x{(count > 0 ? ps[0] : 0):X2}) not wired in T1 " +
                        "(allocating single-byte[] form only)");
                // First resolution has no receiver hint yet -- defer to the
                // receiver-aware re-resolve (Encoding.GetBytes precedent).
                if (receiverType == null) return null;
                var algo = owner == "System.Security.Cryptography.HashAlgorithm" ? receiverType : owner;
                return algo switch
                {
                    "System.Security.Cryptography.MD5" => HashIcall("md5"),
                    "System.Security.Cryptography.SHA1" => HashIcall("sha1"),
                    "System.Security.Cryptography.SHA256" => HashIcall("sha256"),
                    _ => throw new NotSupportedException(
                        $"ComputeHash on receiver '{receiverType}' not wired in T1 (MD5/SHA1/SHA256 only)"),
                };
            default: return null;
        }
    }

    private static ResolvedMethod HashIcall(string algo)
    {
        // sha1.h lives in namespace il2cpp (crypto-domain.md §2.1 alignment
        // item 1 -- the pure functions keep their file-local namespace);
        // md5/sha256 are il2cpp::runtime.
        var cpp = algo switch
        {
            "md5" => "il2cpp::runtime::il2cpp_md5_hash_bytes",
            "sha1" => "il2cpp::il2cpp_sha1_hash_bytes",
            _ => "il2cpp::runtime::il2cpp_sha256_hash_bytes",
        };
        return I(cpp, 1, true, true, true, new[] { "drop", "a" }, returnTypeName: "System.Byte[]");
    }
}
