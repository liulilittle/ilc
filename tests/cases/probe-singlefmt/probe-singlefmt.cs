//------------------------------------------------------------------------------
// probe-singlefmt -- RED probe for the floatfix residual: Single.ToString
// non-R formats (F/E/G/N/P/custom) currently forward to the DOUBLE engine
// (icall_format_double_custom), whose 15-significant-digit base cannot
// reproduce net48's FLOAT-width digit base. net48 Number.FormatSingle
// (oracle-verified on this host, 2026-10-29, zh-CN + Invariant):
//   * F/N/C/P/custom: 7 significant digits (SinglePrecision custom base).
//   * E: base 7 for precision <= 6, base 9 for precision > 6.
//   * G: base 7 for precision <= 7, base 9 for precision > 7.
//   * R: two-tier G7->G9 float round-trip (floatfix, unchanged).
//   * specials: NaN/"Infinity" spellings BEFORE any format work, per provider.
// The double engine would print e.g. 0.1f F10 "0.1000000015" while net48
// prints "0.1000000000" (the 7-digit base zero-padded); 0.1f G15 "0.1" vs
// double "0.100000001490116". The boxed composite path ({0:F10}) rides the
// same defect (icall_format_composite_item routes System.Single to the
// double engine). Records are [id] value lines (difftest record compare);
// a float value plus its raw bits anchors the exact IEEE operand.
// NOTE: ids are plain string concatenation over an explicit counter update --
// increment-as-a-value rides a different (out-of-scope) evaluation surface.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class ProbeSingleFmt
    {
        private static int n;

        private static string Id()
        {
            string s = n.ToString("D4");
            n = n + 1;
            return s;
        }

        private static void F(string fmt, float v)
        {
            string s;
            try { s = v.ToString(fmt, CultureInfo.InvariantCulture); }
            catch (Exception e) { s = e.GetType().Name; }
            Console.WriteLine("[S" + Id() + "] " + s);
        }

        private static void V(float v, string tag)
        {
            Console.WriteLine("[V" + Id() + "] " + tag + " bits=" +
                BitConverter.ToInt32(BitConverter.GetBytes(v), 0).ToString("X8"));
            string[] fmts =
            {
                "F1","F2","F3","F6","F7","F8","F9","F10",
                "E1","E2","E5","E6","E7","E8","E9","E10","E99","E0","e2","e0",
                "G3","G4","G5","G6","G7","G8","G9","G10","G15","G17","G99","G0","g7","g0",
                "N0","N2","N10",
                "P0","P2","P6",
                "R","r",
                "0.00","#.##","0.##########","0.0000000000","000.000","0,0.00",
                "0.00000000E+00","0.0000000E+00","#.###############","abc",
            };
            foreach (var f in fmts) F(f, v);
        }

        public static int Main()
        {
            V(0.1f, "0.1f");
            V(2.675f, "2.675f");
            V(0.15f, "0.15f");
            V(1.2349999f, "1.2349999f");
            V(0.000123f, "0.000123f");
            V(1234567f, "1234567f");
            V(16777216f, "2^24");
            V(12345678f, "12345678f");
            V(3.14159265f, "pi");
            V(9.999999f, "9.999999f");
            V(1e-5f, "1e-5f");
            V(1e7f, "1e7f");
            V(12345.6777f, "12345.6777f");
            V(123456792f, "123456792f");
            V(0.6666666f, "0.6666666f");
            V(-0.1f, "-0.1f");
            V(-123.456f, "-123.456f");
            V(float.MaxValue, "MaxValue");
            V(float.MinValue, "MinValue");
            V(float.Epsilon, "Epsilon");
            V(1.1754944E-38f, "MinNormal");
            V(0.0f, "0f");
            V(-0.0f, "-0f");
            V(float.NaN, "NaN");
            V(float.PositiveInfinity, "+Inf");
            V(float.NegativeInfinity, "-Inf");

            // The no-arg default (icall_single_to_string surface, G7) -- control.
            Console.WriteLine("[S" + Id() + "] " + 3.14159265f.ToString());
            Console.WriteLine("[S" + Id() + "] " + 16777216f.ToString());
            Console.WriteLine("[S" + Id() + "] " + 1e-45f.ToString());

            // Invalid standard specifiers for float -> FormatException.
            F("D5", 123f);
            F("X2", 123f);

            // Provider surface: InvariantCulture (ASCII Infinity) vs the default
            // culture (zh-CN U+221E symbol) -- specials and layouts.
            float inf = float.PositiveInfinity;
            float ninf = float.NegativeInfinity;
            float nan = float.NaN;
            Console.WriteLine("[P" + Id() + "] " + inf.ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("[P" + Id() + "] " + ninf.ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("[P" + Id() + "] " + nan.ToString("F2", CultureInfo.InvariantCulture));
            Console.WriteLine("[P" + Id() + "] " + inf.ToString("F2", CultureInfo.InvariantCulture));
            Console.WriteLine("[P" + Id() + "] " + ninf.ToString("G", CultureInfo.InvariantCulture));
            Console.WriteLine("[P" + Id() + "] " + inf.ToString("F2"));
            Console.WriteLine("[P" + Id() + "] " + ninf.ToString("G"));
            Console.WriteLine("[P" + Id() + "] " + nan.ToString("R"));
            float pva = 1234.5678f;
            float pvb = -1234.5678f;
            Console.WriteLine("[P" + Id() + "] " + pva.ToString("P2", CultureInfo.InvariantCulture));
            Console.WriteLine("[P" + Id() + "] " + pvb.ToString("P2", CultureInfo.InvariantCulture));
            Console.WriteLine("[P" + Id() + "] " + pva.ToString("C2", CultureInfo.InvariantCulture));
            float pg = 0.1f;
            Console.WriteLine("[P" + Id() + "] " + pg.ToString("G", CultureInfo.InvariantCulture));
            Console.WriteLine("[P" + Id() + "] " + pg.ToString("G15", CultureInfo.InvariantCulture));
            float pbig = 16777216f;
            Console.WriteLine("[P" + Id() + "] " + pbig.ToString("G7", CultureInfo.InvariantCulture));
            float ppi = 3.14159265f;
            Console.WriteLine("[P" + Id() + "] " + ppi.ToString("G7", CultureInfo.InvariantCulture));
            Console.WriteLine("[P" + Id() + "] " + ppi.ToString("F7", CultureInfo.InvariantCulture));

            // Boxed composite surface: StringBuilder/String.Format items ride
            // icall_format_composite_item, which must give System.Single boxes
            // the same float-width engine (net48 AppendFormat -> IFormattable).
            Console.WriteLine("[C" + Id() + "] " + string.Format(CultureInfo.InvariantCulture, "{0:F10}", 0.1f));
            Console.WriteLine("[C" + Id() + "] " + string.Format(CultureInfo.InvariantCulture, "{0:E9}", 0.1f));
            Console.WriteLine("[C" + Id() + "] " + string.Format(CultureInfo.InvariantCulture, "{0:G7}", 3.14159265f));
            Console.WriteLine("[C" + Id() + "] " + string.Format(CultureInfo.InvariantCulture, "{0:G15}", 0.1f));
            Console.WriteLine("[C" + Id() + "] " + string.Format(CultureInfo.InvariantCulture, "{0:N5}", 16777216f));
            Console.WriteLine("[C" + Id() + "] " + string.Format(CultureInfo.InvariantCulture, "{0:P4}", 0.15f));
            Console.WriteLine("[C" + Id() + "] " + string.Format(CultureInfo.InvariantCulture, "{0:0.0000000000}", 2.675f));
            Console.WriteLine("[C" + Id() + "] " + string.Format(CultureInfo.InvariantCulture, "{0:R}", 0.6666666f));
            Console.WriteLine("[C" + Id() + "] " + string.Format(CultureInfo.InvariantCulture, "[{0,12:F4}]", 1.5f));
            Console.WriteLine("[C" + Id() + "] " + string.Format(CultureInfo.InvariantCulture, "{0:F2}", float.PositiveInfinity));
            float ifaceVal = 0.1f;
            Console.WriteLine("[C" + Id() + "] " + ((IFormattable)ifaceVal).ToString("F10", CultureInfo.InvariantCulture));
            return 0;
        }
    }
}
