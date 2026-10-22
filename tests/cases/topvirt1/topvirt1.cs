using System;

namespace Cases { public static class TopVirt1 {
    static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

    public static void Main() {
        Base b1 = new D1();
        W("t1", b1.N().ToString());
        Base b0 = new Base();
        W("t0", b0.N().ToString());
        I i = new C();
        W("t2", i.F().ToString());
    }
}}

public class Base { public virtual int N() { return 1; } }
public class D1 : Base { public override int N() { return 2; } }
public interface I { int F(); }
public class C : I { public int F() { return 5; } }
