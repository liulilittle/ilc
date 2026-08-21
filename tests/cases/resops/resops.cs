//------------------------------------------------------------------------------
// resops.cs -- RESOURCE DEEP e2e reference case (net48 oracle, byte-for-byte).
// Created in e2e batch C16 (2026-08-10).
//
// Exercises the embedded-manifest-resource surface AFTER the resource-surface
// work (batch 42 landed the ResourceManager culture fallback chain; batches
// 21-22e landed the resources parser + registry guards). Every line prints
// True/False -- a diff failure shows exactly which assertion flipped.
//
// Covered families:
//   1. Assembly.GetManifestResourceNames() -- the registered manifest names
//      (ordinal table order; the .resources blob + the raw payload file).
//   2. Assembly.GetManifestResourceStream(name) -- the raw payload as byte[]:
//      hit -> non-null + exact content; unknown name -> null (never throws).
//   3. ResourceManager.GetString(name) -- the .resources binary lookup: hit
//      returns the value; a missing key returns null (never throws).
//   4. ResourceManager culture fallback chain -- GetString(name, culture)
//      walks [culture, parents..., neutral=invariant]. With NO satellite
//      assemblies embedded, every culture-suffixed blob is absent, so the
//      chain lands on the INVARIANT blob: a key that exists there resolves,
//      a missing key is null. en-US -> en -> invariant and an unrelated
//      culture (fr-FR -> fr -> invariant) both collapse to the invariant.
//   5. Empty/missing keys -- "" returns null (net48).
//
// The net48 oracle documents the INTENDED semantics. The ILC single-assembly
// model registers the SAME manifest (resource-pool.h -> il2cpp_resources_
// register) so the queries must match byte-for-byte.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.IO;
using System.Resources;

namespace Cases
{
    public static class ResOps
    {
        public static void Main()
        {
            // ===== 1. GetManifestResourceNames =====
            string[] names = typeof(ResOps).Assembly.GetManifestResourceNames();
            Console.WriteLine(names != null);
            Console.WriteLine(names.Length == 2);                          // True
            bool hasStrings = false;
            bool hasBin = false;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == "resops.Strings.resources") hasStrings = true;
                if (names[i] == "resops.payload.bin") hasBin = true;
            }
            Console.WriteLine(hasStrings);                                 // True
            Console.WriteLine(hasBin);                                     // True

            // ===== 2. GetManifestResourceStream =====
            Stream s1 = typeof(ResOps).Assembly.GetManifestResourceStream("resops.payload.bin");
            Console.WriteLine(s1 != null);                                 // True (hit)
            byte[] payload = new byte[4];
            int n = 0;
            if (s1 != null) { n = s1.Read(payload, 0, 4); s1.Dispose(); }
            Console.WriteLine(n == 4);                                     // True
            Console.WriteLine(payload[0] == (byte)0x00 && payload[1] == (byte)0x01
                              && payload[2] == (byte)0x02 && payload[3] == (byte)0x03); // True
            Stream sMiss = typeof(ResOps).Assembly.GetManifestResourceStream("no.such.resource");
            Console.WriteLine(sMiss == null);                              // True (miss -> null)
            Stream sStrings = typeof(ResOps).Assembly.GetManifestResourceStream("resops.Strings.resources");
            Console.WriteLine(sStrings != null);                           // True (.resources blob exists)

            // ===== 3. ResourceManager.GetString (invariant blob) =====
            ResourceManager rm = new ResourceManager("resops.Strings", typeof(ResOps).Assembly);
            Console.WriteLine(rm.GetString("Greeting") == "Hello Resops"); // True (hit)
            Console.WriteLine(rm.GetString("AppName") == "ResopsFixture"); // True (second key)
            Console.WriteLine(rm.GetString("Missing") == null);            // True (miss -> null)
            Console.WriteLine(rm.GetString("") == null);                   // True (empty key)

            // ===== 4. Culture fallback chain -> invariant blob =====
            // en-US chain: en-US -> en -> invariant. No satellite assemblies
            // exist, so "Greeting" resolves from the invariant blob.
            CultureInfo enUS = new CultureInfo("en-US");
            Console.WriteLine(rm.GetString("Greeting", enUS) == "Hello Resops");  // True
            Console.WriteLine(rm.GetString("Missing", enUS) == null);             // True
            // Explicit invariant culture.
            Console.WriteLine(rm.GetString("Greeting", CultureInfo.InvariantCulture) == "Hello Resops"); // True
            // Unrelated culture also falls back to invariant.
            CultureInfo frFR = new CultureInfo("fr-FR");
            Console.WriteLine(rm.GetString("Greeting", frFR) == "Hello Resops");  // True
            Console.WriteLine(rm.GetString("Missing", frFR) == null);             // True
        }
    }
}
