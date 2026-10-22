using System;

namespace Cases { public static class NestVirt1 {
    public class Base { public virtual int N() { return 1; } }
    public class D1 : Base { public override int N() { return 2; } }
    public class D2 : Base { public override int N() { return 3; } }

    static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

    // Receiver whose STATIC type is the base class and whose tracked type is
    // unknown: dispatch MUST go through the vtable.
    static int ViaParam(Base b) { return b.N(); }

    public static void Main() {
        Base b1 = new D1();
        W("v1", b1.N().ToString());
        Base b2 = new D2();
        W("v2", b2.N().ToString());
        Base b0 = new Base();
        W("v0", b0.N().ToString());
        D1 d = new D1();
        W("v3", d.N().ToString());
        W("p1", ViaParam(new D1()).ToString());
        W("p2", ViaParam(new D2()).ToString());
        W("p0", ViaParam(new Base()).ToString());

        // Closed-world sealing guard: the receiver's TRACKED type is Base (the
        // array element type) while the object is a D1. Base has subclasses in
        // the translation set, so it is NOT effectively sealed and the call MUST
        // stay a vtable dispatch (a DerivedCount regression here silently calls
        // Base::N).
        Base[] arr = new Base[] { new D1(), new D2(), new Base() };
        W("e1", arr[0].N().ToString());
        W("e2", arr[1].N().ToString());
        W("e0", arr[2].N().ToString());
    }
}}
