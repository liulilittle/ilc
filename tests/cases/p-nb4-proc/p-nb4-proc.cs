//------------------------------------------------------------------------------
// p-nb4-proc.cs -- nb4 PROC-DIAG difftest probe (net48 oracle == transpiled
// runtime byte-exact).
//
// Pins the three Process diagnostic properties that were unwired:
//   * StartTime   -- GetProcessTimesW creation time, FILETIME->ticks.
//   * ExitTime    -- same structure, read only AFTER the child exited.
//   * WorkingSet64-- GetProcessMemoryInfo peak/current working set; reading
//                    it after the process exited raises InvalidOperationException
//                    (net48, pinned by [pr.postexit.ws.exc]).
//
// Determinism rules: only booleans / the exit code / the exception TYPE name
// are printed -- never the raw DateTime ticks or WS byte counts (they differ
// between the oracle exe and the runtime exe). The child is `cmd.exe /c ping
// 127.0.0.1 -n 3` (~3 s window), so the pre-exit WS/StartTime reads have a
// comfortable race margin and the post-exit reads are pit-stable. NOTE:
// Process.Start(string, string) does NOT redirect stdout (net48 throws
// InvalidOperationException on get_StandardOutput -- redirection needs the
// ProcessStartInfo Start form, batch-2 PROC-STARTINFO scope), so no stdout
// record belongs here.
//------------------------------------------------------------------------------
using System;
using System.Diagnostics;

namespace Cases
{
    public static class PNb4Proc
    {
        public static void Main()
        {
            Process pc = Process.Start("cmd.exe", "/c ping 127.0.0.1 -n 3");
            Console.WriteLine("[pr.pid>0]=" + (pc.Id > 0));
            Console.WriteLine("[pr.starttime>min]=" + (pc.StartTime.Ticks > 0));
            Console.WriteLine("[pr.ws>0]=" + (pc.WorkingSet64 > 0));
            pc.WaitForExit();
            Console.WriteLine("[pr.hasExited]=" + pc.HasExited);
            Console.WriteLine("[pr.exitcode]=" + pc.ExitCode);
            Console.WriteLine("[pr.exitTime>min]=" + (pc.ExitTime.Ticks > 0));

            // net48 caches WorkingSet64 after the first (pre-exit) read, so a
            // post-exit read on pc would NOT throw. Use a SECOND short-lived
            // child whose WorkingSet64 is NEVER read while alive: the first
            // access happens after WaitForExit and forces the OS probe, which
            // raises InvalidOperationException (net48 pinned by [pr.postexit.
            // ws.exc]).
            Process pb = Process.Start("cmd.exe", "/c exit 0");
            pb.WaitForExit();
            string exc = "";
            try { long w = pb.WorkingSet64; } catch (Exception e) { exc = e.GetType().Name; }
            Console.WriteLine("[pr.postexit.ws.exc]=" + exc);
        }
    }
}