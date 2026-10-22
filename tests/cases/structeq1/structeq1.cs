//------------------------------------------------------------------------------
// structeq1 -- a USER struct that OVERRIDES Equals(object).
//
// Theme (single): `a.Equals(a)` on a user value type emits
// ldloca a; constrained. P; callvirt Object::Equals(object). Without a
// user-struct arm the call fell to icall_boxed_equals_ext2(<address>, b).
//
// net48 semantics: constrained. on a VALUE type does NOT box; it calls
// P.Equals(object) directly, with the boxed argument. Every line prints
// key=[value].
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class StructEq1
    {
        public struct P
        {
            public int X;
            public override bool Equals(object o) { return o is P && ((P)o).X == X; }
        }

        public struct Q
        {
            public int A;
            public int B;
            public override bool Equals(object o)
            {
                return o is Q && ((Q)o).A == A && ((Q)o).B == B;
            }
        }

        static void W(string k, bool v) { Console.WriteLine(k + "=[" + v + "]"); }

        static bool F(P p, object o) { return p.Equals(o); }

        public static void Main()
        {
            var a = new P();
            a.X = 3;
            var b = new P();
            b.X = 3;
            var c = new P();
            c.X = 4;

            W("e1", a.Equals(a));
            W("e2", a.Equals(b));
            W("e3", a.Equals(c));
            W("e4", F(a, (object)b));

            var q1 = new Q();
            q1.A = 1;
            q1.B = 2;
            var q2 = new Q();
            q2.A = 1;
            q2.B = 2;
            W("e5", q1.Equals(q2));

            object oa = a;
            W("e6", oa.Equals(b));
            W("e7", oa.Equals(c));
        }
    }
}
