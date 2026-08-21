using System;
using System.Security.Cryptography;
using System.Text;
namespace Cases { public static class AesCase {
    // AES chain e2e (the conservative gap recorded by cryptocase): the full
    // object surface exercised here -- Aes.Create() -> set_Key/get_Key/set_IV/
    // get_IV -> CreateEncryptor(key, iv) -> ICryptoTransform.TransformFinalBlock
    // -> CreateDecryptor(key, iv) -> decrypt -> plaintext roundtrip, plus
    // GenerateKey/GenerateIV and ciphertext determinism. All probes are bool
    // (per e2e convention); net48 is the reference baseline.
    //
    // TRANSPILE STATUS (2026-08-06): GAP RECORDED -- the runtime side is fully
    // wired (crypto_obj.cpp icall_aes_create/set_key/get_key/set_iv/get_iv/
    // create_encryptor/create_decryptor/transform_final_block/generate_key/
    // generate_iv/dispose, receiver-carried key/iv/ctx + mode flag, CBC+PKCS7)
    // and the transpiler MAPS the family (MethodMap.Crypto.cs:69-160), but the
    // mapping is UNREACHABLE from csc-compiled user code:
    //   * csc declares every Aes instance member (set_Key/get_Key/set_IV/
    //     get_IV/CreateEncryptor/CreateDecryptor/GenerateKey/GenerateIV) on
    //     the BCL PARENT System.Security.Cryptography.SymmetricAlgorithm
    //     (verified by decoding the MemberRef table of this exe -- MR rows
    //     15-23, ParentType=SymmetricAlgorithm; only TransformFinalBlock sits
    //     on ICryptoTransform).
    //   * the transpiler has NO SymmetricAlgorithm entry (MethodRefResolver.cs
    //     crypto routing list, lines ~545-563, stops at KeyedHashAlgorithm)
    //     so TryMapCrypto never sees these calls.
    //   * the generic get_* heuristic (MethodRefResolver.cs:337) marks
    //     get_Key STATIC -> transpile fails LOUD:
    //     "callvirt on non-instance method (token 167772177)" (StackFold.cs:
    //     1393). set_Key resolves "instance" only by accident (no get_ prefix)
    //     and would fail at build (nonexistent generated name).
    //   * net48 reference run is 7/7 True (this file compiles + runs correctly
    //     as plain C#); the transpiler gap is the ONLY blocker.
    // SUGGESTED TRANSPILER FIX (transpiler domain, out of scope here): add
    // "System.Security.Cryptography.SymmetricAlgorithm" to the crypto routing
    // list and teach CryptoAlgo() (MethodMap.Crypto.cs:193-198) to derive the
    // algo from receiverType for the SymmetricAlgorithm parent, mirroring the
    // existing HashAlgorithm/KeyedHashAlgorithm handling. The case then runs
    // unchanged.
    //
    // Minimal API on purpose: Create + Key + IV + CreateEncryptor +
    // TransformFinalBlock only. No using blocks: ICryptoTransform.Dispose is
    // not mapped (object chain = Create..TransformFinalBlock per task scope).
    // If the transpiler cannot emit the object chain at all, this file itself
    // records the gap (transpile failure is the evidence).
    public static void Main() {
        byte[] key = Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"); // 32B AES-256
        byte[] iv = Encoding.UTF8.GetBytes("0123456789abcdef");                   // 16B
        byte[] plain = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog"); // 43B -> 48B with PKCS7
        Aes aes = Aes.Create();
        Console.WriteLine(aes != null);                    // 1. Aes.Create() wired
        aes.Key = key;
        aes.IV = iv;
        Console.WriteLine(BytesEqual(aes.Key, key));       // 2. Key roundtrip (set_Key/get_Key)
        Console.WriteLine(BytesEqual(aes.IV, iv));         // 3. IV roundtrip (set_IV/get_IV)
        ICryptoTransform enc = aes.CreateEncryptor(key, iv);
        byte[] cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
        Console.WriteLine(cipher.Length == 48);            // 4. CBC + PKCS7 pad: 43 -> 48
        ICryptoTransform dec = aes.CreateDecryptor(key, iv);
        byte[] back = dec.TransformFinalBlock(cipher, 0, cipher.Length);
        Console.WriteLine(BytesEqual(back, plain));        // 5. encrypt -> decrypt roundtrip
        ICryptoTransform enc2 = aes.CreateEncryptor(key, iv);
        byte[] cipher2 = enc2.TransformFinalBlock(plain, 0, plain.Length);
        Console.WriteLine(BytesEqual(cipher, cipher2));    // 6. deterministic ciphertext (same key/iv)
        aes.GenerateKey(); // net48: void -- the generated key is read back via get_Key
        aes.GenerateIV();
        byte[] genKey = aes.Key;
        byte[] genIv = aes.IV;
        ICryptoTransform genEnc = aes.CreateEncryptor(genKey, genIv);
        ICryptoTransform genDec = aes.CreateDecryptor(genKey, genIv);
        byte[] genCipher = genEnc.TransformFinalBlock(plain, 0, plain.Length);
        byte[] genBack = genDec.TransformFinalBlock(genCipher, 0, genCipher.Length);
        Console.WriteLine(BytesEqual(genBack, plain));     // 7. GenerateKey/GenerateIV + roundtrip
    }
    private static bool BytesEqual(byte[] a, byte[] b) {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) {
            if (a[i] != b[i]) return false;
        }
        return true;
    }
}}
