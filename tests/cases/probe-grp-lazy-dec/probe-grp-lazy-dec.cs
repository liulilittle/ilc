// probe-grp-lazy-dec.cs -- W11 P0-B: decimal-source GroupBy (dec_key arm) is
// DEFERRED too. net48 oracle (auditB-dec-oracle.exe): D1_create=0 / D2_pass1=4
// / D3_pass2=4 / D6_fresh=True / D7_live (late source mutation visible).
// A byte-exact runtime must match: the key selector re-runs on EVERY
// enumeration and the query observes a late decimal[] mutation.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpLazyDec
    {
        static int _calls = 0;

        static int CountingKey(decimal x)
        {
            _calls++;
            return (int)(x % 2m);
        }

        static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        static string Show(System.Linq.IGrouping<int, decimal> g)
        {
            string s = "";
            foreach (decimal v in g) { if (s.Length > 0) s += ","; s += D(v); }
            return g.Key + ":" + s;
        }

        public static void Main()
        {
            decimal[] xs = { 1m, 2m, 3m, 4m };

            _calls = 0;
            var q = xs.GroupBy(CountingKey);
            Console.WriteLine("[create] calls=" + _calls);

            _calls = 0;
            foreach (var g in q) Console.WriteLine("[p1] " + Show(g));
            Console.WriteLine("[p1_calls] calls=" + _calls);

            xs[0] = 99m; // late mutation visible to the next enumeration
            _calls = 0;
            foreach (var g in q) Console.WriteLine("[p2] " + Show(g));
            Console.WriteLine("[p2_calls] calls=" + _calls);
        }
    }
}
