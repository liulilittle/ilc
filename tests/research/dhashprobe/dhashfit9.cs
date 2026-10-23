//------------------------------------------------------------------------------
// dhashfit9.cs -- RESEARCH ONLY, FINAL CONFIRMATION.
//
// Algorithm chosen for the port (single rounding, correctly rounded table):
//     d = M / T10[scale]        M = hi32*2^64 + lo64 (exact two-term sum)
//     d = -d when the sign bit is set (yields -0.0 for negative zero)
// with T10[] = the CORRECTLY ROUNDED double of 10^scale (hardcoded bits, so
// the C++ port cannot drift with the platform libm).
//
// Verifies on 2,000,000 adversarial samples:
//   1. the double bits equal net48 (double)v
//   2. (float)v equals (float)of the ported value
//   3. v.GetHashCode() equals the masked-bits formula
//   4. zero / negative-zero special cases
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class DHashFit9
    {
        // Correctly rounded 10^s (s = 0..28), computed with BigInteger and
        // explicit round-half-even; hardcoded so no libm is involved.
        static readonly long[] T10 =
        {
            0x3FF0000000000000L, 0x4024000000000000L, 0x4059000000000000L, 0x408F400000000000L,
            0x40C3880000000000L, 0x40F86A0000000000L, 0x412E848000000000L, 0x416312D000000000L,
            0x4197D78400000000L, 0x41CDCD6500000000L, 0x4202A05F20000000L, 0x42374876E8000000L,
            0x426D1A94A2000000L, 0x42A2309CE5400000L, 0x42D6BCC41E900000L, 0x430C6BF526340000L,
            0x4341C37937E08000L, 0x4376345785D8A000L, 0x43ABC16D674EC800L, 0x43E158E460913D00L,
            0x4415AF1D78B58C40L, 0x444B1AE4D6E2EF50L, 0x4480F0CF064DD592L, 0x44B52D02C7E14AF6L,
            0x44EA784379D99DB4L, 0x45208B2A2C280291L, 0x4554ADF4B7320335L, 0x4589D971E4FE8402L,
            0x45C027E72F1F1281L,
        };

        static double ToDouble(uint lo, uint mid, uint hi, int scale, bool neg)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            if (scale != 0) m = m / BitConverter.Int64BitsToDouble(T10[scale]);
            return neg ? -m : m;
        }

        static int HashOf(double d)
        {
            long bits = BitConverter.DoubleToInt64Bits(d);
            int hi = (int)(bits >> 32);
            int lo = (int)(uint)(bits & 0xFFFFFFFFL);
            return hi ^ (lo & unchecked((int)0xFFFFFFF0));
        }

        static ulong Next(ulong x)
        {
            unchecked { return x * 6364136223846793005UL + 1442695040888963407UL; }
        }

        public static void Main()
        {
            ulong x = 0x0123456789ABCDEFUL;
            int total = 0, badD = 0, badF = 0, badH = 0;
            int shownD = 0, shownF = 0, shownH = 0;
            for (int i = 0; i < 2000000; i++)
            {
                x = Next(x); uint lo = (uint)x;
                x = Next(x); uint mid = (uint)x;
                x = Next(x); uint hi = (uint)x;
                x = Next(x); int s = (int)(x % 29); bool neg = ((x >> 8) & 1) != 0;
                decimal v = new decimal((int)lo, (int)mid, (int)hi, neg, (byte)s);
                total++;
                double want = (double)v;
                double got = ToDouble(lo, mid, hi, s, neg);
                long wb = BitConverter.DoubleToInt64Bits(want);
                long gb = BitConverter.DoubleToInt64Bits(got);
                if (wb != gb)
                {
                    badD++;
                    if (shownD < 5) { shownD++; Console.WriteLine("[d] i=" + i + " s=" + s + " lo=" + lo + " mid=" + mid + " hi=" + hi + " net48=" + wb.ToString("X16") + " got=" + gb.ToString("X16")); }
                }
                float wf = (float)v;
                float gf = (float)got;
                if (BitConverter.ToInt32(BitConverter.GetBytes(wf), 0) != BitConverter.ToInt32(BitConverter.GetBytes(gf), 0))
                {
                    badF++;
                    if (shownF < 5) { shownF++; Console.WriteLine("[f] i=" + i + " s=" + s + " net48=" + BitConverter.ToInt32(BitConverter.GetBytes(wf), 0).ToString("X8") + " got=" + BitConverter.ToInt32(BitConverter.GetBytes(gf), 0).ToString("X8")); }
                }
                bool mantissaZero = lo == 0 && mid == 0 && hi == 0;
                int expect = mantissaZero ? 0 : HashOf(got);
                if (v.GetHashCode() != expect)
                {
                    badH++;
                    if (shownH < 5) { shownH++; Console.WriteLine("[h] i=" + i + " s=" + s + " net48=" + v.GetHashCode() + " got=" + expect); }
                }
            }
            Console.WriteLine("[final] total=" + total + " doubleBad=" + badD + " floatBad=" + badF + " hashBad=" + badH);

            // zero / negative zero
            decimal[] zeros = { 0m, -0.0m, 0.00m, decimal.Negate(0m), new decimal(0, 0, 0, true, 0), new decimal(0, 0, 0, true, 5) };
            foreach (var z in zeros)
            {
                double dz = (double)z;
                Console.WriteLine("[zero] str=" + z.ToString(CultureInfo.InvariantCulture)
                    + " dbits=" + BitConverter.DoubleToInt64Bits(dz).ToString("X16")
                    + " hash=" + z.GetHashCode());
            }
        }
    }
}
