using System;
using System.Collections.Generic;
using System.Text;

//------------------------------------------------------------------------------
// zz-b4-usercmp -- nb9 H-B3: Array.Sort<T>(T[], IComparer<T>) must dispatch the
// user IComparer<T>.Compare(T,T) slot by element family, not call the object as
// a Comparison<T> delegate (the pre-fix crash: 0xC0000005).
//
// Written without a generic helper (an earlier Join<T> version tripped the
// unrelated generic-method-body gap and masked this result).
//------------------------------------------------------------------------------
namespace Cases
{
    public sealed class RevCmp : IComparer<int>
    {
        public int Compare(int a, int b) { return b.CompareTo(a); }
    }

    public static class ZZB4Usercmp
    {
        public static void Main()
        {
            var arr = new[] { 1, 3, 2, 5, 4 };
            Array.Sort(arr, new RevCmp());
            var sb = new StringBuilder();
            for (int i = 0; i < arr.Length; ++i)
            {
                if (i > 0) sb.Append(",");
                sb.Append(arr[i]);
            }
            Console.WriteLine("[array_user_cmp]=" + sb.ToString());
        }
    }
}
