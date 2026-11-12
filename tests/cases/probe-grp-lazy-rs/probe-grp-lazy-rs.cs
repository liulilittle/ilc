// probe-grp-lazy-rs.cs -- W11 补红探针 ② (D5 / net48 Q8).
//
// net48: GroupBy(keySelector, resultSelector) is DEFERRED too -- the key
// selector (once per source element) AND the result selector (once per bucket)
// run on EVERY enumeration, and 0 times at construction. This runtime's
// rsKind != 0 form returns a materialized ARRAY at call time (eager), so the
// counts at create / pass1 / pass2 are the red signal. SCOPE GAP registered:
// this probe is expected to stay RED against the current seat (rsKind != 0
// deferred is NOT implemented in W11 first slice); it documents the gap with
// an executable, byte-exact oracle-vs-runtime differential.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpLazyRs
    {
        static int _keyCalls = 0;
        static int _resCalls = 0;

        static int CountingKey(int x)
        {
            _keyCalls++;
            return x % 2;
        }

        static string ResultSel(int key, System.Collections.Generic.IEnumerable<int> vals)
        {
            _resCalls++;
            return key + ":" + vals.Count();
        }

        public static void Main()
        {
            int[] xs = { 1, 2, 3, 4 };

            _keyCalls = 0; _resCalls = 0;
            var q = xs.GroupBy(CountingKey, ResultSel);
            Console.WriteLine("[create] key=" + _keyCalls + " res=" + _resCalls);

            _keyCalls = 0; _resCalls = 0;
            foreach (var r in q) Console.WriteLine("[p1] " + r);
            Console.WriteLine("[p1_calls] key=" + _keyCalls + " res=" + _resCalls);

            _keyCalls = 0; _resCalls = 0;
            foreach (var r in q) Console.WriteLine("[p2] " + r);
            Console.WriteLine("[p2_calls] key=" + _keyCalls + " res=" + _resCalls);
        }
    }
}
