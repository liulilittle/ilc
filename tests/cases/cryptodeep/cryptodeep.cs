//------------------------------------------------------------------------------
// cryptodeep.cs -- MD5/SHA1 DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C69 (2026-08-12), AFTER the crypto
// object domain wiring (MethodMap.Crypto.cs -> crypto_obj.cpp Create +
// ComputeHash streaming family).
//
// DEEP DIMENSIONS (all surfaces below are WIRED):
//   1. RFC 1321 MD5 TEST SUITE (the seven official vectors): "" / "a" /
//      "abc" / "message digest" / lowercase alphabet / mixed-case alphabet
//      + digits (62 chars -- CROSSES the 64-byte block boundary) / the
//      80-char digits string (also multi-block).
//   2. FIPS 180-1 SHA1 TEST SUITE: "" / "abc" / the 56-char
//      "abcdbcdecdef..." vector (EXACTLY at the 448-bit padding boundary) /
//      the 112-char "abcdefghbcdef..." vector (TRUE multi-block, 2 blocks).
//   3. MULTI-BLOCK BOUNDARY SWEEP via new string('a', n) for n in
//      {55,56,57,63,64,65,100,1000}: one-shot ComputeHash MUST equal the
//      range form (0, len), and a SECOND fresh instance MUST produce the
//      same digest (cross-instance determinism) -- net48 is the byte
//      reference, so both the block-loop path and the finalizer length
//      handling are pinned byte-for-byte.
//   4. ARBITRARY-BYTE multi-block: a 1000-byte buffer filled with a
//      non-repeating byte pattern (i % 251) hashed one-shot == range form,
//      and MD5 == MD5 across two instances.
//   5. ROUNDTRIP (往返): ComputeHash(ComputeHash(x)) -- the digest bytes
//      fed BACK into the same algorithm -- is deterministic and identical
//      across two independent instances (digest-of-digest roundtrip);
//      repeated calls on ONE instance are independent (each ComputeHash
//      starts fresh).
//   6. DIGEST LENGTHS: MD5 16 / SHA1 20 bytes; hex lengths 32 / 40.
//
// The Hex helper is duplicated here (not shared) so the transpiled byte[]
// is the only array in play.
//
// EVERY line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Security.Cryptography;
using System.Text;

class Program
{
    static string Hex(byte[] arr)
    {
        string s = "";
        for (int i = 0; i < arr.Length; i++)
        {
            int hi = arr[i] >> 4;
            int lo = arr[i] & 0x0F;
            s += (char)(hi < 10 ? '0' + hi : 'a' + hi - 10);
            s += (char)(lo < 10 ? '0' + lo : 'a' + lo - 10);
        }
        return s;
    }

