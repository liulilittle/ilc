//------------------------------------------------------------------------------
// stopwatchdeep.cs -- System.Diagnostics.Stopwatch DEEP probe (e2e batch C56):
// timing / restart / precision faces, byte-exact vs the net48 oracle.
//
// Complements swcase (StartNew factory chain) and swcase2 (new Stopwatch()
// instance chain + multi-segment cumulative timing). New surface here:
//   * RESTART face: Restart() invoked from a RUNNING state and from a STOPPED
//     state -- both reset the accumulator to near-zero and leave it running
//     (net48 Restart = Reset + Start).
//   * NO-OP face: Stop() on a stopped watch and Start() on a running watch are
//     no-ops -- the accumulator is never reset by a redundant Start.
//   * PRECISION face (frozen watch, s = raw counter ticks):
//       ElapsedMilliseconds  = trunc(s*1000/Frequency)
//       Elapsed.Ticks        = trunc(s*1e7/Frequency)   (TimeSpan, 100ns units)
//       so Elapsed.Ticks always lies in [ms*10000, (ms+1)*10000) and
//       TotalMilliseconds - ElapsedMilliseconds in [0, 1) -- machine-speed
//       independent floor relationships, deterministic on net48 AND the runtime.
//   * TIMING face: three-cycle cumulative accumulation, frozen-stability on the
//     final stop, Frequency process-stability, GetTimestamp monotonicity.
// Every probe prints a raw bool (net48 reference baseline; no exact-value
// asserts -- timer jitter). ElapsedMicroseconds is NOT used: net48 lacks it.
//------------------------------------------------------------------------------
using System;
using System.Diagnostics;

namespace Cases { public static class Stopwatchdeep {
    private static double Burn(int n) {
        double a = 0;
        for (int i = 0; i < n; i++) a += i * 0.125;
        return a;
    }
    public static void Main() {
        double burn = 0;

        // ================= S1: timing face (3-cycle cumulative) ================
        Stopwatch sw = new Stopwatch();
        sw.Start();
        burn += Burn(10000000); Console.WriteLine(burn > 0);
        long a0 = sw.ElapsedTicks;
        burn += Burn(10000000); Console.WriteLine(burn > 0);
        long a1 = sw.ElapsedTicks;
        Console.WriteLine(a1 >= a0);                       // True: cycle 1 monotonic
        sw.Stop();
        long a2 = sw.ElapsedTicks;
        Console.WriteLine(a2 >= a1);                       // True: frozen >= last sample
        Console.WriteLine(sw.IsRunning == false);          // True: stopped

        sw.Start();
        burn += Burn(10000000); Console.WriteLine(burn > 0);
        long b0 = sw.ElapsedTicks;
        Console.WriteLine(b0 >= a2);                       // True: cycle 2 resumes (no reset)
        burn += Burn(10000000); Console.WriteLine(burn > 0);
        sw.Stop();
        long b1 = sw.ElapsedTicks;
        Console.WriteLine(b1 >= b0);                       // True: cumulative
        Console.WriteLine(sw.IsRunning == false);          // True

        sw.Start();
        burn += Burn(10000000); Console.WriteLine(burn > 0);
        long c0 = sw.ElapsedTicks;
        Console.WriteLine(c0 >= b1);                       // True: cycle 3 resumes
        sw.Stop();
        long c1 = sw.ElapsedTicks;
        Console.WriteLine(c1 >= c0);                       // True
        long c2 = sw.ElapsedTicks;
        Console.WriteLine(c2 == c1);                       // True: frozen stability
        Console.WriteLine(sw.Elapsed.TotalMilliseconds >= 0.0 && sw.Elapsed.TotalMilliseconds < 100000.0);

        // ================= S2: restart face ====================================
        sw.Restart();                                      // stopped -> running, ~0
        Console.WriteLine(sw.IsRunning);                   // True
        long r0 = sw.ElapsedTicks;
        Console.WriteLine(r0 < c1);                        // True: Restart reset the accumulator
        burn += Burn(10000000); Console.WriteLine(burn > 0);
        long r1 = sw.ElapsedTicks;
        Console.WriteLine(r1 >= r0);                       // True: fresh run accumulating
        sw.Restart();                                      // running -> running, ~0 (no Stop first)
        Console.WriteLine(sw.IsRunning);                   // True
        long r2 = sw.ElapsedTicks;
        Console.WriteLine(r2 < r1);                        // True: restart-from-running also resets
        burn += Burn(10000000); Console.WriteLine(burn > 0);
        long r3 = sw.ElapsedTicks;
        Console.WriteLine(r3 >= r2);                       // True: still accumulating after restart
        sw.Stop();

        // ================= S3: no-op face ======================================
        sw.Stop();                                         // already stopped -> no-op
        Console.WriteLine(sw.IsRunning == false);          // True
        long s0 = sw.ElapsedTicks;
        sw.Start();
        sw.Start();                                        // Start on running -> no-op (NO reset)
        burn += Burn(10000000); Console.WriteLine(burn > 0);
        long s1 = sw.ElapsedTicks;
        Console.WriteLine(s1 >= s0);                       // True: redundant Start did not reset
        Console.WriteLine(sw.IsRunning);                   // True
        sw.Stop();

        // ================= S4: precision face (frozen) ========================
        Stopwatch p = new Stopwatch();
        p.Start();
        burn += Burn(10000000); Console.WriteLine(burn > 0);
        p.Stop();
        long pm = p.ElapsedMilliseconds;
        long pt = p.Elapsed.Ticks;
        Console.WriteLine(pt >= pm * 10000);               // True: TimeSpan ticks floor
        Console.WriteLine(pt < (pm + 1) * 10000);          // True: < (ms+1)*10000
        Console.WriteLine(p.Elapsed.TotalMilliseconds >= pm);        // True
        Console.WriteLine(p.Elapsed.TotalMilliseconds - pm < 1.0);   // True
        Console.WriteLine(p.ElapsedMilliseconds >= 0);     // True
        long pt2 = p.Elapsed.Ticks;
        Console.WriteLine(pt2 == pt);                      // True: frozen precision stable
        Console.WriteLine(p.Elapsed.Ticks >= 0);           // True

        // ================= S5: frequency / timestamp primitives ===============
        long f1 = Stopwatch.Frequency;
        long f2 = Stopwatch.Frequency;
        Console.WriteLine(f1 > 0);                         // True
        Console.WriteLine(f1 == f2);                       // True: process-stable
        long t0 = Stopwatch.GetTimestamp();
        burn += Burn(10000000); Console.WriteLine(burn > 0);
        long t1 = Stopwatch.GetTimestamp();
        long t2 = Stopwatch.GetTimestamp();
        Console.WriteLine(t1 >= t0);                       // True: monotonic
        Console.WriteLine(t2 >= t1);                       // True: monotonic
        Console.WriteLine(Stopwatch.Frequency > 0);        // True
    }
}}
