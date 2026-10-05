using System;
using System.Security.Cryptography;
using System.Text;
namespace Cases { public static class HashCase {
    // Hash domain: MD5/SHA1/SHA256 ComputeHash over UTF-8 bytes, compared as
    // lowercase hex against known vectors. All bool probes (no hex ToString,
    // per e2e convention). net48 (the project's reference baseline) has no
    // HashData() (that is .NET 5+), so use X.Create().ComputeHash().
    // Vectors: "abc"/"" are published NIST/well-known values; "a"*100 is
    // reference-derived (multi-block: 100 bytes > 64-byte block size for all
    // three algorithms). Hex is hand-rolled (no BitConverter dependency).
    //
    // TRANSPILE STATUS (2026-08-05, updated after the T4 Hash wiring):
    // MD5.Create() static misclassification is FIXED (MethodMap.Hash.cs maps
    // Create() -> ::null:: fold with the algorithm type hint; ComputeHash
    // (byte[]) -> the one-shot hash_bytes ARRAY icall, receiver dropped) --
    // transpile now EXIT=0. Encoding.UTF8.GetBytes is also wired (M6
    // allocating GetBytes). The sole remaining blocker for THIS case is the
    // String ctor(char, int) gap (`new string('a', 100)` -> the emitted
    // System_String__ctor_* symbol is undeclared -- String domain work
    // item), worked around below with a 100-char literal (byte-identical
    // payload, same RFC 1321/FIPS two-block vectors). expected.txt/
    // ref_out.txt remain the net48 reference run (9/9 True).
    public static void Main() {
        byte[] abc = Encoding.UTF8.GetBytes("abc");
        byte[] empty = Encoding.UTF8.GetBytes("");
        byte[] a100 = Encoding.UTF8.GetBytes("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        Console.WriteLine(Hex(MD5.Create().ComputeHash(abc)) == "900150983cd24fb0d6963f7d28e17f72");     // True (MD5 "abc")
        Console.WriteLine(Hex(SHA1.Create().ComputeHash(abc)) == "a9993e364706816aba3e25717850c26c9cd0d89d"); // True (SHA1 "abc")
        Console.WriteLine(Hex(SHA256.Create().ComputeHash(abc)) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"); // True (SHA256 "abc")
        Console.WriteLine(Hex(MD5.Create().ComputeHash(empty)) == "d41d8cd98f00b204e9800998ecf8427e");   // True (MD5 "")
        Console.WriteLine(Hex(SHA1.Create().ComputeHash(empty)) == "da39a3ee5e6b4b0d3255bfef95601890afd80709"); // True (SHA1 "")
        Console.WriteLine(Hex(SHA256.Create().ComputeHash(empty)) == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"); // True (SHA256 "")
        Console.WriteLine(Hex(MD5.Create().ComputeHash(a100)) == "36a92cc94a9e0fa21f625f8bfb007adf");    // True (MD5 "a"*100, 2 blocks)
        Console.WriteLine(Hex(SHA1.Create().ComputeHash(a100)) == "7f9000257a4918d7072655ea468540cdcbd42e0c"); // True (SHA1 "a"*100, 2 blocks)
        Console.WriteLine(Hex(SHA256.Create().ComputeHash(a100)) == "2816597888e4a0d3a36b82b83316ab32680eb8f00f8cd3b904d681246d285a0e"); // True (SHA256 "a"*100, 2 blocks)
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
