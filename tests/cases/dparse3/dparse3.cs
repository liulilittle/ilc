// dparse3 -- NumberStyles FLAG-BY-FLAG matrix (one flag per line, plus the
// composite presets). Every value is printed, so the net48 oracle diff is a
// byte-exact check of the styled decimal parser.
using System;
using System.Globalization;

namespace Cases
{
    public static class DParse3
    {
        static void P(string k, string s, int st)
        {
            try
            {
                Console.WriteLine(k + "=[" + decimal.Parse(s, (NumberStyles)st, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "]");
            }
            catch (Exception e) { Console.WriteLine(k + "=<" + e.GetType().Name + ">"); }
        }

        static void T(string k, string s, int st)
        {
            decimal d;
            try
            {
                bool ok = decimal.TryParse(s, (NumberStyles)st, CultureInfo.InvariantCulture, out d);
                Console.WriteLine(k + "=[" + ok + ":" + d.ToString(CultureInfo.InvariantCulture) + "]");
            }
            catch (Exception e) { Console.WriteLine(k + "=<" + e.GetType().Name + ">"); }
        }

        public static void Main()
        {
            // ---- AllowLeadingWhite / AllowTrailingWhite (1 / 2) ----
            P("lw1", " 1.5", (int)(NumberStyles.AllowLeadingWhite | NumberStyles.AllowDecimalPoint));
            P("lw2", "1.5 ", (int)(NumberStyles.AllowLeadingWhite | NumberStyles.AllowDecimalPoint));
            P("tw1", "1.5 ", (int)(NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint));
            P("tw2", " 1.5", (int)(NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint));
            P("ws1", " \t\r\n\v\f1.5 ", (int)(NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint));
            P("ws2", "1.5", (int)(NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint));

            // ---- AllowLeadingSign (4) ----
            P("ls1", "-1.5", (int)(NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint));
            P("ls2", "+1.5", (int)(NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint));
            P("ls3", "1.5-", (int)(NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint));
            P("ls4", "-1.5", (int)(NumberStyles.AllowDecimalPoint));

            // ---- AllowTrailingSign (8) ----
            P("ts1", "1.5-", (int)(NumberStyles.AllowTrailingSign | NumberStyles.AllowDecimalPoint));
            P("ts2", "1.5+", (int)(NumberStyles.AllowTrailingSign | NumberStyles.AllowDecimalPoint));
            P("ts3", "-1.5", (int)(NumberStyles.AllowTrailingSign | NumberStyles.AllowDecimalPoint));
            P("ts4", "-1.5-", (int)(NumberStyles.AllowTrailingSign | NumberStyles.AllowDecimalPoint));
            P("ts5", "1.5 -", (int)(NumberStyles.AllowTrailingSign | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint));

            // ---- AllowParentheses (16) ----
            P("pa1", "(1.5)", (int)(NumberStyles.AllowParentheses | NumberStyles.AllowDecimalPoint));
            P("pa2", "(1.5)", (int)(NumberStyles.AllowDecimalPoint));
            P("pa3", "(-1.5)", (int)(NumberStyles.AllowParentheses | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign));
            P("pa4", "(1.5", (int)(NumberStyles.AllowParentheses | NumberStyles.AllowDecimalPoint));
            P("pa5", "1.5)", (int)(NumberStyles.AllowParentheses | NumberStyles.AllowDecimalPoint));
            P("pa6", "(1.5)-", (int)(NumberStyles.AllowParentheses | NumberStyles.AllowDecimalPoint | NumberStyles.AllowTrailingSign));

            // ---- AllowDecimalPoint (32) ----
            P("dp1", "1.5", (int)(NumberStyles.AllowDecimalPoint));
            P("dp2", "1.5", (int)(NumberStyles.None));
            P("dp3", "1.5.5", (int)(NumberStyles.AllowDecimalPoint));
            P("dp4", ".5", (int)(NumberStyles.AllowDecimalPoint));
            P("dp5", "5.", (int)(NumberStyles.AllowDecimalPoint));

            // ---- AllowThousands (64) ----
            P("th1", "1,234.5", (int)(NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint));
            P("th2", "1,234.5", (int)(NumberStyles.AllowDecimalPoint));
            P("th3", "1,23", (int)(NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint));
            P("th4", ",123", (int)(NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint));
            P("th5", "1,", (int)(NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint));
            P("th6", "1,,2", (int)(NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint));
            P("th7", "1,000,000.25", (int)(NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint));
            P("th8", "1.5,", (int)(NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint));
            P("th9", "1,5,", (int)(NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint));

            // ---- AllowExponent (128) ----
            P("ex1", "1e3", (int)(NumberStyles.AllowExponent | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign));
            P("ex2", "1E3", (int)(NumberStyles.AllowExponent | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign));
            P("ex3", "1e+3", (int)(NumberStyles.AllowExponent | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign));
            P("ex4", "1e-3", (int)(NumberStyles.AllowExponent | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign));
            P("ex5", "1.5e2", (int)(NumberStyles.AllowExponent | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign));
            P("ex6", "1e3", (int)(NumberStyles.AllowDecimalPoint));
            P("ex7", "1e", (int)(NumberStyles.AllowExponent | NumberStyles.AllowDecimalPoint));
            P("ex8", "1e+", (int)(NumberStyles.AllowExponent | NumberStyles.AllowDecimalPoint));
            P("ex9", "1e40", (int)(NumberStyles.Float));
            P("ex10", "1e28", (int)(NumberStyles.Float));
            P("ex11", "1e-28", (int)(NumberStyles.Float));
            P("ex12", "1e-29", (int)(NumberStyles.Float));
            P("ex13", "123456789012345678901234567890e-10", (int)(NumberStyles.Float));
            P("ex14", "1e1000", (int)(NumberStyles.Float));
            P("ex15", "1e-1000", (int)(NumberStyles.Float));
            P("ex16", "1e999999999", (int)(NumberStyles.Float));
            P("ex17", "1e-999999999", (int)(NumberStyles.Float));
            P("ex18", "1e2147483648", (int)(NumberStyles.Float));
            P("ex19", "0e999999999999", (int)(NumberStyles.Float));
            P("ex20", "1.50e0", (int)(NumberStyles.Float));
            P("ex21", "15.0e-1", (int)(NumberStyles.Float));
            P("ex22", "1.000e2", (int)(NumberStyles.Float));
            P("ex23", "1.000e-2", (int)(NumberStyles.Float));

            // ---- AllowCurrencySymbol (256) ----
            P("cu1", "\u00A41.5", (int)(NumberStyles.Currency));
            P("cu2", "1.5\u00A4", (int)(NumberStyles.Currency));
            P("cu3", "-\u00A41.5", (int)(NumberStyles.Currency));
            P("cu4", "\u00A4-1.5", (int)(NumberStyles.Currency));
            P("cu5", "1.5-\u00A4", (int)(NumberStyles.Currency));
            P("cu6", "1.5\u00A4-", (int)(NumberStyles.Currency));
            P("cu7", "(\u00A41.5)", (int)(NumberStyles.Currency));
            P("cu8", "\u00A41.5", (int)(NumberStyles.AllowCurrencySymbol | NumberStyles.AllowDecimalPoint));
            P("cu9", "1,234.5", (int)(NumberStyles.Currency));
            P("cu10", "\u00A41,234.5", (int)(NumberStyles.Currency));
            P("cu11", "\u00A4 1.5", (int)(NumberStyles.Currency));
            P("cu12", "\u00A41.5\u00A4", (int)(NumberStyles.Currency));
            P("cu13", "1\u00A4.5", (int)(NumberStyles.Currency));
            P("cu14", "\u00A4", (int)(NumberStyles.Currency));
            P("cu15", "\u00A4-1,234.5", (int)(NumberStyles.Currency));

            // ---- composite presets ----
            P("n1", " 1,234.5 ", (int)(NumberStyles.Number));
            P("n2", "1.5-", (int)(NumberStyles.Number));
            P("n3", "(1.5)", (int)(NumberStyles.Number));
            P("n4", "1e3", (int)(NumberStyles.Number));
            P("f1", " 1.5e3 ", (int)(NumberStyles.Float));
            P("f2", "1,234.5", (int)(NumberStyles.Float));
            P("f3", "1.5-", (int)(NumberStyles.Float));
            P("f4", "(1.5)", (int)(NumberStyles.Float));
            P("i1", " -12 ", (int)(NumberStyles.Integer));
            P("i2", "1.5", (int)(NumberStyles.Integer));
            P("i3", "1,234", (int)(NumberStyles.Integer));
            P("a1", "\u00A4(1,234.5)", (int)(NumberStyles.Any));
            P("a2", " 1.5e3 ", (int)(NumberStyles.Any));
            P("a3", "1.5-", (int)(NumberStyles.Any));
            P("z1", "1", (int)(NumberStyles.None));
            P("z2", " 1", (int)(NumberStyles.None));
            P("z3", "-1", (int)(NumberStyles.None));

            // ---- TryParse consistency over the same shapes ----
            T("t1", "1,234.5", (int)(NumberStyles.Number));
            T("t2", "1.5-", (int)(NumberStyles.Number));
            T("t3", "(1.5)", (int)(NumberStyles.Number | NumberStyles.AllowParentheses));
            T("t4", "1e3", (int)(NumberStyles.Float));
            T("t5", "\u00A41.5", (int)(NumberStyles.Currency));
            T("t6", "1.5", (int)(NumberStyles.None));
            T("t7", "1e40", (int)(NumberStyles.Float));
        }
    }
}
