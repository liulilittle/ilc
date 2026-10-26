//------------------------------------------------------------------------------
// loud-linq-grpref.cs -- e2e reference case for Enumerable.GroupBy over a
// REFERENCE-element source (net48 GroupBy<TSource,TKey>(source, keySelector)
// and GroupBy<TSource,TKey,TResult>(source, keySelector, resultSelector) with
// TSource = System.String).
//
// The reference element crosses as Il2CppObject* (the key/element selectors
// take the reference), and the bucket stores reference cells (elemKind 1).
// Before this change the shape LOUD-rejected ("the runtime grouping kernel
// reads int32 cells or 16-byte System.Decimal cells only").
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqGrpRef
    {
        public static void Main()
        {
            string[] a = { "apple", "banana", "avocado", "cherry", "apricot" };
            var g = a.GroupBy(x => x.Length);
            foreach (var grp in g)
            {
                Console.WriteLine("key=" + grp.Key + " count=" + grp.Count());
                foreach (var s in grp)
                    Console.WriteLine("  " + s);
            }
            var g2 = a.GroupBy(x => x.Length, (k, items) => k + ":" + items.Count());
            foreach (var s in g2)
                Console.WriteLine("res=" + s);
            Console.WriteLine("done");
        }
    }
}
