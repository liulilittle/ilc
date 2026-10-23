//------------------------------------------------------------------------------
// dhashprobe.cs -- RESEARCH ONLY (net48 oracle). Determines the exact net48
// decimal.GetHashCode algorithm; never transpiled.
//
// For every value we print:
//   [id] hash=<d.GetHashCode()> dhash=<((double)d).GetHashCode()>
//        bits=<BitConverter.DoubleToInt64Bits((double)d) hex>
//        cbits=<BitConverter.DoubleToInt64Bits(Convert.ToDouble(d)) hex>
//        gb=<lo>,<mid>,<hi>,<flags> str=<invariant>
//        m=<hash == maskCandidate> r=<hash == rawCandidate> e=<hash == dhash>
//   maskCandidate = (int)((bits >> 32) ^ (int)(bits & 0xFFFFFFF0))
//   rawCandidate  = (int)((bits >> 32) ^ (int)bits)
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Numerics;

namespace Cases
{
    public static class DHashProbe
    {
        static int mismMask = 0, mismRaw = 0, mismEq = 0, total = 0, mismExact = 0, mismExactMask = 0;

        static void P(string id, decimal d)
        {
            int h = d.GetHashCode();
            double dd = (double)d;
            double cd = Convert.ToDouble(d);
            long bits = BitConverter.DoubleToInt64Bits(dd);
            long cbits = BitConverter.DoubleToInt64Bits(cd);
            int dh = dd.GetHashCode();
            int[] gb = decimal.GetBits(d);
            int maskCand = (int)((bits >> 32) ^ (int)(bits & unchecked((long)0xFFFFFFF0)));
            int rawCand = (int)((bits >> 32) ^ (int)bits);
            double ex = ExactToDouble(gb[0], gb[1], gb[2], (gb[3] & unchecked((int)0x80000000)) != 0, (gb[3] >> 16) & 0xFF);
            long exBits = BitConverter.DoubleToInt64Bits(ex);
            bool exactSame = (exBits == bits);
            int exactMaskCand = (int)((exBits >> 32) ^ (int)(exBits & unchecked((long)0xFFFFFFF0)));
            bool m = (h == maskCand);
            bool r = (h == rawCand);
            bool e = (h == dh);
            total++;
            if (!m) mismMask++;
            if (!r) mismRaw++;
            if (!e) mismEq++;
            if (!exactSame) mismExact++;
            if (h != exactMaskCand) mismExactMask++;
            Console.WriteLine("[" + id + "] hash=" + h.ToString(CultureInfo.InvariantCulture)
                + " dhash=" + dh.ToString(CultureInfo.InvariantCulture)
                + " bits=" + bits.ToString("X16", CultureInfo.InvariantCulture)
                + " cbits=" + cbits.ToString("X16", CultureInfo.InvariantCulture)
                + " gb=" + gb[0].ToString(CultureInfo.InvariantCulture) + "," + gb[1].ToString(CultureInfo.InvariantCulture)
                + "," + gb[2].ToString(CultureInfo.InvariantCulture) + "," + gb[3].ToString(CultureInfo.InvariantCulture)
                + " str=" + d.ToString(CultureInfo.InvariantCulture)
                + " m=" + (m ? "1" : "0") + " r=" + (r ? "1" : "0") + " e=" + (e ? "1" : "0")
                + " ex=" + exBits.ToString("X16", CultureInfo.InvariantCulture)
                + " xm=" + (h == exactMaskCand ? "1" : "0") + " xs=" + (exactSame ? "1" : "0"));
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

        // Exact correctly-rounded (M / 10^scale) -> double via BigInteger.
        // Used as the "ideal" oracle to decide whether net48's (double)d is
        // itself correctly rounded, and whether our runtime's two-step
        // ldexp(hi,64)+lo64 then /pow(10,scale) diverges from it.
        static double ExactToDouble(int lo, int mid, int hi, bool neg, int scale)
        {
            BigInteger m = ((BigInteger)(uint)hi << 64) | ((BigInteger)(uint)mid << 32) | (uint)lo;
            if (m.IsZero) return neg ? -0.0 : 0.0;
            BigInteger den = BigInteger.Pow(10, scale);
            // e = floor(log2(m/den))
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
            int shift = 52 - e; // q = round(m * 2^shift / den)
            BigInteger num = m, d2 = den;
            if (shift >= 0) num = m << shift; else d2 = den << (-shift);
            BigInteger q = BigInteger.DivRem(num, d2, out BigInteger r);
            int cmp = (r << 1).CompareTo(d2);
            if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += 1;
            if (q == (BigInteger.One << 53)) { q >>= 1; e++; }
            double res = (double)q * Math.Pow(2.0, e - 52);
            return neg ? -res : res;
        }
        public static void Main()
        {
            P("v00", 0m);
            P("v50", -0.0m);
            P("v51", 0.00m);
            P("v52", decimal.Parse("-0", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v53", decimal.Negate(0m));
            P("v01", 1m);
            P("v02", -1m);
            P("v03", 100m);
            P("v04", 1234m);
            P("v05", 1.5m);
            P("v06", 2.5m);
            P("v07", -1.5m);
            P("v08", 0.1m);
            P("v09", 1.1m);
            P("v10", 0.3m);
            P("v11", 0.7m);
            P("v12", 1234.5678m);
            P("v13", 1.0m);
            P("v14", 1.00m);
            P("v15", 100.00m);
            P("v16", decimal.MaxValue);
            P("v17", decimal.MinValue);
            P("v18", 0.0000000000000000000000000001m);
            P("v19", decimal.Parse("1E-28", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v20", decimal.Parse("0.0000000000000000000000000002", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v21", 3.1415926535897932384626433832m);
            P("v22", 2.7182818284590452353602874713m);
            P("v23", decimal.Parse("0.3333333333333333333333333333", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v24", 123456789012345678901234567m);
            P("v25", 1234567890123456789012345678m);
            P("v26", 9007199254740993m);
            P("v27", 9007199254740992m);
            P("v28", 18446744073709551616m);
            P("v29", 4294967296m);
            P("v30", decimal.Parse("1E28", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v31", 0.5m);
            P("v32", 0.25m);
            P("v33", 0.125m);
            P("v34", 0.2m);
            P("v35", 0.4m);
            P("v36", 0.6m);
            P("v37", 0.8m);
            P("v38", 0.9m);
            P("v39", -0.1m);
            P("v40", -1234.5678m);
            P("v41", decimal.Parse("123.456", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v42", decimal.Parse("1E-10", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v43", decimal.Parse("1E10", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v44", decimal.Parse("1.2345678901234567890123456789", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v45", decimal.Parse("79228162514264337593543950335", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v46", decimal.Parse("0.0000000000000000000000000003", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v47", decimal.Parse("12345678901234567890123456789", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v48", decimal.Parse("1.0000000000000000000000000001", NumberStyles.Float, CultureInfo.InvariantCulture));
            P("v49", decimal.Parse("-0.0000000000000000000000000001", NumberStyles.Float, CultureInfo.InvariantCulture));
            // ---- randomized cross-check of OUR to_double algorithm ----
            // ours = ldexp((double)hi,64) + (double)lo64, then /pow(10,scale)
            // (src/runtime/src/decimal.cpp:2592 icall_decimal_to_double).
            var rnd = new Random(20261023);
            int rndDiff = 0;
            for (int i = 0; i < 2000; i++)
            {
                int lo = rnd.Next(int.MinValue, int.MaxValue);
                int mid = rnd.Next(int.MinValue, int.MaxValue);
                int hi = rnd.Next(int.MinValue, int.MaxValue);
                int scale = rnd.Next(0, 29);
                bool neg = rnd.Next(2) == 1;
                decimal v;
                try { v = new decimal(lo, mid, hi, neg, (byte)scale); }
                catch (ArgumentException) { continue; }
                double exact = (double)v;
                ulong lo64 = (uint)lo | ((ulong)(uint)mid << 32);
                double m = (double)(uint)hi * 18446744073709551616.0 + (double)lo64;
                if (scale > 0) m = m / Math.Pow(10.0, scale);
                if (neg) m = -m;
                long eb = BitConverter.DoubleToInt64Bits(exact);
                long ob = BitConverter.DoubleToInt64Bits(m);
                if (eb != ob)
                {
                    rndDiff++;
                    if (rndDiff <= 10)
                        Console.WriteLine("[rnddiff" + rndDiff + "] gb=" + lo + "," + mid + "," + hi + ","
                            + (neg ? "-2147483648" : "0") + "+" + scale
                            + " exact=" + eb.ToString("X16") + " ours=" + ob.ToString("X16"));
                }
            }
            Console.WriteLine("[rnd] tested=2000 toDoubleDiff=" + rndDiff.ToString(CultureInfo.InvariantCulture));            Console.WriteLine("[summary] total=" + total.ToString(CultureInfo.InvariantCulture)
                + " maskMismatch=" + mismMask.ToString(CultureInfo.InvariantCulture)
                + " rawMismatch=" + mismRaw.ToString(CultureInfo.InvariantCulture)
                + " eqMismatch=" + mismEq.ToString(CultureInfo.InvariantCulture)
                + " exactConvMismatch=" + mismExact.ToString(CultureInfo.InvariantCulture)
                + " exactMaskMismatch=" + mismExactMask.ToString(CultureInfo.InvariantCulture));
        }
    }
}
