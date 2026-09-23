using System;
using System.Runtime;
namespace Cases { public static class Prog {
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void L(string id, long v) { Console.WriteLine("[" + id + "] " + v); }
    static void Call(string id, Func<int> f) { try { I(id, f()); } catch (Exception) { Console.WriteLine("[" + id + "] EX gap"); } }
    static int MaxGen() { return GC.MaxGeneration; }
    static int IsServerGC() { return GCSettings.IsServerGC ? 1 : 0; }
    static int LatencyGet() { return (int)GCSettings.LatencyMode; }
    static int LatencySet() { GCSettings.LatencyMode = GCLatencyMode.Batch; return (int)GCSettings.LatencyMode; }
    static int GenOfNew() { return GC.GetGeneration(new object()); }
    static int CollectionCount0() { return GC.CollectionCount(0) >= 0 ? 1 : 0; }
    static int TotalMem() { return GC.GetTotalMemory(false) > 0 ? 1 : 0; }
    static int TotalMemBlocking() { return GC.GetTotalMemory(true) > 0 ? 1 : 0; }
    static int KeepAlive() { var o = new object(); GC.KeepAlive(o); return o == null ? 0 : 1; }
    static int SuppressFinalize() { var o = new object(); GC.SuppressFinalize(o); return 1; }
    static int ReRegister() { var o = new object(); GC.ReRegisterForFinalize(o); return 1; }
    static int WaitFinalizers() { GC.WaitForPendingFinalizers(); return 1; }
    static int CollectAndCount() { GC.Collect(); return GC.CollectionCount(0) >= 0 ? 1 : 0; }
    static int SuppressThenCollect() { var o = new object(); GC.SuppressFinalize(o); GC.KeepAlive(o); GC.Collect(0); return 1; }
    static int EnvNewLine() { return Environment.NewLine.Length; }
    static int EnvProcCount() { return Environment.ProcessorCount > 0 ? 1 : 0; }
    static int EnvIs64() { return Environment.Is64BitProcess ? 1 : 0; }
    static int EnvPath() { return Environment.GetEnvironmentVariable("PATH") != null ? 1 : 0; }
    static int EnvFolder() { return Environment.GetFolderPath(Environment.SpecialFolder.Windows).Length > 0 ? 1 : 0; }
    static int EnvTickCount() { return Environment.TickCount != 0 ? 1 : 0; }
    static int EnvVersion() { return Environment.Version.Major >= 4 ? 1 : 0; }
    static int EnvWorkingSet() { return Environment.WorkingSet > 0 ? 1 : 0; }
    public static void Main() {
        Console.WriteLine("-- shard env001 start --");
        Call("env.gc.maxgen", MaxGen);
        Call("env.gc.isservergc", IsServerGC);
        Call("env.gc.latency.get", LatencyGet);
        Call("env.gc.latency.set", LatencySet);
        Call("env.gc.generation.new", GenOfNew);
        Call("env.gc.collectioncount0", CollectionCount0);
        Call("env.gc.totalmem", TotalMem);
        Call("env.gc.totalmem.blocking", TotalMemBlocking);
        Call("env.gc.keepalive", KeepAlive);
        Call("env.gc.suppressfinalize", SuppressFinalize);
        Call("env.gc.reregisterfinalize", ReRegister);
        Call("env.gc.waitfinalizers", WaitFinalizers);
        Call("env.gc.collect", CollectAndCount);
        Call("env.gc.suppress.collect", SuppressThenCollect);
        Call("env.newline.len", EnvNewLine);
        Call("env.proc.count.ok", EnvProcCount);
        Call("env.is64", EnvIs64);
        Call("env.path.present", EnvPath);
        Call("env.folder.windows", EnvFolder);
        Call("env.tickcount", EnvTickCount);
        Call("env.version.major4", EnvVersion);
        Call("env.workingset", EnvWorkingSet);
        Console.WriteLine("-- shard env001 end --");
    }
}}
