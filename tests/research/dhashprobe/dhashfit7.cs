//------------------------------------------------------------------------------
// dhashfit7.cs -- RESEARCH ONLY, DECISIVE.
//
// The earlier fits used Math.Pow, whose bits are NOT guaranteed to match the
// C++ std::pow the shipped runtime will call. This probe removes libm from
// the equation: every power of ten / five is a HARDCODED, correctly rounded
// bit pattern, so the C# oracle and the C++ port consume byte-identical
// constants. It then asks net48 which expression it follows.
//
//   C1 = fl( M / P10[s] )                 single rounding (divide by 10^s)
//   C2 = fl( M / P5[s] ) * 2^-s           divide by 5^s, then exact pow2 scale
//   C3 = correctly rounded (BigInteger exact quotient, half-even)
//
// Also prints, per scale, whether P10[s] == P5[s] * 2^s as an EXACT real
// (i.e. whether the two expressions are mathematically the same division).
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Numerics;

namespace Cases
{
    public static class DHashFit7
    {
        // Correctly rounded 10^s, s = 0..28 (generated from BigInteger).
        static readonly long[] P10 =
        {
            0x3FF0000000000000L, 0x4024000000000000L, 0x4059000000000000L, 0x408F400000000000L,
            0x40C3880000000000L, 0x40F86A0000000000L, 0x412E848000000000L, 0x416312D000000000L,
            0x4197D78400000000L, 0x41CDCD6500000000L, 0x4202A05F20000000L, 0x42374876E8000000L,
            0x426D1A94A2000000L, 0x42A2309CE5400000L, 0x42D6BCC41E900000L, 0x430C6BF526340000L,
            0x4341C37937E08000L, 0x4376345785D8A000L, 0x43ABC16D674EC800L, 0x43E158E460913D00L,
            0x4415AF1D78B58C40L, 0x444B1AE4D6E2EF50L, 0x4480F0CF064DD592L, 0x44B52D02C7E14AF6L,
            0x44EA784379D99DB4L, 0x45208B2A2C280290L, 0x4554ADF4B7320334L, 0x4589D971E4FE8401L,
            0x45C027E72F1F1281L,
        };

        // Correctly rounded 5^s, s = 0..28.
        static readonly long[] P5 =
        {
            0x3FF0000000000000L, 0x4014000000000000L, 0x4039000000000000L, 0x405F400000000000L,
            0x4083880000000000L, 0x40A86A0000000000L, 0x40CE848000000000L, 0x40F312D000000000L,
            0x4117D78400000000L, 0x413DCD6500000000L, 0x4162A05F20000000L, 0x41874876E8000000L,
            0x41AD1A94A2000000L, 0x41D2309CE5400000L, 0x41F6BCC41E900000L, 0x421C6BF526340000L,
            0x4241C37937E08000L, 0x4266345785D8A000L, 0x428BC16D674EC800L, 0x42B158E460913D00L,
            0x42D5AF1D78B58C40L, 0x42FB1AE4D6E2EF50L, 0x4320F0CF064DD592L, 0x43452D02C7E14AF6L,
            0x436A784379D99DB4L, 0x43908B2A2C280290L, 0x43B4ADF4B7320334L, 0x43D9D971E4FE8401L,
            0x440027E72F1F1281L,
        };

        static double D(long bits) { return BitConverter.Int64BitsToDouble(bits); }

