// paircase.cs -- multi-type-template generic analysis fixture (compile-pruning
// decisioning, GenericUsageAnalyzer unit test input).
//
//   Pair<T1,T2>      -- pure object-compatible slot access (shareable: both
//                       parameters collapse onto an object slot).
//   PairArr<T1,T2>   -- newarr T1[] inside (parameter 0 is DIRECTED) -> the
//                       whole definition must stay per-instance (conservative).
//   PairCon<T1,T2>   -- `where T1 : IComparable` (directed constraint) -> the
//                       whole definition must stay per-instance.
//
// The Main body is a plain semantic probe (net48 byte-correct output) so the
// fixture is e2e-ready; the unit test reads METADATA ONLY (no execution).

using System;

public class Pair<T1, T2>
{
    public T1 first;
    public T2 second;
    public T1 GetFirst() { return first; }
    public T2 GetSecond() { return second; }
    public void Set(T1 f, T2 s) { first = f; second = s; }
}

public class PairArr<T1, T2>
{
    public T1[] Make() { return new T1[4]; }
    public T2 other() { return default; }
}

public class PairCon<T1, T2> where T1 : IComparable
{
    public int Cmp(T1 a, T1 b) { return a.CompareTo(b); }
}

namespace Cases { public static class PairCase {
    public static void Main() {
        Pair<int, string> p = new Pair<int, string>();
        p.Set(11, "ab");
        Console.WriteLine(p.GetFirst() + p.GetSecond());
        Pair<string, string> q = new Pair<string, string>();
        q.Set("x", "y");
        Console.WriteLine(q.GetFirst() + q.GetSecond());
        var pa = new PairArr<int, string>();
        Console.WriteLine(pa.Make().Length);
        var pc = new PairCon<string, string>();
        Console.WriteLine(pc.Cmp("b", "a"));
    }
}}