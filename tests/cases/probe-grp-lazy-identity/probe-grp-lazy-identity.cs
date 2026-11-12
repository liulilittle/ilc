// probe-grp-lazy-identity.cs -- W11 补红探针 ① (D3 foreach identity).
//
// net48: a GroupBy result is DEFERRED and yields FRESH IGrouping objects per
// enumeration. Two separate foreach passes must NOT share the first bucket's
// reference (ReferenceEquals == False). The stage-1 runtime get_enumerator
// path used to hand out the SAME cached bucket objects (section 89 only fixed
// ToArray's CLONE), so this probe covers the get_enumerator blind spot that
// probe-grp-lookup-setops (all .ToArray()) cannot reach.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpLazyIdentity
    {
        static int _calls = 0;

        static int CountingKey(int x)
        {
            _calls++;
            return x % 2;
        }

        public static void Main()
        {
            int[] xs = { 1, 2, 3, 4 };

            _calls = 0;
            var q = xs.GroupBy(CountingKey);
            Console.WriteLine("[create] calls=" + _calls);

            _calls = 0;
            IGrouping<int, int> first1 = null;
            foreach (var g in q) { if (first1 == null) first1 = g; }
            Console.WriteLine("[p1] calls=" + _calls + " first=" + first1.Key);

            _calls = 0;
            IGrouping<int, int> first2 = null;
            foreach (var g in q) { if (first2 == null) first2 = g; }
            Console.WriteLine("[p2] calls=" + _calls + " first=" + first2.Key);

            bool same = ReferenceEquals(first1, first2);
            Console.WriteLine("[identity] same=" + same);
        }
    }
}
