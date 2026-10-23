//------------------------------------------------------------------------------
// dhashfit5.cs -- RESEARCH ONLY. Decides how net48 converts decimal -> float.
//
//   F1 = (float)E(d)        where E is the fitted net48 decimal->double
//   F2 = correctly rounded (exact rational -> nearest float, half-even)
//   F3 = (float)A(d)        where A = M / pow(10,s)  (our current to_double)
//
// If net48 == F1 everywhere then static_cast<float>(to_double) is already
// correct and icall_decimal_to_float needs no change.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Numerics;

namespace Cases
{
    public static class DHashFit5
    {
        static double M(uint lo, uint mid, uint hi)
        {
            return (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
        }

        // net48 decimal -> double (fitted: E)
        static double E(uint lo, uint mid, uint hi, int s)
        {
            if (s == 0) return M(lo, mid, hi);
            return Math.Pow(2.0, -s) * (M(lo, mid, hi) / Math.Pow(5.0, s));
        }

        // our current runtime to_double (A)
        static double A(uint lo, uint mid, uint hi, int s)
        {
            if (s == 0) return M(lo, mid, hi);
            return M(lo, mid, hi) / Math.Pow(10.0, s);
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

        // correctly rounded exact rational -> float (24-bit significand)
        static float ExactToFloat(int lo, int mid, int hi, int s)
        {
            BigInteger m = ((BigInteger)(uint)hi << 64) | ((BigInteger)(uint)mid << 32) | (uint)lo;
            if (m.IsZero) return 0.0f;
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
            int shift = 23 - e;
            BigInteger num = m, d2 = den;
            if (shift >= 0) num = m << shift; else d2 = den << (-shift);
            BigInteger q = BigInteger.DivRem(num, d2, out BigInteger r);
            int cmp = (r << 1).CompareTo(d2);
            if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += 1;
            if (q == (BigInteger.One << 24)) { q >>= 1; e++; }
            // assemble the float bit pattern directly (double math would round twice)
            int exp = e + 127;
            if (exp <= 0) return (float)((double)q * Math.Pow(2.0, e - 23));
            ulong frac = (ulong)(q - (BigInteger.One << 23));
            uint bits = ((uint)exp << 23) | (uint)(frac & 0x7FFFFF);
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        public static void Main()
        {
            int nF1 = 0, nF2 = 0, nF3 = 0, total = 0;
            var rnd = new Random(31337);
            for (int i = 0; i < 500000; i++)
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
                uint ulo = (uint)lo, umid = (uint)mid, uhi = (uint)hi;
                float want = (float)v;
                int wantBits = BitConverter.ToInt32(BitConverter.GetBytes(want), 0);
                float f1 = (float)E(ulo, umid, uhi, s); if (neg) f1 = -f1;
                float f2 = ExactToFloat(lo, mid, hi, s); if (neg) f2 = -f2;
                float f3 = (float)A(ulo, umid, uhi, s); if (neg) f3 = -f3;
                if (BitConverter.ToInt32(BitConverter.GetBytes(f1), 0) != wantBits) nF1++;
                if (BitConverter.ToInt32(BitConverter.GetBytes(f2), 0) != wantBits) nF2++;
                if (BitConverter.ToInt32(BitConverter.GetBytes(f3), 0) != wantBits) nF3++;
                if (i < 8)
                    Console.WriteLine("[sample" + i + "] s=" + s + " neg=" + (neg ? 1 : 0)
                        + " gb=" + lo + "," + mid + "," + hi
                        + " want=" + wantBits.ToString("X8")
                        + " F1=" + BitConverter.ToInt32(BitConverter.GetBytes(f1), 0).ToString("X8")
                        + " F2=" + BitConverter.ToInt32(BitConverter.GetBytes(f2), 0).ToString("X8")
                        + " F3=" + BitConverter.ToInt32(BitConverter.GetBytes(f3), 0).ToString("X8"));
            }
            Console.WriteLine("[float] total=" + total + " F1_bad=" + nF1 + " F2_bad=" + nF2 + " F3_bad=" + nF3);

            // Named edge values (the ones a probe case will pin).
            decimal[] vals = {
                0m, 1m, -1m, 1.5m, 0.1m, 1.1m, 1234.5678m, decimal.MaxValue, decimal.MinValue,
                0.0000000000000000000000000001m, 100m, 3.1415926535897932384626433832m,
                16777216m, 16777217m, 123456789012345678901234567m,
                decimal.Parse("1E-28", NumberStyles.Float, CultureInfo.InvariantCulture),
                decimal.Parse("1E28", NumberStyles.Float, CultureInfo.InvariantCulture),
                decimal.Parse("1.2345678901234567890123456789", NumberStyles.Float, CultureInfo.InvariantCulture),
            };
            foreach (var v in vals)
            {
                int[] gb = decimal.GetBits(v);
                int s = (gb[3] >> 16) & 0xFF;
                bool neg = (gb[3] & unchecked((int)0x80000000)) != 0;
                float want = (float)v;
                uint ulo = (uint)gb[0], umid = (uint)gb[1], uhi = (uint)gb[2];
                float f1 = (float)E(ulo, umid, uhi, s); if (neg) f1 = -f1;
                float f2 = ExactToFloat(gb[0], gb[1], gb[2], s); if (neg) f2 = -f2;
                Console.WriteLine("[named] str=" + v.ToString(CultureInfo.InvariantCulture)
                    + " want=" + BitConverter.ToInt32(BitConverter.GetBytes(want), 0).ToString("X8")
                    + " F1=" + BitConverter.ToInt32(BitConverter.GetBytes(f1), 0).ToString("X8")
                    + " F2=" + BitConverter.ToInt32(BitConverter.GetBytes(f2), 0).ToString("X8")
                    + " eq1=" + (BitConverter.ToInt32(BitConverter.GetBytes(f1), 0) == BitConverter.ToInt32(BitConverter.GetBytes(want), 0) ? "1" : "0")
                    + " eq2=" + (BitConverter.ToInt32(BitConverter.GetBytes(f2), 0) == BitConverter.ToInt32(BitConverter.GetBytes(want), 0) ? "1" : "0"));
            }
        }
    }
}
