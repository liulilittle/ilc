//------------------------------------------------------------------------------
// dhashfit2.cs -- RESEARCH ONLY. Per-scale discrimination of the net48
// decimal -> double algorithm. Prints a per-scale mismatch table so the
// winning candidate must match on EVERY scale, not just in aggregate.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Numerics;

namespace Cases
{
    public static class DHashFit2
    {
        static readonly double[] Pow10 = BuildPow10();
        static double[] BuildPow10()
        {
            double[] p = new double[29];
            p[0] = 1.0;
            for (int i = 1; i < p.Length; i++) p[i] = p[i - 1] * 10.0;
            return p;
        }

        static double P5(int s) { return Math.Pow(5.0, s); }

        static double A(uint lo, uint mid, uint hi, int s)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            return s > 0 ? m / Pow10[s] : m;
        }
        static double B(uint lo, uint mid, uint hi, int s)
        {
            double m = (double)hi * 4294967296.0 + (double)mid;
            m = m * 4294967296.0 + (double)lo;
            return s > 0 ? m / Pow10[s] : m;
        }
        static double E(uint lo, uint mid, uint hi, int s)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            if (s == 0) return m;
            return Math.Pow(2.0, -s) * (m / P5(s));
        }
        static double E2(uint lo, uint mid, uint hi, int s)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            if (s == 0) return m;
            return (m / P5(s)) * Math.Pow(2.0, -s);
        }
        static double E3(uint lo, uint mid, uint hi, int s)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            if (s == 0) return m;
            return m * Math.Pow(2.0, -s) / P5(s);
        }
        static double E4(uint lo, uint mid, uint hi, int s)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            if (s == 0) return m;
            return m / (P5(s) * Math.Pow(2.0, s));
        }
        static double G(uint lo, uint mid, uint hi, int s)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            if (s == 0) return m;
            return m * Math.Pow(10.0, -s);
        }
        static double R(uint lo, uint mid, uint hi, int s)
        {
            double m = (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
            for (int i = 0; i < s; i++) m /= 10.0;
            return m;
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

        static double C(int lo, int mid, int hi, int s)
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

        public static void Main()
        {
            string[] names = { "A", "B", "C", "E", "E2", "E3", "E4", "G", "R" };
            var bad = new int[names.Length, 29];
            var tot = new int[29];
            var rnd = new Random(424242);
            for (int i = 0; i < 400000; i++)
            {
                int lo = rnd.Next(int.MinValue, int.MaxValue);
                int mid = rnd.Next(int.MinValue, int.MaxValue);
                int hi = rnd.Next(int.MinValue, int.MaxValue);
                int s = rnd.Next(0, 29);
                bool neg = rnd.Next(2) == 1;
                decimal v;
                try { v = new decimal(lo, mid, hi, neg, (byte)s); }
                catch (ArgumentException) { continue; }
                long want = BitConverter.DoubleToInt64Bits((double)v);
                uint ulo = (uint)lo, umid = (uint)mid, uhi = (uint)hi;
                double sg = neg ? -1.0 : 1.0;
                tot[s]++;
                double[] cand = {
                    sg * A(ulo, umid, uhi, s), sg * B(ulo, umid, uhi, s), sg * C(lo, mid, hi, s),
                    sg * E(ulo, umid, uhi, s), sg * E2(ulo, umid, uhi, s), sg * E3(ulo, umid, uhi, s),
                    sg * E4(ulo, umid, uhi, s), sg * G(ulo, umid, uhi, s), sg * R(ulo, umid, uhi, s) };
                for (int k = 0; k < names.Length; k++)
                {
                    if (BitConverter.DoubleToInt64Bits(cand[k]) != want) bad[k, s]++;
                }
            }
            for (int s = 0; s < 29; s++)
            {
                string line = "s=" + s.ToString("00") + " n=" + tot[s].ToString();
                for (int k = 0; k < names.Length; k++) line += " " + names[k] + "=" + bad[k, s];
                Console.WriteLine("[per] " + line);
            }
            string sum = "[sum]";
            for (int k = 0; k < names.Length; k++)
            {
                int t = 0;
                for (int s = 0; s < 29; s++) t += bad[k, s];
                sum += " " + names[k] + "=" + t;
            }
            Console.WriteLine(sum);
        }
    }
}
