using System;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpSelect
    {
        public static void Main()
        {
            string[] ss = { "b", "A", "c", "a" };
            Console.WriteLine("[g]=" + string.Join(",", ss.GroupBy(s => s)
                .Select(g => g.Key + ":" + g.Count()).ToArray()));
            Console.WriteLine("[ga]=" + string.Join(",", ss.GroupBy(s => s)
                .Select(g => g.Key + ":" + (g.Any() ? "T" : "F")).ToArray()));
            Console.WriteLine("[rc]=" + ss.GroupBy(s => s).Count());
            Console.WriteLine("[plain]=" + string.Join(",", new[] { 1, 2, 1, 3 }.GroupBy(x => x % 2)
                .Select(g => g.Key + ":" + g.Count()).ToArray()));
        }
    }
}