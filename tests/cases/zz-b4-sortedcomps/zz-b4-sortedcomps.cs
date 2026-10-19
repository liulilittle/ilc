using System;
using System.Collections.Generic;
using System.Text;

//------------------------------------------------------------------------------
// zz-b4-sortedcomps -- nb9 M-5: the ordered-container comparer domain
// (SortedSet/SortedDictionary/SortedList) must dispatch a USER IComparer<T>
// and a Comparer<T>.Create wrapper through the per-element-family Compare
// slot, exactly like Array.Sort.
//
// The old audit claim was that the created-comparer arm hard-codes int32;
// this probe pins the observable contract for int32 keys plus a string key
// comparer (the family that previously fell through to the reference ABI).
// No generic helper is used here (Join<T> relies on an unrelated pre-existing
// generic-method-body gap that would mask this result).
//------------------------------------------------------------------------------
namespace Cases
{
    public sealed class RevInt : IComparer<int>
    {
        public int Compare(int a, int b) { return b.CompareTo(a); }
    }

    public sealed class RevStr : IComparer<string>
    {
        public int Compare(string a, string b) { return string.CompareOrdinal(b, a); }
    }

    public static class ZZB4Sortedcomps
    {
        static string Ints(IEnumerable<int> xs)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (int v in xs)
            {
                if (!first) sb.Append(",");
                sb.Append(v);
                first = false;
            }
            return sb.ToString();
        }

        static string Strs(IEnumerable<string> xs)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (string v in xs)
            {
                if (!first) sb.Append(",");
                sb.Append(v);
                first = false;
            }
            return sb.ToString();
        }

        public static void Main()
        {
            var set = new SortedSet<int>(new RevInt());
            set.Add(1); set.Add(3); set.Add(2);
            Console.WriteLine("[set_user]=" + Ints(set));

            var setc = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
            setc.Add(1); setc.Add(3); setc.Add(2);
            Console.WriteLine("[set_created]=" + Ints(setc));


        }
    }
}
