//------------------------------------------------------------------------------
// parsegroupsep1.cs -- GROUP SEPARATORS in numeric parse text (net48 oracle,
// byte-for-byte).
//
// Regression target: the runtime comments in il2cpp_string_ext.h /
// il2cpp_parse.h / il2cpp_format.h claimed "no group separators in double
// text ("1,000" fails)". MEASURED net48: double.Parse("1,000") == 1000 (the
// default is NumberStyles.Float | AllowThousands with the CURRENT culture),
// and the accepted separator comes from the PROVIDER's NumberFormatInfo --
// "," for invariant/zh-CN, "." for de-DE, U+202F for fr-FR. A hardcoded ","
// would pass the invariant rows and fail the de-DE/fr-FR ones.
//
// Asserted contract:
//   * default-provider double/float/decimal parse accepts the CURRENT
//     culture's group separator ("," on this zh-CN host).
//   * explicit provider: invariant "," / de-DE "." / fr-FR U+202F.
//   * de-DE swaps the roles: "1.000" == 1000 and "1,000" == 1 (the ',' is
//     the DECIMAL point); "1.000,5" == 1000.5, "1,000.5" is a FormatException.
//   * grouping is NOT validated (net48 skips separator runs): "1,00" == 100,
//     "1,,2" == 12, "1,0" == 10, "1," == 1; a LEADING separator (",1") is a
//     FormatException.
//   * NumberStyles.Float WITHOUT AllowThousands still rejects "1,000".
//   * int.Parse REJECTS group separators in every culture (the int half of
//     the old comment is TRUE and must stay).
//
// EVERY line prints "label=value"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class ParseGroupSep1
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly CultureInfo Zh = CultureInfo.GetCultureInfo("zh-CN");
        static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
        static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        static string D(Func<double> f)
        {
            try { return "v:" + f().ToString("R", CultureInfo.InvariantCulture); }
            catch (Exception e) { return "T:" + e.GetType().Name; }
        }

        static string F(Func<float> f)
        {
            try { return "v:" + f().ToString("R", CultureInfo.InvariantCulture); }
            catch (Exception e) { return "T:" + e.GetType().Name; }
        }

        static string M(Func<decimal> f)
        {
            try { return "v:" + f().ToString(CultureInfo.InvariantCulture); }
            catch (Exception e) { return "T:" + e.GetType().Name; }
        }

        static string I(Func<int> f)
        {
            try { return "v:" + f().ToString(CultureInfo.InvariantCulture); }
            catch (Exception e) { return "T:" + e.GetType().Name; }
        }

        public static void Main()
        {
            // ---- 1. (no NumberFormatInfo getters here: the separator
            //         itself is asserted THROUGH parsing, which is the
            //         contract under test) ----
            Console.WriteLine("start=1");

            // ---- 2. DEFAULT provider = CurrentCulture ("," on this host) ----
            Console.WriteLine("dft.1000=" + D(() => double.Parse("1,000")));
            Console.WriteLine("dft.1000_5=" + D(() => double.Parse("1,000.5")));
            Console.WriteLine("dft.1m=" + D(() => double.Parse("1,000,000")));
            Console.WriteLine("dft.badgrp=" + D(() => double.Parse("1,00")));
            Console.WriteLine("dft.trail=" + D(() => double.Parse("1,")));
            Console.WriteLine("dft.lead=" + D(() => double.Parse(",1")));
            Console.WriteLine("dft.dbl=" + D(() => double.Parse(",,1")));
            Console.WriteLine("dft.dblmid=" + D(() => double.Parse("1,,2")));
            Console.WriteLine("dft.e3=" + D(() => double.Parse("1,000e3")));
            Console.WriteLine("dft.neg=" + D(() => double.Parse("-1,000")));
            Console.WriteLine("dft.plus=" + D(() => double.Parse("+1,000")));
            Console.WriteLine("dft.nbsp=" + D(() => double.Parse("1\u00A0000")));
            Console.WriteLine("dft.space=" + D(() => double.Parse("1 000")));
            Console.WriteLine("dft.float=" + F(() => float.Parse("1,000")));
            Console.WriteLine("dft.dec=" + M(() => decimal.Parse("1,000")));

            // ---- 3. explicit invariant provider ----
            Console.WriteLine("inv.1000=" + D(() => double.Parse("1,000", Inv)));
            Console.WriteLine("inv.1000_5=" + D(() => double.Parse("1,000.5", Inv)));
            Console.WriteLine("inv.thous=" + D(() => double.Parse("1,000", NumberStyles.Float | NumberStyles.AllowThousands, Inv)));
            Console.WriteLine("inv.floatonly=" + D(() => double.Parse("1,000", NumberStyles.Float, Inv)));
            Console.WriteLine("inv.num=" + D(() => double.Parse("1,000.5", NumberStyles.Number, Inv)));
            Console.WriteLine("inv.zh.1000=" + D(() => double.Parse("1,000", Zh)));
            Console.WriteLine("inv.dec=" + M(() => decimal.Parse("1,000", Inv)));
            Console.WriteLine("inv.dec.thous=" + M(() => decimal.Parse("1,000", NumberStyles.Number, Inv)));

            // ---- 4. de-DE: '.' groups, ',' is the decimal point ----
            Console.WriteLine("de.1000=" + D(() => double.Parse("1.000", De)));
            Console.WriteLine("de.comma=" + D(() => double.Parse("1,000", De)));
            Console.WriteLine("de.full=" + D(() => double.Parse("1.000,5", De)));
            Console.WriteLine("de.mixed=" + D(() => double.Parse("1,000.5", De)));
            Console.WriteLine("de.1m=" + D(() => double.Parse("1.000.000", De)));
            Console.WriteLine("de.exp=" + D(() => double.Parse("1.000,5e3", De)));
            Console.WriteLine("de.f.default=" + F(() => float.Parse("1.000")));
            Console.WriteLine("de.dec=" + M(() => decimal.Parse("1.000", De)));
            Console.WriteLine("de.dec.full=" + M(() => decimal.Parse("1.000,5", De)));
            Console.WriteLine("de.thous=" + D(() => double.Parse("1.000", NumberStyles.Float | NumberStyles.AllowThousands, De)));
            Console.WriteLine("de.floatonly=" + D(() => double.Parse("1.000", NumberStyles.Float, De)));

            // ---- 5. fr-FR: U+202F narrow no-break space groups ----
            Console.WriteLine("fr.narrow=" + D(() => double.Parse("1\u202F000", Fr)));
            Console.WriteLine("fr.space=" + D(() => double.Parse("1 000", Fr)));
            Console.WriteLine("fr.nbsp=" + D(() => double.Parse("1\u00A0000", Fr)));

            // ---- 6. TryParse (no throw) shares the rule ----
            Console.WriteLine("try.inv=" + TryD("1,000", (int)(NumberStyles.Float | NumberStyles.AllowThousands), Inv));
            Console.WriteLine("try.inv.floatonly=" + TryD("1,000", (int)NumberStyles.Float, Inv));
            Console.WriteLine("try.de=" + TryD("1.000", (int)(NumberStyles.Float | NumberStyles.AllowThousands), De));
            Console.WriteLine("try.de.1000bad=" + TryD("1,000", (int)(NumberStyles.Float | NumberStyles.AllowThousands), De));
            Console.WriteLine("try.inv.lead=" + TryD(",1", (int)(NumberStyles.Float | NumberStyles.AllowThousands), Inv));
            Console.WriteLine("try.inv.badgrp=" + TryD("1,00", (int)(NumberStyles.Float | NumberStyles.AllowThousands), Inv));

            // ---- 7. int REJECTS group separators in every culture ----
            Console.WriteLine("int.inv=" + I(() => int.Parse("1,000")));
            Console.WriteLine("int.zh=" + I(() => int.Parse("1,000")));
            Console.WriteLine("int.de.dot=" + I(() => int.Parse("1.000")));
            Console.WriteLine("int.de.comma=" + I(() => int.Parse("1,000")));
            Console.WriteLine("int.try.default=" + int.TryParse("1,000", out int _));

            // ---- 8. the same text under a DIFFERENT provider differs ----
            Console.WriteLine("diff.1000=" + (D(() => double.Parse("1,000", Inv)) != D(() => double.Parse("1,000", De))));
            Console.WriteLine("diff.1dot000=" + (D(() => double.Parse("1.000", Inv)) != D(() => double.Parse("1.000", De))));
        }

        static string TryD(string s, int st, IFormatProvider p)
        {
            try
            {
                return double.TryParse(s, (NumberStyles)st, p, out double d)
                    ? "t:" + d.ToString("R", CultureInfo.InvariantCulture)
                    : "f";
            }
            catch (Exception e) { return "T:" + e.GetType().Name; }
        }
    }
}





