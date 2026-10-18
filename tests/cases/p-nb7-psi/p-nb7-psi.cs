//------------------------------------------------------------------------------
// p-nb7-psi.cs -- nb7 PROC-STARTINFO difftest probe (net48 oracle == transpiled
// runtime byte-exact).
//
// Pins the ProcessStartInfo object model + Start(ProcessStartInfo) + the
// StandardOutput/StandardError FULL (multi-line) redirected read:
//   * field round-trip: FileName / Arguments / UseShellExecute /
//     RedirectStandardOutput / RedirectStandardError / CreateNoWindow
//     (defaults then set-then-get; net48 default UseShellExecute == true).
//   * Start(psi): a cmd.exe child with both streams redirected -> ReadToEnd
//     returns the FULL multi-line stdout/stderr (not just the first line).
//
// Determinism rules (the processcase / p-nb4-proc model): only booleans and the
// exact (ASCII) line contents are printed; CRLF runs are folded to "|" so the
// record is platform-independent. The child is `cmd.exe` (host-identical on
// both sides); UseShellExecute=false keeps it console-independent (a genuine
// net48 redirect run, unlike the ShellExecute static Start form).
//------------------------------------------------------------------------------
using System;
using System.Diagnostics;

namespace Cases
{
    public static class PNb7Psi
    {
        static string B(bool b) { return b ? "True" : "False"; }

        public static void Main()
        {
            // --- ProcessStartInfo field model (defaults + round-trip) --------
            ProcessStartInfo def = new ProcessStartInfo();
            Console.WriteLine("[si.def.useShell]=" + B(def.UseShellExecute));
            Console.WriteLine("[si.def.fn.len]=" + def.FileName.Length);
            Console.WriteLine("[si.def.args.len]=" + def.Arguments.Length);
            Console.WriteLine("[si.def.redirOut]=" + B(def.RedirectStandardOutput));
            Console.WriteLine("[si.def.redirErr]=" + B(def.RedirectStandardError));
            Console.WriteLine("[si.def.noWindow]=" + B(def.CreateNoWindow));

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c echo one & echo two & echo three & echo errline 1>&2";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            Console.WriteLine("[si.fn]=" + psi.FileName);
            Console.WriteLine("[si.args]=" + psi.Arguments);
            Console.WriteLine("[si.useShell]=" + B(psi.UseShellExecute));
            Console.WriteLine("[si.redirOut]=" + B(psi.RedirectStandardOutput));
            Console.WriteLine("[si.redirErr]=" + B(psi.RedirectStandardError));
            Console.WriteLine("[si.noWindow]=" + B(psi.CreateNoWindow));

            // --- Start(ProcessStartInfo) + full redirected read --------------
            Process p = Process.Start(psi);
            Console.WriteLine("[psi.pid>0]=" + (p.Id > 0));
            string so = p.StandardOutput.ReadToEnd();
            string se = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Console.WriteLine("[psi.stdout.len>0]=" + (so.Length > 0));
            Console.WriteLine("[psi.stdout]=" + so.Replace("\r\n", "|").Replace("\n", "|"));
            Console.WriteLine("[psi.stderr]=" + se.Replace("\r\n", "|").Replace("\n", "|"));
            Console.WriteLine("[psi.exitcode]=" + p.ExitCode);
        }
    }
}
