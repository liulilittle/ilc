using System;

namespace Cases { public static class NestAbs1 {
    public abstract class B { public abstract int F(); public virtual int G() { return 100; } }
    public class D : B { public override int F() { return 7; } public override int G() { return 200; } }
    public class E : B { public override int F() { return 9; } }

    static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

    public static void Main() {
        B b = new D();
        W("a1", b.F().ToString());
        W("a2", b.G().ToString());
        B e = new E();
        W("a3", e.F().ToString());
        W("a4", e.G().ToString());
    }
}}
