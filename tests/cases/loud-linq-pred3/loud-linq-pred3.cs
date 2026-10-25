//------------------------------------------------------------------------------
// loud-linq-pred3.cs -- e2e reference case for the BATCH-3 LINQ wiring:
//
//   * the REFERENCE-source (element, index) overloads Where(Func<T,int,bool>)
//     and Select(Func<T,int,TResult>) -- il2cpp_linq_where_idx_ref /
//     il2cpp_linq_select_idx_ref_ref (the reference counterparts of the int32
//     index kernels);
//   * Single / SingleOrDefault WITH a predicate (Func<T,bool>) over the int32
//     and reference element families -- il2cpp_linq_single[_or_default]_pred_i4
//     / _ref.
//
// Before this batch each shape LOUD-rejected (transpile-time NotSupportedException
// / no runtime kernel). They now resolve to real kernels and must match the
// net48 oracle byte-for-byte.
//
// net48 Single(predicate) semantics (the predicate runs on EVERY element):
//   * Single          -- 0 or >1 matches -> InvalidOperationException; 1 -> it
//   * SingleOrDefault -- 0 -> default(T); >1 -> InvalidOperationException
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqPred3
    {
        public static void Main()
        {
            // ---- int32 (element, index) Where ----
            int[] nums = { 10, 11, 12, 13, 14 };
            int[] widx = nums.Where((x, i) => i % 2 == 0).ToArray();
            Console.WriteLine("i4_where_idx=" + (widx.Length == 3 && widx[0] == 10 && widx[1] == 12 && widx[2] == 14));

            // ---- int32 Single(predicate) / SingleOrDefault(predicate) ----
            int[] single = { 1, 2, 3 };
            Console.WriteLine("i4_single_pred=" + (single.Single(x => x == 2) == 2));
            Console.WriteLine("i4_sod_pred_none=" + (single.SingleOrDefault(x => x == 9) == 0));

            bool spMany = false;
            try { single.Single(x => x > 0); }
            catch (InvalidOperationException) { spMany = true; }
            Console.WriteLine("i4_single_pred_many_throw=" + spMany);

            bool sodMany = false;
            try { single.SingleOrDefault(x => x > 0); }
            catch (InvalidOperationException) { sodMany = true; }
            Console.WriteLine("i4_sod_pred_many_throw=" + sodMany);

            bool spNone = false;
            try { single.Single(x => x == 9); }
            catch (InvalidOperationException) { spNone = true; }
            Console.WriteLine("i4_single_pred_none_throw=" + spNone);

            // ---- reference (element, index) Where ----
            string[] words = { "aa", "bb", "cc", "dd", "ee" };
            string[] widxRef = words.Where((w, i) => i < 2).ToArray();
            Console.WriteLine("ref_where_idx=" + (widxRef.Length == 2 && widxRef[0] == "aa" && widxRef[1] == "bb"));

            // ---- reference (element, index) Select returning a reference ----
            string[] selRef = words.Select((w, i) => i < 2 ? w : "tail").ToArray();
            Console.WriteLine("ref_select_idx=" + (selRef.Length == 5 && selRef[0] == "aa" && selRef[1] == "bb"
                && selRef[2] == "tail" && selRef[4] == "tail"));

            // ---- reference Single(predicate) / SingleOrDefault(predicate) ----
            string[] one = { "a", "bbbb", "cc" };
            Console.WriteLine("ref_single_pred=" + (one.Single(s => s.Length == 4) == "bbbb"));
            Console.WriteLine("ref_sod_pred_none=" + (one.SingleOrDefault(s => s.Length == 5) == null));

            string[] two = { "bbbb", "cccc" };
            bool rspMany = false;
            try { two.Single(s => s.Length == 4); }
            catch (InvalidOperationException) { rspMany = true; }
            Console.WriteLine("ref_single_pred_many_throw=" + rspMany);
        }
    }
}
