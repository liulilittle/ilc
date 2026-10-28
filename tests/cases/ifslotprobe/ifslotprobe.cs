using System;
using System.Collections.Generic;

// E-cluster probe: InterfaceSlot(iface, sig) lookup by VTablePlanner.SigKey.
// Every shape here reaches a callvirt whose owner is a TRANSLATED interface,
// so the emitter must resolve the interface slot from (name, ParamTypes).
// The probe covers the signature encodings that could make the call-site
// MemberRef signature diverge from the interface's MethodDef signature:
// int32 / decimal / DateTime / generic-instantiation / byref / generic
// interface instantiation / explicit implementation / inherited interface /
// value-type implementer.

public interface IBaseline
{
    int Add(int a, int b);
    string Name { get; }
}

public interface IValues
{
    decimal Dec(decimal d);
    DateTime Dt(DateTime t);
    decimal Mix(int i, decimal d, DateTime t);
}

public interface IGenericArgs
{
    int Sum(List<int> xs);
    List<int> Make(int n);
    string Join(string a, string b);
}

public interface IByref
{
    void Bump(ref int v);
    bool TryGet(int k, out int v);
}

public interface IGen<T>
{
    T Echo(T v);
    int Count();
}

public interface IBase2
{
    int Base(int x);
}

public interface IDerived2 : IBase2
{
    int Derived(int x);
}

public interface IExplicit
{
    int Value(int x);
}

public class Baseline : IBaseline
{
    private readonly int _bias;
    public Baseline(int bias) { _bias = bias; }
    public int Add(int a, int b) { return a + b + _bias; }
    public string Name { get { return "baseline"; } }
}

public class Values : IValues
{
    public decimal Dec(decimal d) { return d * 2m + 1m; }
    public DateTime Dt(DateTime t) { return t.AddDays(3); }
    public decimal Mix(int i, decimal d, DateTime t) { return d + i + t.Day; }
}

public class GenericArgs : IGenericArgs
{
    public int Sum(List<int> xs)
    {
        int s = 0;
        for (int i = 0; i < xs.Count; i++) s += xs[i];
        return s;
    }
    public List<int> Make(int n)
    {
        List<int> r = new List<int>();
        for (int i = 0; i < n; i++) r.Add(i * i);
        return r;
    }
    public string Join(string a, string b) { return a + "|" + b; }
}

public class Byref : IByref
{
    public void Bump(ref int v) { v = v + 7; }
    public bool TryGet(int k, out int v) { v = k * 3; return k > 0; }
}

public class GenInt : IGen<int>
{
    public int Echo(int v) { return v + 1; }
    public int Count() { return 42; }
}

public class GenStr : IGen<string>
{
    public string Echo(string v) { return v + "!"; }
    public int Count() { return 7; }
}

public class Base2Impl : IDerived2
{
    public int Base(int x) { return x + 10; }
    public int Derived(int x) { return x + 20; }
}

public class ExplicitImpl : IExplicit
{
    int IExplicit.Value(int x) { return x * 5; }
}

public static class P
{
    private static int Calls(IValues v, int i, decimal d, DateTime t)
    {
        return (int)v.Mix(i, d, t);
    }

    private static decimal DecCalls(IValues v)
    {
        return v.Dec(1.5m);
    }

    private static DateTime DtCalls(IValues v)
    {
        return v.Dt(new DateTime(2020, 1, 2));
    }

    public static void Main()
    {
        IBaseline ib = new Baseline(100);
        Console.WriteLine("[slot-int]=" + ib.Add(2, 3));
        Console.WriteLine("[slot-prop]=" + ib.Name);

        IValues iv = new Values();
        Console.WriteLine("[slot-dec]=" + iv.Dec(1.5m));
        Console.WriteLine("[slot-dt]=" + iv.Dt(new DateTime(2020, 1, 2)).ToString("yyyy-MM-dd"));
        Console.WriteLine("[slot-mix]=" + iv.Mix(4, 2.5m, new DateTime(2020, 3, 4)));
        IValues iv2 = new Values();
        Console.WriteLine("[slot-mix2]=" + Calls(iv2, 1, 1.25m, new DateTime(2021, 5, 6)));

        IGenericArgs ig = new GenericArgs();
        List<int> xs = new List<int>();
        xs.Add(3); xs.Add(4); xs.Add(5);
        Console.WriteLine("[slot-listarg]=" + ig.Sum(xs));
        List<int> mk = ig.Make(3);
        Console.WriteLine("[slot-listret]=" + mk.Count + ":" + mk[0] + ":" + mk[2]);
        Console.WriteLine("[slot-str]=" + ig.Join("a", "b"));

        IByref ir = new Byref();
        int v = 5;
        ir.Bump(ref v);
        Console.WriteLine("[slot-ref]=" + v);
        int got;
        bool ok = ir.TryGet(4, out got);
        Console.WriteLine("[slot-out]=" + ok + ":" + got);

        IGen<int> gi = new GenInt();
        Console.WriteLine("[slot-gen-int]=" + gi.Echo(9) + ":" + gi.Count());
        IGen<string> gs = new GenStr();
        Console.WriteLine("[slot-gen-str]=" + gs.Echo("x") + ":" + gs.Count());

        IDerived2 id2 = new Base2Impl();
        Console.WriteLine("[slot-derived]=" + id2.Derived(1) + ":" + id2.Base(2));

        IExplicit ie = new ExplicitImpl();
        Console.WriteLine("[slot-explicit]=" + ie.Value(6));

        Console.WriteLine("[slot-dec-via]=" + DecCalls(new Values()));
        Console.WriteLine("[slot-dt-via]=" + DtCalls(new Values()).ToString("yyyy-MM-dd"));
    }
}