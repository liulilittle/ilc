using System;
using System.Security.Cryptography;
using System.Text;
namespace Cases { public static class CryptoCase {
    // Crypto chain e2e: hash family (MD5/SHA1/SHA256, hashcase pattern) +
    // HMAC-SHA256 object probes (new HMACSHAx(byte[] key) -> ctor icall,
    // ComputeHash -> icall_hmac_compute_hash, receiver-carried state). All
    // bool probes (no hex ToString, per e2e convention); net48 is the
    // reference baseline. Hash vectors are NIST/well-known; HMAC vectors
    // are RFC 2104-style published values, verified by the net48 reference
    // run before expected.txt was frozen (all True).
    //
    // TRANSPILE STATUS (2026-08-05): HMAC is fully wired on both sides --
    // MethodMap.Crypto.cs (hmac-* ctor + compute_hash) and runtime
    // crypto_obj.cpp icall_hmac_ctor/compute_hash/dispose with the
    // RFC 2104 core in hmac.cpp. The long-key probe (key > 64-byte SHA-256
    // block, "a"*80) exercises the K' = H(K) key-normalization path.
    //
    // NOT TESTED HERE (conservative scope per task): AES -- Aes.Create()/
    // set_Key/set_IV/CreateEncryptor/CreateDecryptor/TransformFinalBlock/
    // GenerateKey/GenerateIV ARE wired (MethodMap.Crypto.cs:69-160 +
    // icall_aes_* in crypto_obj.cpp) but the object surface (Key/IV
    // properties, ICryptoTransform) is complex; recorded as a gap for a
    // follow-up aescase. Legacy note: `new string('a', N)` is still an
    // unwired ctor (String domain work item) -- long payloads use string
    // literals instead (byte-identical payload).
    public static void Main() {
        byte[] abc = Encoding.UTF8.GetBytes("abc");
        byte[] empty = Encoding.UTF8.GetBytes("");
        byte[] fox = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
        byte[] key = Encoding.UTF8.GetBytes("key");
        byte[] longKey = Encoding.UTF8.GetBytes("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        Console.WriteLine(Hex(MD5.Create().ComputeHash(abc)) == "900150983cd24fb0d6963f7d28e17f72"); // True (MD5 "abc")
        Console.WriteLine(Hex(SHA1.Create().ComputeHash(abc)) == "a9993e364706816aba3e25717850c26c9cd0d89d"); // True (SHA1 "abc")
        Console.WriteLine(Hex(SHA256.Create().ComputeHash(abc)) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"); // True (SHA256 "abc")
        Console.WriteLine(Hex(SHA256.Create().ComputeHash(empty)) == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"); // True (SHA256 "")
        Console.WriteLine(Hex(new HMACSHA256(key).ComputeHash(empty)) == "5d5d139563c95b5967b9bd9a8c9b233a9dedb45072794cd232dc1b74832607d0"); // True (HMAC-SHA256 "key" over "")
        Console.WriteLine(Hex(new HMACSHA256(key).ComputeHash(fox)) == "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8"); // True (HMAC-SHA256 "key" over fox)
        Console.WriteLine(Hex(new HMACSHA256(longKey).ComputeHash(fox)) == "cf36e4b1f1f0b388807b9d0b0baf9617981875a98d72405488b580374cb2001d"); // True (long key, K' = H(K) path)
    }
    private static string Hex(byte[] arr) {
        string s = "";
        for (int i = 0; i < arr.Length; i++) {
            int hi = arr[i] >> 4;
            int lo = arr[i] & 0x0F;
            s += (char)(hi < 10 ? '0' + hi : 'a' + hi - 10);
            s += (char)(lo < 10 ? '0' + lo : 'a' + lo - 10);
        }
        return s;
    }
}}
