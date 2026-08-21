//------------------------------------------------------------------------------
// resmgmt.cs -- end-to-end reference case for System.Resources.ResourceManager
// deep surface (net48 oracle): GetString hit / missing-key -> null / culture
// fallback (specific -> parent -> invariant) / explicit invariant.
//
// WIRING STATUS (2026-08-09, HEAD 8921ff8): TRANSPILER-SIDE CTOR GAP. The
// runtime GetString icall (il2cpp_resource_manager_get_string) and the
// culture-fallback primitive (il2cpp_resources_find_string_fallback, RM-CULT)
// ARE wired, and the resource-pool emission (embedded .resources ->
// resource-pool.h -> registry) is wired. But the ResourceManager CTOR is NOT
// mapped (MethodMap.Resources.cs handle-gap forward ABI: "Until that ctor
// mapping lands, a ResourceManager receiver is only reachable through newobj
// + the unwired .ctor"). Empirically the fixture transpiles but the generated
// C++ fails to compile: System_Resources_ResourceManager_class /
// System_Resources_ResourceManager__ctor_* / System_Type_get_Assembly_* are
// all undeclared (no class descriptor, no ctor symbol, no Type.get_Assembly
// icall). See GAPS.md D-RES-1..3.
//
// The net48 oracle below documents the INTENDED semantics (the fallback chain
// with no satellite assemblies collapses to the invariant blob, which IS the
// current invariant-pinned ILC behavior). This is a forward gate: it becomes
// green when the ctor wiring lands.
//
// CULTURE PROBES on net48 with ONLY the invariant blob embedded: every
// GetString(name, culture) walks [culture, parents..., invariant]; each
// culture-suffixed satellite is absent -> the chain lands on the invariant
// blob, so "Greeting" resolves and "Missing" is null. This documents the
// fallback-to-invariant tail of the chain without needing satellite
// assemblies (which a single-exe e2e case cannot embed).
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Resources;

namespace Cases
{
    public static class Resmgmt
    {
        public static void Main()
        {
            ResourceManager rm = new ResourceManager("resmgmt.Strings", typeof(Resmgmt).Assembly);

            // ===== 1. GetString hit =====
            string g = rm.GetString("Greeting");
            Console.WriteLine("hit=" + (g == "Hello Resource"));
            Console.WriteLine("hitval=" + g);

            // ===== 2. Missing key -> null (never throws) =====
            Console.WriteLine("miss=" + (rm.GetString("Missing") == null));
            Console.WriteLine("missval=" + (rm.GetString("Missing") == null ? "null" : "nonnull"));

            // ===== 3. Explicit invariant culture =====
            Console.WriteLine("inv=" + (rm.GetString("Greeting", CultureInfo.InvariantCulture) == "Hello Resource"));

            // ===== 4. Culture fallback to invariant (no satellite assemblies) =====
            // en-US chain: en-US -> en -> invariant. Neither satellite exists,
            // so "Greeting" resolves from the invariant blob.
            CultureInfo enUS = new CultureInfo("en-US");
            Console.WriteLine("enus=" + (rm.GetString("Greeting", enUS) == "Hello Resource"));
            Console.WriteLine("enusmiss=" + (rm.GetString("Missing", enUS) == null));

            // ===== 5. Unrelated culture also falls back to invariant =====
            CultureInfo frFR = new CultureInfo("fr-FR");
            Console.WriteLine("frfr=" + (rm.GetString("Greeting", frFR) == "Hello Resource"));
            Console.WriteLine("frfrmiss=" + (rm.GetString("Missing", frFR) == null));

            // ===== 6. All values in the invariant blob =====
            Console.WriteLine("app=" + (rm.GetString("AppName") == "ResmgmtFixture"));
            Console.WriteLine("empty=" + (rm.GetString("") == null));
        }
    }
}
