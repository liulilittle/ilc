// crypto2.cs -- deep Crypto (MD5/SHA1/SHA256) e2e reference case (net48 baseline).
//
// DEEP probes of the wired hash object surface (MethodMap.Crypto.cs T4
// object layer + crypto_obj.cpp): the Create() instance + ComputeHash
// streaming family.
//
//   1) ONE-SHOT roundtrips: full-array ComputeHash(byte[]) vs the range form
//      ComputeHash(byte[], offset, count) with (0, Length) -- the two entry
//      points must agree for every algorithm.
//   2) OFFSET/COUNT slicing: ComputeHash(full, off, n) hashes exactly the
//      contiguous slice -- probe with a mid-buffer slice ("def" at offset 3)
//      and a head slice ("abc" at offset 0) against their one-shot digests.
//   3) EMPTY input: ComputeHash(empty), range form with count=0, and the
//      empty-array / zero-byte edge of the Hex formatter.
//   4) REPEATED calls on the SAME instance are independent (net48: each
//      ComputeHash runs create+update+final on the inline context).
//   5) Hex OUTPUT formatting: lowercase hex, zero-padded high nibble
//      (leading-zero bytes render "00.."), exact digest byte lengths
//      (16/20/32 -> hex lengths 32/40/64).
//   6) NIST/well-known vectors freeze algorithm correctness (MD5/SHA1/SHA256
//      for "abc", "" and the quick-brown-fox sentence).
//
// All probes print bools/ints only. The Hex helper is duplicated here (not
// shared) so the transpiled byte[] is the only array in play.
//
// INCREMENTAL STREAMING GAP (recorded in GAPS.md, NOT called): the .NET
// TransformBlock/TransformFinalBlock incremental hashing surface is a LOUD
// rejection (MethodMap.Crypto.cs -- v2 work item); the range form is the
// deepest partial-input probe the wired surface can express today.

using System;
using System.Security.Cryptography;
using System.Text;

namespace Cases { public static class Crypto2 {
    public static void Main() {
        byte[] abc = Encoding.UTF8.GetBytes("abc");
        byte[] def = Encoding.UTF8.GetBytes("def");
        byte[] empty = Encoding.UTF8.GetBytes("");
        byte[] fox = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
        byte[] full = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz0123456789");

        // ---- 1) one-shot vs range(0, Length) roundtrips ----
        MD5 md5 = MD5.Create();
        Console.WriteLine(Hex(md5.ComputeHash(abc)) == "900150983cd24fb0d6963f7d28e17f72"); // True (MD5 "abc")
        Console.WriteLine(Hex(md5.ComputeHash(abc, 0, abc.Length)) == "900150983cd24fb0d6963f7d28e17f72"); // True (range == full)
        SHA1 sha1 = SHA1.Create();
        Console.WriteLine(Hex(sha1.ComputeHash(fox)) == "2fd4e1c67a2d28fced849ee1bb76e7391b93eb12"); // True (SHA1 fox)
        Console.WriteLine(Hex(sha1.ComputeHash(fox, 0, fox.Length)) == "2fd4e1c67a2d28fced849ee1bb76e7391b93eb12"); // True
        SHA256 sha256 = SHA256.Create();
        Console.WriteLine(Hex(sha256.ComputeHash(fox)) == "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592"); // True (SHA256 fox)
        Console.WriteLine(Hex(sha256.ComputeHash(fox, 0, fox.Length)) == Hex(sha256.ComputeHash(fox))); // True

        // ---- 2) offset/count slicing ----
        // full starts "abc..."; offset 0, count 3 must equal SHA256("abc").
        Console.WriteLine(Hex(sha256.ComputeHash(full, 0, 3)) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"); // True
        // offset 3, count 3 hashes "def" (mid-buffer slice).
        Console.WriteLine(Hex(sha256.ComputeHash(full, 3, 3)) == Hex(sha256.ComputeHash(def))); // True
        Console.WriteLine(Hex(md5.ComputeHash(full, 0, 3)) == Hex(md5.ComputeHash(abc)));       // True
        Console.WriteLine(Hex(sha1.ComputeHash(full, 3, 3)) == Hex(sha1.ComputeHash(def)));     // True

        // ---- 3) empty input ----
        Console.WriteLine(Hex(MD5.Create().ComputeHash(empty)) == "d41d8cd98f00b204e9800998ecf8427e"); // True (MD5 "")
        Console.WriteLine(Hex(SHA1.Create().ComputeHash(empty)) == "da39a3ee5e6b4b0d3255bfef95601890afd80709"); // True (SHA1 "")
        Console.WriteLine(Hex(sha256.ComputeHash(empty)) == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"); // True (SHA256 "")
        Console.WriteLine(Hex(sha256.ComputeHash(full, 0, 0)) == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"); // True (range count=0 == empty)
        Console.WriteLine(Hex(md5.ComputeHash(empty, 0, 0)).Length == 32); // True (empty MD5 hex length)

        // ---- 4) repeated calls on the same instance are independent ----
        Console.WriteLine(Hex(md5.ComputeHash(abc)) == Hex(md5.ComputeHash(abc)));       // True
        Console.WriteLine(Hex(sha1.ComputeHash(abc)) == "a9993e364706816aba3e25717850c26c9cd0d89d"); // True (SHA1 "abc")
        Console.WriteLine(Hex(sha1.ComputeHash(abc)) == Hex(sha1.ComputeHash(abc)));     // True

        // ---- 5) digest lengths + Hex formatting ----
        Console.WriteLine(md5.ComputeHash(abc).Length == 16);    // True
        Console.WriteLine(sha1.ComputeHash(abc).Length == 20);   // True
        Console.WriteLine(sha256.ComputeHash(abc).Length == 32); // True
        Console.WriteLine(Hex(sha256.ComputeHash(abc)).Length == 64); // True
        Console.WriteLine(Hex(new byte[] { 0, 15, 160, 255 }) == "000fa0ff"); // True (leading zero nibble preserved, lowercase)
        Console.WriteLine(Hex(new byte[] { 16, 32, 64, 128 }) == "10204080"); // True
        Console.WriteLine(Hex(new byte[] { 0 }) == "00");                      // True (single zero byte)
        Console.WriteLine(Hex(new byte[0]).Length == 0);                       // True (empty input -> empty hex)
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
