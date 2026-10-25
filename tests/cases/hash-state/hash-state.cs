using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

//------------------------------------------------------------------------------
// hash-state -- the net48 HashAlgorithm OBJECT surface that batch 6 wires:
//   * Hash (the cached last digest, returned as a CLONE; NullReferenceException
//     before the first ComputeHash; CryptographicUnexpectedOperationException
//     mid-stream after TransformBlock; ObjectDisposedException after Clear)
//   * Clear() / Dispose() (the disposed flag -- a later Hash/ComputeHash/
//     Transform* throws ObjectDisposedException)
//   * TransformBlock / TransformFinalBlock (the ICryptoTransform streaming
//     surface; TransformFinalBlock returns a COPY of the input, not the digest)
//   * ComputeHash(Stream) (the 4096-byte chunked read loop)
//   * the HMAC Hash getter + Clear (HMACSHAx : KeyedHashAlgorithm)
// The hash objects stay LOCAL (never captured by a closure): the transpiler
// tracks the receiver's static type from `var x = <Algo>.Create()`, which the
// receiver-aware crypto mapping needs to resolve the concrete algorithm.
// Byte-exact against the net48 oracle.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class HashState
    {
        public static void Main()
        {
            byte[] abc = Encoding.UTF8.GetBytes("abc");

            // ---- Hash getter before any ComputeHash -> NullReferenceException
            var md5 = MD5.Create();
            try { Console.WriteLine("pre=" + Hex(md5.Hash)); }
            catch (Exception e) { Console.WriteLine("pre=" + e.GetType().Name); }

            // ---- ComputeHash caches the digest; Hash returns a CLONE
            Console.WriteLine("d1=" + Hex(md5.ComputeHash(abc)));
            Console.WriteLine("h1=" + Hex(md5.Hash));
            Console.WriteLine("clone=" + object.ReferenceEquals(md5.Hash, md5.Hash));
            Console.WriteLine("repeat=" + (Hex(md5.ComputeHash(abc)) == Hex(md5.Hash)));

            // ---- TransformBlock -> State != 0 -> Hash throws CUEOE
            byte[] part1 = Encoding.UTF8.GetBytes("ab");
            byte[] out1 = new byte[2];
            int n = md5.TransformBlock(part1, 0, 2, out1, 0);
            Console.WriteLine("tb=" + n + ":" + Hex(out1));
            try { Console.WriteLine("mid=" + Hex(md5.Hash)); }
            catch (Exception e) { Console.WriteLine("mid=" + e.GetType().Name); }

            // ---- TransformFinalBlock finalizes (the digest of "abc")
            byte[] part2 = Encoding.UTF8.GetBytes("c");
            byte[] fin = md5.TransformFinalBlock(part2, 0, 1);
            Console.WriteLine("fin=" + Hex(fin));
            Console.WriteLine("h2=" + Hex(md5.Hash));

            // ---- Clear -> disposed -> Hash throws ODE, ComputeHash throws ODE
            md5.Clear();
            try { Console.WriteLine("post=" + Hex(md5.Hash)); }
            catch (Exception e) { Console.WriteLine("post=" + e.GetType().Name); }
            try { Console.WriteLine("postCompute=" + Hex(md5.ComputeHash(abc))); }
            catch (Exception e) { Console.WriteLine("postCompute=" + e.GetType().Name); }

            // ---- ComputeHash(Stream): the 4096-byte chunked read loop
            var sha1 = SHA1.Create();
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("hello world")))
            {
                Console.WriteLine("stream=" + Hex(sha1.ComputeHash(ms)));
            }

            // ---- SHA256 multi-block TransformBlock + final (null output)
            var sha256 = SHA256.Create();
            byte[] a = Encoding.UTF8.GetBytes("The quick brown fox ");
            byte[] b = Encoding.UTF8.GetBytes("jumps over the lazy dog");
            sha256.TransformBlock(a, 0, a.Length, null, 0);
            byte[] fb = sha256.TransformFinalBlock(b, 0, b.Length);
            Console.WriteLine("multi=" + Hex(sha256.Hash));
            Console.WriteLine("multiOut=" + Hex(fb));

            // ---- HMAC Hash getter + Clear (KeyedHashAlgorithm)
            var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("key"));
            try { Console.WriteLine("hmacPre=" + Hex(hmac.Hash)); }
            catch (Exception e) { Console.WriteLine("hmacPre=" + e.GetType().Name); }
            Console.WriteLine("hmacD=" + Hex(hmac.ComputeHash(abc)));
            Console.WriteLine("hmacH=" + Hex(hmac.Hash));
            hmac.Clear();
            try { Console.WriteLine("hmacPost=" + Hex(hmac.Hash)); }
            catch (Exception e) { Console.WriteLine("hmacPost=" + e.GetType().Name); }
        }

        private static string Hex(byte[] arr)
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
    }
}
