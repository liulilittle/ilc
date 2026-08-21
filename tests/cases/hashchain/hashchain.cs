using System;
using System.Security.Cryptography;
using System.Text;
namespace Cases { public static class HashChainCase {
    // Hash chain e2e (hashcase/cryptocase already cover one-shot MD5/SHA1/
    // SHA256 + HMAC-SHA256): this case extends the hash family to SHA384/
    // SHA512/HMACSHA1/HMACSHA512 and to MULTI-BLOCK payloads -- a
    // deterministic 1000-byte buffer (byte[i] = (byte)(i*7+3); >64-byte
    // SHA-1/256 block, >128-byte SHA-512 block). All bool probes (no hex
    // ToString, per e2e convention). net48 is the reference baseline.
    //
    // STREAMING NOTE: incremental streaming hashing (HashAlgorithm.
    // TransformBlock/TransformFinalBlock) is NOT wired (MethodMap.Crypto.cs
    // throws NotSupportedException, v2 item) -- per the task fallback,
    // incremental CONSISTENCY is probed through the WIRED range overload
    // ComputeHash(byte[],int,int) == whole-buffer ComputeHash(byte[])
    // (icall_*_compute_hash_range, crypto_obj.cpp), plus hash stability
    // (identical input twice -> identical digest). SHA512/SHA384/HMACSHAx are
    // wired (icall_sha512_create/compute_hash, icall_hmac_ctor/
    // icall_hmac_compute_hash). Byte buffers are built by loop (the
    // new string(char,int) ctor remains unwired, hashcase note).
    //
    // Vectors: "abc" NIST FIPS 180-4; RFC 2202 TC2 (HMAC-SHA1);
    // RFC 4231 TC1 (HMAC-SHA512); the 1000-byte digests are
    // reference-derived (verified by the net48 reference run below, all
    // True). Hex is hand-rolled (no BitConverter dependency).
    public static void Main() {
        byte[] data = new byte[1000];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 7 + 3);
        byte[] abc = Encoding.UTF8.GetBytes("abc");
        byte[] key = Encoding.UTF8.GetBytes("key");
        byte[] keyJefe = Encoding.UTF8.GetBytes("Jefe");
        byte[] msg2202 = Encoding.UTF8.GetBytes("what do ya want for nothing?");
        byte[] hiThere = Encoding.UTF8.GetBytes("Hi There");
        byte[] key20x0b = new byte[20];
        for (int i = 0; i < key20x0b.Length; i++) key20x0b[i] = (byte)0x0B;
        // multi-block family digests over the deterministic 1000-byte buffer
        Console.WriteLine(Hex(MD5.Create().ComputeHash(data)) == "10046f077f2082ac19676b8079f1cb1a");   // True (MD5 1000B)
        Console.WriteLine(Hex(SHA1.Create().ComputeHash(data)) == "4231a8a50a10fa9758db8ec71fdef855b751048a"); // True (SHA1 1000B)
        Console.WriteLine(Hex(SHA256.Create().ComputeHash(data)) == "1e9bc38cbf860b9ec31918b065f9b52476c549a782e0e7990bed8ce3868d2371"); // True (SHA256 1000B)
        Console.WriteLine(Hex(SHA384.Create().ComputeHash(data)) == "94c38db521ca733b8904c2d14b6e82d33dcfcc26e1318c579dbee1fc2f472019034e792263b9d46e90e700de2f6e7e91"); // True (SHA384 1000B)
        Console.WriteLine(Hex(SHA512.Create().ComputeHash(data)) == "00e36fccf193e59697a92b5ab24666ce6326d7fa16bf10832d0991ddc591112e9dfa6a636950ed9c4d67344a760654c2ff7785e1d60094d651038735b5dccabd"); // True (SHA512 1000B)
        // NIST FIPS 180-4 single-block vectors for the new family members
        Console.WriteLine(Hex(SHA512.Create().ComputeHash(abc)) == "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f"); // True (SHA512 "abc")
        Console.WriteLine(Hex(SHA384.Create().ComputeHash(abc)) == "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7"); // True (SHA384 "abc")
        // HMAC family extensions: SHA256/SHA512 over the multi-block buffer,
        // RFC 2202 TC2 (SHA1), RFC 4231 TC1 (SHA512)
        Console.WriteLine(Hex(new HMACSHA256(key).ComputeHash(data)) == "30b7fb8eeeae6f980a87bdb8d037fb5cb3b9c86faabefcd72e27f00f0d96ff66"); // True (HMAC-SHA256 1000B)
        Console.WriteLine(Hex(new HMACSHA512(key).ComputeHash(data)) == "f5caf39d10ed807217a8d0fb47be5f4d0dda0e332e0d545f3ec050bd5c68efce85101eccf3a9bd2c05d8a205c1ac746a6e8a27a49656ab424a84ceef23ba86f5"); // True (HMAC-SHA512 1000B)
        Console.WriteLine(Hex(new HMACSHA1(keyJefe).ComputeHash(msg2202)) == "effcdf6ae5eb2fa2d27416d5f184df9c259a7c79"); // True (HMAC-SHA1 RFC 2202 TC2)
        Console.WriteLine(Hex(new HMACSHA512(key20x0b).ComputeHash(hiThere)) == "87aa7cdea5ef619d4ff0b4241a1d6cb02379f4e2ce4ec2787ad0b30545e17cdedaa833b7d6b8a702038b274eaea3f4e4be9d914eeb61f1702e696c203a126854"); // True (HMAC-SHA512 RFC 4231 TC1)
        // hash stability: identical input twice -> identical digest
        Console.WriteLine(Hex(MD5.Create().ComputeHash(data)) == Hex(MD5.Create().ComputeHash(data)));   // True (MD5 stable)
        Console.WriteLine(Hex(SHA256.Create().ComputeHash(data)) == Hex(SHA256.Create().ComputeHash(data))); // True (SHA256 stable)
        // incremental consistency (TransformBlock fallback): the range
        // overload over the full buffer == the one-shot whole-buffer digest
        Console.WriteLine(Hex(MD5.Create().ComputeHash(data, 0, data.Length)) == Hex(MD5.Create().ComputeHash(data)));   // True (MD5 range==whole)
        Console.WriteLine(Hex(SHA1.Create().ComputeHash(data, 0, data.Length)) == Hex(SHA1.Create().ComputeHash(data))); // True (SHA1 range==whole)
        Console.WriteLine(Hex(SHA256.Create().ComputeHash(data, 0, data.Length)) == Hex(SHA256.Create().ComputeHash(data))); // True (SHA256 range==whole)
        Console.WriteLine(Hex(SHA512.Create().ComputeHash(data, 0, data.Length)) == Hex(SHA512.Create().ComputeHash(data))); // True (SHA512 range==whole)
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
