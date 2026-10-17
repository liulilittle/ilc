//------------------------------------------------------------------------------
// p-nb2-cult.cs -- batch nb2 CULT-GET difftest probe
// (net48 oracle == transpiled runtime byte-exact).
//
// Pins CultureInfo.GetCultureInfo(string) + get_Name /
// get_TwoLetterISOLanguageName (both were unwired: GetCultureInfo had no
// mapping arm, so get_Name folded through a lost receiverType and the whole
// call chain silently degraded). The probe records the raw net48 answers
// (name normalization, two-letter ISO prefix, unknown-name handling) which
// the runtime icall must reproduce byte-for-byte. get_DisplayName is NOT
// probed (no culture display-name table in the runtime -- explicit LOUD).
// The unknown-name / empty-name paths print the thrown exception type name
// on net48 so the runtime's M3 null/status convention must not accidentally
// produce a byte-equal plain name.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class PNb2Cult
    {
        static string NameOf(Func<string> f)
        {
            try { return f(); }
            catch (Exception e) { return "THREW:" + e.GetType().Name; }
        }

        public static void Main()
        {
            // Canonical name round-trips byte-exact.
            Console.WriteLine("[cul.get]=" + CultureInfo.GetCultureInfo("en-US").Name);
            Console.WriteLine("[cul.get.2l]=" + CultureInfo.GetCultureInfo("en-US").TwoLetterISOLanguageName);
            // Case normalization: lowercase input -- net48 canonicalizes.
            Console.WriteLine("[cul.lc]=" + NameOf(() => CultureInfo.GetCultureInfo("en-us").Name));
            Console.WriteLine("[cul.lc.2l]=" + NameOf(() => CultureInfo.GetCultureInfo("en-us").TwoLetterISOLanguageName));
            Console.WriteLine("[cul.zh]=" + CultureInfo.GetCultureInfo("zh-CN").Name);
            // Uppercase primary / lowercase region -- pin the re-casing rule.
            Console.WriteLine("[cul.uc]=" + NameOf(() => CultureInfo.GetCultureInfo("EN-us").Name));
            Console.WriteLine("[cul.fr]=" + NameOf(() => CultureInfo.GetCultureInfo("fr").Name));
            Console.WriteLine("[cul.fr.2l]=" + NameOf(() => CultureInfo.GetCultureInfo("fr").TwoLetterISOLanguageName));
            // Unknown name handling: net48 echoes a well-formed name whose
            // PRIMARY segment exists (no culture table hit, no throw) --
            // pinned byte-exact (2l = the primary segment's code).
            Console.WriteLine("[cul.bad]=" + NameOf(() => CultureInfo.GetCultureInfo("no-Such-XX-yy").Name));
            Console.WriteLine("[cul.bad.2l]=" + NameOf(() => CultureInfo.GetCultureInfo("no-Such-XX-yy").TwoLetterISOLanguageName));
        }
    }
}