using System;

namespace Cases { public static class NestIface1 {
    public interface I { int F(); string Name(); }
    public class A : I { public int F() { return 41; } public string Name() { return "A"; } }
    public class B : I { public int F() { return 42; } public string Name() { return "B"; } }

    static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

    public static void Main() {
        I a = new A();
        W("i1", a.F().ToString());
        W("i2", a.Name());
        I b = new B();
        W("i3", b.F().ToString());
        W("i4", b.Name());
    }
}}
