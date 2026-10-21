// LOUD P0-14: a STRING OrderBy key must be rejected at TRANSPILE time.
//
// net48's Comparer<string>.Default is CULTURE-aware: it sorts "a" before "A"
// (lowercase first) and applies OS-NLS collation weights. This runtime has no
// collation infrastructure -- stringext2.cpp documents that the culture modes
// degrade to an ASCII case fold, and string.CompareTo is ordinal. Sorting by an
// ordinal comparison would therefore SILENTLY MISORDER mixed-case input, which
// is a wrong value, not a documented deviation.
//
// The contract: `Enumerable.OrderBy` / `OrderByDescending` with a
// Func<TSource,string> key selector is rejected with NotSupportedException at
// transpile time. This case is EXPECTED TO FAIL TO TRANSPILE; the harness
// records the LOUD message as the evidence.
//
// Do NOT "fix" this case by removing it: it is the regression lock for the
// silent-misorder defect found by the B5 audit (see docs/audit/b5-gaps.md).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public sealed class StringKeys
    {
        public string Self(string s) { return s; }
    }

    public static class LoudP014StrKeyLoud
    {
        public static void Main()
        {
            StringKeys k = new StringKeys();
            Func<string, string> self = k.Self;

            // Reference-array source with a string key.
            string[] words = { "b", "A", "c", "a" };
            Console.WriteLine("[strkey-arr]=" + string.Join(",", words.OrderBy(self).ToArray()));

            // List<string> source with a string key.
            List<string> list = new List<string> { "b", "A", "c", "a" };
            Console.WriteLine("[strkey-list]=" + string.Join(",", list.OrderBy(self).ToArray()));

            // Descending variant.
            Console.WriteLine("[strkey-desc]=" + string.Join(",", words.OrderByDescending(self).ToArray()));
        }
    }
}
