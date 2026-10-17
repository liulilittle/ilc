//------------------------------------------------------------------------------
// p-nb-env.cs -- batch nb1 ENV-SPOT + GC-LEAK difftest probe
// (net48 oracle == transpiled runtime byte-exact, optional -AppArgs).
//
// The oracle evidence (exploration .opencode/tmp/next-batch/oracle_out.txt:
// lines 1-20, 33-37) pins:
//   * GetCommandLineArgs: cla.count=4 with the harness passing
//     "argA argB -x=42" (the exe path is argv[0] and is SKIPPED; the 3
//     trailing arguments are reported). An empty argument is PRESERVED as
//     an empty string (the "空参保留" probe: "\"\"" between real args).
//     NOTE: difftest passes the SAME -AppArgs to the net48 oracle and the
//     transpiled exe, so the byte-exact diff holds.
//   * GetLogicalDrives: C:..G: = 5 drives on this host (bit 2..6 of
//     GetLogicalDrives()).
//   * ExpandEnvironmentVariables: a MISSING variable (%NBX_MISSING%) is
//     preserved VERBATIM, and a set-but-EMPTY variable (%NBX_EMPTY%) is
//     ALSO preserved verbatim (net48 .NET-semantics: empty reads as null).
//     NOT the raw ExpandEnvironmentStringsW behavior.
//   * optional surface: get_SystemDirectory non-empty, get_UserDomainName
//     non-empty, get_UserInteractive true.
//   * GetFolderPath extra SpecialFolder ids beyond the 0/5/28/36 wired
//     set: 26 (ApplicationData) and 35 (CommonApplicationData) resolve via
//     the shell known-folder API on net48 -- byte-exact when the runtime
//     extends its SHGetFolderPathW passthrough.
//   * GC leak small-fill: Collect(int,GCCollectionMode,bool) arity arm,
//     AddMemoryPressure/RemoveMemoryPressure no-ops; GetTotalMemory true
//     shape; MaxGeneration=2 (net48 pinned).
//
// Probe values are deterministic process-local data except the
// machine-specific shapes (drives, system dir) that the diff harness
// resolves identically on both sides (same host, same filesystem).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class PNbEnv
    {
        public static void Main()
        {
            // ================= ENV-SPOT =================
            // GetCommandLineArgs: argv[0] is the EXE PATH -- the byte-exact
            // oracle skips it (the harness runs the two exes from different
            // paths); only the trailing ARGUMENTS are reported verbatim. The
            // harness passes `argA "" -x=42` so the probe ALSO pins the
            // EMPTY-ARG preservation: cla.count=4 (exe + argA + "" + -x=42)
            // and cla.args="argA||-x=42" (a preserved empty element between
            // the pipes -- the "空参保留"/"args 跳 exe 路径" requirements).
            string[] cla = Environment.GetCommandLineArgs();
            Console.WriteLine("[env.cla.count]=" + cla.Length);
            Console.WriteLine("[env.cla.arg0]=" + (cla.Length >= 1 && cla[0].Length > 0));
            string joined = "";
            for (int i = 1; i < cla.Length; i++)
                joined += (i > 1 ? "|" : "") + cla[i];
            Console.WriteLine("[env.cla.args]=" + joined);

            // GetLogicalDrives.
            string[] drv = Environment.GetLogicalDrives();
            Console.WriteLine("[env.drives.count]=" + drv.Length);
            for (int i = 0; i < drv.Length; i++)
                Console.WriteLine("[env.drive." + i + "]=" + drv[i]);

            // ExpandEnvironmentVariables. Process-local pins so the probe is
            // clean and deterministic on every run (environment vars survive
            // only inside this process; the net48 oracle and the transpiled
            // runtime see the SAME process-local environment):
            //   * NBX_MISSING: never set -> %NBX_MISSING% preserved VERBATIM.
            //   * NBX_EMPTY:   set to "" (net48 SetEnvironmentVariable(name,
            //     "") DELETES it) -> the expanded result preserves the
            //     %NBX_EMPTY% token (an empty value reads as null).
            //   * NBX_SET:     a real value -> expands to "works".
            Environment.SetEnvironmentVariable("NBX_MISSING", null);
            Environment.SetEnvironmentVariable("NBX_EMPTY", "");
            Console.WriteLine("[env.expand.missing]=[" + Environment.ExpandEnvironmentVariables("hello world-%NBX_MISSING%") + "]");
            Console.WriteLine("[env.expand.empty]=[" + Environment.ExpandEnvironmentVariables("hello world-%NBX_EMPTY%") + "]");
            Environment.SetEnvironmentVariable("NBX_SET", "works");
            Console.WriteLine("[env.expand.set]=[" + Environment.ExpandEnvironmentVariables("say=%NBX_SET%") + "]");
            Environment.SetEnvironmentVariable("NBX_SET", null); // cleanup

            // Optional surface.
            Console.WriteLine("[env.sysdir]=" + (Environment.SystemDirectory.Length > 0));
            Console.WriteLine("[env.udn]=" + (Environment.UserDomainName.Length > 0));
            Console.WriteLine("[env.ui]=" + Environment.UserInteractive);

            // GetFolderPath extra ids (26 = ApplicationData, 35 = CommonApplicationData).
            Console.WriteLine("[env.folder.appdata]=" + Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            Console.WriteLine("[env.folder.common]=" + Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

            // ================= GC-LEAK =================
            Console.WriteLine("[gc.maxgen]=" + GC.MaxGeneration);
            Console.WriteLine("[gc.tm]=" + (GC.GetTotalMemory(false) > 0));
            // 3-arity Collect(int, GCCollectionMode, bool).
            try
            {
                GC.Collect(0, GCCollectionMode.Optimized, true);
                Console.WriteLine("[gc.collect3]=True");
            }
            catch (Exception e)
            {
                Console.WriteLine("[gc.collect3]=THREW:" + e.GetType().Name);
            }
            // Memory-pressure no-ops. net48: both are simple CLR counters
            // that do not throw for a valid positive value.
            try
            {
                GC.AddMemoryPressure(123456);
                GC.RemoveMemoryPressure(123456);
                Console.WriteLine("[gc.pressure]=True");
            }
            catch (Exception e)
            {
                Console.WriteLine("[gc.pressure]=THREW:" + e.GetType().Name);
            }
        }
    }
}