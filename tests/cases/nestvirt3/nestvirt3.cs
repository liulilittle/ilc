using System;

namespace Cases { public class NestVirt3 {
    public class Base { public virtual int N() { return 11; } }
    public class D1 : Base { public override int N() { return 22; } }

    static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

    public static void Main() {
        Base b = new D1();
        W("v1", b.N().ToString());
        Base b0 = new Base();
        W("v0", b0.N().ToString());
    }
}}
