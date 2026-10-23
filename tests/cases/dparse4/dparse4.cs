// dparse4 -- styled-parse EXCEPTION semantics: FormatException /
// OverflowException / ArgumentNullException / ArgumentException (the
// AllowHexSpecifier + undefined-NumberStyles-bits rejects). Type name and
// ParamName are printed, so the net48 diff also pins the argument checks.
using System;
using System.Globalization;

namespace Cases
{
    public static class DParse4
    {
        static void P(string k, string s, int st)
        {
            try
            {
                decimal v = decimal.Parse(s, (NumberStyles)st, CultureInfo.InvariantCulture);
                Console.WriteLine(k + "=[v:" + v.ToString(CultureInfo.InvariantCulture) + "]");
            }
            catch (Exception e)
            {
                ArgumentException ae = e as ArgumentException;
                Console.WriteLine(k + "=<" + e.GetType().Name + ":" + (ae == null ? "-" : (ae.ParamName == null ? "<null>" : ae.ParamName)) + ">");
            }
        }

        static void T(string k, string s, int st)
        {
            decimal d;
            try
            {
                bool ok = decimal.TryParse(s, (NumberStyles)st, CultureInfo.InvariantCulture, out d);
                Console.WriteLine(k + "=[" + ok + ":" + d.ToString(CultureInfo.InvariantCulture) + "]");
            }
            catch (Exception e)
            {
                ArgumentException ae = e as ArgumentException;
                Console.WriteLine(k + "=<" + e.GetType().Name + ":" + (ae == null ? "-" : (ae.ParamName == null ? "<null>" : ae.ParamName)) + ">");
            }
        }

        public static void Main()
        {
            // ---- FormatException family ----
            P("fmt1", "abc", (int)(NumberStyles.Number));
            P("fmt2", "", (int)(NumberStyles.Number));
            P("fmt3", "   ", (int)(NumberStyles.Number));
            P("fmt4", "1.2.3", (int)(NumberStyles.Number));
            P("fmt5", ",1", (int)(NumberStyles.Number));
            P("fmt6", "+", (int)(NumberStyles.Number));
            P("fmt7", "-", (int)(NumberStyles.Number));
            P("fmt8", "1 2", (int)(NumberStyles.Number));
            P("fmt9", "1e3", (int)(NumberStyles.Number));
            P("fmt10", "1.5x", (int)(NumberStyles.Number));
            P("fmt11", "(", (int)(NumberStyles.Number | NumberStyles.AllowParentheses));
            P("fmt12", "1.5", (int)(NumberStyles.None));
            P("fmt13", "1\u00A4.5", (int)(NumberStyles.Currency));

            // ---- OverflowException family ----
            P("ovf1", "79228162514264337593543950336", (int)(NumberStyles.Number));
            P("ovf2", "-79228162514264337593543950336", (int)(NumberStyles.Number));
            P("ovf3", "1e40", (int)(NumberStyles.Float));
            P("ovf4", "1e1000", (int)(NumberStyles.Float));
            P("ovf5", "1234567890123456789012345678901", (int)(NumberStyles.Number));
            P("ovf6", "1000000000000000000000000000000", (int)(NumberStyles.Number));
            P("ovf7", "79228162514264337593543950335.5", (int)(NumberStyles.Number));

            // ---- rounding at the scale boundary (net48 rounds, never overflows) ----
            P("rnd1", "1e-29", (int)(NumberStyles.Float));
            P("rnd2", "1.5e-29", (int)(NumberStyles.Float));
            P("rnd3", "2.5e-29", (int)(NumberStyles.Float));
            P("rnd4", "0.000000000000000000000000000015", (int)(NumberStyles.Number));
            P("rnd5", "7922816251426433759354395033.6", (int)(NumberStyles.Number));
            P("rnd6", "79228162514264337593543950334.5", (int)(NumberStyles.Number));
            P("rnd7", "1.2345678901234567890123456789e-1", (int)(NumberStyles.Float));

            // ---- ArgumentNullException ----
            P("nul1", null, (int)(NumberStyles.Number));
            P("nul2", null, (int)(NumberStyles.Currency));
            T("nul3", null, (int)(NumberStyles.Number));

            // ---- ArgumentException: AllowHexSpecifier (any combination) ----
            P("hex1", "1F", (int)(NumberStyles.HexNumber));
            P("hex2", "1", (int)(NumberStyles.AllowHexSpecifier));
            P("hex3", "1", (int)(NumberStyles.HexNumber | NumberStyles.AllowLeadingWhite));
            P("hex4", "1", (int)(NumberStyles.Number | NumberStyles.AllowHexSpecifier));
            P("hex5", "1", (int)(NumberStyles.Any | NumberStyles.AllowHexSpecifier));
            P("hex6", null, (int)(NumberStyles.HexNumber));
            T("hex7", "1F", (int)(NumberStyles.HexNumber));
            T("hex8", null, (int)(NumberStyles.HexNumber));

            // ---- ArgumentException: undefined NumberStyles bits ----
            P("sty1", "1", (int)((NumberStyles)0x200));
            P("sty2", "1", (int)((NumberStyles)0x400));
            P("sty3", "1", (int)((NumberStyles)0x1000));
            P("sty4", "1", (int)((NumberStyles)0x8000));
            P("sty5", "1", (int)((NumberStyles)(-1)));
            P("sty6", "1", (int)((NumberStyles)0x1FF));
            T("sty7", "1", (int)((NumberStyles)0x1000));
            T("sty8", null, (int)((NumberStyles)0x1000));

            // ---- TryParse never throws except for the argument checks ----
            T("tp1", "abc", (int)(NumberStyles.Number));
            T("tp2", "", (int)(NumberStyles.Number));
            T("tp3", "1.5.5", (int)(NumberStyles.Number));
            T("tp4", "79228162514264337593543950336", (int)(NumberStyles.Number));
            T("tp5", "1e40", (int)(NumberStyles.Float));
            T("tp6", "1e-29", (int)(NumberStyles.Float));
            T("tp7", "1e-40", (int)(NumberStyles.Float));
            T("tp8", "\u00A41.5", (int)(NumberStyles.Currency));
            T("tp9", "1.5", (int)(NumberStyles.None));
        }
    }
}
