// probe-grp-lazy.cs -- GroupBy LAZINESS red probe (b5-gaps section 89 seat).
//
// Behaviour spec (docs/audit/b5-gaps.md:6848-6873): net48's GroupBy is a
// DEFERRED query -- the key selector is invoked on EVERY enumeration, not once
// when the query object is constructed. Enumerating the same query twice with
// a 2-element source calls the key selector 4 times (2 per pass).
//
// The stage-1 runtime is EAGER (docs/audit/b5-gaps.md:118): it buckets the
// source at GroupBy() call time and stores the materialized groups in
// Il2CppLinqGroupingResult (linq_map.h:673), so the selector runs once.
//
// This probe prints the exact selector call count after each pass. net48 is
// the oracle; a byte-exact runtime must match. The red/green signal is the
// [after_create] / [after_pass1] / [after_pass2] records.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpLazy
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
            Console.WriteLine("[after_create] calls=" + _calls);

            _calls = 0;
            foreach (var g in q)
                Console.WriteLine("[p1] key=" + g.Key + " n=" + g.Count());
            Console.WriteLine("[after_pass1] calls=" + _calls);

            _calls = 0;
            foreach (var g in q)
                Console.WriteLine("[p2] key=" + g.Key + " n=" + g.Count());
            Console.WriteLine("[after_pass2] calls=" + _calls);
        }
    }
}
