// nb17convcast.cs -- the isinst/castclass identity arms for the BCL delegate
// instantiations that dvprobe1/3/4 do NOT exercise (Converter`2 and
// Comparison`1 via an explicit Delegate.Combine castclass).
using System;

namespace Cases
{
    public struct Nb17Pair
    {
        public int A;
        public int B;
        public Nb17Pair(int a, int b) { A = a; B = b; }
    }

    public static class Nb17ConvCmp
    {
        private static string Describe(Nb17Pair p) { return "P" + p.A + "/" + p.B; }
        private static string Describe2(Nb17Pair p) { return "Q" + p.A + "*" + p.B; }

        private static int CmpA(Nb17Pair x, Nb17Pair y) { return x.A.CompareTo(y.A); }
        private static int CmpB(Nb17Pair x, Nb17Pair y) { return x.B.CompareTo(y.B); }

        public static void Main()
        {
            Nb17Pair p = new Nb17Pair(2, 5);

            Converter<Nb17Pair, string> c1 = Describe;
            Converter<Nb17Pair, string> c2 = Describe2;
            Converter<Nb17Pair, string> cc = (Converter<Nb17Pair, string>)Delegate.Combine(c1, c2);
            Console.WriteLine("a_conv_combine=" + cc(p));

            Comparison<Nb17Pair> m1 = CmpA;
            Comparison<Nb17Pair> m2 = CmpB;
            Comparison<Nb17Pair> mm = (Comparison<Nb17Pair>)Delegate.Combine(m1, m2);
            Console.WriteLine("a_cmp_combine=" + mm(new Nb17Pair(2, 5), new Nb17Pair(2, 3)));

            Converter<Nb17Pair, string> cn = Describe;
            cn += Describe2;
            Console.WriteLine("a_conv_plus=" + cn(p));

            Comparison<Nb17Pair> mn = CmpA;
            mn += CmpB;
            Console.WriteLine("a_cmp_plus=" + mn(new Nb17Pair(2, 5), new Nb17Pair(2, 3)));
        }
    }
}