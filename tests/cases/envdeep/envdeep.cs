//------------------------------------------------------------------------------
// envdeep.cs -- System.Environment domain DEEP probe (e2e batch C46):
// variables / paths / process-lifetime / version-shape -- byte-exact vs the
// net48 oracle. Created after the environment domain full chain landed
// (il2cpp_environment.h: NewLine / Get-SetEnvironmentVariable /
// CurrentDirectory / TickCount / ExitCode / ProcessorCount / UserName /
// MachineName / OSVersion / GetFolderPath -- wired in MethodMap.Environment.cs).
// NOTE: SetCurrentDirectory/GetCurrentDirectory/GetCommandLine exist ONLY on
// .NET Core, NOT net48 -- the net48 oracle cannot compile them, so they are
// out of reach (the MethodMap entries are forward-looking).
//
// Reachable-surface discipline (environment2 precedent):
//   * Environment-variable probes use fixed, process-local names and ALWAYS
//     end with the variable deleted -- re-runs are idempotent. Values are
//     ASCII (the icall crosses the A-suffix Win32 API as UTF-8 bytes).
//   * GetEnvironmentVariable / SetEnvironmentVariable round trips probe the
//     delete-on-null and delete-on-empty (net48) semantics.
//   * CurrentDirectory is printed verbatim -- diffable when the reference run
//     and the transpiled run share a working directory (run-tests.ps1 runs
//     both from the same CWD, processcase precedent).
//   * ExitCode: set/get round trip with a RESTORE to 0 at the end (net48
//     surfaces the stored value as the PROCESS exit code -- run-tests.ps1
//     fails a non-zero net48 reference exit; both sides agree only when the
//     final value is 0).
//   * Shape-only probes for the machine-specific surfaces that CANNOT be
//     byte-identical across the two runtimes:
//       - OSVersion: net48 Environment.OSVersion.ToString() = "Microsoft
//         Windows NT X.Y.Z.0" (OperatingSystem + Version with a revision
//         component); the runtime icall returns the simplified 3-component
//         "Microsoft Windows NT X.Y.Z". Only the NON-NULL shape is probed
//         (recorded as a documented surface difference, not a defect).
//       - GetFolderPath: net48 resolves the REAL known-folder path (possibly
//         OneDrive-redirected); the runtime resolves a simplified
//         home-based path. Only the non-empty shape is probed.
//       - UserName / MachineName: same OS on both sides, but kept as
//         non-empty shape probes for robustness.
//   * No GetEnvironmentVariable(null) (net48 throws ArgumentNullException;
//     the runtime returns null), no GetEnvironmentVariables()/ExpandEnvironment
//     Variables (LOUD-rejected, EnvironmentExtra).
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class EnvDeep {

    public static void Main()
    {
        // =============== S1: environment-variable round trips ===============
        Environment.SetEnvironmentVariable("IL2CPP_E2E_DEEP_1", "alpha");
        Console.WriteLine("var1=" + Environment.GetEnvironmentVariable("IL2CPP_E2E_DEEP_1"));
        Environment.SetEnvironmentVariable("IL2CPP_E2E_DEEP_1", "beta");
        Console.WriteLine("var1_overwrite=" + Environment.GetEnvironmentVariable("IL2CPP_E2E_DEEP_1"));
        // net48 delete-on-null.
        Environment.SetEnvironmentVariable("IL2CPP_E2E_DEEP_1", null);
        Console.WriteLine("var1_null_deleted=" + (Environment.GetEnvironmentVariable("IL2CPP_E2E_DEEP_1") == null ? 1 : 0));
        // net48 delete-on-empty.
        Environment.SetEnvironmentVariable("IL2CPP_E2E_DEEP_1", "");
        Console.WriteLine("var1_empty_deleted=" + (Environment.GetEnvironmentVariable("IL2CPP_E2E_DEEP_1") == null ? 1 : 0));
        // never-set variable reads null.
        Console.WriteLine("var_missing=" + (Environment.GetEnvironmentVariable("IL2CPP_E2E_DEEP_NEVER") == null ? 1 : 0));
        // value with spaces + digits (A-suffix path).
        Environment.SetEnvironmentVariable("IL2CPP_E2E_DEEP_2", "path with spaces 123");
        Console.WriteLine("var2=" + Environment.GetEnvironmentVariable("IL2CPP_E2E_DEEP_2"));
        Console.WriteLine("var2_rt=" + (Environment.GetEnvironmentVariable("IL2CPP_E2E_DEEP_2") == "path with spaces 123" ? 1 : 0));
        // overwrite then cleanup.
        Environment.SetEnvironmentVariable("IL2CPP_E2E_DEEP_2", "third");
        Console.WriteLine("var2_overwrite=" + Environment.GetEnvironmentVariable("IL2CPP_E2E_DEEP_2"));
        Environment.SetEnvironmentVariable("IL2CPP_E2E_DEEP_2", null);
        Console.WriteLine("var2_cleanup=" + (Environment.GetEnvironmentVariable("IL2CPP_E2E_DEEP_2") == null ? 1 : 0));
        // real system variable (deterministic on Windows: both sides read the
        // SAME inherited process environment).
        Console.WriteLine("os_var=" + (Environment.GetEnvironmentVariable("OS") == "Windows_NT" ? 1 : 0));

        // =============== S2: paths ===============
        Console.WriteLine("cwd=" + Environment.CurrentDirectory);

        // =============== S3: process lifetime ===============
        Console.WriteLine("exitcode_default=" + (Environment.ExitCode == 0 ? 1 : 0));
        Environment.ExitCode = 42;
        Console.WriteLine("exitcode_set42=" + (Environment.ExitCode == 42 ? 1 : 0));
        Environment.ExitCode = -7;
        Console.WriteLine("exitcode_setneg=" + (Environment.ExitCode == -7 ? 1 : 0));
        Environment.ExitCode = 0;                             // MUST restore (net48 process exit code)
        Console.WriteLine("exitcode_restored=" + (Environment.ExitCode == 0 ? 1 : 0));

        // =============== S4: tick / processors ===============
        int t1 = Environment.TickCount;
        int t2 = Environment.TickCount;
        Console.WriteLine("tick_mono=" + (t2 >= t1 ? 1 : 0));
        Console.WriteLine("procs=" + (Environment.ProcessorCount > 0 ? 1 : 0));

        // =============== S5: machine/user/version SHAPE + net48 byte-exact ===============
        Console.WriteLine("user_nonempty=" + (Environment.UserName != null && Environment.UserName.Length > 0 ? 1 : 0));
        Console.WriteLine("machine_nonempty=" + (Environment.MachineName != null && Environment.MachineName.Length > 0 ? 1 : 0));
        Console.WriteLine("osversion_nonnull=" + (Environment.OSVersion != null ? 1 : 0));
        // D-ENV-1 (2026-08-12, net48 probe-pinned): Environment.OSVersion.ToString()
        // is "Microsoft Windows NT X.Y.Z.R" -- a 4-component Version with the
        // revision component -- resolved through GetVersionEx (the app-compat
        // shimmed value, e.g. "6.2.9200.0"), byte-exact vs the net48 oracle.
        // The runtime's get_OSVersion icall returns the ToString() form as a
        // string, so the byte-exact print below IS the 4-component assertion
        // (the e2e harness byte-diffs it against the net48 oracle output).
        Console.WriteLine("osversion_tostring=" + Environment.OSVersion.ToString());
        Console.WriteLine("newline_len=" + (Environment.NewLine.Length == 2 ? 1 : 0));

        // =============== S6: folders byte-exact (D-ENV-2) ===============
        // net48 probe-pinned: GetFolderPath resolves the REAL known-folder
        // paths via the shell known-folder API -- OneDrive-redirected
        // Desktop/MyDocuments, the actual LocalApplicationData. The runtime's
        // get_special_folder icall now resolves the same shell paths, so the
        // C# probes them byte-exact vs the net48 oracle (paths may contain
        // non-ASCII on this host -- both sides emit the same UTF-8 bytes).
        Console.WriteLine("folder_desktop=" + Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        Console.WriteLine("folder_docs=" + Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Console.WriteLine("folder_local=" + Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        // =============== S7: GetEnvironmentVariable(null) (D-ENV-4) ===============
        // net48 probe-pinned: GetEnvironmentVariable(null) throws
        // ArgumentNullException -- it does NOT return null. Both sides must
        // hit the catch.
        try
        {
            Environment.GetEnvironmentVariable(null);
            Console.WriteLine("gev_null_threw=0");
        }
        catch (ArgumentNullException)
        {
            Console.WriteLine("gev_null_threw=1");
        }
        // empty-string name reads as null on both sides (net48 returns null).
        Console.WriteLine("gev_empty=" + (Environment.GetEnvironmentVariable("") == null ? 1 : 0));
    }
}}
