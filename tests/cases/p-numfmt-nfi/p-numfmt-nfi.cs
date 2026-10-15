//------------------------------------------------------------------------------
// p-numfmt-nfi.cs -- NumberFormatInfo current-culture getter alignment
// regression (probe batch 3, gn20-numfmt). net48 oracle == transpiled runtime
// byte-exact (character-escaped so the diff is code-page independent).
//
// Covers the NumberFormatInfo GETTER surface under the invariant / zh-CN
// two-culture dispatch that P0-4 audited as "hard-coded invariant literals":
// the separator/sign/symbol STRING getters, the int pattern/digit getters,
// the int[] group sizes and the string[] native digits, plus the D1 follow-up
// get_DigitSubstitution (DigitShapes.None == 1 on both cultures) and
// get_IsReadOnly (true on both net48 canonical instances). All values are
// pinned from the net48 oracle (oracle_net48.txt / oracle_xcult.txt): zh-CN
// and invariant agree on every string separator/symbol and on group sizes /
// native digits; the three CULTURE-DIFFERING int patterns (CurrencyNegative
// Pattern 2/0, PercentPositivePattern 1/0, PercentNegativePattern 1/0)
// dispatch on the culture-name receiver at run time.
//
// Composite String.Format rows ({0:N}/{0:C}/{0:P}/{0:E}/{0:F2}) on the
// current-culture host: int/double/negative/large/long/decimal all byte-exact.
// The UNSIGNED boundary (uint32 >= 2^31 / uint64 >= 2^63 decimal-family
// formats) is a pre-existing documented subset boundary -- recorded in the
// GAPS.md, NOT asserted here (net48 renders them; the runtime stays on the
// documented signed-engine boundary).
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class PNumfmtNfi
    {
        private static string Esc(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (c == '\\') { sb.Append("\\x5C"); continue; }
                if (c == '\n') { sb.Append("\\n"); continue; }
                if (c == '\r') { sb.Append("\\r"); continue; }
                if (c >= 0x20 && c < 0x7F) { sb.Append(c); continue; }
                sb.Append("\\u");
                sb.Append(((int)c).ToString("X4"));
            }
            return sb.ToString();
        }

        private static void G(string label, NumberFormatInfo nfi)
        {
            Console.WriteLine("g." + label + ".CurrencySymbol=" + Esc(nfi.CurrencySymbol));
            Console.WriteLine("g." + label + ".PositiveSign=" + Esc(nfi.PositiveSign));
            Console.WriteLine("g." + label + ".NegativeSign=" + Esc(nfi.NegativeSign));
            Console.WriteLine("g." + label + ".NumberDecimalSeparator=" + Esc(nfi.NumberDecimalSeparator));
            Console.WriteLine("g." + label + ".NumberGroupSeparator=" + Esc(nfi.NumberGroupSeparator));
            Console.WriteLine("g." + label + ".CurrencyDecimalSeparator=" + Esc(nfi.CurrencyDecimalSeparator));
            Console.WriteLine("g." + label + ".CurrencyGroupSeparator=" + Esc(nfi.CurrencyGroupSeparator));
            Console.WriteLine("g." + label + ".PercentSymbol=" + Esc(nfi.PercentSymbol));
            Console.WriteLine("g." + label + ".PercentDecimalSeparator=" + Esc(nfi.PercentDecimalSeparator));
            Console.WriteLine("g." + label + ".PercentGroupSeparator=" + Esc(nfi.PercentGroupSeparator));
            Console.WriteLine("g." + label + ".PerMilleSymbol=" + Esc(nfi.PerMilleSymbol));
            Console.WriteLine("g." + label + ".NaNSymbol=" + Esc(nfi.NaNSymbol));
            Console.WriteLine("g." + label + ".PositiveInfinitySymbol=" + Esc(nfi.PositiveInfinitySymbol));
            Console.WriteLine("g." + label + ".NegativeInfinitySymbol=" + Esc(nfi.NegativeInfinitySymbol));
            Console.WriteLine("g." + label + ".NumberNegativePattern=" + nfi.NumberNegativePattern);
            Console.WriteLine("g." + label + ".NumberDecimalDigits=" + nfi.NumberDecimalDigits);
            Console.WriteLine("g." + label + ".CurrencyDecimalDigits=" + nfi.CurrencyDecimalDigits);
            Console.WriteLine("g." + label + ".CurrencyPositivePattern=" + nfi.CurrencyPositivePattern);
            Console.WriteLine("g." + label + ".CurrencyNegativePattern=" + nfi.CurrencyNegativePattern);
            Console.WriteLine("g." + label + ".PercentPositivePattern=" + nfi.PercentPositivePattern);
            Console.WriteLine("g." + label + ".PercentNegativePattern=" + nfi.PercentNegativePattern);
            Console.WriteLine("g." + label + ".PercentDecimalDigits=" + nfi.PercentDecimalDigits);
            Console.WriteLine("g." + label + ".DigitSubstitution=" + (int)nfi.DigitSubstitution);
            Console.WriteLine("g." + label + ".IsReadOnly=" + nfi.IsReadOnly);
            string gs = "";
            for (int i = 0; i < nfi.NumberGroupSizes.Length; i++) { if (i > 0) gs += "|"; gs += nfi.NumberGroupSizes[i]; }
            Console.WriteLine("g." + label + ".NumberGroupSizes=" + gs);
            gs = "";
            for (int i = 0; i < nfi.CurrencyGroupSizes.Length; i++) { if (i > 0) gs += "|"; gs += nfi.CurrencyGroupSizes[i]; }
            Console.WriteLine("g." + label + ".CurrencyGroupSizes=" + gs);
            gs = "";
            for (int i = 0; i < nfi.PercentGroupSizes.Length; i++) { if (i > 0) gs += "|"; gs += nfi.PercentGroupSizes[i]; }
            Console.WriteLine("g." + label + ".PercentGroupSizes=" + gs);
            gs = "";
            for (int i = 0; i < nfi.NativeDigits.Length; i++) { if (i > 0) gs += "|"; gs += Esc(nfi.NativeDigits[i]); }
            Console.WriteLine("g." + label + ".NativeDigits=" + gs);
        }

        private static void F(string label, string format, object value)
        {
            Console.WriteLine("f." + label + "=" + Esc(string.Format(format, value)));
        }

        public static void Main()
        {
            Console.WriteLine("cc.name=" + CultureInfo.CurrentCulture.Name);
            G("cur", NumberFormatInfo.CurrentInfo);
            G("inv", NumberFormatInfo.InvariantInfo);

            string composite = "{0:N}|{0:C}|{0:P}|{0:E}|{0:F2}";
            F("comp.i", composite, 1234567);
            F("comp.ineg", composite, -1234567);
            F("comp.d", composite, 1234567.89);
            F("comp.dneg", composite, -1234567.89);
            F("comp.small", composite, 0.00123);
            F("comp.weight", composite, 2.675);
            F("comp.long", composite, (long)1234567890123456789);
            F("comp.longmax", composite, long.MaxValue);
            F("comp.longmin", composite, long.MinValue);
            F("comp.zero", composite, 0);
            F("u.1.N", "{0:N2}", (ulong)1);
            F("dec.max.N", "{0:N0}", decimal.MaxValue);
            F("dec.max.C2", "{0:C2}", decimal.MaxValue);
            F("dec.max.P1", "{0:P1}", decimal.MaxValue);
            F("dec.d.C2", "{0:C2}", 1234.5678m);
            F("dec.d.P2", "{0:P2}", 0.1234m);
            F("dec.neg.C2", "{0:C2}", -1234.5678m);
            F("sp.inf.N", "{0:N2}", double.PositiveInfinity);
            F("sp.inf.C", "{0:C2}", double.PositiveInfinity);
            F("sp.inf.P", "{0:P1}", double.PositiveInfinity);
            F("sp.nfin.N", "{0:N2}", double.NegativeInfinity);
            F("sp.nan.P", "{0:P1}", double.NaN);
        }
    }
}