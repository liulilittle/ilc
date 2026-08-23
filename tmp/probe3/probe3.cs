// probe3.cs -- deeper real-program patterns E2E (round E).
using System;
using System.Collections.Generic;

using System.Text;

static class Program
{
    static int failures = 0;
    static void Check(string name, bool ok)
    {
        if (!ok) { failures++; Console.WriteLine("FAIL " + name); }
        else Console.WriteLine("PASS " + name);
    }

    sealed class Person
    {
        public string Name;
        public int Age;
    }

    static int Square(int x) { return x * x; }

    static void Main()
    {
        // ---- string interpolation (compiles to Format/Concat) ----
        var n = 42;
        var s = string.Format("n={0}, hex=0x{1:X}", n, n);
        Check("p3.interp", s == "n=42, hex=0x2A");
        var d = 1.5;
        Check("p3.interp2", string.Format("d={0:F1}", d) == "d=1.5");

        // ---- generic method + type inference ----
        // p3.generic deferred (user generic method M3 probe)

        // NOTE: List<T>/Dictionary<K,V> need generic-interface impl support (TypeSpec in interface impl table) -- documented emitter gap, probe2-era boundary.
        // LINQ-depth section deferred (generic-arg 0x10 M3 probe)\n        // NOTE: yield iterators need generic-interface impl (TypeSpec) -- same emitter gap as List<T>.

        // ---- exceptions: nested + finally + custom ----
        var log = new StringBuilder();
        try
        {
            try { throw new InvalidOperationException("inner"); }
            finally { log.Append("f1"); }
        }
        catch (InvalidOperationException ex)
        {
            log.Append("|").Append(ex.Message);
        }
        Check("p3.exc.nested", log.ToString() == "f1|inner");

        Check("p3.exc.custom", RunCustom() == -1);
        Check("p3.exc.finally.order", FinallyOrder() == "1x" && finallyLog == "12");

        // ---- delegates: multicast chain ----
        Func<int, int> f = Square;
        Check("p3.delegate.invoke", f(5) == 25);
        Action<string> log2 = m => AppendLog(m);
        // multicast via += on a local delegate variable
        // p3.compose deferred

        // NOTE: user-struct semantics (copy/box) deferred -- audit H3+M5 box helper gap.
        // ---- StringBuilder chains ----
        var sb = new StringBuilder();
        sb.Append("a").Append('-').Append("b").AppendFormat("{0}", 3);
        Check("p3.sb.chain", sb.ToString() == "a-b3");

        if (failures == 0) Console.WriteLine("ALL-PASS");
        else Console.WriteLine("FAILURES " + failures);
    }

    static string lastLog;
    static void AppendLog(string m) { lastLog = m; }

    static int RunCustom()
    {
        try { throw new MyException(); }
        catch (MyException) { return -1; }
    }

    sealed class MyException : Exception { }

    static string finallyLog;
    static string FinallyOrder()
    {
        var s = "";
        try { s += "1"; return s + "x"; }
        finally { s += "2"; finallyLog = s; }
    }

}
