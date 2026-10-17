using System;
using System.Text;

//------------------------------------------------------------------------------
// p-adb-loud.cs -- adm1 D2 defect-fix probe: AppDomain.ToString must GENUINELY
// throw NotSupportedException at a REAL call site.
//
// The adm1 v1 LOUD arm (MethodMap.Legacy.cs: icall_appdomain_to_string) was
// DEAD code: Roslyn emits `callvirt System.Object::ToString` for the net48
// AppDomain.ToString override, so the resolver reached the Object-domain
// mangled fallback and the runtime printed the klass NAME ("System.AppDomain")
// instead of the LOUD throw (valuesemantics.cpp icall_object_to_string
// fallback). The fix routes Object::ToString on an AppDomain-tracked receiver
// to icall_appdomain_to_string (MethodMap.Object.cs), which throws.
//
// NET48 DELIBERATELY DIVERGES here (net48 returns the localized 名称:... text),
// so this probe is NOT a byte-exact difftest case -- it is a RUNTIME-CONTRACT
// probe: it must be run on the transpiled runtime only, and its output
// assertion is `[loud.tostring]=True` (caught NotSupportedException). The
// runtime-side run is the TDD green evidence; the red (pre-fix) runtime prints
// `[loud.tostring]=False` (no throw -- the klass-name text path).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PAdbLoud
    {
        public static void Main()
        {
            AppDomain ad = AppDomain.CurrentDomain;

            bool loud = false;
            try
            {
                string s = ad.ToString();
                Console.WriteLine("[loud.tostring.nothrow]=" + s);
            }
            catch (NotSupportedException)
            {
                loud = true;
            }
            Console.WriteLine("[loud.tostring]=" + loud);
        }
    }
}