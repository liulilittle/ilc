namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Crypto -- System.Security.Cryptography OBJECT-layer wiring (T4,
// crypto-domain.md section 2.1 object model path 1 + section 4).
// (src/runtime/include/internal/crypto_obj.h + src/runtime/src/crypto_obj.cpp)
//
// SUPERSEDES the T1 one-shot mapping (MethodMap.Hash.cs): instead of
// null-folding Create() and dropping the receiver on hash_bytes, the T4
// object layer allocates REAL instances through the runtime GLOBAL class
// descriptors and streams ComputeHash through the object's inline context:
//
//   <Hash>.Create()                    -> icall_<algo>_create() (static,
//     allocates the object + primes the context; ReturnTypeName carries the
//     concrete algorithm for the next callvirt's receiver hint)
//   <Hash>.ComputeHash(byte[])         -> icall_<algo>_compute_hash(self,
//     byte[]) -- create+update+final on the object's inline ctx, returns a
//     NEW byte[] (streaming; repeated calls are independent -- net48)
//   <Hash>.ComputeHash(byte[],int,int) -> icall_<algo>_compute_hash_range
//   <Hash>.Dispose()                   -> icall_<algo>_dispose (no-op)
//   new <*CryptoServiceProvider/Managed>() -> il2cpp_object_new(&..._class)
//     via the BclAllocExpr default fallback (CodeGenerator.cs NewObjExpr) +
//     icall_<algo>_ctor -- the legacy owners normalize onto the SAME icall
//     family (crypto-domain.md section 4.4)
//   new HMACSHAx(byte[] key)           -> il2cpp_object_new(&System_Security_
//     Cryptography_HMACSHAx_class) + icall_hmac_ctor (key stored as a GC ref)
//   HMACSHAx.ComputeHash(byte[])       -> icall_hmac_compute_hash (the
//     algorithm is dispatched from the receiver's class)
//   Aes.Create() / set_Key/get_Key/set_IV/get_IV / CreateEncryptor/
//     CreateDecryptor(key, iv) / TransformFinalBlock(byte[],int,int) /
//     GenerateKey/GenerateIV / Dispose  -> icall_aes_* family
//   new RNGCryptoServiceProvider() / GetBytes / GetNonZeroBytes /
//     RandomNumberGenerator.Fill(byte[]) -> icall_rng_* family
//
// The compute_hash receiver is PASSED ("", "a" casts -- not dropped): the
// object carries the state. The byte[] argument keeps the "a" cast
// (Il2CppObject* -> Il2CppArray*); the runtime extracts the payload.
//
// LOUD rejections (known-but-unwireable members must not be silent, the
// MethodMap manual convention): HashAlgorithm.Create (CryptoConfig). The hash
// family's Hash / Clear / Dispose / Initialize / ComputeHash(Stream) /
// TransformBlock / TransformFinalBlock, the Aes parameterless
// CreateEncryptor()/CreateDecryptor(), the Aes TransformBlock/TransformFinalBlock
// streaming surface and the RNG string/byte[]/CspParameters ctors are wired.
// Everything else falls back to the generated-name path (return null).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapCrypto(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        // The callvirt shape (csc declares ComputeHash/Dispose on the BCL
        // PARENT -- HashAlgorithm / KeyedHashAlgorithm -- with the concrete
        // algorithm arriving in receiverType, the Create() result's
        // ReturnTypeName): the parent owners need the receiver hint, the
        // concrete owners map directly (the T1 deferral precedent). A null
        // algo means the call is not (yet) pinned to a concrete algorithm:
        // defer (return null) to the receiver-aware re-resolve.
        var algo = CryptoAlgo(owner, receiverType);

        switch (name)
        {
            case "Create":
                if (count != 0)
                    throw new NotSupportedException(
                        "CryptoConfig by-name factories (HashAlgorithm.Create(string)) are not wired " +
                        "(net48 depends on machine-level crypto config)");
                if (algo == null)
                    throw new NotSupportedException($"Create() on owner '{owner}' not wired (concrete algorithm types only)");
                if (algo == "aes")
                    return I("il2cpp::runtime::icall_aes_create", 0, true, true, false,
                        returnTypeName: "System.Security.Cryptography.Aes");
                if (algo.StartsWith("hmac-", StringComparison.Ordinal) || algo == "rng")
                    throw new NotSupportedException($"Create() on '{algo}' not wired (use new + ctor instead)");
                return I($"il2cpp::runtime::icall_{algo}_create", 0, true, true, false,
                    returnTypeName: CryptoAlgoTypeName(algo));

            case ".ctor":
                return CryptoCtor(algo, ps, count);

            case "ComputeHash":
                if (algo == null) return null; // defer to the receiver-aware re-resolve
                return CryptoComputeHash(algo, ps, count);

            case "Dispose":
                if (algo == null) return null;
                return algo switch
                {
                    "aes" => I("il2cpp::runtime::icall_aes_dispose", 0, false, false, true),
                    "rng" => I("il2cpp::runtime::icall_rng_dispose", 0, false, false, true),
                    var h when h.StartsWith("hmac-", StringComparison.Ordinal) =>
                        I("il2cpp::runtime::icall_hmac_dispose", 0, false, false, true),
                    _ => I($"il2cpp::runtime::icall_{algo}_dispose", 0, false, false, true),
                };

            case "TransformBlock":
                // The AES transform's streaming entry point arrives under the
                // ICryptoTransform INTERFACE owner (CreateEncryptor's result is
                // typed ICryptoTransform in user code). net48 v2 semantics
                // (RijndaelManagedTransform): inputCount is a positive multiple
                // of the block size, encryption chains every block, decryption
                // buffers the last block for TransformFinalBlock.
                if (owner == "System.Security.Cryptography.ICryptoTransform")
                {
                    if (count == 5 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08
                        && ps[3] == 0x1D && ps[4] == 0x08)
                        // ReturnsRef = false: the return is a plain int (the
                        // number of bytes written); a ref-typed result would
                        // break consumers that use the value as an int.
                        return I("il2cpp::runtime::icall_aes_transform_block", 5, true, false, true,
                            new[] { "", "a", "i", "i", "a", "i" }, resultKind: "i");
                    throw new NotSupportedException("Aes incremental block streaming (TransformBlock) overload not wired (v2)");
                }
                // The HASH family implements ICryptoTransform on HashAlgorithm /
                // KeyedHashAlgorithm (hashalgorithm.cs): TransformBlock feeds the
                // inline context, sets State=1, copies the input to the output
                // buffer and returns the input count.
                if (algo == null) return null;
                if (algo.StartsWith("hmac-", StringComparison.Ordinal))
                    return I("il2cpp::runtime::icall_hmac_transform_block", 5, true, true, true,
                        new[] { "", "a", "i", "i", "a", "i" }, resultKind: "i");
                if (algo is "md5" or "sha1" or "sha256" or "sha384" or "sha512")
                    return I($"il2cpp::runtime::icall_{algo}_transform_block", 5, true, true, true,
                        new[] { "", "a", "i", "i", "a", "i" }, resultKind: "i");
                throw new NotSupportedException($"HashAlgorithm.TransformBlock on '{algo}' not wired");

            case "TransformFinalBlock":
                if (owner == "System.Security.Cryptography.ICryptoTransform")
                {
                    if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                        return I("il2cpp::runtime::icall_aes_transform_final_block", 3, true, true, true,
                            new[] { "", "a", "i", "i" }, returnTypeName: "System.Byte[]");
                    throw new NotSupportedException("Aes incremental block streaming (TransformFinalBlock) is not wired (v2)");
                }
                if (algo == null) return null;
                if (algo.StartsWith("hmac-", StringComparison.Ordinal))
                    return I("il2cpp::runtime::icall_hmac_transform_final_block", 3, true, true, true,
                        new[] { "", "a", "i", "i" }, returnTypeName: "System.Byte[]");
                if (algo is "md5" or "sha1" or "sha256" or "sha384" or "sha512")
                    return I($"il2cpp::runtime::icall_{algo}_transform_final_block", 3, true, true, true,
                        new[] { "", "a", "i", "i" }, returnTypeName: "System.Byte[]");
                throw new NotSupportedException($"HashAlgorithm.TransformFinalBlock on '{algo}' not wired");

            case "get_HashSize":
                // HashAlgorithm.HashSize: the digest size in BITS (net48
                // HashSizeValue). The concrete algorithm comes from the receiver
                // hint (the parent-owner callvirt shape).
                if (algo == null) return null;
                if (algo is "md5" or "sha1" or "sha256" or "sha384" or "sha512")
                    return I($"il2cpp::runtime::icall_{algo}_get_hash_size", 0, true, false, true);
                return null;

            case "get_Hash":
                // HashAlgorithm.Hash: the cached last digest (a CLONE). ODE
                // after Clear()/Dispose(), CryptographicUnexpectedOperationException
                // mid-stream (State != 0), NRE before the first ComputeHash.
                if (algo == null) return null;
                if (algo.StartsWith("hmac-", StringComparison.Ordinal))
                    return I("il2cpp::runtime::icall_hmac_get_hash", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Byte[]");
                if (algo is "md5" or "sha1" or "sha256" or "sha384" or "sha512")
                    return I($"il2cpp::runtime::icall_{algo}_get_hash", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Byte[]");
                return null;

            case "Initialize":
                // HashAlgorithm.Initialize: reset the inline context (net48
                // Initialize -- the tail of every ComputeHash).
                if (algo == null) return null;
                if (algo is "md5" or "sha1" or "sha256" or "sha384" or "sha512")
                    return I($"il2cpp::runtime::icall_{algo}_initialize", 0, false, false, true);
                return null;

            case "Clear":
                // HashAlgorithm.Clear(): (this as IDisposable).Dispose() -- the
                // net48 disposed flag (a later Hash/ComputeHash/Transform* throws
                // ObjectDisposedException). SymmetricAlgorithm.Clear() (Aes)
                // is the same Dispose contract (the CSP handle is closed).
                if (algo == null) return null;
                if (algo.StartsWith("hmac-", StringComparison.Ordinal))
                    return I("il2cpp::runtime::icall_hmac_clear", 0, false, false, true, new[] { "" });
                if (algo == "aes")
                    return I("il2cpp::runtime::icall_aes_dispose", 0, false, false, true, new[] { "" });
                if (algo is "md5" or "sha1" or "sha256" or "sha384" or "sha512")
                    return I($"il2cpp::runtime::icall_{algo}_clear", 0, false, false, true, new[] { "" });
                return null;

            case "set_Key":
            case "get_Key":
            case "set_IV":
            case "get_IV":
                if (algo == null) return null;
                if (algo.StartsWith("hmac-", StringComparison.Ordinal))
                {
                    // HMAC Key property (net48 HMAC.Key / InitializeKey): the
                    // getter clones the stored key; the setter runs
                    // InitializeKey (null -> NRE, > block size -> H(key),
                    // else clone). HMAC has no IV -- a set_IV/get_IV here is
                    // unwireable, not silent.
                    if (name is not ("set_Key" or "get_Key"))
                        throw new NotSupportedException($"HMAC {name} is not wired (HMAC has no IV)");
                    return name == "get_Key"
                        ? I("il2cpp::runtime::icall_hmac_get_key", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Byte[]")
                        : count == 1 && ps[0] == 0x1D
                            ? I("il2cpp::runtime::icall_hmac_set_key", 1, false, false, true, new[] { "", "a" })
                            : throw new NotSupportedException($"HMAC {name} overload (arity {count}) not wired");
                }
                if (algo != "aes") return null;
                return name switch
                {
                    "set_Key" when count == 1 && ps[0] == 0x1D =>
                        I("il2cpp::runtime::icall_aes_set_key", 1, false, false, true, new[] { "", "a" }),
                    "get_Key" when count == 0 =>
                        I("il2cpp::runtime::icall_aes_get_key", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Byte[]"),
                    "set_IV" when count == 1 && ps[0] == 0x1D =>
                        I("il2cpp::runtime::icall_aes_set_iv", 1, false, false, true, new[] { "", "a" }),
                    "get_IV" when count == 0 =>
                        I("il2cpp::runtime::icall_aes_get_iv", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Byte[]"),
                    _ => throw new NotSupportedException($"Aes {name} overload (arity {count}) not wired"),
                };

            case "CreateEncryptor":
            case "CreateDecryptor":
                if (algo != "aes") return null;
                if (count == 2 && ps[0] == 0x1D && ps[1] == 0x1D)
                    return I(name == "CreateEncryptor"
                            ? "il2cpp::runtime::icall_aes_create_encryptor"
                            : "il2cpp::runtime::icall_aes_create_decryptor",
                        2, true, true, true, new[] { "", "a", "a" },
                        returnTypeName: "System.Security.Cryptography.AesTransform");
                // net48 SymmetricAlgorithm.CreateEncryptor()/CreateDecryptor()
                // (parameterless): CreateEncryptor(Key, IV) -- the Key/IV
                // getters generate a fresh key/IV when unset.
                if (count == 0)
                    return I(name == "CreateEncryptor"
                            ? "il2cpp::runtime::icall_aes_create_encryptor_self"
                            : "il2cpp::runtime::icall_aes_create_decryptor_self",
                        0, true, true, true, new[] { "" },
                        returnTypeName: "System.Security.Cryptography.AesTransform");
                throw new NotSupportedException($"Aes {name} overload (arity {count}) not wired");

            case "GenerateKey":
            case "GenerateIV":
                // net48 signature is VOID (GenerateKey()/GenerateIV() mutate the
                // object's context; the key/iv read back through get_Key/
                // get_IV). The runtime icall RETURNS the generated value as a
                // convenience -- the transpiler must match the IL signature
                // (rv=false): a value-returning mapping pushes a phantom temp
                // on a void call, desyncing the fold stack (aescase -- the
                // csc receiver-kept-on-stack Dup pattern then mis-tracks the
                // next receiver as System.Byte[]).
                if (algo != "aes") return null;
                return I(name == "GenerateKey"
                        ? "il2cpp::runtime::icall_aes_generate_key"
                        : "il2cpp::runtime::icall_aes_generate_iv",
                    0, false, false, true, new[] { "" });

            case "GetBytes":
                if (algo != "rng") return null;
                if (count == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_rng_get_bytes", 1, false, false, true, new[] { "", "a" });
                throw new NotSupportedException(
                    $"RNGCryptoServiceProvider.GetBytes overload (arity {count}) not wired (net48 has the byte[] form only)");

            case "GetNonZeroBytes":
                if (algo != "rng") return null;
                if (count == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_rng_get_nonzero_bytes", 1, false, false, true, new[] { "", "a" });
                throw new NotSupportedException(
                    $"RNGCryptoServiceProvider.GetNonZeroBytes overload (arity {count}) not wired (net48 has the byte[] form only)");

            case "Fill":
                if (algo != "rng") return null;
                if (count == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_rng_fill_static", 1, false, false, false, new[] { "a" });
                return null;

            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // Helpers
    //--------------------------------------------------------------------------

    // The concrete algorithm behind a crypto owner, or -- when the call
    // arrived under the BCL parent owner -- behind the receiver hint.
    private static string? CryptoAlgo(string owner, string? receiverType)
    {
        return owner is "System.Security.Cryptography.HashAlgorithm" or "System.Security.Cryptography.KeyedHashAlgorithm"
            or "System.Security.Cryptography.SymmetricAlgorithm"
            ? CryptoAlgoType(receiverType)
            : CryptoAlgoType(owner);
    }

    // Type name -> algorithm tag (the legacy *CryptoServiceProvider / *Managed
    // types normalize onto the same tag -- one icall family per algorithm).
    private static string? CryptoAlgoType(string? type) => type switch
    {
        "System.Security.Cryptography.MD5" or "System.Security.Cryptography.MD5CryptoServiceProvider" => "md5",
        "System.Security.Cryptography.SHA1" or "System.Security.Cryptography.SHA1CryptoServiceProvider"
            or "System.Security.Cryptography.SHA1Managed" => "sha1",
        "System.Security.Cryptography.SHA256" or "System.Security.Cryptography.SHA256CryptoServiceProvider"
            or "System.Security.Cryptography.SHA256Managed" => "sha256",
        "System.Security.Cryptography.SHA384" or "System.Security.Cryptography.SHA384CryptoServiceProvider"
            or "System.Security.Cryptography.SHA384Managed" => "sha384",
        "System.Security.Cryptography.SHA512" or "System.Security.Cryptography.SHA512CryptoServiceProvider"
            or "System.Security.Cryptography.SHA512Managed" => "sha512",
        "System.Security.Cryptography.HMACSHA1" => "hmac-sha1",
        "System.Security.Cryptography.HMACSHA256" => "hmac-sha256",
        "System.Security.Cryptography.HMACSHA384" => "hmac-sha384",
        "System.Security.Cryptography.HMACSHA512" => "hmac-sha512",
        "System.Security.Cryptography.Aes" => "aes",
        "System.Security.Cryptography.RNGCryptoServiceProvider"
            or "System.Security.Cryptography.RandomNumberGenerator" => "rng",
        _ => null,
    };

    // The canonical .NET type per algorithm tag (the Create() result's
    // ReturnTypeName -- it drives the next callvirt's receiver hint).
    private static string CryptoAlgoTypeName(string algo) => algo switch
    {
        "md5" => "System.Security.Cryptography.MD5",
        "sha1" => "System.Security.Cryptography.SHA1",
        "sha256" => "System.Security.Cryptography.SHA256",
        "sha384" => "System.Security.Cryptography.SHA384",
        "sha512" => "System.Security.Cryptography.SHA512",
        "aes" => "System.Security.Cryptography.Aes",
        _ => "System.Security.Cryptography.RNGCryptoServiceProvider",
    };

    private static ResolvedMethod? CryptoCtor(string? algo, byte[] ps, int count)
    {
        if (algo == null) return null;
        if (algo == "rng")
        {
            if (count == 0) return I("il2cpp::runtime::icall_rng_ctor", 0, false, false, true);
            // net48 RNGCryptoServiceProvider(string) / (byte[]) / (CspParameters)
            // all delegate to this((CspParameters) null) -- a no-op (the OS
            // CSPRNG is used regardless of the name/container). The argument is
            // dropped at the call site. ParamCount is the EXPLICIT IL arity
            // (1 -- the MethodMap.String.cs convention): the fold pops one IL
            // argument off its shadow stack even though the "drop" cast keeps
            // it out of the emitted icall (a ParamCount of 0 left the value on
            // the fold stack, desyncing the next join -- "fall-through stack
            // height 0 != join slots 2" for any method that also has a loop).
            if (count == 1 && (ps[0] == 0x0E || ps[0] == 0x1D || ps[0] == 0x12))
                return I("il2cpp::runtime::icall_rng_ctor", 1, false, false, true, new[] { "", "drop" });
            throw new NotSupportedException(
                $"RNGCryptoServiceProvider ctor with {count} param(s) not wired (net48 has the parameterless / string / byte[] / CspParameters forms only)");
        }
        if (algo == "aes") return null; // Aes is abstract -- no direct ctor
        if (algo.StartsWith("hmac-", StringComparison.Ordinal))
        {
            if (count == 1 && ps[0] == 0x1D)
                return I("il2cpp::runtime::icall_hmac_ctor", 1, false, false, true, new[] { "", "a" });
            // net48 HMACSHAx() : this(Utils.GenerateRandom(BlockSizeValue))
            // -- a fresh CSPRNG key of the algorithm's block size.
            if (count == 0)
                return I("il2cpp::runtime::icall_hmac_ctor_default", 0, false, false, true);
            throw new NotSupportedException($"HMACSHAx ctor with {count} param(s) not wired");
        }
        if (count == 0)
            return I($"il2cpp::runtime::icall_{algo}_ctor", 0, false, false, true);
        throw new NotSupportedException($"HashAlgorithm ctor with {count} param(s) not wired");
    }

    private static ResolvedMethod CryptoComputeHash(string algo, byte[] ps, int count)
    {
        var hmac = algo.StartsWith("hmac-", StringComparison.Ordinal);
        if (count == 1 && ps[0] == 0x1D)
            return I(hmac ? "il2cpp::runtime::icall_hmac_compute_hash" : $"il2cpp::runtime::icall_{algo}_compute_hash",
                1, true, true, true, new[] { "", "a" }, returnTypeName: "System.Byte[]");
        if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08 && !hmac)
            return I($"il2cpp::runtime::icall_{algo}_compute_hash_range", 3, true, true, true,
                new[] { "", "a", "i", "i" }, returnTypeName: "System.Byte[]");
        if (count == 1 && ps[0] == 0x12)
        {
            // ComputeHash(Stream): the 4096-byte chunked read loop (net48).
            // The Stream is a reference type -- the "" cast passes it as an
            // Il2CppObject* (the runtime resolves the backend through the handle).
            if (algo.StartsWith("hmac-", StringComparison.Ordinal))
                return I("il2cpp::runtime::icall_hmac_compute_hash_stream", 1, true, true, true,
                    new[] { "", "" }, returnTypeName: "System.Byte[]");
            if (algo is "md5" or "sha1" or "sha256" or "sha384" or "sha512")
                return I($"il2cpp::runtime::icall_{algo}_compute_hash_stream", 1, true, true, true,
                    new[] { "", "" }, returnTypeName: "System.Byte[]");
            throw new NotSupportedException($"ComputeHash(Stream) on '{algo}' not wired");
        }
        throw new NotSupportedException(
            $"ComputeHash overload (arity {count}, param 0x{(count > 0 ? ps[0] : 0):X2}) not wired");
    }
}
