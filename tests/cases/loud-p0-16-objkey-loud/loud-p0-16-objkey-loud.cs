using System;
using System.Collections.Generic;
using System.Linq;

// LOUD P0-16: an `object` (or interface) key whose RUNTIME type is
// System.String must be rejected with NotSupportedException AT RUN TIME.
//
// net48 compares keys with Comparer<TKey>.Default. For a STATIC string key the
// transpiler already rejects the call (no collation in this runtime --
// see loud-p0-14-strkey-loud), but `orderBy<object>(x => x)` has TKey ==
// System.Object and the keys may still BE strings at run time, so only the
// runtime instance check in CompareRefKeys (linq_sort.cpp) can catch it.
//
// Ordinal ordering here is NOT acceptable: net48's Comparer<string>.Default is
// CULTURE-aware and sorts "a" before "A", so an ordinal comparison would
// silently misorder mixed-case input (measured before the fix:
// object[]{"b","A","c","a"}.OrderBy(x => x) returned A,a,b,c instead of
// a,A,b,c).
//
// This case is EXPECTED TO FAIL AT RUN TIME; the harness records the runtime
// stderr (the LOUD message) as the evidence. Do NOT "fix" it by catching the
// exception: the regression lock is the throw itself.
namespace Cases
{
    public static class LoudP016ObjKeyLoud
    {
        public static void Main()
        {
            object[] arr = { "b", "A", "c", "a" };
            object[] sorted = arr.OrderBy(x => x).ToArray();
            Console.WriteLine("[objkey]=" + string.Join(",", sorted));
        }
    }
}