        static double MOf(uint lo, uint mid, uint hi)
        {
            return (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
        }

        static double C1(uint lo, uint mid, uint hi, int s)
        {
            if (s == 0) return MOf(lo, mid, hi);
            return MOf(lo, mid, hi) / D(P10[s]);
        }

        static double C2(uint lo, uint mid, uint hi, int s)
        {
            if (s == 0) return MOf(lo, mid, hi);
            return (MOf(lo, mid, hi) / D(P5[s])) * Math.Pow(2.0, -s);
        }

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

        static double C3(int lo, int mid, int hi, int s)
        {
            BigInteger m = ((BigInteger)(uint)hi << 64) | ((BigInteger)(uint)mid << 32) | (uint)lo;
            if (m.IsZero) return 0.0;
            BigInteger den = BigInteger.Pow(10, s);
            int e = BitLen(m) - BitLen(den);
            while (true)
            {
                if (e >= 0)
                {
                    if (m < (den << e)) { e--; continue; }
                    if (m >= (den << (e + 1))) { e++; continue; }
                }
                else
                {
                    if ((m << (-e)) < den) { e--; continue; }
                    if ((m << (-(e + 1))) >= den) { e++; continue; }
                }
                break;
            }
            int shift = 52 - e;
            BigInteger num = m, d2 = den;
            if (shift >= 0) num = m << shift; else d2 = den << (-shift);
            BigInteger q = BigInteger.DivRem(num, d2, out BigInteger r);
            int cmp = (r << 1).CompareTo(d2);
            if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += 1;
            if (q == (BigInteger.One << 53)) { q >>= 1; e++; }
            return (double)q * Math.Pow(2.0, e - 52);
        }

        static ulong Next(ulong x)
        {
            unchecked { return x * 6364136223846793005UL + 1442695040888963407UL; }
        }

        public static void Main()
        {
            // Exactness audit of the two tables: P10[s] must equal P5[s]*2^s
            // exactly (both are correctly rounded, and 10^s = 5^s * 2^s).
            for (int s = 0; s <= 28; s++)
            {
                double p10 = D(P10[s]);
                double p5scaled = D(P5[s]) * Math.Pow(2.0, s);
                if (BitConverter.DoubleToInt64Bits(p10) != BitConverter.DoubleToInt64Bits(p5scaled))
                    Console.WriteLine("[tbl] s=" + s + " P10=" + P10[s].ToString("X16") + " P5*2^s="
                        + BitConverter.DoubleToInt64Bits(p5scaled).ToString("X16"));
            }
            Console.WriteLine("[tbl] done");

            // libm comparison: Math.Pow(10,s) and Math.Pow(5,s) vs the tables.
            int mp10 = 0, mp5 = 0;
            for (int s = 0; s <= 28; s++)
            {
                if (BitConverter.DoubleToInt64Bits(Math.Pow(10.0, s)) != P10[s]) mp10++;
                if (BitConverter.DoubleToInt64Bits(Math.Pow(5.0, s)) != P5[s]) mp5++;
            }
            Console.WriteLine("[libm] MathPow10Mismatch=" + mp10 + " MathPow5Mismatch=" + mp5);

            // Decisive vote on the LCG sequence (identical to cpp_xcheck.cpp).
            ulong x = 0x0123456789ABCDEFUL;
            int n1 = 0, n2 = 0, n3 = 0, total = 0;
            int shown = 0;
            for (int i = 0; i < 200000; i++)
            {
                x = Next(x); uint lo = (uint)x;
                x = Next(x); uint mid = (uint)x;
                x = Next(x); uint hi = (uint)x;
                x = Next(x); int s = (int)(x % 29); bool neg = ((x >> 8) & 1) != 0;
                decimal v = new decimal((int)lo, (int)mid, (int)hi, neg, (byte)s);
                long want = BitConverter.DoubleToInt64Bits((double)v);
                double c1 = C1(lo, mid, hi, s); if (neg) c1 = -c1;
                double c2 = C2(lo, mid, hi, s); if (neg) c2 = -c2;
                double c3 = C3((int)lo, (int)mid, (int)hi, s); if (neg) c3 = -c3;
                long b1 = BitConverter.DoubleToInt64Bits(c1);
                long b2 = BitConverter.DoubleToInt64Bits(c2);
                long b3 = BitConverter.DoubleToInt64Bits(c3);
                total++;
                if (b1 != want) n1++;
                if (b2 != want) n2++;
                if (b3 != want) n3++;
                if (b1 != b2 && shown < 6)
                {
                    shown++;
                    Console.WriteLine("[c1c2] i=" + i + " s=" + s + " lo=" + lo + " mid=" + mid + " hi=" + hi
                        + " net48=" + want.ToString("X16") + " C1=" + b1.ToString("X16") + " C2=" + b2.ToString("X16"));
                }
            }
            Console.WriteLine("[vote] total=" + total + " C1_bad=" + n1 + " C2_bad=" + n2 + " C3_bad=" + n3);
        }
    }
}
