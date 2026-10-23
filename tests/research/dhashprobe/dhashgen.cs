//------------------------------------------------------------------------------
// dhashgen.cs -- RESEARCH ONLY. Generates the two C++ literal tables the
// net48 test suite needs, so no value in the test is hand-typed:
//   (a) (double)(1e-s) bits for s = 0..28
//   (b) the canonical-pair case: m@14 and (m*10)@15 raw GetBits + hashes
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Numerics;

namespace Cases
{
    public static class DHashGen
    {
        public static void Main()
        {
            Console.WriteLine("// (a) (double)(1 / 10^s), net48 bits, s = 0..28");
            for (int s = 0; s <= 28; s++)
            {
                decimal v = new decimal(1, 0, 0, false, (byte)s);
                long bits = BitConverter.DoubleToInt64Bits((double)v);
                string tail = (s == 28) ? "" : " ";
                Console.WriteLine("0x" + bits.ToString("X16", CultureInfo.InvariantCulture) + "ull," + tail
                    + " // s=" + s + " hash=" + v.GetHashCode().ToString(CultureInfo.InvariantCulture));
            }

            Console.WriteLine();
            Console.WriteLine("// (b) canonical pair m@14 vs (m*10)@15");
            int lo = -2020474143, mid = -1267424804, hi = 289413371;
            decimal a = new decimal(lo, mid, hi, false, 14);
            BigInteger m = ((BigInteger)(uint)hi << 64) | ((BigInteger)(uint)mid << 32) | (uint)lo;
            BigInteger m10 = m * 10;
            int nlo = (int)(uint)(m10 & 0xFFFFFFFF);
            int nmid = (int)(uint)((m10 >> 32) & 0xFFFFFFFF);
            int nhi = (int)(uint)((m10 >> 64) & 0xFFFFFFFF);
            decimal b = new decimal(nlo, nmid, nhi, false, 15);
            int[] ga = decimal.GetBits(a);
            int[] gb = decimal.GetBits(b);
            Console.WriteLine("A lo=" + ga[0] + " mid=" + ga[1] + " hi=" + ga[2] + " flags=" + ga[3]
                + " hash=" + a.GetHashCode()
                + " dBits=0x" + BitConverter.DoubleToInt64Bits((double)a).ToString("X16"));
            Console.WriteLine("B lo=" + gb[0] + " mid=" + gb[1] + " hi=" + gb[2] + " flags=" + gb[3]
                + " hash=" + b.GetHashCode()
                + " dBits=0x" + BitConverter.DoubleToInt64Bits((double)b).ToString("X16"));
            Console.WriteLine("A.lo64=0x" + (((ulong)(uint)ga[0]) | ((ulong)(uint)ga[1] << 32)).ToString("X16")
                + " A.hi32=" + ga[2] + "u");
            Console.WriteLine("B.lo64=0x" + (((ulong)(uint)gb[0]) | ((ulong)(uint)gb[1] << 32)).ToString("X16")
                + " B.hi32=" + gb[2] + "u");
            Console.WriteLine("equal=" + (a == b ? "1" : "0"));
        }
    }
}
