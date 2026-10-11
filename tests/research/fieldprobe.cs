// fieldprobe.cs — field family (ldfld/stfld/ldsfld/stsfld/ldflda) net48 evidence.
// Key points: null receiver NRE (ldfld/stfld), instance access to static fields emits no null check (ldsfld/stsfld),
// cctor timing, field default values, ldflda address-of, volatile fields (memory-order semantics not observable single-threaded).
using System;

class FieldProbe
{
    static void P(string s) => Console.WriteLine(s);
    static void Pi(string label, int v) => P(label + " = " + v);
    static void Try(Action a, string label)
    {
        try { a(); P(label + " -> no exception"); }
        catch (Exception e) { P(label + " -> " + e.GetType().Name); }
    }

    class Box
    {
        public int X;
        public string S;
        public static int StaticCount;
        public static readonly string StaticCtorTrace;

        static Box()
        {
            StaticCtorTrace = "cctor-ran";
            StaticCount = 7;
        }

        public static int StaticX = 99;
    }

    struct Point
    {
        public int X;
        public int Y;
    }

    volatile int _vol;      // C# volatile -> IL volatile. prefix (ldfld.volatile/stfld.volatile)
    static volatile int _svol;

    static void Main()
    {
        // ---- cctor: first access of a static field triggers type init (before ldsfld/stsfld) ----
        P("cctor: trace before first access=" + (Box.StaticCtorTrace ?? "<null>"));
        Pi("ldsfld: Box.StaticCount", Box.StaticCount);
        Box.StaticCount = 11;
        Pi("stsfld: Box.StaticCount=11", Box.StaticCount);

        // ---- Instance access to static fields is outright forbidden in C# 7.3 (CS0176 instance-qualified static member).
        //      The IL ldsfld/stsfld operands hold only a field token, no receiver -> no null check exists:
        //      semantically "null instance accessing a static field" does not throw NRE (ECMA II.III.4.28/4.30). ----
        // var b0 = (Box)null; b0.StaticCount = 5;  // rejected at compile time by CS0176

        // ---- instance fields: default values / read-write ----
        var bb = new Box();
        Pi("ldfld: default int field", bb.X);
        P("ldfld: default string field = " + (bb.S == null ? "<null>" : bb.S));
        bb.X = 123;
        Pi("ldfld: read after write", bb.X);

        // ---- ldfld/stfld null receiver: NullReferenceException ----
        Try(() => { Box n = null; int v = n.X; P("v=" + v); }, "ldfld: null receiver");
        Try(() => { Box n = null; n.X = 1; }, "stfld: null receiver");

        // ---- ldflda: field address (ref write) ----
        {
            var pt = new Point { X = 1, Y = 2 };
            ref int rx = ref pt.X;               // ldloca+ldflda (value-type local)
            rx = 100;
            Pi("ldflda: ref write pt.X", pt.X);
        }

        // ---- volatile fields: single-threaded read-write roundtrip (memory-order semantics in docs; C# volatile compiles to volatile. prefix) ----
        {
            var fp = new FieldProbe();
            fp._vol = 7;
            Pi("volatile field round-trip", fp._vol);
            _svol = 13;
            Pi("volatile static field round-trip", _svol);
        }

        // ---- instance field inheritance: base-class field read through a derived instance ----
        {
            var d = new Derived();
            d.BaseField = 5;
            Pi("ldfld: derived reads base-class field", ((BaseClass)d).BaseField);
        }
    }

    class BaseClass { public int BaseField; }
    class Derived : BaseClass { }
}
