using System;
using System.Security.Cryptography;
using System.Text;

//------------------------------------------------------------------------------
// aes-rng-ctor -- batch 6:
//   * Aes parameterless CreateEncryptor()/CreateDecryptor() (net48
//     SymmetricAlgorithm.CreateEncryptor() -> CreateEncryptor(Key, IV); the
//     Key/IV getters generate a fresh key/IV when unset)
//   * RNGCryptoServiceProvider(string) / (byte[]) ctors (net48 delegates both
//     to this((CspParameters) null) -- a no-op: the OS CSPRNG is used regardless
//     of the name)
// Byte-exact against the net48 oracle.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class AesRngCtor
    {
        public static void Main()
        {
            // ---- Aes parameterless CreateEncryptor/Decryptor (auto-generated key/iv)
            var aes = Aes.Create();
            ICryptoTransform enc = aes.CreateEncryptor();
            byte[] pt = Encoding.UTF8.GetBytes("hello world!!!");
            byte[] ct = enc.TransformFinalBlock(pt, 0, pt.Length);
            ICryptoTransform dec = aes.CreateDecryptor();
            byte[] back = dec.TransformFinalBlock(ct, 0, ct.Length);
            Console.WriteLine("ctLen=" + ct.Length);
            Console.WriteLine("rt=" + Encoding.UTF8.GetString(back));

            // ---- Aes.Clear() closes the CSP handle (net48): a later
            //      CreateEncryptor()/Key throws ObjectDisposedException.
            aes.Clear();
            try { ICryptoTransform enc2 = aes.CreateEncryptor(); Console.WriteLine("afterClear=NO"); }
            catch (Exception e) { Console.WriteLine("afterClear=" + e.GetType().Name); }
            try { Console.WriteLine("afterKey=" + aes.Key.Length); }
            catch (Exception e) { Console.WriteLine("afterKey=" + e.GetType().Name); }

            // ---- RNG named-CSP ctors (no-op -> the OS CSPRNG)
            var r1 = new RNGCryptoServiceProvider("Microsoft Strong Cryptographic Provider");
            var r2 = new RNGCryptoServiceProvider(Encoding.UTF8.GetBytes("seed"));
            byte[] b1 = new byte[16];
            byte[] b2 = new byte[16];
            r1.GetBytes(b1);
            r2.GetBytes(b2);
            Console.WriteLine("r1Len=" + b1.Length);
            Console.WriteLine("r2Len=" + b2.Length);
            Console.WriteLine("diff=" + !Equal(b1, b2));
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
