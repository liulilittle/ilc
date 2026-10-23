//------------------------------------------------------------------------------
// dhashfit4.cs -- RESEARCH ONLY. Final validation of the fitted net48
// decimal -> double algorithm (E) and of the net48 hash formula.
//
//   E(lo,mid,hi,scale) = 2^-scale * (M / 5^scale)   with M = hi*2^64 + lo64
//                        (two IEEE roundings: the divide, then the exact
//                         power-of-two scale)
//
// Checks:
//   1. E reproduces net48 (double)d bit-for-bit on adversarial random inputs
//      (sign included) -- the earlier runs dropped the sign.
//   2. Math.Pow(5.0, s) == the CORRECTLY ROUNDED 5^s for s = 0..28, so a
//      hardcoded table in C++ is bit-identical to what net48 sees.
//   3. Math.Pow(2.0, -s) == the exact 2^-s.
//   4. E is value-canonical: a decimal and the same value written with one
//      extra trailing zero (mantissa*10, scale+1) convert identically, so
//      equal decimals keep equal hashes.
//   5. hash formula == (int)(hi32 ^ (lo32 & 0xFFFFFFF0)) of E, 0 for zero.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Numerics;

namespace Cases
{
    public static class DHashFit4
    {
        static double M(uint lo, uint mid, uint hi)
        {
            return (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
        }

        static double E(uint lo, uint mid, uint hi, int s)
        {
            if (s == 0) return M(lo, mid, hi);
            return Math.Pow(2.0, -s) * (M(lo, mid, hi) / Math.Pow(5.0, s));
        }

        static int HashOf(double d)
        {
            long bits = BitConverter.DoubleToInt64Bits(d);
            return (int)(bits >> 32) ^ (int)(bits & unchecked((long)0xFFFFFFF0));
        }

        // correctly rounded 5^s as a double
        static double ExactPow5(int s)
        {
            BigInteger p = BigInteger.Pow(5, s);
            int bits = BitLen(p);
            if (bits <= 53) return (double)p;
            int shift = bits - 53;
            BigInteger q = p >> shift;
            BigInteger rem = p - (q << shift);
            BigInteger half = BigInteger.One << (shift - 1);
            int cmp = rem.CompareTo(half);
            if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += 1;
            return (double)q * Math.Pow(2.0, shift);
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

        public static void Main()
        {
            // ---- 1. pow tables ----
            int pow5Bad = 0, pow2Bad = 0;
            for (int s = 0; s <= 28; s++)
            {
                double got = Math.Pow(5.0, s), want = ExactPow5(s);
                if (BitConverter.DoubleToInt64Bits(got) != BitConverter.DoubleToInt64Bits(want))
                {
                    pow5Bad++;
                    Console.WriteLine("[pow5] s=" + s + " got=" + BitConverter.DoubleToInt64Bits(got).ToString("X16")
                        + " exact=" + BitConverter.DoubleToInt64Bits(want).ToString("X16"));
                }
                double g2 = Math.Pow(2.0, -s), w2 = 1.0 / Math.Pow(2.0, s);
                if (BitConverter.DoubleToInt64Bits(g2) != BitConverter.DoubleToInt64Bits(w2)) pow2Bad++;
            }
            Console.WriteLine("[pow] pow5Mismatch=" + pow5Bad + " pow2Mismatch=" + pow2Bad);

            // ---- 2. adversarial E vs net48 (sign included) ----
            var rnd = new Random(1234321);
            int total = 0, bad = 0, badHash = 0, badHashZero = 0;
            for (int i = 0; i < 2000000; i++)
            {
                int lo = rnd.Next(int.MinValue, int.MaxValue);
                int mid = rnd.Next(int.MinValue, int.MaxValue);
                int hi = rnd.Next(int.MinValue, int.MaxValue);
                int s = rnd.Next(0, 29);
                bool neg = rnd.Next(2) == 1;
                decimal v;
                try { v = new decimal(lo, mid, hi, neg, (byte)s); }
                catch (ArgumentException) { continue; }
                total++;
                double want = (double)v;
                double got = E((uint)lo, (uint)mid, (uint)hi, s);
                if (neg) got = -got;
                long wb = BitConverter.DoubleToInt64Bits(want);
                long gb = BitConverter.DoubleToInt64Bits(got);
                if (wb != gb)
                {
                    bad++;
                    if (bad <= 10)
                        Console.WriteLine("[badE] s=" + s + " neg=" + (neg ? 1 : 0) + " gb=" + lo + "," + mid + "," + hi
                            + " want=" + wb.ToString("X16") + " got=" + gb.ToString("X16"));
                }
                if (v.GetHashCode() != HashOf(got)) badHash++;
                bool isZero = lo == 0 && mid == 0 && hi == 0;
                int expHash = isZero ? 0 : HashOf(got);
                if (v.GetHashCode() != expHash) badHashZero++;
            }
            Console.WriteLine("[E] total=" + total + " badE=" + bad + " badHash=" + badHash + " badHashZero=" + badHashZero);

            // ---- 3. canonicalization: same value, extra trailing zero ----
            var rnd2 = new Random(555);
            int canonTotal = 0, canonBad = 0;
            for (int i = 0; i < 200000; i++)
            {
                int lo = rnd2.Next(int.MinValue, int.MaxValue);
                int mid = rnd2.Next(int.MinValue, int.MaxValue);
                int hi = rnd2.Next(int.MinValue, int.MaxValue);
                int s = rnd2.Next(0, 28);
                bool neg = rnd2.Next(2) == 1;
                // m * 10 must fit in 96 bits
                BigInteger mm = ((BigInteger)(uint)hi << 64) | ((BigInteger)(uint)mid << 32) | (uint)lo;
                if (mm >= (BigInteger.One << 93)) continue;
                BigInteger m10 = mm * 10;
                int lo2 = (int)(uint)(m10 & 0xFFFFFFFF);
                int mid2 = (int)(uint)((m10 >> 32) & 0xFFFFFFFF);
                int hi2 = (int)(uint)((m10 >> 64) & 0xFFFFFFFF);
                decimal a, b;
                try
                {
                    a = new decimal(lo, mid, hi, neg, (byte)s);
                    b = new decimal(lo2, mid2, hi2, neg, (byte)(s + 1));
                }
                catch (ArgumentException) { continue; }
                canonTotal++;
                if (a != b) { canonBad += 1000000; continue; }
                if (a.GetHashCode() != b.GetHashCode())
                {
                    canonBad++;
                    if (canonBad <= 10)
                        Console.WriteLine("[canon] gb=" + lo + "," + mid + "," + hi + ",s=" + s
                            + " hashA=" + a.GetHashCode() + " hashB=" + b.GetHashCode());
                }
            }
            Console.WriteLine("[canon] total=" + canonTotal + " bad=" + canonBad);

            // ---- 4. our current runtime algorithm (A) vs net48 on same sample ----
            int aBad = 0, aHashBad = 0;
            var rnd3 = new Random(1234321);
            int t3 = 0;
            for (int i = 0; i < 2000000; i++)
            {
                int lo = rnd3.Next(int.MinValue, int.MaxValue);
                int mid = rnd3.Next(int.MinValue, int.MaxValue);
                int hi = rnd3.Next(int.MinValue, int.MaxValue);
                int s = rnd3.Next(0, 29);
                bool neg = rnd3.Next(2) == 1;
                decimal v;
                try { v = new decimal(lo, mid, hi, neg, (byte)s); }
                catch (ArgumentException) { continue; }
                t3++;
                double m = M((uint)lo, (uint)mid, (uint)hi);
                if (s > 0) m = m / Math.Pow(10.0, s);
                if (neg) m = -m;
                if (BitConverter.DoubleToInt64Bits(m) != BitConverter.DoubleToInt64Bits((double)v)) aBad++;
                if (HashOf(m) != v.GetHashCode()) aHashBad++;
            }
            Console.WriteLine("[A] total=" + t3 + " badConv=" + aBad + " badHash=" + aHashBad);
        }
    }
}
