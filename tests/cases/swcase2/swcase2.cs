using System;
using System.Diagnostics;
namespace Cases { public static class SwCase2 {
    // Stopwatch instance-chain probe (net48 oracle; swcase = StartNew factory
    // chain, swcase2 = new Stopwatch() instance chain + multi-segment timing).
    // Cumulative resume semantics under test: Start() after Stop() does NOT
    // reset the accumulator (net48), Reset() zeroes + stops. Deliberately NO
    // exact-value asserts (timer jitter): only range / monotonicity / state
    // assertions, all printed as bools. Every line is deterministic on net48.
    private static double Burn(int n) {
        double a = 0;
        for (int i = 0; i < n; i++) a += i * 0.125;
        return a;
    }
    public static void Main() {
        double burn = 0;
        Stopwatch sw = new Stopwatch();               // ctor -> idle
        Console.WriteLine(sw.IsRunning);              // False: fresh instance idle
        Console.WriteLine(sw.Elapsed.Ticks == 0);     // True: fresh elapsed zero
        Console.WriteLine(sw.ElapsedMilliseconds == 0); // True: fresh ms zero

        sw.Start();                                   // segment 1
        Console.WriteLine(sw.IsRunning);              // True: running
        long a0 = sw.ElapsedTicks;
        burn += Burn(10000000);                       // burn: force the counter to advance
        Console.WriteLine(burn > 0);                  // True: burn loop really ran
        long a1 = sw.ElapsedTicks;
        Console.WriteLine(a1 >= a0);                  // True: segment 1 monotonic
        sw.Stop();
        Console.WriteLine(sw.IsRunning);              // False: stopped
        long a2 = sw.ElapsedTicks;
        Console.WriteLine(a2 >= a1);                  // True: frozen monotonic
        Console.WriteLine(sw.ElapsedMilliseconds >= 0); // True: frozen ms >= 0
        long a2again = sw.ElapsedTicks;
        Console.WriteLine(a2again == a2);             // True: stopped elapsed is stable (frozen)

        sw.Start();                                   // resume WITHOUT Reset
        Console.WriteLine(sw.IsRunning);              // True: resumed running
        long b0 = sw.ElapsedTicks;
        Console.WriteLine(b0 >= a2);                  // True: net48 resume does NOT reset the accumulator
        burn += Burn(10000000);                       // segment 2 burn
        Console.WriteLine(burn > 0);                  // True
        long b1 = sw.ElapsedTicks;
        Console.WriteLine(b1 >= b0);                  // True: segment 2 monotonic
        sw.Stop();
        Console.WriteLine(sw.IsRunning);              // False
        long b2 = sw.ElapsedTicks;
        Console.WriteLine(b2 >= b1);                  // True: cumulative >= last sample
        Console.WriteLine(sw.ElapsedMilliseconds >= 0); // True: cumulative ms >= 0

        sw.Reset();                                   // zero + idle
        Console.WriteLine(sw.IsRunning);              // False: Reset stops
        Console.WriteLine(sw.Elapsed.Ticks == 0);     // True: Reset zeroes ticks
        Console.WriteLine(sw.ElapsedMilliseconds == 0); // True: Reset zeroes ms

        sw.Start();                                   // segment 3 (fresh after Reset)
        Console.WriteLine(sw.IsRunning);              // True: running again
        long c0 = sw.ElapsedTicks;
        burn += Burn(10000000);
        Console.WriteLine(burn > 0);                  // True
        long c1 = sw.ElapsedTicks;
        Console.WriteLine(c1 >= c0);                  // True: fresh segment monotonic
        long m0 = sw.ElapsedMilliseconds;
        burn += Burn(10000000);
        Console.WriteLine(burn > 0);                  // True
        long m1 = sw.ElapsedMilliseconds;
        Console.WriteLine(m1 >= m0);                  // True: ms monotonic while running
        sw.Stop();
        Console.WriteLine(sw.IsRunning);              // False
        long c2 = sw.ElapsedTicks;
        Console.WriteLine(c2 >= c1);                  // True
        Console.WriteLine(sw.Elapsed.TotalMilliseconds >= 0.0);   // True: TotalMs lower bound
        Console.WriteLine(sw.Elapsed.TotalMilliseconds < 100000.0); // True: sane upper bound (< 100 s)

        Console.WriteLine(Stopwatch.Frequency > 0);   // True: frequency positive
        long ts1 = Stopwatch.GetTimestamp();
        burn += Burn(10000000);
        Console.WriteLine(burn > 0);                  // True
        long ts2 = Stopwatch.GetTimestamp();
        Console.WriteLine(ts2 >= ts1);                // True: raw timestamp monotonic
    }
}}
