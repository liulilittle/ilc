//------------------------------------------------------------------------------
// dhashfit.cs -- RESEARCH ONLY (net48 oracle). Fits the exact net48
// decimal -> double conversion by enumerating candidate algorithms over a
// large random sample, so the hash port can reproduce net48 bit-for-bit
// instead of guessing.
//
// Candidates (all produce the IEEE-754 bits of the magnitude, sign applied last):
//   A  hi*2^64 + lo64 (both as double, left-assoc add) then / pow10[scale]
//   B  (hi*2^32 + mid) as double, then *2^32 + lo, then / pow10[scale]
//   C  correctly rounded (BigInteger exact quotient, round-half-even)
//   D  lo64 as double + hi*2^64 in the OTHER add order (lo64 + hi*2^64)
//   E  A but divide via ldexp-style: multiply by 2^-k and divide by 5^scale
//   F  A but the power table built by repeated multiplication (10.0^scale)
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Numerics;

namespace Cases
{
    public static class DHashFit
    {
        static readonly double[] Pow10 = BuildPow10();

        static double[] BuildPow10()
        {
            double[] p = new double[29];
            p[0] = 1.0;
            for (int i = 1; i < p.Length; i++) p[i] = p[i - 1] * 10.0;
            return p;
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

        static double CandC(int lo, int mid, int hi, int scale)
        {
            BigInteger m = ((BigInteger)(uint)hi << 64) | ((BigInteger)(uint)mid << 32) | (uint)lo;
            if (m.IsZero) return 0.0;
            BigInteger den = BigInteger.Pow(10, scale);
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

        static double CandA(uint lo, uint mid, uint hi, int scale)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            return scale > 0 ? m / Pow10[scale] : m;
        }

        static double CandD(uint lo, uint mid, uint hi, int scale)
        {
            double m = (double)((ulong)lo | ((ulong)mid << 32)) + (double)hi * 18446744073709551616.0;
            return scale > 0 ? m / Pow10[scale] : m;
        }

        static double CandB(uint lo, uint mid, uint hi, int scale)
        {
            double m = (double)hi * 4294967296.0 + (double)mid;
            m = m * 4294967296.0 + (double)lo;
            return scale > 0 ? m / Pow10[scale] : m;
        }

        static double CandE(uint lo, uint mid, uint hi, int scale)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            if (scale == 0) return m;
            double p5 = Math.Pow(5.0, scale);
            return Math.Pow(2.0, -scale) * (m / p5);
        }

        static double CandE2(uint lo, uint mid, uint hi, int scale)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            if (scale == 0) return m;
            double p5 = Math.Pow(5.0, scale);
            return (m / p5) * Math.Pow(2.0, -scale);
        }

