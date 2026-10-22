// LOUD P0-18: string Min/Max must be rejected at TRANSPILE time.
//
// net48's Enumerable.Min(this IEnumerable<string>) / Max use
// Comparer<string>.Default, i.e. CULTURE-AWARE collation:
//
//   net48 oracle (this machine, zh-CN and invariant):
//     new[]{"B","a"}.Min()        -> "a"      (ordinal would answer "B")
//     new[]{"B","a"}.Max()        -> "B"      (ordinal would answer "a")
//     new[]{"b","A","c","a"}.Min()-> "a"
//     new[]{"b","A","c","a"}.Max()-> "c"
//     "a".CompareTo("B")          -> -1       (culture)
//     string.CompareOrdinal("a","B") -> +31   (ordinal)
//
// This runtime has no collation infrastructure (stringext2.cpp documents that
// the culture modes degrade to an ASCII case fold), so an ordinal kernel would
// SILENTLY MISORDER mixed-case input -- a wrong value, not a documented
// deviation. The contract is therefore a transpile-time NotSupportedException
// (the loud-p0-14-strkey-loud precedent).
//
// This case is EXPECTED TO FAIL TO TRANSPILE; the harness records the LOUD
// message as the evidence. Do NOT "fix" it by swapping in ordinal comparison.
using System;
using System.Linq;

namespace Cases
{
    public static class LoudP018StrMinLoud
    {
        public static void Main()
        {
            string[] ss = { "B", "a" };
            Console.WriteLine("[strmin]=" + ss.Min());

            string[] words = { "b", "A", "c", "a" };
            Console.WriteLine("[strmax]=" + words.Max());
        }
    }
}
