//------------------------------------------------------------------------------
// probe-proctime.cs -- Process.StartTime / ExitTime lifetime probe
// (net48 oracle == transpiled runtime byte-exact).
//
// Regression target (sweep-round2 p-nb4-proc concurrent-shard defect): the
// runtime cached only the exit CODE in wait_for_exit, closed the process
// handle and then answered StartTime / ExitTime by re-opening the PID. Once
// the child object was recycled that OpenProcess failed and both getters
// silently degraded to DateTime(0) -- net48 keeps the retained
// SafeProcessHandle and stays exact.
//
// Determinism rules: only booleans and the exception TYPE name are printed.
// StartTime / ExitTime are real clocks, so raw ticks can never be diffed.
//
// Coverage:
//   [a.*] Start -> read StartTime while alive; ExitTime while alive is the
//         net48 InvalidOperationException (oracle-pinned).
//   [b.*] WaitForExit -> StartTime is still the alive value, ExitTime is a
//         real time >= StartTime and inside a 60 s window.
//   [c.*] 500 ms after the wait (the child object reclamation window) both
//         getters still return the same values.
//   [d.*] three consecutive post-wait ExitTime reads are identical.
//   [e.*] a HasExited-polled child (never explicitly waited before the read)
//         exposes both times once HasExited flips.
//   [f.*] the untracked current process (GetCurrentProcess) keeps the direct
//         PID-query behavior -- no regression.
//------------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Threading;

namespace Cases
{
    public static class ProbeProcTime
    {
        public static void Main()
        {
            // ~3 s child: a comfortable window for the alive reads.
            Process pc = Process.Start("cmd.exe", "/c ping 127.0.0.1 -n 3");
            Console.WriteLine("[a.pid>0]=" + (pc.Id > 0));
            long startAlive = pc.StartTime.Ticks;
            Console.WriteLine("[a.start>0]=" + (startAlive > 0));
            string aliveExit = "";
            try
            {
                long ignored = pc.ExitTime.Ticks;
                aliveExit = "value";
            }
            catch (Exception e)
            {
                aliveExit = e.GetType().Name;
            }
            Console.WriteLine("[a.exit.alive]=" + aliveExit);

            pc.WaitForExit();
            long startAfter = pc.StartTime.Ticks;
            long exitAfter = pc.ExitTime.Ticks;
            Console.WriteLine("[b.start>0]=" + (startAfter > 0));
            Console.WriteLine("[b.start==alive]=" + (startAfter == startAlive));
            Console.WriteLine("[b.exit>0]=" + (exitAfter > 0));
            Console.WriteLine("[b.exit>=start]=" + (exitAfter >= startAfter));
            Console.WriteLine("[b.delta<60s]=" + ((exitAfter - startAfter) >= 0 && (exitAfter - startAfter) < 600000000L));

            // The child object is reclaimable by now; net48 still answers from
            // the retained handle, the runtime must answer from its cache.
            Thread.Sleep(500);
            long startLate = pc.StartTime.Ticks;
            long exitLate = pc.ExitTime.Ticks;
            Console.WriteLine("[c.start==alive]=" + (startLate == startAlive));
            Console.WriteLine("[c.exit==prev]=" + (exitLate == exitAfter));

            long exit2 = pc.ExitTime.Ticks;
            long exit3 = pc.ExitTime.Ticks;
            Console.WriteLine("[d.repeat3==]=" + (exit2 == exitAfter && exit3 == exitAfter));

            // HasExited polling: the process terminated, but no explicit wait
            // ran before the reads.
            Process ph = Process.Start("cmd.exe", "/c ping 127.0.0.1 -n 2");
            while (!ph.HasExited)
            {
                Thread.Sleep(10);
            }
            Console.WriteLine("[e.start>0]=" + (ph.StartTime.Ticks > 0));
            Console.WriteLine("[e.exit>0]=" + (ph.ExitTime.Ticks > 0));
            ph.WaitForExit();
            Console.WriteLine("[e.waitexit.ok]=" + (ph.ExitCode == 0));

            // Untracked current process: the direct PID query path.
            Process self = Process.GetCurrentProcess();
            Console.WriteLine("[f.self.start>0]=" + (self.StartTime.Ticks > 0));
            Console.WriteLine("[f.self.ws>0]=" + (self.WorkingSet64 > 0));
        }
    }
}