    static void Main()
    {
        byte[] empty = Encoding.UTF8.GetBytes("");
        byte[] a = Encoding.UTF8.GetBytes("a");
        byte[] abc = Encoding.UTF8.GetBytes("abc");
        byte[] msg = Encoding.UTF8.GetBytes("message digest");
        byte[] alpha = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        byte[] mixed = Encoding.UTF8.GetBytes("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789");
        byte[] digits = Encoding.UTF8.GetBytes("12345678901234567890123456789012345678901234567890123456789012345678901234567890");

        // ============ 1. RFC 1321 MD5 test suite ============
        MD5 md5 = MD5.Create();
        Console.WriteLine("m5_empty=" + (Hex(md5.ComputeHash(empty)) == "d41d8cd98f00b204e9800998ecf8427e"));
        Console.WriteLine("m5_a=" + (Hex(md5.ComputeHash(a)) == "0cc175b9c0f1b6a831c399e269772661"));
        Console.WriteLine("m5_abc=" + (Hex(md5.ComputeHash(abc)) == "900150983cd24fb0d6963f7d28e17f72"));
        Console.WriteLine("m5_msg=" + (Hex(md5.ComputeHash(msg)) == "f96b697d7cb7938d525a2f31aaf161d0"));
        Console.WriteLine("m5_alpha=" + (Hex(md5.ComputeHash(alpha)) == "c3fcd3d76192e4007dfb496cca67e13b"));
        Console.WriteLine("m5_mixed=" + (Hex(md5.ComputeHash(mixed)) == "d174ab98d277d9f5a5611c2c9f419d9f"));
        Console.WriteLine("m5_digits=" + (Hex(md5.ComputeHash(digits)) == "57edf4a22be3c955ac49da2e2107b67a"));

        // ============ 2. FIPS 180-1 SHA1 test suite ============
        SHA1 sha1 = SHA1.Create();
        Console.WriteLine("s1_empty=" + (Hex(sha1.ComputeHash(empty)) == "da39a3ee5e6b4b0d3255bfef95601890afd80709"));
        Console.WriteLine("s1_abc=" + (Hex(sha1.ComputeHash(abc)) == "a9993e364706816aba3e25717850c26c9cd0d89d"));
        byte[] sha1_56 = Encoding.UTF8.GetBytes("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq");
        Console.WriteLine("s1_56=" + (Hex(sha1.ComputeHash(sha1_56)) == "84983e441c3bd26ebaae4aa1f95129e5e54670f1"));
        byte[] sha1_112 = Encoding.UTF8.GetBytes("abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu");
        Console.WriteLine("s1_112=" + (Hex(sha1.ComputeHash(sha1_112)) == "a49b2446a02c645bf419f995b67091253a04a259"));

        // ============ 3. multi-block boundary sweep (string-built) ============
        // one-shot == range(0, len) AND a second fresh instance == first.
        int[] lens = new int[] { 55, 56, 57, 63, 64, 65, 100, 1000 };
        bool sweepMd5 = true, sweepSha1 = true;
        foreach (int n in lens)
        {
            byte[] buf = Encoding.UTF8.GetBytes(new string('a', n));
            string one = Hex(md5.ComputeHash(buf));
            string rng = Hex(md5.ComputeHash(buf, 0, buf.Length));
            string other = Hex(MD5.Create().ComputeHash(buf));
            if (one != rng || one != other) sweepMd5 = false;
            string o1 = Hex(sha1.ComputeHash(buf));
            string r1 = Hex(sha1.ComputeHash(buf, 0, buf.Length));
            string o2 = Hex(SHA1.Create().ComputeHash(buf));
            if (o1 != r1 || o1 != o2) sweepSha1 = false;
        }
        Console.WriteLine("mb_md5=" + sweepMd5);
        Console.WriteLine("mb_sha1=" + sweepSha1);

        // ============ 4. arbitrary-byte multi-block buffer ============
        byte[] big = new byte[1000];
        for (int i = 0; i < big.Length; i++) big[i] = (byte)(i % 251);
        Console.WriteLine("big_md5_oneshot=" + (Hex(md5.ComputeHash(big)) == Hex(md5.ComputeHash(big, 0, big.Length))));
        Console.WriteLine("big_md5_cross=" + (Hex(MD5.Create().ComputeHash(big)) == Hex(md5.ComputeHash(big))));
        Console.WriteLine("big_sha1_oneshot=" + (Hex(sha1.ComputeHash(big)) == Hex(sha1.ComputeHash(big, 0, big.Length))));
        Console.WriteLine("big_sha1_cross=" + (Hex(SHA1.Create().ComputeHash(big)) == Hex(sha1.ComputeHash(big))));

        // ============ 5. roundtrip: digest-of-digest across instances ============
        byte[] d1 = md5.ComputeHash(abc);
        byte[] dd1 = md5.ComputeHash(d1);
        byte[] dd2 = MD5.Create().ComputeHash(MD5.Create().ComputeHash(abc));
        Console.WriteLine("rt_md5_digest=" + (Hex(dd1) == Hex(dd2)));
        byte[] e1 = sha1.ComputeHash(abc);
        byte[] ee1 = sha1.ComputeHash(e1);
        byte[] ee2 = SHA1.Create().ComputeHash(SHA1.Create().ComputeHash(abc));
        Console.WriteLine("rt_sha1_digest=" + (Hex(ee1) == Hex(ee2)));
        // repeated calls on ONE instance are independent (fresh each time)
        Console.WriteLine("rt_md5_repeat=" + (Hex(md5.ComputeHash(abc)) == Hex(md5.ComputeHash(abc))));
        Console.WriteLine("rt_sha1_repeat=" + (Hex(sha1.ComputeHash(abc)) == Hex(sha1.ComputeHash(abc))));
        // MD5-of-MD5 vs SHA1-of-SHA1 produce different hex (different algorithms)
        Console.WriteLine("rt_algo_diff=" + (Hex(dd1) != Hex(ee1)));

        // ============ 6. digest lengths ============
        Console.WriteLine("len_md5=" + (md5.ComputeHash(abc).Length == 16));
        Console.WriteLine("len_sha1=" + (sha1.ComputeHash(abc).Length == 20));
        Console.WriteLine("len_md5hex=" + (Hex(md5.ComputeHash(abc)).Length == 32));
        Console.WriteLine("len_sha1hex=" + (Hex(sha1.ComputeHash(abc)).Length == 40));
        Console.WriteLine("len_big=" + (md5.ComputeHash(big).Length == 16 && sha1.ComputeHash(big).Length == 20));
    }
}
