//------------------------------------------------------------------------------
// structtostr1 -- a USER struct that OVERRIDES ToString().
//
// Theme (single): `a.ToString()` on a user value type emits
// ldloca a; constrained. P; callvirt Object::ToString(). The fold has no
// user-struct arm, so the call fell to icall_object_to_string(<address>) --
// the struct address handed to an Il2CppObject* parameter (uncompilable).
//
// net48 semantics: constrained. on a VALUE type does NOT box; it calls
// P.ToString() directly. Every line prints key=[value].
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class StructToStr1
    {
        public struct P
        {
            public int X;
            public override string ToString() { return "P" + X; }
        }

        public struct Q
        {
            public int A;
            public int B;
            public override string ToString() { return "Q(" + A + "," + B + ")"; }
        }

        static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

        static string F(P p) { return p.ToString(); }

        public static void Main()
        {
            var a = new P();
            a.X = 3;
            W("s1", a.ToString());
            W("s2", F(a));

            var q = new Q();
            q.A = 7;
            q.B = 9;
            W("s3", q.ToString());

            P[] arr = new P[2];
            arr[0].X = 11;
            arr[1].X = 12;
            W("s4", arr[0].ToString());
            W("s5", arr[1].ToString());

            object o = a;
            W("s6", o.ToString());
        }
    }
}
