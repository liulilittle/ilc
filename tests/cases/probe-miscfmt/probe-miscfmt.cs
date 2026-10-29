//------------------------------------------------------------------------------
// probe-miscfmt -- RED probe for the singlefmt residual: the two float
// NO-FORMAT surfaces that still ride the double text.
//
//   1. Single.ToString() (no arguments). net48 renders the FLOAT G7 text
//      (3.14159265f -> "3.141593", 16777216f -> "1.677722E+07") and drops the
//      sign of a zero magnitude (-0.0f -> "0"). The current
//      icall_single_to_string runs snprintf("%.7g") on the widened double and
//      renders -0.0f as "-0".
//   2. Console.WriteLine(float) / Console.Write(float) / Console.Out.Write*
//      (the console TextWriter surface). net48 renders the SAME float G7 text
//      (0.1f -> "0.1", 3.14159265f -> "3.141593", 16777216f -> "1.677722E+07",
//      -0.0f -> "0", 1e-8f -> "1E-08"); the current console float arms route
//      to the double G15 icalls (MethodMap.Console.cs "documented deviation").
//   3. String interpolation of a float with no format specifier: net48 float
//      G7 text through the composite engine.
//
// Records are "[<id>] ..." lines (difftest record compare); the raw IEEE bits
// anchor the exact operand. Double/decimal controls pin the untouched faces.
// NOTE: ids are plain string concatenation over an explicit counter update --
// increment-as-a-value rides a different (out-of-scope) evaluation surface.
//
// REPORTED RESIDUAL (not pinned here): an EXACT tie at the 7th significant
// digit exposes a systemic float-engine rounding gap -- net48 rounds half AWAY
// FROM ZERO (12345645f -> "1.234565E+07", 1.0078125f -> "1.007813") while the
// CRT base rounds ties to EVEN ("1.234564E+07"). It affects
// icall_single_to_string AND the float format engine (Single.ToString("G7")),
// so it is a separate residual, not part of these two registered defects.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class ProbeMiscFmt
    {
        private static int n;

        private static string Id()
        {
            string s = n.ToString("D4");
            n = n + 1;
            return s;
        }

        private static string Bits(float v)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(v), 0).ToString("X8");
        }

        // 1. Single.ToString() no-argument.
        private static void A(float v, string tag)
        {
            string s;
            try { s = v.ToString(); }
            catch (Exception e) { s = e.GetType().Name; }
            Console.WriteLine("[A" + Id() + "] " + tag + " bits=" + Bits(v) + " -> " + s);
        }

        // 2a. Console.WriteLine(float).
        private static void B(float v, string tag)
        {
            Console.Write("[B" + Id() + "] " + tag + " -> ");
            Console.WriteLine(v);
        }

        // 2b. Console.Write(float) + terminator.
        private static void C(float v, string tag)
        {
            Console.Write("[C" + Id() + "] " + tag + " -> ");
            Console.Write(v);
            Console.WriteLine();
        }

        // 2c. Console.Out (TextWriter) Write/WriteLine(float).
        private static void D(float v, string tag)
        {
            Console.Out.Write("[D" + Id() + "] " + tag + " -> ");
            Console.Out.Write(v);
            Console.WriteLine();
        }

        private static void D2(float v, string tag)
        {
            Console.Out.Write("[D" + Id() + "] " + tag + " -> ");
            Console.Out.WriteLine(v);
        }

        // 3. String interpolation, no format specifier.
        private static void E(float v, string tag)
        {
            Console.WriteLine("[E" + Id() + "] " + tag + " -> " + $"{v}");
        }

        public static int Main()
        {
            // 1. Single.ToString() -- boundary values + the zero sign.
            A(0f, "0f");
            A(-0.0f, "-0.0f");
            A(3.14159265f, "pi");
            A(0.1f, "0.1f");
            A(1f / 3f, "1/3");
            A(2f / 3f, "2/3");
            A(0.1f + 0.2f, "0.1f+0.2f");
            A(16777216f, "2^24");
            A(16777215f, "2^24-1");
            A(16777217f, "2^24+1");
            A(1234567f, "1234567f");
            A(12345678f, "12345678f");
            A(123456789f, "123456789f");
            A(9999999f, "9999999f");
            A(10000000f, "1e7f");
            A(1e7f, "1e7f");
            A(1e-5f, "1e-5f");
            A(1e-7f, "1e-7f");
            A(1e-8f, "1e-8f");
            A(-1e-8f, "-1e-8f");
            A(0.000123f, "0.000123f");
            A(9.999999f, "9.999999f");
            A(2.675f, "2.675f");
            A(1.2349999f, "1.2349999f");
            A(-1.5f, "-1.5f");
            A(1.17549435E-38f, "min-normal");
            A(3.40282347E+38f, "max");
            A(-3.40282347E+38f, "min");
            A(float.Epsilon, "epsilon");
            A(float.MaxValue, "MaxValue");
            A(float.MinValue, "MinValue");
            A(float.NaN, "NaN");
            A(float.PositiveInfinity, "+Inf");
            A(float.NegativeInfinity, "-Inf");
            A(1e21f, "1e21f");

            // 2. Console float no-format surfaces.
            float[] vals = { 0.1f, 3.14159265f, 16777216f, -0.0f, 1e-8f, 1f / 3f, float.MaxValue };
            string[] tags = { "0.1f", "pi", "2^24", "-0.0f", "1e-8f", "1/3", "MaxValue" };
            for (int i = 0; i < vals.Length; ++i) B(vals[i], tags[i]);
            for (int i = 0; i < vals.Length; ++i) C(vals[i], tags[i]);
            for (int i = 0; i < vals.Length; ++i) D(vals[i], tags[i]);
            for (int i = 0; i < vals.Length; ++i) D2(vals[i], tags[i]);
            for (int i = 0; i < vals.Length; ++i) E(vals[i], tags[i]);

            // Controls: the double / decimal no-format faces stay untouched.
            Console.Write("[F" + Id() + "] dbl-wl 0.1 -> ");
            Console.WriteLine(0.1);
            Console.Write("[F" + Id() + "] dbl-wl 1/3 -> ");
            Console.WriteLine(1.0 / 3.0);
            Console.Write("[F" + Id() + "] dbl-wl -0.0 -> ");
            Console.WriteLine(-0.0);
            Console.Write("[F" + Id() + "] dbl-w -> ");
            Console.Write(0.1 + 0.2);
            Console.WriteLine();
            Console.Write("[F" + Id() + "] dec-wl -> ");
            Console.WriteLine(1.5m);
            Console.WriteLine("[F" + Id() + "] dbl-tostring -> " + (0.1).ToString());
            Console.WriteLine("[F" + Id() + "] dec-tostring -> " + (1.5m).ToString());
            Console.Write("[F" + Id() + "] dbl-stream-wl -> ");
            Console.Out.WriteLine(1.0 / 3.0);
            Console.Write("[F" + Id() + "] dbl-stream-w -> ");
            Console.Out.Write(0.1 + 0.2);
            Console.WriteLine();

            return 0;
        }
    }
}
