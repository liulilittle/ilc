using System;
using System.Linq;

namespace Cases
{
    public static class W11ProbeLinqGrpDec
    {
        public static void Main()
        {
            var g = new[] { 1.5m, 2.5m, 1.5m }.GroupBy(x => x);
            foreach (var grp in g)
            {
                Console.WriteLine(grp.Key + ":" + grp.First() + ":" + grp.Last());
            }
        }
    }
}
