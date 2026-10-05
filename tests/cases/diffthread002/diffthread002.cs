using System;
using System.Threading;
namespace Cases { public static class Prog {
    public static void Main() {
        Console.WriteLine("[newthread.ctor.start.join] begin");
        Thread t = new Thread(new ThreadStart(delegate { Console.WriteLine("[newthread.body] ran"); }));
        Console.WriteLine("[newthread.ctor] ok");
        t.Start();
        Console.WriteLine("[newthread.start] ok");
        t.Join();
        Console.WriteLine("[newthread.join] ok");
    }
}}
