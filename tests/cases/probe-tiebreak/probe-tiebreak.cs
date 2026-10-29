//------------------------------------------------------------------------------
// probe-tiebreak -- RED probe for the miscfmt float digit-base residual: the
// net48 float conversion base rounds half AWAY FROM ZERO, but our float engine
// builds its 7- (and 9-) significant-digit base with the CRT's %.6e/%.8e
// converter, which rounds half to EVEN. The two disagree only at an EXACT tie:
// the binary value is precisely D.5 at the base width.
//
// The tie family is exact, not sampled: a float is a 7-digit tie iff its exact
// decimal expansion has exactly EIGHT significant digits and the last one is 5
// (v = (10X+5) * 10^(exp10-7)). Such a value exists only for exp10 in
// [-4, 7] (a wider exponent needs 5^k to divide the 24-bit mantissa) and is
// representable only when the odd part fits the 24-bit significand, which the
// constants below satisfy exactly (the [V...] record pins the IEEE bits).
// The base-9 (E precision > 6 / G precision > 7) ladder has the same defect at
// its NINTH digit -- the second family below (ten significant digits).
//
// Every value is printed with the format battery (G/G7/G9/G10/F/E/N/C/P/R/
// custom) plus the no-argument Single.ToString() / concat / composite surfaces.
// net48 (oracle on this host) must render the ties with the magnitude rounded
// UP at the tie: 12345645f "G7" -> "1.234565E+07", not the CRT's
// "1.234564E+07". The odd-prefix controls (X odd) already agree with the CRT
// and pin that the fix must not double-round them.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class ProbeTiebreak
    {
        private static int n;

        private static string Id()
        {
            string s = n.ToString("D4");
            n = n + 1;
            return s;
        }

        private static void F(string tag, string fmt, float v)
        {
            string s;
            try { s = v.ToString(fmt, CultureInfo.InvariantCulture); }
            catch (Exception e) { s = e.GetType().Name; }
            Console.WriteLine("[T" + Id() + "] " + tag + "|" + fmt + "|" + s);
        }

        private static void V(float v, string tag)
        {
            Console.WriteLine("[V" + Id() + "] " + tag + " bits=" +
                BitConverter.ToInt32(BitConverter.GetBytes(v), 0).ToString("X8"));
            string[] fmts =
            {
                "G7", "G", "g7", "G6", "G8", "G9", "G10", "G15",
                "F0", "F2", "F7", "F10",
                "E6", "E7", "E8", "E9", "E10",
                "N0", "N2", "N10",
                "C2", "P2", "P0",
                "R", "r",
                "0.00", "0.0000000000", "#.########", "0.00000000E+00",
            };
            foreach (var f in fmts) F(tag, f, v);
            // The no-format surfaces (icall_single_to_string is the float G7
            // text; the composite item rides icall_format_float_custom).
            Console.WriteLine("[D" + Id() + "] " + tag + "|noarg|" + v.ToString());
            Console.WriteLine("[D" + Id() + "] " + tag + "|concat|" + ("" + v));
            Console.WriteLine("[D" + Id() + "] " + tag + "|composite|" +
                string.Format(CultureInfo.InvariantCulture, "{0:G7}", v));
        }

        private static void D(double v, string tag, string fmt)
        {
            string s;
            try { s = v.ToString(fmt, CultureInfo.InvariantCulture); }
            catch (Exception e) { s = e.GetType().Name; }
            Console.WriteLine("[B" + Id() + "] " + tag + "|" + fmt + "|" + s);
        }

        public static int Main()
        {
            // --- base 7, exp10 = 7: eight-digit integers ending in 5 ---
            V(12345645f, "i12345645");   // X=1234564 EVEN -> CRT rounds DOWN (RED)
            V(12345655f, "i12345655");   // X=1234565 odd  -> CRT rounds up (control)
            V(12345605f, "i12345605");   // X=1234560 EVEN -> RED
            V(-12345645f, "ni12345645"); // negative tie (RED)
            V(16777215f, "i2p24m1");     // X=1677721 odd  -> control (max mantissa)

            // --- base 7, exp10 = 6: X.5 in the millions ---
            V(1234564.5f, "x1234564_5"); // X=1234564 EVEN -> RED
            V(1234565.5f, "x1234565_5"); // X=1234565 odd  -> control
            V(6000000.5f, "x6000000_5"); // X=6000000 EVEN -> RED
            V(5999999.5f, "x5999999_5"); // X=5999999 odd  -> control

            // --- base 7, exp10 = 5..-4: the tie walks down the fraction ---
            V(600000.25f, "e5a");        // X=6000002 EVEN -> RED
            V(599999.75f, "e5b");        // X=5999997 odd  -> control
            V(123456.25f, "e5c");        // X=1234562 EVEN -> RED
            V(60000.125f, "e4a");        // X=6000012 EVEN -> RED
            V(59999.875f, "e4b");        // X=5999987 odd  -> control
            V(6000.0625f, "e3a");        // X=6000062 EVEN -> RED
            V(5999.9375f, "e3b");        // X=5999937 odd  -> control
            V(600.03125f, "e2a");        // X=6000312 EVEN -> RED
            V(599.96875f, "e2b");        // X=5999687 odd  -> control
            V(60.015625f, "e1a");        // X=6001562 EVEN -> RED
            V(59.984375f, "e1b");        // X=5998437 odd  -> control
            V(6.0078125f, "e0a");        // X=6007812 EVEN -> RED
            V(5.9921875f, "e0b");        // X=5992187 odd  -> control
            V(0.59765625f, "em1a");      // X=5976562 EVEN -> RED
            V(0.60546875f, "em1b");      // X=6054687 odd  -> control
            V(0.064453125f, "em2a");     // X=6445312 EVEN -> RED
            V(0.060546875f, "em2b");     // X=6054687 odd  -> control
            V(0.0048828125f, "em3a");    // X=4882812 EVEN -> RED
            V(0.0068359375f, "em3b");    // X=6835937 odd  -> control
            V(0.00048828125f, "em4a");   // X=4882812 EVEN -> RED (smallest tie)
            V(-600000.25f, "ne5a");      // negative tie (RED)
            V(-0.064453125f, "nem2a");   // negative tie (RED)

            // --- base 9 (E>6 / G>7): ten significant digits, digit 9 = 5 ---
            V(1234567.625f, "b9a");      // X(9)=123456762 EVEN -> RED at base 9
            V(1234567.875f, "b9b");      // X(9)=123456787 odd  -> control
            V(123456.8125f, "b9c");      // X(9)=123456812 EVEN -> RED
            V(123456.9375f, "b9d");      // X(9)=123456937 odd  -> control
            V(12345.65625f, "b9e");      // X(9)=123456562 EVEN -> RED

            // --- double controls: the double face must stay byte-identical ---
            D(0.1, "d0.1", "G15");
            D(12345645.0, "di12345645", "G15");
            D(2.675, "d2.675", "F2");
            D(123456789012345.5, "dtie15", "G15");
            D(123456789012345.5, "dtie15f", "F1");
            return 0;
        }
    }
}
