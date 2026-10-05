using System;
using System.Threading;
namespace Cases { public static class Prog {
    static int s_b1 = -1; static int s_b2 = -1;
    static void Body1() { s_b1 = Thread.CurrentThread.ManagedThreadId; }
    static void Body2() { s_b2 = Thread.CurrentThread.ManagedThreadId; }
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    public static void Main() {
        Console.WriteLine("-- shard th004 start --");
        int cur = -1; try { cur = Thread.CurrentThread.ManagedThreadId; } catch (Exception) { }
        I("mt.cur", cur);
        I("mt.cur.pos", cur > 0 ? 1 : 0);
        Thread t1 = new Thread(new ThreadStart(Body1));
        I("mt.t1.ctor", t1.ManagedThreadId);
        I("mt.t1.ctor.repeat", t1.ManagedThreadId);
        Thread t2 = new Thread(new ThreadStart(Body2));
        I("mt.t2.ctor", t2.ManagedThreadId);
        Thread t3 = new Thread(new ThreadStart(Body1));
        I("mt.t3.ctor", t3.ManagedThreadId);
        int c1 = t1.ManagedThreadId, c2 = t2.ManagedThreadId, c3 = t3.ManagedThreadId;
        I("mt.pos.all", (c1 > 0 && c2 > 0 && c3 > 0) ? 1 : 0);
        I("mt.distinct.all", (c1 != c2 && c2 != c3 && c1 != c3 && c1 != cur && c2 != cur && c3 != cur) ? 1 : 0);
        I("mt.delta12", c2 - c1);
        I("mt.delta23", c3 - c2);
        try { t1.Start(); } catch (Exception) { I("mt.t1.start.ex", 1); }
        try { t2.Start(); } catch (Exception) { I("mt.t2.start.ex", 1); }
        try { t1.Join(); } catch (Exception) { }
        try { t2.Join(); } catch (Exception) { }
        I("mt.t1.afterjoin", t1.ManagedThreadId);
        I("mt.t1.stable", (t1.ManagedThreadId == c1) ? 1 : 0);
        I("mt.t2.stable", (t2.ManagedThreadId == c2) ? 1 : 0);
        I("mt.body1", s_b1);
        I("mt.body2", s_b2);
        I("mt.body1.eq.ctor", (s_b1 == c1) ? 1 : 0);
        I("mt.body2.eq.ctor", (s_b2 == c2) ? 1 : 0);
        I("mt.distinct.bodies", (s_b1 != s_b2) ? 1 : 0);
        Console.WriteLine("-- shard th004 end --");
    }
}}
