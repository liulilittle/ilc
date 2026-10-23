//------------------------------------------------------------------------------
// dhashfit8.cs -- RESEARCH ONLY, DECISIVE #2.
//
// (a) Computes the CORRECTLY ROUNDED bits of 5^s and 10^s for s = 0..28 by
//     exact BigInteger arithmetic + explicit round-half-even, and compares
//     them against (i) Math.Pow and (ii) the tables used by the earlier fits.
// (b) Re-runs the SAME LCG sequence as cpp_xcheck.cpp with FOUR variants so
//     the net48 vote is unambiguous:
//       V_pow   = M / Math.Pow(10,s)
//       V_pow5  = (M / Math.Pow(5,s)) * 2^-s
//       V_exact = M / exactP10[s]
//       V_exact5= (M / exactP5[s]) * 2^-s
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Numerics;

namespace Cases
{
    public static class DHashFit8
    {
        static int BitLen(BigInteger v)
        {
            if (v.IsZero) return 0;
            byte[] b = v.ToByteArray();
            int n = b.Length - 1;
            int top = b[n];
            int bits = 0;
            while (top != 0) { bits++; top >>= 1; }
            return n * 8 + bits;
        }

        // Correctly rounded BigInteger -> double (round half to even).
        static double RoundToDouble(BigInteger v)
        {
            if (v.IsZero) return 0.0;
            bool neg = v.Sign < 0;
            if (neg) v = -v;
            int bits = BitLen(v);
            if (bits <= 53) return neg ? -(double)(long)v : (double)(long)v;
            int shift = bits - 53;
            BigInteger q = v >> shift;
            BigInteger rem = v - (q << shift);
            BigInteger half = BigInteger.One << (shift - 1);
            int cmp = rem.CompareTo(half);
            if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += 1;
            // q may now be 2^53; scale handles it
            double result = 0.0;
            // build via exact power-of-two scaling of an integer <= 2^53
            double qd = (double)(long)q;
            result = Math.Pow(2.0, shift) * qd;
            return neg ? -result : result;
        }

        static ulong Next(ulong x)
        {
            unchecked { return x * 6364136223846793005UL + 1442695040888963407UL; }
        }

        public static void Main()
        {
            var e10 = new long[29];
            var e5 = new long[29];
            for (int s = 0; s <= 28; s++)
            {
                e10[s] = BitConverter.DoubleToInt64Bits(RoundToDouble(BigInteger.Pow(10, s)));
                e5[s] = BitConverter.DoubleToInt64Bits(RoundToDouble(BigInteger.Pow(5, s)));
            }
            int m10 = 0, m5 = 0;
            for (int s = 0; s <= 28; s++)
            {
                long lp10 = BitConverter.DoubleToInt64Bits(Math.Pow(10.0, s));
                long lp5 = BitConverter.DoubleToInt64Bits(Math.Pow(5.0, s));
                if (lp10 != e10[s]) { m10++; Console.WriteLine("[p10] s=" + s + " MathPow=" + lp10.ToString("X16") + " exact=" + e10[s].ToString("X16")); }
                if (lp5 != e5[s]) { m5++; Console.WriteLine("[p5 ] s=" + s + " MathPow=" + lp5.ToString("X16") + " exact=" + e5[s].ToString("X16")); }
            }
            Console.WriteLine("[exact] MathPow10Mismatch=" + m10 + " MathPow5Mismatch=" + m5);

            // exact tables are the authority; emit them for the C++ port
            Console.Write("[tab10] ");
            for (int s = 0; s <= 28; s++) Console.Write("0x" + e10[s].ToString("X16") + (s == 28 ? "\n" : ", "));
            Console.Write("[tab5 ] ");
            for (int s = 0; s <= 28; s++) Console.Write("0x" + e5[s].ToString("X16") + (s == 28 ? "\n" : ", "));

            // ---- vote on the LCG sequence ----
            ulong x = 0x0123456789ABCDEFUL;
            int bPow = 0, bPow5 = 0, bEx = 0, bEx5 = 0, total = 0;
            for (int i = 0; i < 200000; i++)
            {
                x = Next(x); uint lo = (uint)x;
                x = Next(x); uint mid = (uint)x;
                x = Next(x); uint hi = (uint)x;
                x = Next(x); int s = (int)(x % 29); bool neg = ((x >> 8) & 1) != 0;
                decimal v = new decimal((int)lo, (int)mid, (int)hi, neg, (byte)s);
                long want = BitConverter.DoubleToInt64Bits((double)v);
                double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
                double vPow, vPow5, vEx, vEx5;
                if (s == 0) { vPow = vPow5 = vEx = vEx5 = m; }
                else
                {
                    vPow = m / Math.Pow(10.0, s);
                    vPow5 = (m / Math.Pow(5.0, s)) * Math.Pow(2.0, -s);
                    vEx = m / BitConverter.Int64BitsToDouble(e10[s]);
                    vEx5 = (m / BitConverter.Int64BitsToDouble(e5[s])) * Math.Pow(2.0, -s);
                }
                if (neg) { vPow = -vPow; vPow5 = -vPow5; vEx = -vEx; vEx5 = -vEx5; }
                total++;
                if (BitConverter.DoubleToInt64Bits(vPow) != want) bPow++;
                if (BitConverter.DoubleToInt64Bits(vPow5) != want) bPow5++;
                if (BitConverter.DoubleToInt64Bits(vEx) != want) bEx++;
                if (BitConverter.DoubleToInt64Bits(vEx5) != want) bEx5++;
            }
            Console.WriteLine("[vote] total=" + total + " pow10_bad=" + bPow + " pow5_bad=" + bPow5
                + " exact10_bad=" + bEx + " exact5_bad=" + bEx5);
        }
    }
}
