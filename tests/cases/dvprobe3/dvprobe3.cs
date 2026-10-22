// dvprobe3.cs -- delegate Invoke with USER-DEFINED value types, printing only
// scalar projections (no struct ToString: that is an unrelated pre-existing
// gap in the object_to_string icall, not a delegate-signature issue).
using System;

namespace Cases
{
    public struct Pair
    {
        public int A;
        public int B;
        public Pair(int a, int b) { A = a; B = b; }
    }

    public enum Color { Red = 1, Green = 2, Blue = 3 }

    public delegate Pair PairFn(Pair p);

    public static class DelegateUserValue
    {
        private static Pair Swap(Pair p) { return new Pair(p.B, p.A); }
        private static Pair SwapAgain(Pair p) { return new Pair(p.A + 1, p.B + 2); }
        private static int Sum(Pair p) { return p.A + p.B; }
        private static bool BothPositive(Pair p) { return p.A > 0 && p.B > 0; }
        private static int First(Pair p) { return p.A; }

        private static Color Next(Color c) { return (Color)(((int)c % 3) + 1); }
        private static int ColorNum(Color c) { return (int)c; }

        private static string Describe(Pair p) { return "P" + p.A + "/" + p.B; }

        public static void Main()
        {
            Pair p = new Pair(2, 5);

            // ---- Func<Pair,Pair> (struct arg AND struct return) ----
            Func<Pair, Pair> f = Swap;
            Pair r1 = f(p);
            Console.WriteLine("u_pair_direct=" + r1.A + "/" + r1.B);
            f += SwapAgain;
            Pair r2 = f(p);
            Console.WriteLine("u_pair_multi=" + r2.A + "/" + r2.B);

            // ---- Func<Pair,int> ----
            Func<Pair, int> s = Sum;
            Console.WriteLine("u_sum_direct=" + s(p));
            s += First;
            Console.WriteLine("u_sum_multi=" + s(p));

            // ---- Predicate<Pair> ----
            Predicate<Pair> pp = BothPositive;
            Console.WriteLine("u_pred_pos=" + pp(p));
            Console.WriteLine("u_pred_neg=" + pp(new Pair(-1, 5)));

            // ---- Comparison<Pair> (int return, two struct args) ----
            Comparison<Pair> cmp = (x, y) => x.A.CompareTo(y.A);
            Console.WriteLine("u_cmp=" + cmp(p, new Pair(1, 9)));

            // ---- Converter<Pair,string> (struct arg, reference return) ----
            Converter<Pair, string> conv = Describe;
            Console.WriteLine("u_conv=" + conv(p));

            // ---- enum generic argument (VALUETYPE, int32 carrier) ----
            Func<Color, Color> nc = Next;
            Console.WriteLine("u_enum_direct=" + (int)nc(Color.Red));
            nc += Next;
            Console.WriteLine("u_enum_multi=" + (int)nc(Color.Green));
            Func<Color, int> cn = ColorNum;
            Console.WriteLine("u_enum_num=" + cn(Color.Blue));

            // ---- user-declared delegate type over a struct ----
            PairFn pf = Swap;
            Pair r3 = pf(p);
            Console.WriteLine("u_usertype_direct=" + r3.A + "/" + r3.B);
            pf += SwapAgain;
            Pair r4 = pf(p);
            Console.WriteLine("u_usertype_multi=" + r4.A + "/" + r4.B);
        }
    }
}
