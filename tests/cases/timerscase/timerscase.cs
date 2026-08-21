using System;
using System.Timers;
namespace Cases { public static class TimersCase {
    static int g_fires;
    static void OnElapsed(object sender, ElapsedEventArgs e) { g_fires++; }
    public static void Main() {
        // 1. one-shot via AutoReset=false; dispose via using.
        using (System.Timers.Timer t = new System.Timers.Timer(50)) {
            t.Elapsed += OnElapsed;
            t.AutoReset = false;
            t.Start();
            System.Threading.Thread.Sleep(120);
            t.Stop();
        }
        Console.WriteLine(g_fires == 1);
        // 2. periodic via AutoReset=true (default).
        System.Timers.Timer t2 = new System.Timers.Timer();
        t2.Interval = 30;
        t2.AutoReset = true;
        t2.Elapsed += OnElapsed;
        t2.Start();
        System.Threading.Thread.Sleep(100);
        t2.Stop();
        t2.Dispose();
        Console.WriteLine(g_fires >= 2);
        Console.WriteLine(g_fires <= 5);
        // 3. parameterless ctor + explicit interval via setter + Start.
        g_fires = 0;
        System.Timers.Timer t3 = new System.Timers.Timer();
        t3.Interval = 20;
        t3.Elapsed += OnElapsed;
        t3.Start();
        System.Threading.Thread.Sleep(60);
        t3.Stop();
        t3.Dispose();
        Console.WriteLine(g_fires >= 1);
    }
}}
