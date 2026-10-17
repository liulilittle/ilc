//------------------------------------------------------------------------------
// p-nb3-proc.cs -- nb3 PROC-GAP difftest probe (net48 oracle == transpiled
// runtime byte-exact).
//
// Pins the PID-domain Process closures that were phantom/unwired:
// get_ProcessName / HasExited / Kill (the start/wait/exit-code Id core was
// wired in processcase -- this probe extends it).
//
// Determinism rules (the processcase model): only booleans, exit codes and
// the CHILD process name are printed -- nothing that differs between the two
// executables. In particular GetCurrentProcess().ProcessName is only reduced
// to `non-empty` (the oracle exe is p-nb3-proc.exe, the runtime exe is
// il2cpp_gen_exe.exe, so the raw name CANNOT be diffed); the child started
// here is cmd.exe / ping.exe so its ProcessName ("cmd" / "ping", no ".exe")
// is host-identical on both sides.
//
// Timing bounds: `ping -n 30 127.0.0.1` lives ~29 s, so HasExited==False
// immediately after Start has a ~29 s race margin (Kill terminates the ping
// itself, no orphan). Exit codes (`cmd /c exit 5`) are exact on both sides.
//------------------------------------------------------------------------------
using System;
using System.Diagnostics;

namespace Cases
{
    public static class PNb3Proc
    {
        public static void Main()
        {
            // Current process HasExited is always False (deterministic) and
            // ProcessName is non-empty (reduced, not diffed verbatim).
            Process self = Process.GetCurrentProcess();
            Console.WriteLine("[proc.self.hasxt]=" + self.HasExited);
            string selfName = self.ProcessName;
            Console.WriteLine("[proc.self.name.nonempty]=" + (selfName != null && selfName.Length > 0));

            // ProcessName of a launched child: no ".exe", host-identical.
            Process pc = Process.Start("cmd.exe", "/c exit 5");
            Console.WriteLine("[proc.start.id]=" + (pc.Id > 0));
            Console.WriteLine("[proc.name]=" + pc.ProcessName);
            pc.WaitForExit();
            Console.WriteLine("[proc.hasxt.afterwait]=" + pc.HasExited);
            Console.WriteLine("[proc.exitcode]=" + pc.ExitCode);

            // Kill: a long-running child is alive (HasExited False), Kill()
            // terminates it, WaitForExit then confirms HasExited True.
            Process pk = Process.Start("ping.exe", "-n 30 127.0.0.1");
            Console.WriteLine("[proc.kill.hasxt.before]=" + pk.HasExited);
            pk.Kill();
            pk.WaitForExit();
            Console.WriteLine("[proc.kill.hasxt.after]=" + pk.HasExited);
        }
    }
}