        static double CandE3(uint lo, uint mid, uint hi, int scale)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            if (scale == 0) return m;
            double p5 = Math.Pow(5.0, scale);
            return m * Math.Pow(2.0, -scale) / p5;
        }

        static double CandE4(uint lo, uint mid, uint hi, int scale)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            if (scale == 0) return m;
            double p5 = Math.Pow(5.0, scale);
            return m / (p5 * Math.Pow(2.0, scale));
        }

        static double CandF(uint lo, uint mid, uint hi, int scale)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            if (scale == 0) return m;
            double p = 1.0;
            for (int i = 0; i < scale; i++) p *= 10.0;
            return m / p;
        }

        public static void Main()
        {
            int nA = 0, nB = 0, nC = 0, nD = 0, nE = 0, nF = 0, total = 0, nE2 = 0, nE3 = 0, nE4 = 0;
            var rnd = new Random(20261023);
            for (int i = 0; i < 20000; i++)
            {
                int lo = rnd.Next(int.MinValue, int.MaxValue);
                int mid = rnd.Next(int.MinValue, int.MaxValue);
                int hi = rnd.Next(int.MinValue, int.MaxValue);
                int scale = rnd.Next(0, 29);
                bool neg = rnd.Next(2) == 1;
                decimal v;
                try { v = new decimal(lo, mid, hi, neg, (byte)scale); }
                catch (ArgumentException) { continue; }
                long want = BitConverter.DoubleToInt64Bits((double)v);
                uint ulo = (uint)lo, umid = (uint)mid, uhi = (uint)hi;
                double s = neg ? -1.0 : 1.0;
                total++;
                if (BitConverter.DoubleToInt64Bits(s * CandA(ulo, umid, uhi, scale)) != want) nA++;
                if (BitConverter.DoubleToInt64Bits(s * CandB(ulo, umid, uhi, scale)) != want) nB++;
                if (BitConverter.DoubleToInt64Bits(s * CandC(lo, mid, hi, scale)) != want) nC++;
                if (BitConverter.DoubleToInt64Bits(s * CandD(ulo, umid, uhi, scale)) != want) nD++;
                if (BitConverter.DoubleToInt64Bits(s * CandE(ulo, umid, uhi, scale)) != want) nE++;
                if (BitConverter.DoubleToInt64Bits(s * CandF(ulo, umid, uhi, scale)) != want) nF++;
                if (BitConverter.DoubleToInt64Bits(s * CandE2(ulo, umid, uhi, scale)) != want) nE2++;
                if (BitConverter.DoubleToInt64Bits(s * CandE3(ulo, umid, uhi, scale)) != want) nE3++;
                if (BitConverter.DoubleToInt64Bits(s * CandE4(ulo, umid, uhi, scale)) != want) nE4++;
            }
            Console.WriteLine("[fit] total=" + total
                + " A=" + nA + " B=" + nB + " C=" + nC
                + " D=" + nD + " E=" + nE + " E2=" + nE2 + " E3=" + nE3 + " E4=" + nE4 + " F=" + nF);

            // Second pass: concentrate on the regions where A and C disagree,
            // and report which candidate net48 follows there.
            int t2 = 0, a2 = 0, c2 = 0, b2 = 0, d2 = 0, e2 = 0, f2 = 0;
            var rnd2 = new Random(777);
            for (int i = 0; i < 200000; i++)
            {
                int lo = rnd2.Next(int.MinValue, int.MaxValue);
                int mid = rnd2.Next(int.MinValue, int.MaxValue);
                int hi = rnd2.Next(int.MinValue, int.MaxValue);
                int scale = rnd2.Next(0, 29);
                bool neg = rnd2.Next(2) == 1;
                decimal v;
                try { v = new decimal(lo, mid, hi, neg, (byte)scale); }
                catch (ArgumentException) { continue; }
                uint ulo = (uint)lo, umid = (uint)mid, uhi = (uint)hi;
                double a = CandA(ulo, umid, uhi, scale);
                double c = CandC(lo, mid, hi, scale);
                if (BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(c)) continue;
                long want = BitConverter.DoubleToInt64Bits((double)v);
                double s = neg ? -1.0 : 1.0;
                t2++;
                if (BitConverter.DoubleToInt64Bits(s * a) == want) a2++;
                if (BitConverter.DoubleToInt64Bits(s * c) == want) c2++;
                if (BitConverter.DoubleToInt64Bits(s * CandB(ulo, umid, uhi, scale)) == want) b2++;
                if (BitConverter.DoubleToInt64Bits(s * CandD(ulo, umid, uhi, scale)) == want) d2++;
                if (BitConverter.DoubleToInt64Bits(s * CandE(ulo, umid, uhi, scale)) == want) e2++;
                if (BitConverter.DoubleToInt64Bits(s * CandF(ulo, umid, uhi, scale)) == want) f2++;
            }
            Console.WriteLine("[fit2] A!=C cases=" + t2 + " A=" + a2 + " B=" + b2 + " C=" + c2 + " D=" + d2 + " E=" + e2 + " F=" + f2);
        }
    }
}
