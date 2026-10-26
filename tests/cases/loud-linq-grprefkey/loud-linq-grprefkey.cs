//------------------------------------------------------------------------------
// loud-linq-grprefkey.cs -- e2e reference case for Enumerable.GroupBy with a
// REFERENCE key (net48 GroupBy<TSource,TKey> with TKey = System.String) over a
// decimal / int32 / reference source.
//
// The bucket key rides the bucket's keyRef slot (a GC reference) and the
// default comparer is value equality (icall_object_equals -- string.Equals for
// a string key). Before this change the shape LOUD-rejected ("a reference
// (string) key has no runtime key carrier yet").
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqGrpRefKey
    {
        public static void Main()
        {
            decimal[] a = { 1m, 2m, 3m, 4m };
            var g = a.GroupBy(x => x.ToString(CultureInfo.InvariantCulture));
            foreach (var grp in g) Console.WriteLine("dec str key=" + grp.Key + " n=" + grp.Count());

            int[] b = { 1, 2, 3, 4 };
            var g2 = b.GroupBy(x => x.ToString(CultureInfo.InvariantCulture));
            foreach (var grp in g2) Console.WriteLine("int str key=" + grp.Key + " n=" + grp.Count());

            string[] s = { "aa", "ab", "ba", "bb" };
            var g3 = s.GroupBy(x => x.Substring(0, 1));
            foreach (var grp in g3) Console.WriteLine("str str key=" + grp.Key + " n=" + grp.Count());

            Console.WriteLine("done");
        }
    }
}
