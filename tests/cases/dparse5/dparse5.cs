// dparse5 -- provider identity matrix. The runtime culture model is
// invariant-only, so the provider is only observed for PRESENCE: for
// CULTURE-NEUTRAL input (digits, signs, whitespace, thousands separators,
// exponents, parentheses) null / InvariantCulture / CurrentCulture must all
// produce the same value. The invariant currency symbol (U+00A4) is the one
// the runtime accepts for every provider; net48's null/CurrentCulture provider
// resolves to the zh-CN host culture (U+00A5), a documented divergence, so the
// currency rows are asserted through InvariantCulture only.
using System;
using System.Globalization;

namespace Cases
{
    public static class DParse5
    {
        static string R(string s, int st, IFormatProvider p)
        {
            try { return "v:" + decimal.Parse(s, (NumberStyles)st, p).ToString(CultureInfo.InvariantCulture); }
            catch (Exception e) { return "<" + e.GetType().Name + ">"; }
        }

        static string R2(string s, IFormatProvider p)
        {
            try { return "v:" + decimal.Parse(s, p).ToString(CultureInfo.InvariantCulture); }
            catch (Exception e) { return "<" + e.GetType().Name + ">"; }
        }

        static string T(string s, int st, IFormatProvider p)
        {
            decimal d;
            try { return (decimal.TryParse(s, (NumberStyles)st, p, out d) ? "t:" : "f:") + d.ToString(CultureInfo.InvariantCulture); }
            catch (Exception e) { return "<" + e.GetType().Name + ">"; }
        }

        static void Cmp(string k, string s, int st)
        {
            string a = R(s, st, null);
            string b = R(s, st, CultureInfo.InvariantCulture);
            string c = R(s, st, CultureInfo.CurrentCulture);
            Console.WriteLine(k + "=[" + a + "|" + b + "|" + c + "|" + (a == b && b == c) + "]");
        }

        static void Cmp2(string k, string s)
        {
            string a = R2(s, null);
            string b = R2(s, CultureInfo.InvariantCulture);
            string c = R2(s, CultureInfo.CurrentCulture);
            Console.WriteLine(k + "=[" + a + "|" + b + "|" + c + "|" + (a == b && b == c) + "]");
        }

        static void CmpT(string k, string s, int st)
        {
            string a = T(s, st, null);
            string b = T(s, st, CultureInfo.InvariantCulture);
            string c = T(s, st, CultureInfo.CurrentCulture);
            Console.WriteLine(k + "=[" + a + "|" + b + "|" + c + "|" + (a == b && b == c) + "]");
        }

        public static void Main()
        {
            Console.WriteLine("culture=" + CultureInfo.CurrentCulture.Name);

            // ---- culture-neutral: all three providers must agree ----
            Cmp("a1", "1.5", (int)NumberStyles.Number);
            Cmp("a2", "1,234.5", (int)NumberStyles.Number);
            Cmp("a3", "  -2.25  ", (int)NumberStyles.Number);
            Cmp("a4", "1.5e3", (int)NumberStyles.Float);
            Cmp("a6", "(1.5)", (int)(NumberStyles.Number | NumberStyles.AllowParentheses));
            Cmp("a7", "1.5-", (int)NumberStyles.Number);
            Cmp("a8", "abc", (int)NumberStyles.Number);
            Cmp("a9", "1e40", (int)NumberStyles.Float);

            Cmp2("b1", "1.5");
            Cmp2("b2", "1,234.5");
            Cmp2("b3", "  7  ");
            Cmp2("b4", "1e3");
            Cmp2("b5", null);

            CmpT("c1", "1.5", (int)NumberStyles.Number);
            CmpT("c2", "1.5e3", (int)NumberStyles.Float);
            CmpT("c4", "zzz", (int)NumberStyles.Number);
            CmpT("c5", null, (int)NumberStyles.Number);

            // ---- currency: the invariant symbol (U+00A4) is only reachable with the
            // INVARIANT provider (net48 resolves a null provider to the zh-CN
            // host culture and rejects U+00A4 -- the documented divergence) ----
            Console.WriteLine("d1=[" + R("\u00A41.5", (int)NumberStyles.Currency, CultureInfo.InvariantCulture) + "]");
            Console.WriteLine("d3=[" + R("(\u00A41,234.5)", (int)NumberStyles.Currency, CultureInfo.InvariantCulture) + "]");
            Console.WriteLine("d4=[" + R("\u00A4-1.5", (int)NumberStyles.Currency, CultureInfo.InvariantCulture) + "]");
            Console.WriteLine("d5=[" + R("1.5\u00A4", (int)NumberStyles.Currency, CultureInfo.InvariantCulture) + "]");
            Console.WriteLine("d6=[" + T("\u00A41.5", (int)NumberStyles.Currency, CultureInfo.InvariantCulture) + "]");
        }
    }
}
