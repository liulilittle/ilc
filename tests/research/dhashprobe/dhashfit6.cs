//------------------------------------------------------------------------------
// dhashfit6.cs -- RESEARCH ONLY. C# twin of cpp_xcheck.cpp: the SAME
// deterministic LCG case sequence, printing the net48 oracle bits. Diffing
// the two outputs proves (or disproves) bit-exactness of the C++ port.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class DHashFit6
    {
        static ulong Next(ulong x)
        {
            unchecked { return x * 6364136223846793005UL + 1442695040888963407UL; }
        }

        static uint Bits(double d)
        {
            return 0; // unused
        }

        public static void Main()
        {
            ulong x = 0x0123456789ABCDEFUL;
            for (int i = 0; i < 200000; i++)
            {
                x = Next(x); uint lo = (uint)x;
                x = Next(x); uint mid = (uint)x;
                x = Next(x); uint hi = (uint)x;
                x = Next(x); int scale = (int)(x % 29); bool neg = ((x >> 8) & 1) != 0;
                decimal v = new decimal((int)lo, (int)mid, (int)hi, neg, (byte)scale);
                double d = (double)v;
                float f = (float)v;
                long db = BitConverter.DoubleToInt64Bits(d);
                int fb = BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
                Console.WriteLine(i.ToString("D6", CultureInfo.InvariantCulture)
                    + " " + db.ToString("X16", CultureInfo.InvariantCulture)
                    + " " + fb.ToString("X8", CultureInfo.InvariantCulture)
                    + " hash=" + v.GetHashCode().ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
