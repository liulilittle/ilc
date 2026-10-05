//------------------------------------------------------------------------------
// linqfirst.cs -- end-to-end deep probe of the System.Linq.Enumerable
// First / FirstOrDefault surface (the "after-wiring" complement to linqcase,
// which was created AHEAD of the LINQ wiring). Every call is on the WIRED
// int32 (i4) family surface (MethodMap.Linq.cs -> linq_map.h):
//   * First()                  -> il2cpp_linq_first_i4          (hit, empty)
//   * First(predicate)         -> il2cpp_linq_first_predicate   (hit at index
//     0 / middle / last; no-match; empty; single-element hit/miss)
//   * FirstOrDefault()         -> il2cpp_linq_first_or_default_i4
//   * Where/Select chains ending in First / FirstOrDefault (the "hit path"
//     through a materialized filter/map -- values compared, never side
//     effects: the stage-1 Where double-pass deviation makes a Where-side
//     side-effect COUNT differ from net48 by design, so chain lines assert
//     VALUES only).
//
// CALLBACK POLICY -- STATIC-METHOD PREDICATES ONLY (the linqdeep shape,
// proven PASS): the LINQ callback contract (linq-domain.md 2 /
// RequireStaticCallback) rejects instance-bound (captured-lambda) delegates
// with InvalidOperationException; the transpiled delegate-new path for a
// static method group is the single-cast target==null ABI the runtime
// accepts. Predicates therefore live in the SAME static class as Main (the
// linqdeep precedent).
//
// SHORT-CIRCUIT PROBE (side-effect counting, First(pred) ONLY -- no Where in
// these lines): Enumerable.First(predicate) evaluates the predicate once per
// element until the FIRST match then stops (net48 and the runtime
// il2cpp_linq_first_predicate agree: single pass, early return). The count
// asserts: hit at index 0 -> 1 call; miss over a 5-element array -> 5 calls
// then InvalidOperationException; hit at the LAST index -> 9 calls.
//
// NOT-WIRED SURFACE (documented, NOT in the executable path -- including it
// would abort the transpile at the NotSupportedException): 
//   * FirstOrDefault(source, predicate) -- the runtime linq_map.h surface
//     has only the no-predicate first_or_default_i4; MethodMap.Linq.cs
//     throws NotSupportedException for the predicate form. Recorded as a
//     defect in the C31 report, covered once the runtime entry lands.
//
// EVERY line of output is a bool comparison (key=True/False), so the
// reference output is deterministic and hand-verifiable.
//
// Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class Linqfirst
    {
        // Side-effect counters -- reset before each short-circuit probe.
        public static int SideCalls = 0;
        public static bool SideHit5(int x) { SideCalls = SideCalls + 1; return x == 5; }
        public static bool SideHit6(int x) { SideCalls = SideCalls + 1; return x == 6; }

        public static bool IsEven(int x) { return x % 2 == 0; }
        public static bool IsBig(int x) { return x > 4; }
        public static bool IsNeg(int x) { return x < 0; }
        public static bool Is6(int x) { return x == 6; }
        public static int Dbl(int x) { return x * 2; }

        public static void Main()
        {
            int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };   // 9 elements
            int[] evens = { 2, 4, 6, 8, 10 };             // 5 elements
            int[] single = { 42 };
            int[] empty = new int[0];
            int[] allNeg = { -1, -2, -3 };

            Func<int, bool> even = IsEven;
            Func<int, bool> big = IsBig;
            Func<int, bool> neg = IsNeg;
            Func<int, bool> is6 = Is6;
            Func<int, int> dbl = Dbl;

            // ===== 1. First() -- plain, multi + single + empty =====
            Console.WriteLine("first_multi=" + (nums.First() == 5));
            Console.WriteLine("first_single=" + (single.First() == 42));
            bool firstEmptyThrown = false;
            try { int x = empty.First(); } catch (InvalidOperationException) { firstEmptyThrown = true; }
            Console.WriteLine("first_empty_throws=" + firstEmptyThrown);

            // ===== 2. First(predicate) -- hit at index 0 / middle / last =====
            Console.WriteLine("fp_hit_first=" + (evens.First(even) == 2));   // evens[0] matches
            Console.WriteLine("fp_hit_mid=" + (nums.First(big) == 5));       // 5 > 4, first big
            Console.WriteLine("fp_hit_last=" + (nums.First(is6) == 6));      // 6 is the LAST element
            Console.WriteLine("fp_neg_hit=" + (allNeg.First(neg) == -1));

            // ===== 3. First(predicate) -- no-match / empty / single-element =====
            bool fpMissThrown = false;
            try { int x = nums.First(neg); } catch (InvalidOperationException) { fpMissThrown = true; }
            Console.WriteLine("fp_miss_throws=" + fpMissThrown);             // no negatives in nums
            bool fpEmptyThrown = false;
            try { int x = empty.First(even); } catch (InvalidOperationException) { fpEmptyThrown = true; }
            Console.WriteLine("fp_empty_throws=" + fpEmptyThrown);
            Console.WriteLine("fp_single_hit=" + (single.First(even) == 42)); // 42 is even
            bool fpSingleMissThrown = false;
            try { int x = single.First(is6); } catch (InvalidOperationException) { fpSingleMissThrown = true; }
            Console.WriteLine("fp_single_miss=" + fpSingleMissThrown);

            // ===== 4. Short-circuit side-effect counts (First(pred) only) =====
            SideCalls = 0;
            int scHit = nums.First(SideHit5);                                // 5 at index 0
            Console.WriteLine("sc_value=" + (scHit == 5));
            Console.WriteLine("sc_hit_first_calls=" + (SideCalls == 1));     // short-circuit after 1

            SideCalls = 0;
            int scMiss = -1;
            bool scMissThrown = false;
            try { scMiss = evens.First(SideHit5); } catch (InvalidOperationException) { scMissThrown = true; }
            Console.WriteLine("sc_miss_throws=" + scMissThrown);             // no 5 in evens
            Console.WriteLine("sc_miss_scanned=" + (SideCalls == 5));        // scanned all 5 elements

            SideCalls = 0;
            int scLast = nums.First(SideHit6);                               // 6 at the LAST index (8)
            Console.WriteLine("sc_last_value=" + (scLast == 6));
            Console.WriteLine("sc_last_calls=" + (SideCalls == 9));          // scanned 5,3,9,1,7,2,8,4 then 6

            // ===== 5. FirstOrDefault() -- hit / single / empty =====
            Console.WriteLine("fod_first=" + (nums.FirstOrDefault() == 5));
            Console.WriteLine("fod_single=" + (single.FirstOrDefault() == 42));
            Console.WriteLine("fod_empty=" + (empty.FirstOrDefault() == 0));

            // ===== 6. Chains ending in First / FirstOrDefault (VALUES only) =====
            Console.WriteLine("chain_first=" + (nums.Where(even).First() == 2));          // first even in order
            Console.WriteLine("chain_sel=" + (nums.Select(dbl).First() == 10));           // 5*2
            Console.WriteLine("chain_both=" + (nums.Where(big).Select(dbl).First() == 10));
            bool chainEmptyThrown = false;
            try { int x = empty.Where(even).First(); } catch (InvalidOperationException) { chainEmptyThrown = true; }
            Console.WriteLine("chain_empty_throws=" + chainEmptyThrown);
            bool chainNomatchThrown = false;
            try { int x = nums.Where(neg).First(); } catch (InvalidOperationException) { chainNomatchThrown = true; }
            Console.WriteLine("chain_nomatch=" + chainNomatchThrown);
            Console.WriteLine("chain_fod=" + (nums.Where(big).FirstOrDefault() == 5));
            Console.WriteLine("chain_fod_sel=" + (nums.Select(dbl).FirstOrDefault() == 10));
            Console.WriteLine("chain_fod_empty=" + (empty.Where(even).FirstOrDefault() == 0));
        }
    }
}
