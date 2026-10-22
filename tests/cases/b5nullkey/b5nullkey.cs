using System;
using System.Linq;

// B5-R6: a NULL reference key must still order correctly.
//
// The runtime rejects a System.String key (no collation -- see
// docs/audit/b5-gaps.md section 2), and an `object` key can BE a string at run
// time. The rejection must NOT fire for null keys: net48 orders a null key
// BEFORE every non-null key without consulting the comparer at all, so
// object[] { null, "a" }.OrderBy(x => x) is answerable and must return
// { null, "a" }. CompareRefKeys therefore checks null first.
//
// Assertions are element-level (not string.Join) on purpose: the JOIN of an
// object[] carries its own pre-existing type-tracking precision issue
// (docs/audit/b5-gaps.md section 1), unrelated to key ordering.
namespace Cases
{
    public static class B5NullKey
    {
        public static void Main()
        {
            object[] withNullFirst = { null, "a" };
            object[] s1 = withNullFirst.OrderBy(x => x).ToArray();
            Console.WriteLine("[s1-len]=" + s1.Length);
            Console.WriteLine("[s1-0-null]=" + (s1[0] == null));
            Console.WriteLine("[s1-1]=" + s1[1]);

            object[] withNullLast = { "a", null };
            object[] s2 = withNullLast.OrderBy(x => x).ToArray();
            Console.WriteLine("[s2-0-null]=" + (s2[0] == null));
            Console.WriteLine("[s2-1]=" + s2[1]);

            // Descending: net48 puts the null key LAST.
            object[] s3 = withNullFirst.OrderByDescending(x => x).ToArray();
            Console.WriteLine("[s3-0]=" + s3[0]);
            Console.WriteLine("[s3-1-null]=" + (s3[1] == null));

            // Two null keys compare equal and keep source order (stable).
            object[] twoNulls = { null, null };
            object[] s4 = twoNulls.OrderBy(x => x).ToArray();
            Console.WriteLine("[s4-nulls]=" + ((s4[0] == null) && (s4[1] == null)));

            // A null key mixed with a NON-string IComparable key still orders by
            // IComparable (the string rejection must not fire here either).
            object[] mixed = { null, 5 };
            object[] s5 = mixed.OrderBy(x => x).ToArray();
            Console.WriteLine("[s5-0-null]=" + (s5[0] == null));
            Console.WriteLine("[s5-1]=" + s5[1]);
        }
    }
}
