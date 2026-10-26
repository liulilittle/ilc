//------------------------------------------------------------------------------
// loud-linq-grpguid.cs -- e2e reference case for Enumerable.GroupBy over a
// System.Guid[] SOURCE (net48 GroupBy<TSource,TKey> / GroupBy<TSource,TKey,
// TElement> with TSource = System.Guid).
//
// The Guid element crosses the key/element selectors BY VALUE (the generated
// Func<Il2CppGuid,TKey> leaf ABI); a key-only overload stores the 16-byte Guid
// cells in the bucket (GroupElemKind::Guid). Before this change the shape
// LOUD-rejected ("over a value-type source is not wired: the decimal-source
// grouping surface covers System.Decimal[] only").
//
// NOTE: iterating a bucket whose TElement is a VALUE TYPE is a pre-existing
// transpiler limitation (the grouping bucket's element enumerator does not
// propagate the TElement carrier); the value-type bucket is therefore exercised
// through Count/Key, and the element-selector form projects to a reference so
// the per-bucket iteration is covered too.
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqGrpGuid
    {
        private static Guid[] Guids()
        {
            Guid g1 = new Guid("11111111-1111-1111-1111-111111111111");
            Guid g2 = new Guid("22222222-2222-2222-2222-222222222222");
            return new Guid[] { g1, g2, g1 };
        }

        private static void RunIntKey()
        {
            var g = Guids().GroupBy(x => x.ToString().Length);
            foreach (var grp in g)
                Console.WriteLine("key=" + grp.Key + " n=" + grp.Count());
        }

        private static void RunGuidKey()
        {
            var g = Guids().GroupBy(x => x);
            foreach (var grp in g)
                Console.WriteLine("gkey=" + grp.Key.ToString() + " n=" + grp.Count());
        }

        private static void RunElementSelector()
        {
            var g = Guids().GroupBy(x => x.ToString().Length, x => x.ToString().Substring(0, 2));
            foreach (var grp in g)
            {
                Console.WriteLine("ekey=" + grp.Key + " n=" + grp.Count());
                foreach (var e in grp)
                    Console.WriteLine("  e=" + e);
            }
        }

        public static void Main()
        {
            RunIntKey();
            RunGuidKey();
            RunElementSelector();
            Console.WriteLine("done");
        }
    }
}
