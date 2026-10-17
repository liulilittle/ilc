using System;
using System.Text;

//------------------------------------------------------------------------------
// p-adb.cs -- System.AppDomain net48 audit probe (adm1 batch: the AppDomain
// minimal object closure). net48 oracle == transpiled runtime byte-exact.
//
// This is the SINGLE-SOURCE difftest probe for the test case tests/cases/p-adb:
// the SAME source runs under the net48 oracle AND the transpiled runtime, and
// the difftest harness (scripts/difftest.ps1 / run-tests.ps1) requires the two
// outputs to be byte-identical. Every record below is therefore host-INVARIANT:
// no absolute paths / host names / clocks are printed -- records assert shapes
// (null vs non-null), constants (Id == 1), and in-process self-consistency
// (SetupInformation.ApplicationBase == BaseDirectory, GetData("APPBASE") ==
// BaseDirectory, the ConfigurationFile ".exe.Config" suffix, the trailing
// separator, the singleton identity refeq). The net48 measured values are
// pinned in .opencode/tmp/appdomain/p-adb_adb.txt (56-line oracle).
//
// The six adm1 LOUD members (ExecuteAssembly / PermissionSet / Evidence /
// ApplicationTrust / GetAssemblies / ToString) are mapped to RUNTIME
// NotSupportedException (appdomain.cpp) so this single source compiles+links
// on the runtime -- before adm1 they aborted the transpile. The probe asserts
// the loud mapping explicitly:
//   * ExecuteAssembly(missing path) -- the oracle measured FileNotFoundException
//     on net48; the runtime throws NotSupportedException for the SAME call. The
//     byte-exact record is "throws ANY explicit exception" (catch Exception ->
//     True on BOTH sides).
//   * PermissionSet / Evidence / ApplicationTrust / GetAssemblies / ToString --
//     net48 returns VALUEs the runtime has no object model for (CAS / assembly
//     loader / culture-localized text). Those net48 VALUE bytes cannot be
//     reproduced byte-exact from a single source, so the probe does NOT call
//     them in the byte-exact path; their NotSupported mapping is asserted in
//     the per-arm unit test (MethodMapTests.Legacy.cs) and documented in
//     GAPS.md instead.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PAdb
    {
        private static string Repr(object v)
        {
            if (v == null) return "<null>";
            string s = v as string;
            if (s != null)
            {
                var sb = new StringBuilder();
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
            return v.ToString();
        }

        private static void R(string id, object v)
        {
            Console.WriteLine("[" + id + "]=" + Repr(v));
        }

        private static bool Throws(Action a)
        {
            try { a(); return false; }
            catch (Exception) { return true; }
        }

        private static string ExcName(Action a)
        {
            try { a(); return "<none>"; }
            catch (Exception ex) { return ex.GetType().Name; }
        }

        private static void OnUnhandled(object s, UnhandledExceptionEventArgs e) { }

        public static void Main()
        {
            // Ensure the StringBuilder descriptor is REGISTERED before the
            // CreateInstance arms exercise it (the runtime registry is
            // type-reachability lazy; the p-adb records rely on
            // il2cpp_class_by_name("System.Text.StringBuilder") succeeding).
            new StringBuilder("adb-bootstrap");

            AppDomain ad = AppDomain.CurrentDomain;

            // --- object identity legs (the singleton object model) ---
            R("ad.gettype", ad.GetType().FullName);
            R("ad.null", ad == null);
            R("ad.isinst", ad is AppDomain);
            AppDomain ad2 = AppDomain.CurrentDomain;
            R("ad.refeq", object.ReferenceEquals(ad, ad2));

            // --- get_Id (net48 default domain -> 1) ---
            R("ad.id", ad.Id);

            // --- get_FriendlyName (exe base name; shape asserted only) ---
            string fname = ad.FriendlyName;
            R("ad.fname.nonnull", fname != null);
            R("ad.fname.nonempty", fname != null && fname.Length > 0);

            // --- get_BaseDirectory (exe dir + trailing separator) ---
            string bd = ad.BaseDirectory;
            R("ad.basedir.nonnull", bd != null);
            R("ad.basedir.trailing", bd != null && (bd.EndsWith("\\") || bd.EndsWith("/")));
            R("ad.relpath.null", ad.RelativeSearchPath == null);
            R("ad.dm.null", ad.DomainManager == null);

            // --- SetupInformation -> AppDomainSetup carrier ---
            AppDomainSetup si = ad.SetupInformation;
            R("ad.setup.type", si.GetType().FullName);
            string siAb = si.ApplicationBase;
            R("ad.si.appbase.eq.basedir", siAb == bd);
            string cfg = si.ConfigurationFile;
            R("ad.si.cfg.nonnull", cfg != null);
            R("ad.si.cfg.execonfig.suffix", cfg != null && cfg.EndsWith(".exe.Config"));
            R("ad.si.cfg.appbase.prefix", cfg != null && bd != null && cfg.StartsWith(bd));

            // --- GetData / SetData (process-level table) ---
            R("ad.gd.unset.null", ad.GetData("adb.unset") == null);
            R("ad.gd.nullkey.throw", Throws(() => ad.GetData(null)));
            R("ad.gd.appbase.eq.basedir", (string)ad.GetData("APPBASE") == bd);

            ad.SetData("adb.v1", "__adb.v1");
            R("ad.sd.v1", ad.GetData("adb.v1"));
            ad.SetData("adb.v1", "__adb.v2");
            R("ad.sd.overwrite", ad.GetData("adb.v1"));
            object marker = new object();
            ad.SetData("adb.obj", marker);
            R("ad.sd.obj.roundtrip", object.ReferenceEquals(ad.GetData("adb.obj"), marker));
            R("ad.sd.nullkey.throw", Throws(() => ad.SetData(null, 1)));

            // --- CreateInstance -> ObjectHandle; Unwrap -> the instance ---
            object ci = ad.CreateInstance("mscorlib", "System.Text.StringBuilder");
            R("ad.ci.type", ci.GetType().FullName);
            var h = (System.Runtime.Remoting.ObjectHandle)ci;
            object uw = h.Unwrap();
            R("ad.ci.unwrap.ok", uw != null);
            R("ad.ci.unwrap.type", uw.GetType().FullName);

            // --- CreateInstanceAndUnwrap -> the created object directly ---
            object sobj = ad.CreateInstanceAndUnwrap("mscorlib", "System.Text.StringBuilder");
            R("ad.ciu.type", sobj.GetType().FullName);
            StringBuilder sbc = sobj as StringBuilder;
            R("ad.ciu.is.sb", sbc != null);
            if (sbc != null)
            {
                // The Append icall's RESULT is tracked as System.Text.StringBuilder
                // (the Append mapping carries returnTypeName), so chaining
                // ToString() through it dispatches to the StringBuilder ToString
                // icall on BOTH sides (the as-cast local's direct ToString is a
                // byte-exact landmine: the transpiler resolves it via the Object
                // domain -> the type-name text).
                StringBuilder appended = sbc.Append("adb");
                R("ad.ciu.work", appended == sbc);
                R("ad.ciu.str", appended.ToString());
            }

            // bad assembly -> FileNotFoundException (net48 measured; the runtime
            // maps the same call -> FileNotFoundException) / null type-name throws.
            R("ad.ciu.badassem.throw", Throws(() => ad.CreateInstanceAndUnwrap("nonexistent.assembly", "System.Text.StringBuilder")));
            R("ad.ciu.badassem.exc", ExcName(() => ad.CreateInstanceAndUnwrap("nonexistent.assembly", "System.Text.StringBuilder")));
            R("ad.ciu.nulltype.throw", Throws(() => ad.CreateInstanceAndUnwrap("mscorlib", null)));

            // --- LOUD: ExecuteAssembly maps to a runtime NotSupportedException
            //     (net48 throws FileNotFoundException for a missing file) -- the
            //     "throws any explicit exception" record is byte-exact on both. ---
            R("ad.exec.missing.throw", Throws(() => ad.ExecuteAssembly("adb-nonexistent-app.exe")));

            // --- constants (the default host / the AOT process IS the domain) ---
            R("ad.isdef", ad.IsDefaultAppDomain());
            R("ad.istr", ad.IsFullyTrusted);
            R("ad.ishom", ad.IsHomogenous);

            // --- UnhandledException event add/remove (succeeds; never raised) ---
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
            R("ad.unhal.add.remove", true);
        }
    }
}