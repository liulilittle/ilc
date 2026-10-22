//------------------------------------------------------------------------------
// structtostr2 -- a USER struct that does NOT override ToString().
//
// Theme (single): net48 prints the type's FULL NAME with the '+' separator for
// a nested type ("Cases.StructToStr2+P"). The transpiled call must produce the
// same text from the value (no override to dispatch to).
//
// Every line prints key=[value].
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class StructToStr2
    {
        public struct P
        {
            public int X;
            public int Y;
        }

        public struct S
        {
            public int V;
        }

        static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

        static string F(P p) { return p.ToString(); }

        public static void Main()
        {
            var a = new P();
            a.X = 3;
            a.Y = 4;
            W("t1", a.ToString());
            W("t2", F(a));

            var s = new S();
            s.V = 1;
            W("t3", s.ToString());

            object o = a;
            W("t4", o.ToString());
        }
    }
}
