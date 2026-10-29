//------------------------------------------------------------------------------
// probe-tiebreak2 -- RED probe for the two tie residuals left by 554710cf.
//
// DEFECT 1 (console float face): console.cpp's console_format_single still
// rides snprintf("%.7g"), whose CRT conversion rounds an EXACT half-way tie to
// EVEN. net48's Single text rounds half AWAY FROM ZERO, so the direct
// Console.WriteLine(float) / Console.Write(float) / Console.Out.Write*(float)
// surfaces (NOT the console-stream Write(float) arm, which already rides
// icall_single_to_string) print the tie one digit low:
//   Console.WriteLine(12345645f)   -> CRT "1.234564E+07", net48 "1.234565E+07"
//   Console.WriteLine(6000000.5f)  -> CRT "6000000",      net48 "6000001"
//   Console.WriteLine(0.00048828125f) -> CRT "0.0004882812", net48 "0.0004882813"
// The special/zero spellings (NaN, +-U+221E, -0.0f -> "0") are pinned too: a
// delegation to icall_format_float_custom must keep them byte-identical.
//
// DEFECT 2 (double 15-digit base): the double engine builds its 15-significant-
// digit base with the CRT's "%.14e", which rounds an EXACT tie to EVEN, while
// net48 rounds half AWAY FROM ZERO. The tie family below is exact, not
// sampled: a double is a 15-digit tie iff its exact decimal expansion has
// exactly SIXTEEN significant digits and the last one is 5
// (v = (10X+5) * 10^(exp10-15), X a 15-digit integer). The values are
// representable only when the odd part fits the 53-bit significand; every
// constant below is exact (v = m / 2^p with m < 2^53) and the [V...] record
// pins the IEEE bits. With X EVEN the CRT rounds DOWN (RED); the X-odd rows
// and the near-tie rows (16th digit 4 / 6) are controls that already agree.
// The battery covers G15/G16/G17/F/E/N/C/P/R/custom plus the no-argument,
// concat, composite and console(double) surfaces.
//
// The live net48 oracle (same source) is the diff authority.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class ProbeTiebreak2
    {
        private static int n;

        private static string Id()
        {
            string s = n.ToString("D4");
            n = n + 1;
            return s;
        }

        // Console float surfaces: WriteLine(float), Write(float),
        // Console.Out.WriteLine(float), Console.Out.Write(float).
        private static void CF(string tag, float v)
        {
            Console.Write("[C" + Id() + "] " + tag + "|WriteLine|");
            Console.WriteLine(v);
            Console.Write("[C" + Id() + "] " + tag + "|Write|");
            Console.Write(v);
            Console.WriteLine();
            Console.Write("[C" + Id() + "] " + tag + "|OutWriteLine|");
            Console.Out.WriteLine(v);
            Console.Write("[C" + Id() + "] " + tag + "|OutWrite|");
            Console.Out.Write(v);
            Console.WriteLine();
        }

        // Console double surfaces (console_format_double: snprintf("%.15g")).
        private static void CD(string tag, double v)
        {
            Console.Write("[C" + Id() + "] " + tag + "|DWriteLine|");
            Console.WriteLine(v);
            Console.Write("[C" + Id() + "] " + tag + "|DWrite|");
            Console.Write(v);
            Console.WriteLine();
            Console.Write("[C" + Id() + "] " + tag + "|DOutWriteLine|");
            Console.Out.WriteLine(v);
            Console.Write("[C" + Id() + "] " + tag + "|DOutWrite|");
            Console.Out.Write(v);
            Console.WriteLine();
        }

        private static void F(string tag, string fmt, float v)
        {
            string s;
            try { s = v.ToString(fmt, CultureInfo.InvariantCulture); }
            catch (Exception e) { s = e.GetType().Name; }
            Console.WriteLine("[T" + Id() + "] " + tag + "|" + fmt + "|" + s);
        }

        private static void D(string tag, double v, string fmt)
        {
            string s;
            try { s = v.ToString(fmt, CultureInfo.InvariantCulture); }
            catch (Exception e) { s = e.GetType().Name; }
            Console.WriteLine("[B" + Id() + "] " + tag + "|" + fmt + "|" + s);
        }

        private static void V(double v, string tag)
        {
            Console.WriteLine("[V" + Id() + "] " + tag + " bits=" +
                BitConverter.ToInt64(BitConverter.GetBytes(v), 0).ToString("X16"));
            string[] fmts =
            {
                "G15", "G", "g15", "G16", "G17",
                "F0", "F1", "F2", "F15",
                "E6", "E14", "E15",
                "N2", "N15", "C2", "P2",
                "R", "r",
                "0.000000000000000", "0.000000000000000E+00",
            };
            foreach (var f in fmts) D(tag, v, f);
            Console.WriteLine("[D" + Id() + "] " + tag + "|noarg|" + v.ToString());
            Console.WriteLine("[D" + Id() + "] " + tag + "|concat|" + ("" + v));
            Console.WriteLine("[D" + Id() + "] " + tag + "|composite|" +
                string.Format(CultureInfo.InvariantCulture, "{0:G15}", v));
            CD(tag, v);
        }

        public static int Main()
        {
            // ================= defect 1: console FLOAT ties =================
            // base 7, exp10 = 7: eight-digit integers ending in 5.
            CF("f_i12345645", 12345645f);      // X EVEN -> CRT down (RED)
            CF("f_i12345655", 12345655f);      // X odd  -> control
            CF("f_ni12345645", -12345645f);    // negative tie (RED)
            // base 7, exp10 = 6: X.5 in the millions.
            CF("f_x6000000_5", 6000000.5f);    // X EVEN -> RED
            CF("f_x5999999_5", 5999999.5f);    // X odd  -> control
            // base 7, exp10 = -4: the smallest exact tie.
            CF("f_em4a", 0.00048828125f);      // X EVEN -> RED
            CF("f_nem4a", -0.00048828125f);    // negative tie (RED)
            // non-tie + special/zero spellings (must stay identical).
            CF("f_01", 0.1f);
            CF("f_negzero", -0.0f);
            CF("f_nan", float.NaN);
            CF("f_posinf", float.PositiveInfinity);
            CF("f_neginf", float.NegativeInfinity);

            // The same ties on the Single.ToString(format) face (already fixed
            // by 554710cf) are the GREEN control group for this probe.
            F("f_i12345645", "G7", 12345645f);
            F("f_x6000000_5", "G7", 6000000.5f);
            F("f_em4a", "G7", 0.00048828125f);

            // ================= defect 2: double 15-digit ties =================
            // exp10 = 15 .. -4, X EVEN (CRT half-even rounds DOWN -> RED).
            V(1234567890123445.0, "d_e15");
            V(100000000000000.5, "d_e14");
            V(10000000000000.25, "d_e13");
            V(1000000000000.125, "d_e12");
            V(10000000.00390625, "d_e7");
            V(1.000030517578125, "d_e0");
            V(0.1000518798828125, "d_em1");
            V(0.0001010894775390625, "d_em4");
            V(-1234567890123445.0, "d_ne15");
            V(-0.1000518798828125, "d_nem1");

            // Controls: X ODD (CRT also rounds up), near-ties (16th digit
            // 4 / 6) and a plain non-tie must stay byte-identical.
            V(1234567890123455.0, "d_c_odd15");
            V(100000000000001.5, "d_c_odd14");
            V(123456789012345.5, "d_c_odd14b");
            V(1234567890123444.0, "d_c_near4");
            V(1234567890123446.0, "d_c_near6");
            V(0.1, "d_c_01");

            // ============ defect 2b: the WIDE (16/17-digit) base ============
            // net48 E needs prec+1 significant digits, so E15/E16/E17 read the
            // 16-/17-digit base (F/N/C/P stay on the 15-digit one).
            V(1000000000000000.25, "d_w17a");   // 18 exact digits, tie at 17
            V(1000000000000000.75, "d_w17b");   // 18 exact digits, tie at 17
            V(1000000000000001.25, "d_w17c");

            // ============ randomized control battery ============
            // A deterministic LCG over the raw bit patterns (finite normals
            // only): the tie family above is exact, this pins that the whole
            // G/F/E/N/P/R/custom matrix stays byte-identical for arbitrary
            // values (it also exercises the widened E base and the P scale
            // shift away from ties).
            ulong state = 0x9E3779B97F4A7C15UL;
            for (int k = 0; k < 120; ++k)
            {
                state = state * 6364136223846793005UL + 1442695040888963407UL;
                ulong bits = state ^ (state >> 33);
                bits &= 0x7FEFFFFFFFFFFFFFUL; // finite, non-zero, normal
                double v = BitConverter.Int64BitsToDouble((long)bits);
                D("rnd" + k.ToString("D3"), v, "G15");
                D("rnd" + k.ToString("D3"), v, "G16");
                D("rnd" + k.ToString("D3"), v, "G17");
                D("rnd" + k.ToString("D3"), v, "E6");
                D("rnd" + k.ToString("D3"), v, "E14");
                D("rnd" + k.ToString("D3"), v, "E15");
                D("rnd" + k.ToString("D3"), v, "E16");
                D("rnd" + k.ToString("D3"), v, "E17");
                D("rnd" + k.ToString("D3"), v, "F2");
                D("rnd" + k.ToString("D3"), v, "F15");
                D("rnd" + k.ToString("D3"), v, "F16");
                D("rnd" + k.ToString("D3"), v, "N2");
                D("rnd" + k.ToString("D3"), v, "P2");
                D("rnd" + k.ToString("D3"), v, "P0");
                D("rnd" + k.ToString("D3"), v, "C2");
                D("rnd" + k.ToString("D3"), v, "R");
            }

            return 0;
        }
    }
}
