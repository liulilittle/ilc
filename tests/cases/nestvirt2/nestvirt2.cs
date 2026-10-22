using System;

namespace Cases { public static class NestVirt2 {
    public class Base { public virtual string N() { return "base"; } }
    public class D1 : Base { public override string N() { return "derived"; } }

    static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

    public static void Main() {
        Base b = new D1();
        W("s1", b.N());
        Base b0 = new Base();
        W("s0", b0.N());
        W("len", b.N().Length.ToString());
    }
}}
