//------------------------------------------------------------------------------
// ressat.cs -- R2 SATELLITE CULTURE CHAIN e2e reference case (net48 oracle,
// byte-for-byte). Resource batch B (W11), 2026-10-07.
//
// A single-assembly AOT model has NO satellite assemblies; net48's satellite
// lookup collapses to a culture-suffixed EMBEDDED resource in the SAME manifest
// ("<baseName>.<culture>.resources"). This case embeds the REAL net48 satellite
// shape -- invariant + en + en-US .resources -- and drives the ResourceFallback
// chain [en-US, en, invariant] through ResourceManager.GetString(name, culture).
//
// net48 semantics exercised (the reference run IS the oracle):
//   1. SPECIFIC hit: en-US overrides Greeting ("Hello US" beats "Hello Resource")
//      and adds a satellite-only key OnlyUS.
//   2. PARENT hit: en adds OnlyEn, absent from en-US -> the chain falls to en.
//   3. INVARIANT hit: OnlyNeutral lives only in the invariant blob.
//   4. UNRELATED culture (fr-FR -> fr -> invariant): lands on the invariant blob;
//      a satellite-only key (OnlyUS) is a miss there -> null.
//   5. MISS semantics: a missing key is null at every chain step (never throws).
//   6. Explicit invariant culture: GetString(name, CultureInfo.InvariantCulture)
//      resolves the invariant blob directly.
//
// Every probe is a bool line so a diff failure shows exactly which chain step
// flipped; the ILC runtime must reproduce the chain byte-for-byte.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Resources;

namespace Cases
{
    public static class Ressat
    {
        public static void Main()
        {
            ResourceManager rm = new ResourceManager("ressat.Strings", typeof(Ressat).Assembly);

            // ===== 1. Invariant path (1-arg) =====
            Console.WriteLine(rm.GetString("Greeting") == "Hello Resource");   // True (invariant hit)
            Console.WriteLine(rm.GetString("OnlyUS") == null);                 // True (satellite-only key absent from invariant)
            Console.WriteLine(rm.GetString("AppName") == "RessatFixture");     // True (second invariant key)

            // ===== 2. en-US specific hit =====
            CultureInfo enUS = new CultureInfo("en-US");
            Console.WriteLine(rm.GetString("Greeting", enUS) == "Hello US");   // True (specific-culture override)
            Console.WriteLine(rm.GetString("OnlyUS", enUS) == "US Only");      // True (satellite-only key)

            // ===== 3. Parent-culture hit (en-US lacks OnlyEn -> en) =====
            Console.WriteLine(rm.GetString("OnlyEn", enUS) == "English Only"); // True (parent en hit)

            // ===== 4. Invariant tail hit (OnlyNeutral only in invariant) =====
            Console.WriteLine(rm.GetString("OnlyNeutral", enUS) == "Neutral Value"); // True (invariant hit)

            // ===== 5. Unrelated culture -> invariant blob =====
            CultureInfo frFR = new CultureInfo("fr-FR");
            Console.WriteLine(rm.GetString("Greeting", frFR) == "Hello Resource"); // True (fr-FR -> invariant)
            Console.WriteLine(rm.GetString("OnlyUS", frFR) == null);               // True (satellite-only key miss under fr-FR)

            // ===== 6. Miss semantics across the whole chain =====
            Console.WriteLine(rm.GetString("Missing", enUS) == null);          // True (miss with culture)
            Console.WriteLine(rm.GetString("Missing") == null);                // True (miss, invariant)

            // ===== 7. Explicit invariant culture =====
            Console.WriteLine(rm.GetString("Greeting", CultureInfo.InvariantCulture) == "Hello Resource"); // True
        }
    }
}
