// probe-grp-lazy-sidefx.cs -- W11 补红探针 ③ (A14/A16/A17/A23-A28).
//
// Covers: (a) a throwing key selector re-throws on EVERY enumeration and does
// not leave a half-built view; (b) a LIVE source mutation between two passes
// is observed (deferred re-reads the array); (c) the comparer and the element
// selector re-run on EVERY enumeration; (d) eager null validation stays at
// GroupBy() call time. All records are byte-exact vs the net48 oracle.
//------------------------------------------------------------------------------
using System;
using System.Linq;
using System.Collections.Generic;

namespace Cases
{
    public static class ProbeGrpLazySidefx
    {
        static int _keyCalls = 0;
        static int _elemCalls = 0;
        static int _cmpCalls = 0;

        static int Key(int x) { _keyCalls++; return x % 2; }
        static int Elem(int x) { _elemCalls++; return x * 10; }

        sealed class ParityCmp : IEqualityComparer<int>
        {
            public bool Equals(int a, int b) { _cmpCalls++; return a == b; }
            public int GetHashCode(int x) { return x; }
        }

        static void Pass(string tag, IEnumerable<IGrouping<int, int>> q)
        {
            foreach (var g in q)
            {
                int sum = 0;
                foreach (var e in g) sum += e;
                Console.WriteLine("[" + tag + "] key=" + g.Key + " sum=" + sum);
            }
        }

        public static void Main()
        {
            // (a) live source re-read + (c) element/comparer side effects
            int[] xs = { 1, 2, 3, 4 };
            var cmp = new ParityCmp();
            _keyCalls = 0; _elemCalls = 0; _cmpCalls = 0;
            var q = xs.GroupBy(Key, Elem, cmp);
            Console.WriteLine("[create] key=" + _keyCalls + " elem=" + _elemCalls + " cmp=" + _cmpCalls);

            _keyCalls = 0; _elemCalls = 0; _cmpCalls = 0;
            Pass("p1", q);
            int p1key = _keyCalls, p1elem = _elemCalls, p1cmp = _cmpCalls;
            // key/elem absolute counts ARE byte-exact (4/4 on both sides);
            // comparer's absolute count is NOT (see [rerun] below).
            Console.WriteLine("[p1_calls] key=" + _keyCalls + " elem=" + _elemCalls);

            xs[0] = 99; // late mutation visible to the next enumeration
            // Force a compacting collection between the two enumerations: the
            // persisted src/delegate reference fields MUST survive it (the
            // g_groupingResultClass refFieldBitmap must cover slots 3..7).
            // A cleared bitmap bit would leave a dangling old address here.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            _keyCalls = 0; _elemCalls = 0; _cmpCalls = 0;
            Pass("p2", q);
            Console.WriteLine("[p2_calls] key=" + _keyCalls + " elem=" + _elemCalls);
            // ABSOLUTE comparer call counts are algorithm-dependent (net48's
            // hash Lookup: 2/pass; this runtime's linear bucket scan: 4/pass)
            // and PRE-EXISTING (eager too) -- not a deferred concern. What
            // deferred MUST guarantee: every delegate re-runs EVERY pass, i.e.
            // p1 == p2 and both > 0. That equality IS byte-exact comparable.
            Console.WriteLine("[rerun] key=" + (p1key == _keyCalls && _keyCalls > 0)
                + " elem=" + (p1elem == _elemCalls && _elemCalls > 0)
                + " cmp=" + (p1cmp == _cmpCalls && _cmpCalls > 0));
            // Any() is ALSO a full enumeration in net48 (key selector runs once
            // per source element, NOT short-circuited to 1 element).
            _keyCalls = 0;
            bool anyResult = q.Any();
            Console.WriteLine("[any_calls] calls=" + _keyCalls + " result=" + anyResult);
            // A SINGLE ToArray() is ONE rebuild (selector runs 4 times, not N
            // times): auditA §3 精化 1's rebuild-boundary guard (M9).
            _keyCalls = 0;
            int bucketCount = q.ToArray().Length;
            Console.WriteLine("[toarray_calls] calls=" + _keyCalls + " buckets=" + bucketCount);

            // (b) throwing selector re-throws on BOTH enumerations
            int boomCalls = 0;
            Func<int, int> boom = x => { boomCalls++; if (x == 3) throw new InvalidOperationException("boom"); return x % 2; };
            int[] ys = { 1, 2, 3, 4 };
            var qb = ys.GroupBy(boom);
            Console.WriteLine("[throw_create] calls=" + boomCalls);
            boomCalls = 0;
            try { Pass("throw_p1", qb); } catch (InvalidOperationException e) { Console.WriteLine("[throw_p1] " + e.Message + " calls=" + boomCalls); }
            boomCalls = 0;
            try { Pass("throw_p2", qb); } catch (InvalidOperationException e) { Console.WriteLine("[throw_p2] " + e.Message + " calls=" + boomCalls); }

            // (d) eager null validation at call time
            try { xs.GroupBy((Func<int, int>)null); Console.WriteLine("[nullkey] no-throw"); }
            catch (ArgumentNullException e) { Console.WriteLine("[nullkey] " + e.GetType().Name); }
        }
    }
}
