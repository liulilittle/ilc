using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ThenByDecChain
    {
        static string J(decimal[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i].ToString("0.0");
            }
            return s;
        }
        static string JL(List<decimal> a)
        {
            string s = "";
            for (int i = 0; i < a.Count; ++i)
            {
                if (i > 0) s += ",";
                s += a[i].ToString("0.0");
            }
            return s;
        }

        static decimal[] Ds()
        {
            return new decimal[]
            {
                3.4m, 1.1m, 2.6m, 1.3m, 2.2m,
                3.8m, 1.7m, 2.5m, 3.1m, 1.9m,
            };
        }

        // Primary int key (floor), secondary int key (fraction % 3), tertiary decimal key.
        static int KI(decimal x) { return (int)(x); }
        static int KJ(decimal x) { return (int)((x * 10) % 3); }
        static decimal KDec(decimal x) { return x % 1m; }

        public static void Run()
        {
            var ds = Ds();
            Console.WriteLine("[d3.iid.dec]=" + J(ds.OrderBy(KI).ThenBy(KJ).ThenByDescending(KDec).ToArray()));
            Console.WriteLine("[d3.desc.iid]=" + J(ds.OrderByDescending(KI).ThenBy(KJ).ThenBy(KDec).ToArray()));
            Console.WriteLine("[d3.i.ddi]=" + J(ds.OrderBy(KI).ThenByDescending(KJ).ThenBy(KDec).ToArray()));
            var lst = new List<decimal>(ds);
            Console.WriteLine("[l3.iid.dec]=" + JL(lst.OrderBy(KI).ThenBy(KJ).ThenByDescending(KDec).ToList()));
            Console.WriteLine("[l3.desc.iid]=" + JL(lst.OrderByDescending(KI).ThenBy(KJ).ThenBy(KDec).ToList()));
            Console.WriteLine("[l2.i.dec]=" + JL(lst.OrderBy(KI).ThenByDescending(KDec).ToList()));
            Console.WriteLine("[l3.dec.dec]=" + JL(lst.OrderBy(KDec).ThenBy(KI).ThenBy(KJ).ToList()));
        }

        public static void Main()
        {
            Run();
        }
    }
}