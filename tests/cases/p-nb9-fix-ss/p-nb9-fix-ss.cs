using System;
using System.Collections.Generic;
using System.Text;

//------------------------------------------------------------------------------
// p-nb9-fix-ss -- nb9 fix batch M4/M18: SortedSet<int> ctor disambiguation,
// generic IEnumerable carriers and a real IComparer<T>.
//
// Before this fix ANY 1-arg GENERICINST ctor argument was routed to the
// int32[] ctor (IComparer<int> and IEnumerable<int> are both 0x15), and the
// runtime rejected every non-int32[] carrier with NotSupportedException.
//
// net48 oracle (host .NET Framework 4.x):
//   [ss_list]=3:1,3,5
//   [ss_queue]=2,4,6
//   [ss_hash]=7,8
//   [ss_cmp]=1,2,3
//   [ss_revcmp]=3,2,1
//   [ss_empty_cmp]=0
//   [ss_arr_cmp]=3,2,1
//------------------------------------------------------------------------------
namespace Cases
{
    // A USER IComparer<int> implementation (not Comparer<int>.Default and not
    // Comparer<int>.Create): its reflected Compare slot is emitted with the
    // real i(i,i) signature, so the runtime must unbox the int32 keys before
    // the call.
    public sealed class RevCmp : IComparer<int>
    {
        public int Compare(int a, int b) { return b.CompareTo(a); }
    }

    public static class PNb9FixSs
    {
        static string Join(IEnumerable<int> items)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (int v in items)
            {
                if (!first) sb.Append(",");
                sb.Append(v);
                first = false;
            }
            return sb.ToString();
        }

        public static void Main()
        {
            var fromList = new SortedSet<int>(new List<int> { 3, 1, 5, 3 });
            Console.WriteLine("[ss_list]=" + fromList.Count + ":" + Join(fromList));

            var q = new Queue<int>();
            q.Enqueue(4);
            q.Enqueue(2);
            q.Enqueue(6);
            var fromQueue = new SortedSet<int>(q);
            Console.WriteLine("[ss_queue]=" + Join(fromQueue));

            var fromHash = new SortedSet<int>(new HashSet<int> { 7, 8 });
            Console.WriteLine("[ss_hash]=" + Join(fromHash));

            var cmp = new SortedSet<int>(Comparer<int>.Default);
            cmp.Add(2);
            cmp.Add(1);
            cmp.Add(3);
            Console.WriteLine("[ss_cmp]=" + Join(cmp));

            var rev = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
            rev.Add(3);
            rev.Add(1);
            rev.Add(2);
            Console.WriteLine("[ss_revcmp]=" + Join(rev));

            var emptyCmp = new SortedSet<int>(Comparer<int>.Default);
            Console.WriteLine("[ss_empty_cmp]=" + emptyCmp.Count);

            // M18: the 2-arg (IEnumerable<int>, IComparer<int>) ctor.
            var arrCmp = new SortedSet<int>(new[] { 1, 3, 2 }, Comparer<int>.Create((a, b) => b.CompareTo(a)));
            Console.WriteLine("[ss_arr_cmp]=" + Join(arrCmp));

            // nb9 fix H3: a USER IComparer<int> (reflected i(i,i) Compare slot).
            var userCmp = new SortedSet<int>(new RevCmp());
            userCmp.Add(2);
            userCmp.Add(1);
            userCmp.Add(3);
            Console.WriteLine("[ss_user_cmp]=" + Join(userCmp));

            var userCmp2 = new SortedSet<int>(new[] { 3, 1, 2 }, new RevCmp());
            Console.WriteLine("[ss_user_cmp2]=" + Join(userCmp2));
        }
    }
}
