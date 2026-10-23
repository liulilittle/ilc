//------------------------------------------------------------------------------
// dhashcanon2.cs -- RESEARCH ONLY. Emits exact net48 oracle rows for the
// "trailing zero changes the hash" assertion (large-mantissa case), printing
// BOTH mantissa triples so the C++ test can rebuild them verbatim.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class DHashCanon2
    {
        static void Show(string tag, decimal a, decimal b)
        {
            int[] ga = decimal.GetBits(a);
            int[] gb = decimal.GetBits(b);
            Console.WriteLine(tag
                + " A=" + ga[0] + "," + ga[1] + "," + ga[2] + "," + ga[3]
                + " B=" + gb[0] + "," + gb[1] + "," + gb[2] + "," + gb[3]
                + " equal=" + (a == b ? "1" : "0")
                + " hashA=" + a.GetHashCode()
                + " hashB=" + b.GetHashCode()
                + " dA=" + BitConverter.DoubleToInt64Bits((double)a).ToString("X16")
                + " dB=" + BitConverter.DoubleToInt64Bits((double)b).ToString("X16"));
        }

        public static void Main()
        {
            // A 96-bit mantissa whose m*10 also fits in 96 bits, at a scale
            // where the two rounded doubles differ.
            // m = hi 289413371 (0x1140FFFB) etc. Use a constructed case:
            long lo = -2020474143L, mid = -1267424804L, hi = 289413371L;
            int scale = 14;
            decimal a = new decimal((int)lo, (int)mid, (int)hi, false, (byte)scale);
            // m * 10 exact, 96-bit safe (hi < 0x19999999)
            ulong lo64 = (ulong)(uint)lo | ((ulong)(uint)mid << 32);
            ulong p = lo64 * 10UL;                 // low 64 bits
            ulong hiCarry = ((ulong)(uint)hi * 10UL) + (p < lo64 ? 1UL : 0UL);
            // exact: build with BigInteger to be safe
            var big = (new System.Numerics.BigInteger((uint)hi) << 64)
                      | (new System.Numerics.BigInteger((uint)mid) << 32)
                      | (uint)lo;
            big *= 10;
            int nlo = (int)(uint)(big & 0xFFFFFFFF);
            int nmid = (int)(uint)((big >> 32) & 0xFFFFFFFF);
            int nhi = (int)(uint)((big >> 64) & 0xFFFFFFFF);
            decimal b = new decimal(nlo, nmid, nhi, false, (byte)(scale + 1));
            Show("case1", a, b);

            // A second, smaller case that is easier to reason about.
            decimal c = 1.234567890123456789012345m;      // scale 24
            decimal d = 1.2345678901234567890123450m;     // same value, scale 25
            Show("case2", c, d);

            // A case where the hash DOES stay equal (small mantissa).
            decimal e = 1.5m, f = 1.50m;
            Show("case3", e, f);
        }
    }
}
