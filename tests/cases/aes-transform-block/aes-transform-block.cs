using System;
using System.Security.Cryptography;
using System.Text;

//------------------------------------------------------------------------------
// aes-transform-block -- batch 7: ICryptoTransform.TransformBlock on the AES
// transform (net48 v2 RijndaelManagedTransform streaming semantics).
//
//   * Encrypt: TransformBlock processes each full block with CBC chaining; the
//     final partial block is padded by TransformFinalBlock. The streamed
//     ciphertext must equal a single TransformFinalBlock of the concatenation.
//   * Decrypt: TransformBlock buffers the last ciphertext block (the padding is
//     only stripped by TransformFinalBlock); the streamed plaintext must equal
//     the original.
//
// Byte-exact against the net48 oracle.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class AesTransformBlock
    {
        private static byte[] Key()
        {
            byte[] k = new byte[16];
            for (int i = 0; i < 16; i++) k[i] = (byte)(i + 1);
            return k;
        }

        private static byte[] Iv()
        {
            byte[] v = new byte[16];
            for (int i = 0; i < 16; i++) v[i] = (byte)(0xA0 + i);
            return v;
        }

        public static void Main()
        {
            byte[] key = Key();
            byte[] iv = Iv();
            byte[] p1 = Encoding.ASCII.GetBytes("0123456789ABCDEF"); // 16
            byte[] p2 = Encoding.ASCII.GetBytes("GHIJKLMNOPQRSTUV");  // 16
            byte[] p3 = Encoding.ASCII.GetBytes("WXYZ!");             // 5
            byte[] all = Concat(p1, p2, p3);

            // ---- Encrypt streaming: 16 + 16 + final 5
            var aes = Aes.Create();
            ICryptoTransform enc = aes.CreateEncryptor(key, iv);
            byte[] o1 = new byte[16];
            int n1 = enc.TransformBlock(p1, 0, p1.Length, o1, 0);
            byte[] o2 = new byte[16];
            int n2 = enc.TransformBlock(p2, 0, p2.Length, o2, 0);
            byte[] o3 = enc.TransformFinalBlock(p3, 0, p3.Length);
            Console.WriteLine("n1=" + n1 + " n2=" + n2 + " o3len=" + o3.Length);

            byte[] ct = Concat(o1, o2, o3);
            var aes2 = Aes.Create();
            ICryptoTransform enc2 = aes2.CreateEncryptor(key, iv);
            byte[] ctRef = enc2.TransformFinalBlock(all, 0, all.Length);
            Console.WriteLine("ctEq=" + Equal(ct, ctRef));
            Console.WriteLine("ctHex=" + Hex(ct));

            // ---- Decrypt streaming: 16 + 16 + final (rest)
            var aes3 = Aes.Create();
            ICryptoTransform dec = aes3.CreateDecryptor(key, iv);
            byte[] d1 = new byte[16];
            int m1 = dec.TransformBlock(ct, 0, 16, d1, 0);
            byte[] d2 = new byte[16];
            int m2 = dec.TransformBlock(ct, 16, 16, d2, 0);
            byte[] d3 = dec.TransformFinalBlock(ct, 32, ct.Length - 32);
            Console.WriteLine("m1=" + m1 + " m2=" + m2 + " d3len=" + d3.Length);
            byte[] pt = Concat(CopyOf(d1, m1), CopyOf(d2, m2), CopyOf(d3, d3.Length));
            Console.WriteLine("ptEq=" + Equal(pt, all));
            Console.WriteLine("ptHex=" + Hex(pt));
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int n = 0;
            foreach (byte[] p in parts) n += p.Length;
            byte[] r = new byte[n];
            int at = 0;
            foreach (byte[] p in parts) { Array.Copy(p, 0, r, at, p.Length); at += p.Length; }
            return r;
        }

        private static byte[] CopyOf(byte[] src, int count)
        {
            byte[] r = new byte[count];
            Array.Copy(src, 0, r, 0, count);
            return r;
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static string Hex(byte[] a)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in a) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
