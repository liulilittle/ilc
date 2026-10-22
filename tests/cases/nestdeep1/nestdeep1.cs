using System;

namespace Cases { public static class NestDeep1 {
    public class Base { public virtual int N() { return 1; } public virtual string S() { return "B"; } }
    public class Mid : Base { public override int N() { return 2; } public override string S() { return "M"; } }
    public class Leaf : Mid { public override int N() { return 3; } public override string S() { return "L"; } }

    static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

    public static void Main() {
        Base b = new Leaf();
        W("d1", b.N().ToString());
        W("d2", b.S());
        Mid m = new Leaf();
        W("d3", m.N().ToString());
        W("d4", m.S());
        Mid m2 = new Mid();
        W("d5", m2.N().ToString());
        W("d6", m2.S());
        Base b2 = new Mid();
        W("d7", b2.N().ToString());
    }
}}
