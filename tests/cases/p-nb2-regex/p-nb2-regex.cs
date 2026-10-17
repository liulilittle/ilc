//------------------------------------------------------------------------------
// p-nb2-regex.cs -- batch nb2 REGEX-OPT difftest probe
// (net48 oracle == transpiled runtime byte-exact).
//
// Pins the RegexOptions 3/4-parameter STATIC Regex overloads whose real
// (Roslyn-csc) signature encodes the RegexOptions enum as a VALUETYPE
// (0x11) parameter -- the resolver arms used to recognize only the
// unit-synthesized i4 (0x08) form, so real calls resolved to phantom
// generated-name symbols (silent fold fall-through -> C++ compile error,
// nb2 bw_build.log: Regex_IsMatch_97e1fa... not declared). This probe
// exercises IgnoreCase/Multiline/Singleline (the runtime whitelist) plus a
// 4-parameter IsMatch whose trailing TimeSpan matchTimeout is DROPPED by the
// runtime (the engine step budget replaces it -- regex.h section 1.4), and a
// 2-parameter baseline.
//------------------------------------------------------------------------------
using System;
using System.Text.RegularExpressions;

namespace Cases
{
    public static class PNb2Regex
    {
        public static void Main()
        {
            // ---- 3-parameter IsMatch(input, pattern, RegexOptions) ----
            Console.WriteLine("[rg.opt.ic]=" + (Regex.IsMatch("ABC", "^abc$", RegexOptions.IgnoreCase) ? "Y" : "N"));
            Console.WriteLine("[rg.opt.ml]=" + (Regex.IsMatch("x\nabc\ny", "^abc$", RegexOptions.Multiline) ? "Y" : "N"));
            Console.WriteLine("[rg.opt.sl]=" + (Regex.IsMatch("a\nb", "a.b", RegexOptions.Singleline) ? "Y" : "N"));
            Console.WriteLine("[rg.opt.combo]=" + (Regex.IsMatch("X\nAbC\ny", "^abc$", RegexOptions.IgnoreCase | RegexOptions.Multiline) ? "Y" : "N"));
            // ---- 4-parameter IsMatch(..., RegexOptions, TimeSpan) ----
            Console.WriteLine("[rg.opt.ts]=" + (Regex.IsMatch("ABC", "^abc$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500)) ? "Y" : "N"));
            // ---- Replace / Split with RegexOptions ----
            Console.WriteLine("[rg.repl.opt]=" + Regex.Replace("aBc", "b", "X", RegexOptions.IgnoreCase));
            Console.WriteLine("[rg.split.opt]=" + string.Join("|", Regex.Split("a,b,C", ",", RegexOptions.IgnoreCase)));
            // ---- 2-parameter baseline (already wired) ----
            Console.WriteLine("[rg.is2]=" + (Regex.IsMatch("abc", "^abc$") ? "Y" : "N"));
        }
    }
}