using System;
using System.Security.Cryptography;

//------------------------------------------------------------------------------
// rng-csp-ctor -- batch 7:
//   * RNGCryptoServiceProvider(CspParameters) -- net48 delegates to
//     this((CspParameters) null): a no-op (the OS CSPRNG is used regardless of
//     the container name), so the argument is dropped at the call site.
//
// The random bytes themselves are not byte-comparable; the case pins the
// observable contract (the ctor does not throw, GetBytes fills the requested
// length, two instances produce different streams, GetNonZeroBytes leaves no
// zero byte).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class RngCspCtor
    {
        public static void Main()
        {
            var r1 = new RNGCryptoServiceProvider((CspParameters)null);
            var r2 = new RNGCryptoServiceProvider((CspParameters)null);
            byte[] b1 = new byte[16];
            byte[] b2 = new byte[16];
            r1.GetBytes(b1);
            r2.GetBytes(b2);
            Console.WriteLine("len=" + b1.Length);
            Console.WriteLine("diff=" + !Equal(b1, b2));

            byte[] nz = new byte[16];
            r1.GetNonZeroBytes(nz);
            bool allNonZero = true;
            for (int i = 0; i < nz.Length; i++)
                if (nz[i] == 0) allNonZero = false;
            Console.WriteLine("allNonZero=" + allNonZero);
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
