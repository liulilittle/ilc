//------------------------------------------------------------------------------
// structhash1 -- a USER struct that OVERRIDES GetHashCode().
//
// Theme (single): `a.GetHashCode()` on a user value type emits
// ldloca a; constrained. P; callvirt Object::GetHashCode(). Without a
// user-struct arm the call fell to icall_object_get_hash_code(<address>).
//
// net48 semantics: constrained. on a VALUE type does NOT box; it calls
// P.GetHashCode() directly. Every line prints key=[value].
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class StructHash1
    {
        public struct P
        {
            public int X;
            public override int GetHashCode() { return X * 7; }
        }

        public struct Q
        {
            public int A;
            public int B;
            public override int GetHashCode() { return A * 31 + B; }
        }

        static void W(string k, int v) { Console.WriteLine(k + "=[" + v + "]"); }

        static int F(P p) { return p.GetHashCode(); }

        public static void Main()
        {
            var a = new P();
            a.X = 3;
            W("h1", a.GetHashCode());
            W("h2", F(a));

            var q = new Q();
            q.A = 2;
            q.B = 5;
            W("h3", q.GetHashCode());

            P[] arr = new P[2];
            arr[0].X = 4;
            W("h4", arr[0].GetHashCode());

            object o = a;
            W("h5", o.GetHashCode());
        }
    }
}
