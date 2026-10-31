//------------------------------------------------------------------------------
// probe-procname.cs -- Process.ProcessName lifetime probe
// (net48 oracle == transpiled runtime byte-exact).
//
// Regression target: the runtime closed the process handle in wait_for_exit /
// the Kill fold and then answered ProcessName by re-opening the PID.
// QueryFullProcessImageNameW fails on a terminated process object
// (ERROR_GEN_FAILURE=31, measured with a native probe) and the re-open fails
// once the PID is recycled -- so the getter silently returned null, while
// net48 (which caches the name in its Process object) still answered.
//
// net48 oracle semantics (measured on .NET Framework 4.8, 2026-10-30):
//   * read while alive        -> "cmd" / "PING" (stem, no ".exe")
//   * read after WaitForExit  -> the SAME value, if read while alive
//   * read 500 ms later       -> the SAME value (child object reclaimed)
//   * repeat reads            -> identical value (net48 caches the instance)
//   * Kill + WaitForExit, read
//                             -> the cached value if read while alive
//                                (40/40 runs), InvalidOperationException otherwise
//                                (100/100 runs)
//   * Kill, then read WITHOUT a full-exit barrier
//                             -> NOT deterministic (3/40 runs answered the name
//                                while the process object was still being torn
//                                down); this probe does not pin that race -- see
//                                [h]/[h2] below for the two deterministic arms
//   * NEVER read while alive, then read after exit
//                             -> InvalidOperationException
//   * GetCurrentProcess()     -> this exe's stem (reduced to non-empty here:
//                                the two executables have different names)
//
// Determinism rules: no absolute paths, no raw self name. The child stems are
// host-identical ("cmd" / "PING"), so content is asserted with predicates and
// an exact length. Every read is wrapped in try/catch: a throwing read must
// surface as a printed record (the harness diffs records, not crashes).
//------------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Threading;

namespace Cases
{
    public static class ProbeProcName
    {
        static bool Same(string a, string b)
        {
            if (a == null || b == null)
            {
                return a == null && b == null;
            }
            return a.Equals(b);
        }

