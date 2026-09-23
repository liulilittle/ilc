using System;
using System.Threading;
namespace Cases { public static class Prog {
    static string s_seen = "<unset>";
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + (v == null ? "<null>" : v)); }
    static void Call(string id, Func<int> f) { try { I(id, f()); } catch (NotSupportedException) { Console.WriteLine("[" + id + "] EX notsupported"); } catch (ThreadStateException) { Console.WriteLine("[" + id + "] EX threadstate"); } catch (InvalidOperationException) { Console.WriteLine("[" + id + "] EX invalidop"); } catch (ArgumentException) { Console.WriteLine("[" + id + "] EX argument"); } catch (Exception) { Console.WriteLine("[" + id + "] EX other"); } }
    static void CallS(string id, Func<string> f) { try { S(id, f()); } catch (NotSupportedException) { Console.WriteLine("[" + id + "] EX notsupported"); } catch (ThreadStateException) { Console.WriteLine("[" + id + "] EX threadstate"); } catch (InvalidOperationException) { Console.WriteLine("[" + id + "] EX invalidop"); } catch (ArgumentException) { Console.WriteLine("[" + id + "] EX argument"); } catch (Exception) { Console.WriteLine("[" + id + "] EX other"); } }

    static string NameDefault() { Thread t = new Thread(new ThreadStart(delegate { })); return t.Name; }
    static int NameSetGet() { Thread t = new Thread(new ThreadStart(delegate { })); t.Name = "abc"; return (t.Name == "abc") ? 1 : 0; }
    static string NameSetGetValue() { Thread t = new Thread(new ThreadStart(delegate { })); t.Name = "abc"; return t.Name; }
    static int NameSetTwice() { Thread t = new Thread(new ThreadStart(delegate { })); t.Name = "abc"; try { t.Name = "def"; return 0; } catch (InvalidOperationException) { return 1; } }
    static int NameSetNull() { Thread t = new Thread(new ThreadStart(delegate { })); t.Name = null; return (t.Name == null) ? 1 : 0; }
    static int NameCrossThreadRead() {
        Thread t = new Thread(new ThreadStart(delegate { s_seen = Thread.CurrentThread.Name; }));
        t.Name = "worker";
        t.Start();
        t.Join();
        return (s_seen == "worker") ? 1 : 0;
    }
    static string NameCrossThreadReadValue() {
        Thread t = new Thread(new ThreadStart(delegate { s_seen = Thread.CurrentThread.Name; }));
        t.Name = "worker";
        t.Start();
        t.Join();
        return s_seen;
    }
    static int NameMainDefault() { return (Thread.CurrentThread.Name == null) ? 1 : 0; }

    public static void Main() {
        Console.WriteLine("-- shard thread005 start --");
        CallS("th.name.default", NameDefault);
        Call("th.name.setget", NameSetGet);
        CallS("th.name.setget.value", NameSetGetValue);
        Call("th.name.set.twice", NameSetTwice);
        Call("th.name.set.null", NameSetNull);
        Call("th.name.crossthread.read", NameCrossThreadRead);
        CallS("th.name.crossthread.value", NameCrossThreadReadValue);
        Call("th.name.main.default", NameMainDefault);
        Console.WriteLine("-- shard thread005 end --");
    }
}}
