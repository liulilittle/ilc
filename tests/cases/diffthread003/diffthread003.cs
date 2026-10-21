using System;
using System.Threading;
namespace Cases { public static class Prog {
    static int hits = 0;
    static ManualResetEvent entered;
    static ManualResetEvent release;
    static void StaticBody() {
        entered.Set();
        release.WaitOne();
        hits = 1;
        Console.WriteLine("[newthread3.body] ran");
    }
    public static void Main() {
        Console.WriteLine("[newthread3.begin]");
        entered = new ManualResetEvent(false);
        release = new ManualResetEvent(false);
        Thread t = new Thread(new ThreadStart(StaticBody));
        Console.WriteLine("[newthread3.ctor.static] ok");
        Console.WriteLine("[newthread3.managedid.pos] " + (t.ManagedThreadId != 0 ? 1 : 0));
        t.IsBackground = true;
        Console.WriteLine("[newthread3.isbackground.set] ok");
        t.Start();
        Console.WriteLine("[newthread3.start] ok");
        entered.WaitOne();
        Console.WriteLine("[newthread3.alive.during] " + (t.IsAlive ? 1 : 0));
        release.Set();
        t.Join();
        Console.WriteLine("[newthread3.join] ok");
        Console.WriteLine("[newthread3.alive.after] " + (t.IsAlive ? 0 : 1));
        Console.WriteLine("[newthread3.hits] " + hits);
    }
}}
