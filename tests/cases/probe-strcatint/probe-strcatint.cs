using System;
using System.Linq;

namespace Cases
{
    public static class ProbeStrCatInt
    {
        static int Ninja() { return 7; }
        public static void Main()
        {
            int a = 5;
            Console.WriteLine("[w]" + ("a" + a));
            Console.WriteLine("[lead]" + (a + "!"));
            Console.WriteLine("[n]" + ("a" + Ninja()));
            int[] xs = { 1, 2, 3 };
            Console.WriteLine("[c]" + string.Join(",", xs.Select(x => "v" + x).ToArray()));
            string s = "a" + xs.Length;
            Console.WriteLine("[l]" + s);
            Console.WriteLine("[g]" + string.Join(",", xs.GroupBy(x => x % 2).Select(g => g.Key + ":" + g.Count()).ToArray()));
            Console.WriteLine("[ga]" + string.Join(",", xs.GroupBy(x => x % 2).Select(g => g.Key + ":" + (g.Any() ? "T" : "F")).ToArray()));
            var gr = xs.GroupBy(x => x % 2);
            Console.WriteLine("[rc]" + gr.Count());
        }
    }
}