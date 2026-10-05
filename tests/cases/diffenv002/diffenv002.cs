using System;
using System.Threading;
using System.Diagnostics;
using System.Runtime;
namespace Cases { public static class Prog {
    static void L(string id, long v) { Console.WriteLine("[" + id + "] " + v); }
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + v); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    static void GcProbe() {
        try { I("gc.latency.get", (int)GCSettings.LatencyMode); } catch (Exception) { G("gc.latency.get"); }
        try { GCSettings.LatencyMode = GCLatencyMode.Batch; I("gc.latency.batch", (int)GCSettings.LatencyMode); } catch (Exception) { G("gc.latency.batch"); }
        try { GCSettings.LatencyMode = GCLatencyMode.Interactive; I("gc.latency.interactive", (int)GCSettings.LatencyMode); } catch (Exception) { G("gc.latency.interactive"); }
        try { GCSettings.LatencyMode = GCLatencyMode.LowLatency; I("gc.latency.low", (int)GCSettings.LatencyMode); } catch (Exception) { G("gc.latency.low"); }
        try { GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency; I("gc.latency.sustained", (int)GCSettings.LatencyMode); } catch (Exception) { G("gc.latency.sustained"); }
        try {
            try { GCSettings.LatencyMode = (GCLatencyMode)999; I("gc.latency.bad", 0); }
            catch (ArgumentException) { X("gc.latency.bad", "ArgumentException"); }
        } catch (Exception) { G("gc.latency.bad"); }
        try { GCSettings.LatencyMode = GCLatencyMode.Interactive; I("gc.latency.restore", 1); } catch (Exception) { G("gc.latency.restore"); }
        try { I("gc.maxgen", GC.MaxGeneration); } catch (Exception) { G("gc.maxgen"); }
        try { object o = new object(); I("gc.getgen0", GC.GetGeneration(o)); GC.KeepAlive(o); } catch (Exception) { G("gc.getgen0"); }
        try { I("gc.totalmem.pos", (GC.GetTotalMemory(false) > 0) ? 1 : 0); } catch (Exception) { G("gc.totalmem.pos"); }
        try { GC.Collect(); I("gc.collect", 1); } catch (Exception) { G("gc.collect"); }
        try { GC.Collect(0); I("gc.collect.gen0", 1); } catch (Exception) { G("gc.collect.gen0"); }
        try { GC.WaitForPendingFinalizers(); I("gc.waitfinal", 1); } catch (Exception) { G("gc.waitfinal"); }
        try { GC.SuppressFinalize(new object()); I("gc.suppress", 1); } catch (Exception) { G("gc.suppress"); }
        try { GC.ReRegisterForFinalize(new object()); I("gc.reregister", 1); } catch (Exception) { G("gc.reregister"); }
    }
    static void EnvProbe() {
        try { I("env.proccount", Environment.ProcessorCount); } catch (Exception) { G("env.proccount"); }
        try { I("env.tick.pos", (Environment.TickCount != 0) ? 1 : 0); } catch (Exception) { G("env.tick.pos"); }
        try { I("env.is64", Environment.Is64BitProcess ? 1 : 0); } catch (Exception) { G("env.is64"); }
        try { S("env.newline", Environment.NewLine); } catch (Exception) { G("env.newline"); }
    }
    static int counter;
    static void ThreadProbe() {
        object sync = new object();
        try { Monitor.Enter(sync); I("thr.monitor.enter", 1); Monitor.Exit(sync); I("thr.monitor.exit", 1); } catch (Exception) { G("thr.monitor.exit"); }
        try { I("thr.interlocked.add", Interlocked.Add(ref counter, 5)); } catch (Exception) { G("thr.interlocked.add"); }
        try { I("thr.interlocked.cas", Interlocked.CompareExchange(ref counter, 7, 5)); } catch (Exception) { G("thr.interlocked.cas"); }
        try { I("thr.interlocked.inc", Interlocked.Increment(ref counter)); } catch (Exception) { G("thr.interlocked.inc"); }
    }
    public static void Main() {
        Console.WriteLine("-- shard env002 start --");
        try { GcProbe(); } catch (Exception) { G("gc"); }
        try { EnvProbe(); } catch (Exception) { G("env"); }
        try { ThreadProbe(); } catch (Exception) { G("thr"); }
        Console.WriteLine("-- shard env002 end --");
    }
}}
