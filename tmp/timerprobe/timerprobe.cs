using System;
using System.Threading;
namespace Cases { public static class TimerCase {
    static int g_fires;
    static void Cb(object state) { g_fires++; }
    public static void Main() {
        using (Timer t = new Timer(Cb, null, 50, 100)) { Thread.Sleep(260); }
        Console.WriteLine("after-periodic: " + g_fires);
        g_fires = 0;
        Timer t2 = new Timer(Cb, null, 20, Timeout.Infinite);
        Thread.Sleep(60);
        t2.Dispose();
        Console.WriteLine("after-oneshot: " + g_fires);
        Timer t3 = new Timer(Cb, null, Timeout.Infinite, Timeout.Infinite);
        Thread.Sleep(40);
        Console.WriteLine("after-unarmed: " + g_fires);
        t3.Change(10, Timeout.Infinite);
        Thread.Sleep(40);
        t3.Dispose();
        Console.WriteLine("after-change: " + g_fires);
    }
}}
