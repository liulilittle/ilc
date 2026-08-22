using System;
using System.Timers;
namespace Probe { public static class TimersProbe {
    static int g_fires;
    static void OnElapsed(object sender, ElapsedEventArgs e) { g_fires++; }
    public static void Main() {
        using (System.Timers.Timer t = new System.Timers.Timer(50)) {
            t.Elapsed += OnElapsed;
            t.AutoReset = false;
            t.Start();
            System.Threading.Thread.Sleep(120);
            t.Stop();
        }
        Console.WriteLine(g_fires == 1);
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
    }
}}
