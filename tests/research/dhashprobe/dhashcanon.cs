//------------------------------------------------------------------------------
// dhashcanon.cs -- RESEARCH ONLY. Precise measurement of net48 decimal hash
// canonicalization: for a value written with extra trailing zeros (m*10^k,
// scale+k, mathematically the same number) does GetHashCode stay equal?
//
// Also measures the size of the hash-collision-free contract that a port must
// reproduce, so the e2e probe can pin the right facts.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class DHashCanon
    {
        static ulong Next(ulong x)
        {
            unchecked { return x * 6364136223846793005UL + 1442695040888963407UL; }
        }

        public static void Main()
        {
            // --- 1. small / realistic values: append up to 3 trailing zeros ---
            decimal[] small =
            {
                1m, 2m, 10m, 100m, 0.1m, 1.1m, 1.5m, 2.5m, 1234.5678m, -1.5m,
                0.0000000001m, 1.2345678901234567890123456789m, 123.456m,
                0.0000000000000000000000000001m, 7922816251426433759354395033m,
                9007199254740993m, 123456789012345678901234567m,
            };
            int smallBad = 0, smallTotal = 0;
            foreach (decimal baseVal in small)
            {
                decimal v = baseVal;
                for (int k = 1; k <= 3; k++)
                {
                    decimal scaled;
                    try
                    {
                        // multiply mantissa by 10 and bump the scale by one
                        int[] b = decimal.GetBits(v);
                        int scale = (b[3] >> 16) & 0xFF;
                        if (scale >= 28) break;
                        decimal factor = 10m;
                        // rebuild via the 5-arg ctor to force scale+1 exactly
                        decimal.TryParse("0", out scaled);
                        scaled = v;
                        // Use the internal 5-arg ctor with mantissa*10:
                        // mantissa fits in 96 bits for these values.
                        decimal ten = new decimal(10);
                        scaled = v * ten / ten; // no-op, keeps value
                        // The ONLY portable way to force the scale: format with
                        // extra decimals then parse with NumberStyles.
                        string s = v.ToString(CultureInfo.InvariantCulture);
                        string padded = PadDecimals(s, k);
                        decimal parsed = decimal.Parse(padded, NumberStyles.Number, CultureInfo.InvariantCulture);
                        if (parsed != v) { continue; }
                        smallTotal++;
                        if (parsed.GetHashCode() != v.GetHashCode())
                        {
                            smallBad++;
                            Console.WriteLine("[smallbad] " + s + " -> " + padded
                                + " hashA=" + v.GetHashCode() + " hashB=" + parsed.GetHashCode()
                                + " dBitsA=" + BitConverter.DoubleToInt64Bits((double)v).ToString("X16")
                                + " dBitsB=" + BitConverter.DoubleToInt64Bits((double)parsed).ToString("X16"));
                        }
                    }
                    catch (Exception) { break; }
                }
            }
            Console.WriteLine("[small] total=" + smallTotal + " bad=" + smallBad);

            // --- 2. random 96-bit mantissas: exact m*10 with scale+1 ---
            ulong x = 0xDEADBEEFCAFEBABEUL;
            int randTotal = 0, randBad = 0, skipped = 0;
            for (int i = 0; i < 200000; i++)
            {
                x = Next(x); int lo = (int)(uint)x;
                x = Next(x); int mid = (int)(uint)x;
                x = Next(x); int hi = (int)(uint)x;
                x = Next(x); int s = (int)(x % 28); bool neg = ((x >> 8) & 1) != 0;
                decimal a, b;
                try
                {
                    a = new decimal(lo, mid, hi, neg, (byte)s);
                    // m*10 must fit in 96 bits: check hi32 < 2^28-ish
                    if ((uint)hi >= 0x19999999u) { skipped++; continue; }
                    // multiply mantissa by 10 using BigInteger-free long math
                    ulong lo64 = (ulong)(uint)lo | ((ulong)(uint)mid << 32);
                    ulong hiPart = (uint)hi;
                    // 96-bit * 10
                    ulong p0 = lo64 * 10UL;
                    ulong carry = 0;
                    // do it with 32-bit limbs to stay exact
                    uint l0 = (uint)lo, l1 = (uint)mid, l2 = (uint)hi;
                    ulong t = (ulong)l0 * 10UL; uint n0 = (uint)t; ulong c = t >> 32;
                    t = (ulong)l1 * 10UL + c; uint n1 = (uint)t; c = t >> 32;
                    t = (ulong)l2 * 10UL + c; uint n2 = (uint)t; c = t >> 32;
                    if (c != 0) { skipped++; continue; }
                    b = new decimal((int)n0, (int)n1, (int)n2, neg, (byte)(s + 1));
                }
                catch (ArgumentException) { skipped++; continue; }
                if (a != b) { skipped++; continue; }
                randTotal++;
                if (a.GetHashCode() != b.GetHashCode())
                {
                    randBad++;
                    if (randBad <= 5)
                        Console.WriteLine("[randbad] lo=" + lo + " mid=" + mid + " hi=" + hi + " s=" + s
                            + " neg=" + (neg ? 1 : 0)
                            + " hashA=" + a.GetHashCode() + " hashB=" + b.GetHashCode()
                            + " dA=" + BitConverter.DoubleToInt64Bits((double)a).ToString("X16")
                            + " dB=" + BitConverter.DoubleToInt64Bits((double)b).ToString("X16"));
                }
            }
            Console.WriteLine("[rand] total=" + randTotal + " bad=" + randBad + " skipped=" + skipped);
        }

        static string PadDecimals(string s, int extra)
        {
            int dot = s.IndexOf('.');
            if (dot < 0) return s + "." + new string('0', extra);
            return s + new string('0', extra);
        }
    }
}
