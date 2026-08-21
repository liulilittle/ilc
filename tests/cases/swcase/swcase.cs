using System;
using System.Diagnostics;
namespace Cases { public static class SwCase {
    // Stopwatch timing-chain probe (new domain: System.Diagnostics.Stopwatch).
    // net48 is the reference oracle. Deliberately NO exact-value asserts
    // (timer jitter): only range / monotonicity / state assertions, all
    // printed as bools. Every line is deterministic on net48.
    private static double Burn(int n) {
        double a = 0;
        for (int i = 0; i < n; i++) a += i * 0.125;
        return a;
    }
    public static void Main() {
        double burn = 0;
        Stopwatch sw = Stopwatch.StartNew();              // static factory -> running
        Console.WriteLine(sw.IsRunning);                  // True: running after StartNew
        long ms0 = sw.ElapsedMilliseconds;
        Console.WriteLine(ms0 >= 0);                      // True: ms never negative
        long t0 = sw.ElapsedTicks;
        Console.WriteLine(t0 >= 0);                       // True: ticks never negative
        Console.WriteLine(sw.Elapsed.TotalMilliseconds >= 0.0); // True: TotalMs lower bound
        burn += Burn(10000000);                           // burn: force the counter to advance
        Console.WriteLine(burn > 0);                      // True: burn loop really ran
        long t1 = sw.ElapsedTicks;
        Console.WriteLine(t1 >= t0);                      // True: ticks monotonic (2nd >= 1st)
        long ms1 = sw.ElapsedMilliseconds;
        Console.WriteLine(ms1 >= ms0);                    // True: ms monotonic
        Console.WriteLine(sw.Elapsed.TotalMilliseconds >= 0.0); // True: still running, lower bound
        sw.Stop();
        Console.WriteLine(sw.IsRunning);                  // False: stopped
        long t2 = sw.ElapsedTicks;
        Console.WriteLine(t2 >= t1);                      // True: elapsed frozen monotonic
        Console.WriteLine(sw.ElapsedMilliseconds >= ms1); // True: ms frozen monotonic
        double totalMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine(totalMs >= 0.0 && totalMs < 100000.0); // True: sane upper bound (< 100 s)
        Console.WriteLine(sw.Elapsed.Ticks >= 0);         // True: TimeSpan ticks >= 0
        long t2again = sw.ElapsedTicks;
        Console.WriteLine(t2again == t2);                 // True: stopped elapsed is stable (frozen)
        sw.Restart();
        Console.WriteLine(sw.IsRunning);                  // True: Restart resumes
        long t3 = sw.ElapsedTicks;
        Console.WriteLine(t3 < t2);                       // True: Restart reset the accumulator
        long freq = Stopwatch.Frequency;
        Console.WriteLine(freq > 0);                      // True: frequency positive
        long ts1 = Stopwatch.GetTimestamp();
        long ts2 = Stopwatch.GetTimestamp();
        Console.WriteLine(ts2 >= ts1);                    // True: raw timestamp monotonic
        burn += Burn(10000000);                           // second segment burn
        Console.WriteLine(burn > 0);                      // True
        long t4 = sw.ElapsedTicks;
        Console.WriteLine(t4 >= t3);                      // True: segment 2 accumulating
        sw.Stop();
        Console.WriteLine(sw.ElapsedTicks >= t4);         // True: cumulative >= last sample
        Console.WriteLine(sw.ElapsedMilliseconds >= 0);   // True: cumulative ms >= 0
    }
}}
