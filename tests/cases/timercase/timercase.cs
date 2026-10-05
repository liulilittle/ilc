using System;
using System.Threading;
namespace Cases { public static class TimerCase {
    static int g_fires;
    static void Cb(object state) { g_fires++; }
    public static void Main() {
        // 1. periodic timer: due=50, period=100; dispose via using.
        //    Count is timing-sensitive (260ms / 100ms period -> 2 or 3
        //    fires), so the assert is a RANGE (>= 1, <= 10).
        using (Timer t = new Timer(Cb, null, 50, 100)) {
            Thread.Sleep(260);
        }
        Console.WriteLine(g_fires >= 1);
        Console.WriteLine(g_fires <= 10);
        // 2. one-shot via Timeout.Infinite period (fires exactly once).
        g_fires = 0;
        Timer t2 = new Timer(Cb, null, 20, Timeout.Infinite);
        Thread.Sleep(60);
        t2.Dispose();
        Console.WriteLine(g_fires == 1);
        // 3. unarmed create (due=Infinite) fires NOTHING until Change.
        Timer t3 = new Timer(Cb, null, Timeout.Infinite, Timeout.Infinite);
        Thread.Sleep(40);
        Console.WriteLine(g_fires == 1);
        t3.Change(10, Timeout.Infinite);
        Thread.Sleep(40);
        t3.Dispose();
        Console.WriteLine(g_fires == 2);
        // 4. explicit IDisposable::Dispose (the using-shape's callvirt).
        g_fires = 0;
        Timer t4 = new Timer(Cb, null, 15, Timeout.Infinite);
        IDisposable id = t4;
        Thread.Sleep(50);
        id.Dispose();
        Console.WriteLine(g_fires == 1);
        // 5. two concurrent timers (batch 159 extension): each has its own
        //    thread; both fire independently.
        g_fires = 0;
        Timer c1 = new Timer(Cb, null, 20, Timeout.Infinite);
        Timer c2 = new Timer(Cb, null, 40, Timeout.Infinite);
        Thread.Sleep(80);
        c1.Dispose();
        c2.Dispose();
        Console.WriteLine(g_fires == 2);
        // 6. change re-arms a one-shot timer BEFORE it fires (net48: the
        //    change applies to the pending due).
        g_fires = 0;
        Timer c3 = new Timer(Cb, null, 100, Timeout.Infinite);
        Thread.Sleep(20);
        c3.Change(10, Timeout.Infinite);
        Thread.Sleep(40);
        c3.Dispose();
        Console.WriteLine(g_fires == 1);
    }
}}
