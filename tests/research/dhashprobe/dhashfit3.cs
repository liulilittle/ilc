//------------------------------------------------------------------------------
// dhashfit3.cs -- RESEARCH ONLY. Finds inputs where the surviving candidate
// decimal->double algorithms disagree, then asks the net48 runtime itself
// which one it follows. This is the disambiguation pass: candidates must
// match net48 on the ADVERSARIAL inputs, not just on random samples.
//
// E   = pow2(-s) * (m / pow(5,s))
// E2  = (m / pow(5,s)) * pow2(-s)          [== E by commutativity]
// E3  = (m * pow2(-s)) / pow(5,s)
// E4  = m / (pow(5,s) * pow2(s))
// A   = m / pow10_iter(s)
// H   = m / pow(10,s)                      [libm pow]
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class DHashFit3
    {
        static readonly double[] Pow10 = BuildPow10();
        static double[] BuildPow10()
        {
            double[] p = new double[29];
            p[0] = 1.0;
            for (int i = 1; i < p.Length; i++) p[i] = p[i - 1] * 10.0;
            return p;
        }

        static double M(uint lo, uint mid, uint hi)
        {
            return (double)hi * 18446744073709551616.0 + (double)((ulong)lo | ((ulong)mid << 32));
        }
        static double E(uint lo, uint mid, uint hi, int s)
        {
            if (s == 0) return M(lo, mid, hi);
            return Math.Pow(2.0, -s) * (M(lo, mid, hi) / Math.Pow(5.0, s));
        }
        static double E3(uint lo, uint mid, uint hi, int s)
        {
            if (s == 0) return M(lo, mid, hi);
            return (M(lo, mid, hi) * Math.Pow(2.0, -s)) / Math.Pow(5.0, s);
        }
        static double E4(uint lo, uint mid, uint hi, int s)
        {
            if (s == 0) return M(lo, mid, hi);
            return M(lo, mid, hi) / (Math.Pow(5.0, s) * Math.Pow(2.0, s));
        }
        static double A(uint lo, uint mid, uint hi, int s)
        {
            if (s == 0) return M(lo, mid, hi);
            return M(lo, mid, hi) / Pow10[s];
        }
        static double H(uint lo, uint mid, uint hi, int s)
        {
            if (s == 0) return M(lo, mid, hi);
            return M(lo, mid, hi) / Math.Pow(10.0, s);
        }

        public static void Main()
        {
            var rnd = new Random(99);
            int eVsE3 = 0, eVsE4 = 0, eVsA = 0, eVsH = 0, tested = 0, disagreements = 0;
            int eOk = 0, e3Ok = 0, e4Ok = 0, aOk = 0, hOk = 0;
            for (int i = 0; i < 3000000; i++)
            {
                int lo = rnd.Next(int.MinValue, int.MaxValue);
                int mid = rnd.Next(int.MinValue, int.MaxValue);
                int hi = rnd.Next(int.MinValue, int.MaxValue);
                int s = rnd.Next(1, 29);
                bool neg = rnd.Next(2) == 1;
                decimal v;
                try { v = new decimal(lo, mid, hi, neg, (byte)s); }
                catch (ArgumentException) { continue; }
                uint ulo = (uint)lo, umid = (uint)mid, uhi = (uint)hi;
                double e = E(ulo, umid, uhi, s);
                double e3 = E3(ulo, umid, uhi, s);
                double e4 = E4(ulo, umid, uhi, s);
                double a = A(ulo, umid, uhi, s);
                double h = H(ulo, umid, uhi, s);
                long be = BitConverter.DoubleToInt64Bits(e);
                long be3 = BitConverter.DoubleToInt64Bits(e3);
                long be4 = BitConverter.DoubleToInt64Bits(e4);
                long ba = BitConverter.DoubleToInt64Bits(a);
                long bh = BitConverter.DoubleToInt64Bits(h);
                if (be == be3 && be == be4 && be == ba && be == bh) continue;
                disagreements++;
                long want = BitConverter.DoubleToInt64Bits((double)v);
                tested++;
                if (be != be3) eVsE3++;
                if (be != be4) eVsE4++;
                if (be != ba) eVsA++;
                if (be != bh) eVsH++;
                if (be == want) eOk++;
                if (be3 == want) e3Ok++;
                if (be4 == want) e4Ok++;
                if (ba == want) aOk++;
                if (bh == want) hOk++;
                if (tested <= 12)
                    Console.WriteLine("[case" + tested + "] s=" + s + " neg=" + (neg ? 1 : 0)
                        + " gb=" + lo + "," + mid + "," + hi
                        + " want=" + want.ToString("X16")
                        + " E=" + be.ToString("X16") + " E3=" + be3.ToString("X16")
                        + " E4=" + be4.ToString("X16") + " A=" + ba.ToString("X16")
                        + " H=" + bh.ToString("X16"));
            }
            Console.WriteLine("[dis] sampled=3000000 disagreementCases=" + disagreements
                + " E_vs_E3=" + eVsE3 + " E_vs_E4=" + eVsE4 + " E_vs_A=" + eVsA + " E_vs_H=" + eVsH);
            Console.WriteLine("[win] n=" + tested + " E=" + eOk + " E3=" + e3Ok + " E4=" + e4Ok
                + " A=" + aOk + " H=" + hOk);
        }
    }
}
