//------------------------------------------------------------------------------
// loud-linq-widekey-orderby.cs -- e2e reference case for the WIDE-key OrderBy /
// ThenBy family (linq_sort.h) over int32 and int64 sources.
//
// The int32-source OrderBy entries read the key from the int32 slot; a long /
// double key crosses in the int64 / F-stack slot, so it needs its own runtime
// entry (il2cpp_linq_order_by_key[_desc]_i4_i8 / _i4_d; the int64 source has
// il2cpp_linq_order_by_key[_desc]_i8_d). The fused ThenBy composite follows the
// primary key family (il2cpp_linq_then_by_key[_desc]_i4_i8 / _i4_d / _i8_d).
//
// This case drives those entries and asserts the net48 semantics:
//   * ORDER: ascending / descending by the wide key;
//   * STABILITY: equal keys keep their source order;
//   * KEY-CACHE-ONCE: a side-effecting key selector runs exactly once per
//     element (net48 EnumerableSorter -- a re-invoking sort over-counts);
//   * COPY: the source array is never written and the result is a fresh array;
//   * ThenBy: the fused (k1 asc, k2 asc / desc) composite;
//   * EDGE INPUTS: empty / single / all-equal-key arrays.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqWideKeyOrderBy
    {
        private static int _calls = 0;

        // Wide key selectors (static leaves -- the transpiler's delegate ABI).
        private static long LongKey(int x) { return 100L - x; }
        private static double DoubleKey(int x) { return 100.0 - x; }
        private static long CountingLongKey(int x) { _calls++; return x; }
        private static long TensKey(int x) { return x / 10; }
        private static long UnitsKey(int x) { return x % 10; }

        // int64-source key selectors.
        private static double DoubleKeyLong(long x) { return 100.0 - x; }
        private static long LongKeyLong(long x) { return 100L - x; }

        public static void Main()
        {
            int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };

            // ===== 1. int32 source, int64 key (ascending / descending) =====
            int[] byLong = nums.OrderBy(LongKey).ToArray();
            Console.WriteLine("i4_i8_asc=" + (byLong[0] == 9 && byLong[1] == 8 && byLong[2] == 7 &&
                                            byLong[3] == 6 && byLong[4] == 5 && byLong[5] == 4 &&
                                            byLong[6] == 3 && byLong[7] == 2 && byLong[8] == 1));
            int[] byLongDesc = nums.OrderByDescending(LongKey).ToArray();
            Console.WriteLine("i4_i8_desc=" + (byLongDesc[0] == 1 && byLongDesc[1] == 2 && byLongDesc[2] == 3 &&
                                               byLongDesc[3] == 4 && byLongDesc[4] == 5 && byLongDesc[5] == 6 &&
                                               byLongDesc[6] == 7 && byLongDesc[7] == 8 && byLongDesc[8] == 9));

            // ===== 2. int32 source, double key =====
            int[] byDouble = nums.OrderBy(DoubleKey).ToArray();
            Console.WriteLine("i4_d_asc=" + (byDouble[0] == 9 && byDouble[8] == 1));
            int[] byDoubleDesc = nums.OrderByDescending(DoubleKey).ToArray();
            Console.WriteLine("i4_d_desc=" + (byDoubleDesc[0] == 1 && byDoubleDesc[8] == 9));

            // ===== 3. stability under equal int64 keys =====
            // value = key*10 + source-rank; key = x/10. Equal-key groups keep
            // their source order (net48 OrderBy is stable).
            int[] marked = { 51, 32, 53, 11, 54, 33, 12, 31, 52 };
            int[] stable = marked.OrderBy(TensKey).ToArray();
            Console.WriteLine("i4_i8_stable=" + (stable[0] == 11 && stable[1] == 12 && stable[2] == 32 &&
                                               stable[3] == 33 && stable[4] == 31 && stable[5] == 51 &&
                                               stable[6] == 53 && stable[7] == 54 && stable[8] == 52));

            // ===== 4. key-cache-once (each key computed EXACTLY ONCE) =====
            _calls = 0;
            int[] once = nums.OrderBy(CountingLongKey).ToArray();
            Console.WriteLine("i4_i8_cache=" + (_calls == nums.Length));
            Console.WriteLine("i4_i8_cache_len=" + (once.Length == 9));

            // ===== 5. copy semantics (source never written, fresh result) =====
            int[] src = { 8, 3, 6, 1, 9 };
            int[] copy = src.OrderBy(LongKey).ToArray();
            src[0] = 999;
            src[4] = -999;
            Console.WriteLine("i4_i8_copy_fresh=" + (copy[0] == 9 && copy[4] == 1));
            Console.WriteLine("i4_i8_copy_src_dirty=" + (src[0] == 999 && src[4] == -999));

            // ===== 6. ThenBy: fused (k1 asc, k2 asc / desc) composite =====
            int[] grp = { 53, 54, 51, 12, 11 };
            int[] tbg = grp.OrderBy(TensKey).ThenBy(UnitsKey).ToArray();
            Console.WriteLine("i4_i8_thenby=" + (tbg[0] == 11 && tbg[1] == 12 && tbg[2] == 51 &&
                                              tbg[3] == 53 && tbg[4] == 54));
            int[] tbd = grp.OrderBy(TensKey).ThenByDescending(UnitsKey).ToArray();
            Console.WriteLine("i4_i8_thenbydesc=" + (tbd[0] == 12 && tbd[1] == 11 && tbd[2] == 54 &&
                                                 tbd[3] == 53 && tbd[4] == 51));

            // ===== 7. int64 source, double key =====
            long[] lnums = { 5L, 3L, 9L, 1L, 7L, 2L, 8L, 4L, 6L };
            long[] lbyDouble = lnums.OrderBy(DoubleKeyLong).ToArray();
            Console.WriteLine("i8_d_asc=" + (lbyDouble[0] == 9L && lbyDouble[8] == 1L));
            long[] lbyDoubleDesc = lnums.OrderByDescending(DoubleKeyLong).ToArray();
            Console.WriteLine("i8_d_desc=" + (lbyDoubleDesc[0] == 1L && lbyDoubleDesc[8] == 9L));

            // ===== 8. int64 source, int64 key (existing family, regression) =====
            long[] lbyLong = lnums.OrderBy(LongKeyLong).ToArray();
            Console.WriteLine("i8_i8_asc=" + (lbyLong[0] == 9L && lbyLong[8] == 1L));

            // ===== 9. edge inputs =====
            int[] empty = new int[0];
            Console.WriteLine("edge_empty=" + (empty.OrderBy(LongKey).Count() == 0));
            int[] single = { 42 };
            Console.WriteLine("edge_single=" + (single.OrderBy(LongKey).First() == 42));
            int[] allEq = { 7, 7, 7, 7 };
            Console.WriteLine("edge_alleq=" + (allEq.OrderBy(LongKey).Count() == 4));

            Console.WriteLine("done");
        }
    }
}
