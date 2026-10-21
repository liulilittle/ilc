using System;
using System.Threading;
namespace Cases { public static class Prog {
    public static void Main() {
        Console.WriteLine("[newthread.ctor.start.join] begin");
        ManualResetEvent entered = new ManualResetEvent(false);
        ManualResetEvent release = new ManualResetEvent(false);
        Thread t = new Thread(new ThreadStart(delegate { entered.Set(); release.WaitOne(); Console.WriteLine("[newthread.body] ran"); }));
        Console.WriteLine("[newthread.ctor] ok");
        t.Start();
        Console.WriteLine("[newthread.start] ok");
        if (!entered.WaitOne(TimeSpan.FromSeconds(10))) {
            Console.WriteLine("[timeout] newthread.entered");
            Environment.ExitCode = 1;
            release.Set();
            return;
        }
        release.Set();
        if (!t.Join(10000)) {
            Console.WriteLine("[timeout] newthread.join");
            Environment.ExitCode = 1;
            return;
        }
        Console.WriteLine("[newthread.join] ok");
    }
}}
