//------------------------------------------------------------------------------
// p-nb8-stdin.cs -- nb8 STDIN-WRITE difftest probe (net48 oracle == transpiled
// runtime byte-exact).
//
// Pins ProcessStartInfo.RedirectStandardInput + Process.StandardInput (a
// StreamWriter over the child's anonymous INPUT pipe): AutoFlush, WriteLine,
// Close (EOF) and the child's echoed stdout.
//
// Determinism: the child is `cmd.exe /c findstr .*` (host-identical on both
// sides; verified to echo every non-empty line verbatim with CRLF); only ASCII
// line contents are printed, CRLF folded to "|". UseShellExecute=false keeps
// the redirect legal (net48 would throw otherwise).
//------------------------------------------------------------------------------
using System;
using System.Diagnostics;

namespace Cases
{
    public static class PNb8Stdin
    {
        static string B(bool b) { return b ? "True" : "False"; }

        public static void Main()
        {
            ProcessStartInfo def = new ProcessStartInfo();
            Console.WriteLine("[si.def.redirIn]=" + B(def.RedirectStandardInput));

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c findstr .*";
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            Console.WriteLine("[si.redirIn]=" + B(psi.RedirectStandardInput));

            Process p = Process.Start(psi);
            Console.WriteLine("[stdin.pid>0]=" + (p.Id > 0));
            p.StandardInput.AutoFlush = true;
            Console.WriteLine("[stdin.autoflushtrue]=" + B(p.StandardInput.AutoFlush));
            p.StandardInput.WriteLine("hello");
            p.StandardInput.WriteLine("world");
            p.StandardInput.Write("abc");
            p.StandardInput.Close();
            string so = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Console.WriteLine("[stdin.echo]=" + so.Replace("\r\n", "|").Replace("\n", "|"));
            Console.WriteLine("[stdin.ec]=" + p.ExitCode);
        }
    }
}
