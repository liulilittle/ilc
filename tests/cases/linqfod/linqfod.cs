//------------------------------------------------------------------------------
// linqfod.cs -- end-to-end deep probe of System.Linq.Enumerable.FirstOrDefault
// across BOTH wired forms, on the WIRED int32 (i4) family surface
// (MethodMap.Linq.cs -> linq_map.h, HEAD a97c1ed):
//   * FirstOrDefault()                    -> il2cpp_linq_first_or_default_i4
//   * FirstOrDefault(predicate)           -> il2cpp_linq_first_or_default_predicate
//     (walks from the start, returns the FIRST match; no match or an empty
//     source -> the int32 default 0, NEVER throws -- the complement of
//     First(predicate), which throws InvalidOperationException)
//
// The predicate form is the batch-74 wiring that linqfirst (C31) could only
// document as a NOT-WIRED surface -- this case exercises it live.
//
// Probe families:
//   1. Plain form: hit / single-element / empty (-> 0).
//   2. Predicate form, hit: at index 0 / middle / LAST index; a matched
//      element whose VALUE is 0 (the "0 is a real value" ambiguity probe --
//      FirstOrDefault returns the matched 0, indistinguishable by value from
//      the empty default, which is exactly the net48 contract).
//   3. Predicate form, miss / empty / single-element: no match -> 0 (NOT
//      InvalidOperationException), empty -> 0, single hit -> element,
//      single miss -> 0.
//   4. Short-circuit side-effect counting (FirstOrDefault(pred) ONLY -- no
//      Where in these lines): hit at index 0 -> 1 predicate call; miss over
//      a 5-element array -> 5 calls then 0; empty -> 0 calls (never enters
//      the scan). Counts assert net48's single-pass early-return contract.
//   5. Null arguments: null source (plain + predicate) -> ArgumentNull-
//      Exception; null predicate -> ArgumentNullException.
//   6. Chains ending in FirstOrDefault (VALUES only -- the stage-1 Where
//      double-pass deviation makes Where-side side-effect COUNTS differ by
//      design, so chain lines never count side effects).
//
// CALLBACK POLICY -- STATIC-METHOD PREDICATES ONLY (the linqdeep/linqfirst
// shape, proven PASS): the LINQ callback contract rejects instance-bound
// delegates with InvalidOperationException. Predicates live in the SAME
// static class as Main.
//
// Every line of output is a bool comparison (key=True/False) or an int
// count comparison, so the reference output is deterministic and
// hand-verifiable.
//
// Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class Linqfod
    {
        // Side-effect counters -- reset before each short-circuit probe.
        public static int SideCalls = 0;
        public static bool SideHit5(int x) { SideCalls = SideCalls + 1; return x == 5; }

        public static bool IsEven(int x) { return x % 2 == 0; }
        public static bool IsBig(int x) { return x > 4; }
        public static bool IsNeg(int x) { return x < 0; }
        public static bool Is6(int x) { return x == 6; }
        public static bool IsZero(int x) { return x == 0; }
        public static int Dbl(int x) { return x * 2; }

        public static void Main()
        {
            int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };   // 9 elements, 6 LAST
            int[] evens = { 2, 4, 6, 8, 10 };             // 5 elements, no 5
            int[] single = { 42 };
            int[] empty = new int[0];
            int[] allNeg = { -1, -2, -3 };
            int[] zeros = { 0, 0, 0 };                    // all elements are 0

            Func<int, bool> even = IsEven;
            Func<int, bool> big = IsBig;
            Func<int, bool> neg = IsNeg;
            Func<int, bool> is6 = Is6;
            Func<int, bool> isZero = IsZero;
            Func<int, int> dbl = Dbl;

            // ===== 1. FirstOrDefault() -- plain, multi + single + empty =====
            Console.WriteLine("fod_first=" + (nums.FirstOrDefault() == 5));
            Console.WriteLine("fod_single=" + (single.FirstOrDefault() == 42));
            Console.WriteLine("fod_empty=" + (empty.FirstOrDefault() == 0));
            Console.WriteLine("fod_zero_elem=" + (zeros.FirstOrDefault() == 0));   // first element IS 0

            // ===== 2. FirstOrDefault(predicate) -- hit at index 0 / middle / last =====
            Console.WriteLine("fodp_hit_first=" + (evens.FirstOrDefault(even) == 2)); // evens[0] matches
            Console.WriteLine("fodp_hit_mid=" + (nums.FirstOrDefault(big) == 5));     // 5 > 4, first big
            Console.WriteLine("fodp_hit_last=" + (nums.FirstOrDefault(is6) == 6));    // 6 is the LAST element
            Console.WriteLine("fodp_neg_hit=" + (allNeg.FirstOrDefault(neg) == -1));
            Console.WriteLine("fodp_zero_match=" + (zeros.FirstOrDefault(isZero) == 0)); // matched value IS 0

            // ===== 3. FirstOrDefault(predicate) -- no-match / empty / single =====
            // No-match -> 0 (NOT InvalidOperationException -- the core semantic
            // difference from First(predicate)).
            Console.WriteLine("fodp_miss=" + (nums.FirstOrDefault(neg) == 0));       // no negatives in nums
            Console.WriteLine("fodp_miss_not_ex=" + !(nums.FirstOrDefault(neg) == -1));
            Console.WriteLine("fodp_empty=" + (empty.FirstOrDefault(even) == 0));
            Console.WriteLine("fodp_single_hit=" + (single.FirstOrDefault(even) == 42)); // 42 is even
            Console.WriteLine("fodp_single_miss=" + (single.FirstOrDefault(is6) == 0));  // 42 is not 6 -> 0

            // ===== 4. Short-circuit side-effect counts (FirstOrDefault(pred) only) =====
            SideCalls = 0;
            int scHit = nums.FirstOrDefault(SideHit5);                               // 5 at index 0
            Console.WriteLine("sc_value=" + (scHit == 5));
            Console.WriteLine("sc_hit_first_calls=" + (SideCalls == 1));             // short-circuit after 1

            SideCalls = 0;
            int scMiss = evens.FirstOrDefault(SideHit5);                             // no 5 in evens
            Console.WriteLine("sc_miss_value=" + (scMiss == 0));                     // miss -> 0, no throw
            Console.WriteLine("sc_miss_scanned=" + (SideCalls == 5));                // scanned all 5 elements

            SideCalls = 0;
            int scEmpty = empty.FirstOrDefault(SideHit5);                            // empty source
            Console.WriteLine("sc_empty_value=" + (scEmpty == 0));
            Console.WriteLine("sc_empty_calls=" + (SideCalls == 0));                 // never entered the scan

            // ===== 5. Null arguments (net48 -> ArgumentNullException) =====
            int[] nil = null;
            bool nullSrcThrown = false;
            try { int x = nil.FirstOrDefault(); } catch (ArgumentNullException) { nullSrcThrown = true; }
            Console.WriteLine("null_src_plain=" + nullSrcThrown);
            bool nullSrcPredThrown = false;
            try { int x = nil.FirstOrDefault(even); } catch (ArgumentNullException) { nullSrcPredThrown = true; }
            Console.WriteLine("null_src_pred=" + nullSrcPredThrown);
            bool nullPredThrown = false;
            try { int x = nums.FirstOrDefault((Func<int, bool>)null); } catch (ArgumentNullException) { nullPredThrown = true; }
            Console.WriteLine("null_pred=" + nullPredThrown);

            // ===== 6. Chains ending in FirstOrDefault (VALUES only) =====
            Console.WriteLine("chain_fod=" + (nums.Where(even).FirstOrDefault() == 2));   // first even in order
            Console.WriteLine("chain_sel=" + (nums.Select(dbl).FirstOrDefault() == 10));  // 5*2
            Console.WriteLine("chain_both=" + (nums.Where(big).Select(dbl).FirstOrDefault() == 10));
            Console.WriteLine("chain_fod_pred=" + (nums.Where(big).FirstOrDefault(big) == 5));
            Console.WriteLine("chain_fod_empty=" + (empty.Where(even).FirstOrDefault() == 0));
            Console.WriteLine("chain_fod_nomatch=" + (nums.Where(neg).FirstOrDefault() == 0));
            Console.WriteLine("chain_fod_pred_miss=" + (nums.Where(big).FirstOrDefault(neg) == 0));
            Console.WriteLine("chain_fod_sel_empty=" + (empty.Select(dbl).FirstOrDefault() == 0));
        }
    }
}
