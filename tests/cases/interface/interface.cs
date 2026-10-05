using System;
public interface ICalc { int Calc(int v); int Double(int v); }
public interface IMul { int Mul(int a, int b); }
public class Doubler : ICalc {
    public int Calc(int v) => v * 2;
    public int Double(int v) => v * 2;
}
public class Tripler : ICalc {
    public int Calc(int v) => v * 3;
    public int Double(int v) => v * 6;
}
public class Multi : ICalc, IMul {
    public int Calc(int v) => v * 7;
    public int Double(int v) => v * 14;
    public int Mul(int a, int b) => a * b;
}
public class Inherited : Doubler, ICalc { }
// audit H7: a derived type that does NOT re-declare the interface still
// implements it through the base chain -- dispatch on a DerivedOnly instance
// must find the inherited interface map.
public class BaseImpl : ICalc {
    public int Calc(int v) => v * 2;
    public int Double(int v) => v * 2;
}
public class DerivedOnly : BaseImpl { }
namespace Cases { public static class Interface {
    public static void Main() {
        ICalc c = new Doubler();
        Console.WriteLine(c.Calc(21));               // 42
        Console.WriteLine(c.Double(5));              // 10
        c = new Tripler();
        Console.WriteLine(c.Calc(21));               // 63
        Console.WriteLine(c.Double(3));              // 18
        IMul m = new Multi();
        Console.WriteLine(m.Mul(6, 7));              // 42
        ICalc c2 = new Multi();
        Console.WriteLine(c2.Calc(6));               // 42
        Console.WriteLine(c2.Double(3));             // 42
        Inherited inh = new Inherited();
        ICalc ci = inh;
        Console.WriteLine(ci.Calc(10));              // 20 (inherited Doubler impl via interface)
        Console.WriteLine(ci.Double(4));             // 8
        // audit H7: interface implemented ONLY by the base class.
        DerivedOnly d = new DerivedOnly();
        ICalc c3 = d;
        Console.WriteLine(c3.Calc(21));              // 42
        Console.WriteLine(c3.Double(4));             // 8
        Console.WriteLine(d is ICalc ? 1 : 0);       // 1 (isinst walks the base chain)
    }
}}
