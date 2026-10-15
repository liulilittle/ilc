//------------------------------------------------------------------------------
// p-appcontext.cs -- System.AppContext / CompatibilitySwitch net48 audit probe
// (GN-20 MSCORLIB batch, legacy AppContext gap). net48 oracle == transpiled
// runtime byte-exact (records escaped for code-page independence).
//
// Audit record (docs/audit/icall-inventory-net48.md row 780): the public
// System.Runtime.Versioning.CompatibilitySwitch.GetAppContextOverridesInternalCall
// was the ONLY remaining realGap in the net48 icall audit (realGap=2 alongside
// System.Security.Principal.Win32.OpenThreadToken), and System.AppContext has
// no MethodMap surface at all. This probe pins, on a default zh-CN console
// PROCESS WITHOUT any App.config / runtimeconfig overrides, the net48-observable
// contract so the wiring can be byte-exact:
//
//   * AppContext.GetData(key)                          -- null for EVERY key on
//     the default no-config process (incl. "BaseDirectory"/"TargetFrameworkName"
//     -- net48 measured: the property getters are NOT GetData keys).
//   * AppContext.SetSwitch(k,true) + TryGetSwitch(k)  -- pair returns true
//   * AppContext.SetSwitch(k,false) + TryGetSwitch(k) -- pair returns false
//   * TryGetSwitch(neverSet)                          -- false (unset switch)
//   * AppContext.BaseDirectory / TargetFrameworkName  -- host-influenced:
//     BaseDirectory is the exe dir (varies by run dir, asserted shape-only);
//     TargetFrameworkName is the FIXED net48 host string
//     ".NETFramework,Version=v4.8" (asserted verbatim).
//   * CompatibilitySwitch (System.Runtime.Versioning) -- probed by a separate
//     full-framework harness (the type is hidden from the net48 SDK reference
//     assemblies and cannot be compiled in this project). The internal
//     GetAppContextOverridesInternalCall returns the raw <AppContextSwitch-
//     Overrides> string (null/"" when no config), so IsEnabled(unset) is
//     false and GetValue(unset) is null on the default process; those facts
//     are pinned in REPORT-APPCONTEXT.
//
// Every record is host-INVARIANT (no absolute paths / host names / clock):
// records assert shapes (string-vs-null) and in-process self-consistency.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class PAppcontext
    {
        private static string Repr(object v)
        {
            if (v == null) return "<null>";
            string s = v as string;
            if (s != null)
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
            return v.ToString();
        }

        private static void R(string id, object v)
        {
            Console.WriteLine("[" + id + "]=" + Repr(v));
        }

        public static void Main()
        {
            // --- AppContext.GetData on unset keys ---
            R("gd.unset", AppContext.GetData("no.such.key"));
            R("gd.switchkey", AppContext.GetData("Switch.System.Probe.Unknown"));
            R("gd.base.shape.isstring", AppContext.GetData("BaseDirectory") is string);
            R("gd.base.nonempty", (AppContext.GetData("BaseDirectory") as string) != null
                && ((string)AppContext.GetData("BaseDirectory")).Length > 0);
            R("gd.probe.after.set.before", AppContext.GetData("Switch.System.Probe.On"));

            // --- SetSwitch + TryGetSwitch pairs ---
            AppContext.SetSwitch("Switch.System.Probe.On", true);
            bool en = false;
            bool got1 = AppContext.TryGetSwitch("Switch.System.Probe.On", out en);
            R("tsg.on.ret", got1);
            R("tsg.on.val", en);
            R("gd.probe.after.set", AppContext.GetData("Switch.System.Probe.On"));

            AppContext.SetSwitch("Switch.System.Probe.Off", false);
            bool en2 = true;
            bool got2 = AppContext.TryGetSwitch("Switch.System.Probe.Off", out en2);
            R("tsg.off.ret", got2);
            R("tsg.off.val", en2);

            // --- override + re-read ---
            AppContext.SetSwitch("Switch.System.Probe.On", false);
            bool en3 = true;
            bool got3 = AppContext.TryGetSwitch("Switch.System.Probe.On", out en3);
            R("tsg.retoggle.ret", got3);
            R("tsg.retoggle.val", en3);

            // --- TryGetSwitch for a switch never set ---
            bool en4 = true;
            bool got4 = AppContext.TryGetSwitch("Switch.System.Probe.NeverSet", out en4);
            R("tsg.unset.ret", got4);
            R("tsg.unset.outval", en4);

            // --- Argument validation (net48 throws) ---
            bool e1 = false;
            try { AppContext.TryGetSwitch(null, out e1); R("tsg.null.threw", false); }
            catch (ArgumentNullException) { R("tsg.null.threw", true); }
            try { AppContext.SetSwitch(null, true); R("ss.null.threw", false); }
            catch (ArgumentNullException) { R("ss.null.threw", true); }
            try { AppContext.SetSwitch("", true); R("ss.empty.threw", false); }
            catch (ArgumentException) { R("ss.empty.threw", true); }

            // --- property shapes (host-invariant assertions only) ---
            R("bd.nonnull", AppContext.BaseDirectory != null);
            R("bd.len", AppContext.BaseDirectory != null ? AppContext.BaseDirectory.Length > 0 : false);
            R("tfn.shape", AppContext.TargetFrameworkName == null ? "null" : "string");
            R("tfn.value", AppContext.TargetFrameworkName);
            R("gd.targetfwname", AppContext.GetData("TargetFrameworkName"));

            // --- CompatibilitySwitch public wraps: probed separately (the
            //     type is hidden from the net48 SDK reference assemblies, so
            //     it cannot be COMPILED in this probe; the runtime behavior is
            //     pinned in REPORT-APPCONTEXT via a full-framework harness.
            //     Decision record: IsEnabled(unset)->False, GetValue(unset)->
            //     null, GetAppContextOverridesInternalCall()->null/empty on
            //     the no-config default process. ---
        }
    }
}