        public static void Main()
        {
            // [a] alive read, [b] after WaitForExit, [c] 500 ms later,
            // [d] three consecutive post-exit reads.
            Process pc = Process.Start("cmd.exe", "/c ping 127.0.0.1 -n 2");
            string nameAlive;
            try
            {
                nameAlive = pc.ProcessName;
            }
            catch (Exception e)
            {
                nameAlive = "!" + e.GetType().Name;
            }
            Console.WriteLine("[a.alive.nonempty]=" + (nameAlive != null && nameAlive.Length > 0));
            Console.WriteLine("[a.alive.eq_expected]=" + Same(nameAlive, "cmd"));
            Console.WriteLine("[a.alive.len]=" + (nameAlive == null ? -1 : nameAlive.Length));

            pc.WaitForExit();
            string nameAfter;
            try
            {
                nameAfter = pc.ProcessName;
            }
            catch (Exception e)
            {
                nameAfter = "!" + e.GetType().Name;
            }
            Console.WriteLine("[b.after_wait.nonnull]=" + (nameAfter != null));
            Console.WriteLine("[b.after_wait.eq_alive]=" + Same(nameAfter, nameAlive));
            Console.WriteLine("[b.after_wait.eq_expected]=" + Same(nameAfter, "cmd"));

            // The child object is reclaimable by now.
            Thread.Sleep(500);
            string nameLate;
            try
            {
                nameLate = pc.ProcessName;
            }
            catch (Exception e)
            {
                nameLate = "!" + e.GetType().Name;
            }
            Console.WriteLine("[c.after_500ms.eq_alive]=" + Same(nameLate, nameAlive));

            string r1;
            string r2;
            string r3;
            try
            {
                r1 = pc.ProcessName;
                r2 = pc.ProcessName;
                r3 = pc.ProcessName;
            }
            catch (Exception e)
            {
                r1 = "!" + e.GetType().Name;
                r2 = r1;
                r3 = r1;
            }
            Console.WriteLine("[d.repeat3]=" + (Same(r1, nameAlive) && Same(r2, nameAlive) && Same(r3, nameAlive)));
            Console.WriteLine("[d.repeat3.len]=" + (r1 == null ? -1 : r1.Length));

            // [e]: Kill after a live read -- the cached name survives.
            Process pk = Process.Start("ping.exe", "-n 30 127.0.0.1");
            string killAlive;
            try
            {
                killAlive = pk.ProcessName;
            }
            catch (Exception e)
            {
                killAlive = "!" + e.GetType().Name;
            }
            Console.WriteLine("[e.kill.alive.eq_expected]=" + Same(killAlive, "PING"));
            Console.WriteLine("[e.kill.alive.len]=" + (killAlive == null ? -1 : killAlive.Length));
            pk.Kill();
            int spins = 0;
            while (!pk.HasExited && spins < 600)
            {
                Thread.Sleep(10);
                spins++;
            }
            string killAfter;
            try
            {
                killAfter = pk.ProcessName;
            }
            catch (Exception e)
            {
                killAfter = "!" + e.GetType().Name;
            }
            Console.WriteLine("[e.kill.after.eq_alive]=" + Same(killAfter, killAlive));
            Console.WriteLine("[e.kill.after.eq_expected]=" + Same(killAfter, "PING"));

            // [f]: GetCurrentProcess -- direct PID query, no regression.
            Process self = Process.GetCurrentProcess();
            string selfName;
            try
            {
                selfName = self.ProcessName;
            }
            catch (Exception e)
            {
                selfName = "!" + e.GetType().Name;
            }
            Console.WriteLine("[f.self.nonempty]=" + (selfName != null && selfName.Length > 0));
            string selfAgain;
            try
            {
                selfAgain = self.ProcessName;
            }
            catch (Exception e)
            {
                selfAgain = "!" + e.GetType().Name;
            }
            Console.WriteLine("[f.self.selfeq]=" + Same(selfAgain, selfName));

            // [g]: never read while alive, read after WaitForExit -> net48
            // raises InvalidOperationException ("process has exited").
            Process pg = Process.Start("cmd.exe", "/c ping 127.0.0.1 -n 2");
            pg.WaitForExit();
            string gName;
            try
            {
                gName = pg.ProcessName;
            }
            catch (Exception e)
            {
                gName = "!" + e.GetType().Name;
            }
            Console.WriteLine("[g.unread_after_wait]=" + gName);

            // [h]: the throwing arm, deterministic. Never read while alive, Kill,
            // then WaitForExit as the full-exit barrier; the first-ever read
            // raises InvalidOperationException (net48 scratch: 100/100 runs).
            // The old HasExited-poll-only formulation raced the kernel teardown
            // of the process object and answered "PING" in 3/40 runs, so it
            // pinned neither arm; WaitForExit removes that window (candidate b).
            Process ph = Process.Start("ping.exe", "-n 30 127.0.0.1");
            ph.Kill();
            ph.WaitForExit();
            string hName;
            try
            {
                hName = ph.ProcessName;
            }
            catch (Exception e)
            {
                hName = "!" + e.GetType().Name;
            }
            Console.WriteLine("[h.unread_after_kill]=" + hName);

            // [h2]: the answering arm, deterministic. A live read fills net48's
            // cached name, then Kill + WaitForExit: the SAME stem is still
            // answered (net48 scratch: PING|PING 40/40 runs). This is the
            // cached-name half of the old [h] race, pinned exactly instead of
            // by timing.
            Process ph2 = Process.Start("ping.exe", "-n 30 127.0.0.1");
            string h2Alive;
            try
            {
                h2Alive = ph2.ProcessName;
            }
            catch (Exception e)
            {
                h2Alive = "!" + e.GetType().Name;
            }
            ph2.Kill();
            ph2.WaitForExit();
            string h2After;
            try
            {
                h2After = ph2.ProcessName;
            }
            catch (Exception e)
            {
                h2After = "!" + e.GetType().Name;
            }
            Console.WriteLine("[h.cached_after_kill]=" + h2After);
            Console.WriteLine("[h.cached_after_kill.eq_alive]=" + Same(h2After, h2Alive));

            // [i]: never read while alive, HasExited poll only (the process
            // handle is still held by the slot but the object is terminated),
            // then the first-ever read -> net48 still raises IOE.
            Process pi = Process.Start("cmd.exe", "/c ping 127.0.0.1 -n 2");
            int spins3 = 0;
            while (!pi.HasExited && spins3 < 600)
            {
                Thread.Sleep(10);
                spins3++;
            }
            string iName;
            try
            {
                iName = pi.ProcessName;
            }
            catch (Exception e)
            {
                iName = "!" + e.GetType().Name;
            }
            Console.WriteLine("[i.unread_after_hasexited]=" + iName);

            // [j]: the cache must survive a compacting collection. A live read
            // fills it, GC.Collect() runs a full (Gen0-compacting) cycle, then
            // the post-exit read must still answer the same stem. This pins the
            // GC-safety of the cache (the runtime keeps NON-managed text and
            // materializes the managed string per read).
            Process pj = Process.Start("cmd.exe", "/c ping 127.0.0.1 -n 3");
            string jAlive;
            try
            {
                jAlive = pj.ProcessName;
            }
            catch (Exception e)
            {
                jAlive = "!" + e.GetType().Name;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            string jAfterGc;
            try
            {
                jAfterGc = pj.ProcessName;
            }
            catch (Exception e)
            {
                jAfterGc = "!" + e.GetType().Name;
            }
            Console.WriteLine("[j.gc_alive.eq_expected]=" + Same(jAlive, "cmd"));
            Console.WriteLine("[j.gc_after.eq_alive]=" + Same(jAfterGc, jAlive));
            pj.WaitForExit();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            string jAfterExitGc;
            try
            {
                jAfterExitGc = pj.ProcessName;
            }
            catch (Exception e)
            {
                jAfterExitGc = "!" + e.GetType().Name;
            }
            Console.WriteLine("[j.gc_exited.eq_alive]=" + Same(jAfterExitGc, jAlive));
        }
    }
}
