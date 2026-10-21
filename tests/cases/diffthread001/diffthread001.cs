using System;
using System.Threading;
namespace Cases { public static class Prog {
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + (v == null ? "<null>" : v)); }
    static void Call(string id, Func<int> f) { try { I(id, f()); } catch (NotSupportedException) { Console.WriteLine("[" + id + "] EX notsupported"); } catch (ThreadStateException) { Console.WriteLine("[" + id + "] EX threadstate"); } catch (ArgumentException) { Console.WriteLine("[" + id + "] EX argument"); } catch (Exception) { Console.WriteLine("[" + id + "] EX other"); } }
    static void Body() { }
    static int CurrentNotNull() { return Thread.CurrentThread == null ? 0 : 1; }
    static int ManagedIdPos() { return Thread.CurrentThread.ManagedThreadId > 0 ? 1 : 0; }
    static int IsAliveSelf() { return Thread.CurrentThread.IsAlive ? 1 : 0; }
    static int IsBackgroundMain() { return Thread.CurrentThread.IsBackground ? 0 : 1; }
    static int PriorityGet() { return (int)Thread.CurrentThread.Priority; }
    static int PrioritySet() { Thread.CurrentThread.Priority = ThreadPriority.Normal; return (int)Thread.CurrentThread.Priority; }
    static int StateOfSelf() { return (int)Thread.CurrentThread.ThreadState; }
    static int Sleep0() { Thread.Sleep(0); return 1; }
    static int Sleep1() { Thread.Sleep(1); return 1; }
    static int Yield() { Thread.Yield(); return 1; }
    static int MemoryBarrier() { Thread.MemoryBarrier(); return 1; }
    static int NewThreadRun() {
        int hits = 0;
        Thread t = new Thread(new ThreadStart(delegate { hits = 1; }));
        t.Start();
        t.Join();
        return hits;
    }
    static int NewThreadIdPos() {
        int ok = 0;
        Thread t = new Thread(new ThreadStart(delegate { ok = Thread.CurrentThread.ManagedThreadId > 0 ? 1 : 0; }));
        t.Start();
        t.Join();
        return ok;
    }
    static int NewThreadAliveAfter() {
        ManualResetEvent entered = new ManualResetEvent(false);
        ManualResetEvent release = new ManualResetEvent(false);
        Thread t = new Thread(new ThreadStart(delegate { entered.Set(); release.WaitOne(); }));
        t.Start();
        if (!entered.WaitOne(TimeSpan.FromSeconds(10))) {
            Console.WriteLine("[timeout] th.new.alive.entered");
            Environment.ExitCode = 1;
            release.Set();
            return 0;
        }
        bool during = t.IsAlive;
        release.Set();
        if (!t.Join(10000)) {
            Console.WriteLine("[timeout] th.new.alive.join");
            Environment.ExitCode = 1;
            return 0;
        }
        bool after = t.IsAlive;
        return (during && !after) ? 1 : 0;
    }
    static int NewThreadIsBackgroundSet() {
        Thread t = new Thread(new ThreadStart(delegate { }));
        t.IsBackground = true;
        bool b = t.IsBackground;
        t.Start();
        t.Join();
        return b ? 1 : 0;
    }
    public static void Main() {
        Console.WriteLine("-- shard thread001 start --");
        Call("th.current.notnull", CurrentNotNull);
        Call("th.current.managedid.pos", ManagedIdPos);
        Call("th.current.isalive", IsAliveSelf);
        Call("th.current.isbackground.false", IsBackgroundMain);
        Call("th.priority.get", PriorityGet);
        Call("th.priority.set", PrioritySet);
        Call("th.state.self", StateOfSelf);
        Call("th.sleep0", Sleep0);
        Call("th.sleep1", Sleep1);
        Call("th.yield", Yield);
        Call("th.memorybarrier", MemoryBarrier);
        Call("th.new.run", NewThreadRun);
        Call("th.new.managedid.pos", NewThreadIdPos);
        Call("th.new.alive.after", NewThreadAliveAfter);
        Call("th.new.isbackground.set", NewThreadIsBackgroundSet);
        Console.WriteLine("-- shard thread001 end --");
    }
}}
