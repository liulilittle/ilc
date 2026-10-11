// boxprobe.cs — boxing family (box/unbox.any/isinst/castclass) net48 evidence.
// Key points: boxing snapshot copy, boxed-object reference identity (no value-type interning), null inputs to isinst/castclass,
// type-mismatch failure semantics, unbox.any null -> NRE.
using System;

class BoxProbe
{
    static void P(string s) => Console.WriteLine(s);
    static void Pb(string label, bool v) => P(label + " = " + (v ? 1 : 0));
    static void Pi(string label, int v) => P(label + " = " + v);
    static void Try(Action a, string label)
    {
        try { a(); P(label + " -> no exception"); }
        catch (Exception e) { P(label + " -> " + e.GetType().Name); }
    }

    struct S
    {
        public int X;
        public override string ToString() => "S(" + X + ")";
    }

    class A { }
    class B : A { }

    static void Main()
    {
        // ---- box: value snapshot copy ----
        {
            S s = new S { X = 1 };
            object o = s;          // box: copy
            s.X = 2;
            Pi("box: boxed snapshot (changing the original does not affect the box)", ((S)o).X);   // 1
        }

        // ---- box identity: boxing the same value twice = different objects (no interning) ----
        {
            object a = 5;
            object b = 5;
            Pb("box identity: a == b (reference comparison)", a == b);        // false
            Pb("box identity: a.Equals(b) (value comparison)", a.Equals(b)); // true
            object c = a;
            Pb("box identity: a == c (same reference)", a == c);          // true
        }

        // ---- box big integers / other types ----
        {
            object l = 5L;
            Pb("isinst: boxed long is long", l is long);
            Pb("isinst: boxed long is int", l is int);
            Pl("unbox.any: (long)boxed long", (long)l);
            object d = 2.5;
            P("unbox.any: (double)boxed double = " + (double)d);
        }

        // ---- unbox.any: null -> NullReferenceException ----
        Try(() => { int v = (int)(object)null; P("v=" + v); }, "unbox.any: (int)null");

        // ---- unbox.any: type mismatch -> InvalidCastException ----
        Try(() => { string s = (string)(object)5; P("s=" + s); }, "unbox.any: (string)boxed int");
        Try(() => { long v = (long)(object)5; P("v=" + v); }, "unbox.any: (long)boxed int");

        // ---- isinst: on match returns the original reference / on mismatch returns null (does not throw) ----
        {
            object o = 5;
            Pb("isinst: boxed int is int", o is int);
            Pb("isinst: boxed int is string", o is string);
            object nullObj = null;
            Pb("isinst: null is string", nullObj is string);   // false
            A a = new B();
            Pb("isinst: B instance is A (base class)", a is A);
            Pb("isinst: B instance is B", a is B);
            Pb("isinst: A instance is B", new A() is B);
        }

        // ---- castclass: null input -> null (does not throw) ----
        {
            string s = (string)(object)null;
            P("castclass: (string)null = " + (s == null ? "<null>" : s));
        }

        // ---- castclass: mismatch -> InvalidCastException ----
        Try(() => { string s = (string)(object)5; }, "castclass: (string)boxed int");

        // ---- castclass: isinst path (as first, then cast) ----
        {
            object o = "hello";
            string s = (string)o;
            Pi("castclass: (string)object[string]", s.Length);
        }

        // ---- isinst precision of a boxed double (bit pattern) ----
        {
            object d1 = 0.1;
            object d2 = 0.1;
            Pb("isinst: two boxes of 0.1 reference-equal", d1 == d2);
            Pb("isinst: two boxes of 0.1 Equals", d1.Equals(d2));
        }
    }

    static void Pl(string label, long v) => P(label + " = " + v);
}
