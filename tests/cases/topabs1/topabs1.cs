using System;

namespace Cases { public static class TopAbs1 {
    static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

    public static void Main() {
        B b = new D();
        W("a1", b.F().ToString());
        W("a2", b.G().ToString());
        I i = new C();
        W("a3", i.F().ToString());
    }
}}

public abstract class B { public abstract int F(); public virtual int G() { return 100; } }
public class D : B { public override int F() { return 7; } public override int G() { return 200; } }
public interface I { int F(); }
public class C : I { public int F() { return 5; } }
