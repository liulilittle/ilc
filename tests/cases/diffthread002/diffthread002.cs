using System;
using System.Threading;
namespace Cases { public static class Prog {
    public static void Main() {
        Console.WriteLine("[newthread.ctor.start.join] begin");
        ManualResetEvent release = new ManualResetEvent(false);
        Thread t = new Thread(new ThreadStart(delegate { release.WaitOne(); Console.WriteLine("[newthread.body] ran"); }));
        Console.WriteLine("[newthread.ctor] ok");
        t.Start();
        Console.WriteLine("[newthread.start] ok");
        release.Set();
        t.Join();
        Console.WriteLine("[newthread.join] ok");
    }
}}
