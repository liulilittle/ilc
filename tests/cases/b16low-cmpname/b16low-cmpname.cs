//------------------------------------------------------------------------------
// b16low-cmpname.cs -- nb16 audit LOW: the runtime identified the DEFAULT
// comparer singletons by a SUBSTRING probe on klass->name
// (std::strstr(name, "System.Collections.Generic.Comparer`1") in
// arraysort.cpp / sortedlist.cpp). A USER comparer whose full type name ends
// with that text was silently treated as the BCL default singleton: its
// Compare was never called and the builtin ascending order was substituted.
//
// The probe types below are ordinary user classes whose reflection full names
// happen to end with the BCL prefix:
//   My.System.Collections.Generic.Comparer`1          (Comparer arm)
//   MyLib.System.Collections.Generic.EqualityComparer`1 (EqualityComparer arm)
//   MySystem.Collections.Generic.Comparer`1           (substring, no dot)
// Each is generic arity 1 (that is what makes the metadata name carry the
// "`1" the probe looked for) and implements IComparer<int> with a strictly
// DESCENDING order. On the pre-fix runtime every [user_*] record comes back in
// DEFAULT (ascending) order -- i.e. the user comparer was dropped.
//
// The [ctrl_user_sort] record uses a normally-named user comparer and passes
// on the pre-fix runtime: the defect is the name probe, not the dispatch.
// The [def_*] records pin the real default singletons (int/uint/long/double/
// string/object, non-generic Comparer.Default, Comparer<T>.Create,
// EqualityComparer<T>.Default, BinarySearch) so the pointer-identity fix
// cannot regress them.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace My.System.Collections.Generic
{
    public sealed class Comparer<T> : IComparer<int>
    {
        public int Compare(int a, int b) { return b < a ? -1 : (b > a ? 1 : 0); }
    }
}

namespace MyLib.System.Collections.Generic
{
    public sealed class EqualityComparer<T> : IComparer<int>
    {
        public int Compare(int a, int b) { return b < a ? -1 : (b > a ? 1 : 0); }
    }
}

namespace MySystem.Collections.Generic
{
    public sealed class Comparer<T> : IComparer<int>
    {
        public int Compare(int a, int b) { return b < a ? -1 : (b > a ? 1 : 0); }
    }
}

public sealed class PlainRevInt : IComparer<int>
{
    public int Compare(int a, int b) { return b < a ? -1 : (b > a ? 1 : 0); }
}

public static class CmpNameProg
{
    public static void Main()
    {
        // Control: a normally-named user comparer dispatches correctly both
        // before and after the fix.
        {
            int[] a = new int[] { 3, 1, 2 };
            Array.Sort(a, new PlainRevInt());
            Console.WriteLine("[ctrl_user_sort]=" + a[0] + "," + a[1] + "," + a[2]);
        }

        // 1) Array.Sort<T>(T[], IComparer<T>): user Comparer`1-suffixed name.
        {
            int[] a = new int[] { 3, 1, 2 };
            Array.Sort(a, new My.System.Collections.Generic.Comparer<int>());
            Console.WriteLine("[user_cmp_sort]=" + a[0] + "," + a[1] + "," + a[2]);
        }

        // 2) Range sort with the same comparer.
        {
            int[] a = new int[] { 9, 1, 3, 2, 0 };
            Array.Sort(a, 1, 3, new My.System.Collections.Generic.Comparer<int>());
            Console.WriteLine("[user_cmp_range]=" + a[0] + "," + a[1] + "," + a[2] + "," + a[3] + "," + a[4]);
        }

        // 3) BinarySearch<T>(T[], T, IComparer<T>) with a descending array and
        //    the user comparer: a dropped comparer searches the wrong order.
        {
            int[] a = new int[] { 9, 7, 5, 3, 1 };
            Console.WriteLine("[user_cmp_bsearch]=" + Array.BinarySearch(a, 7, new My.System.Collections.Generic.Comparer<int>()));
        }

        // 4) EqualityComparer`1-suffixed name through BinarySearch.
        {
            int[] a = new int[] { 9, 7, 5, 3, 1 };
            Console.WriteLine("[user_eq_bsearch]=" + Array.BinarySearch(a, 7, new MyLib.System.Collections.Generic.EqualityComparer<int>()));
        }

        // 5) SortedSet<int>(IComparer<int>): sortedlist.cpp IsDefaultComparerObject
        //    must not classify the user comparer as the default (enumeration
        //    order is the discriminator).
        {
            SortedSet<int> ss = new SortedSet<int>(new My.System.Collections.Generic.Comparer<int>());
            ss.Add(1); ss.Add(2); ss.Add(3);
            int[] got = new int[3];
            int n = 0;
            foreach (int v in ss) got[n++] = v;
            Console.WriteLine("[user_cmp_sortedset]=" + got[0] + "," + got[1] + "," + got[2]);
        }

        // 6) SortedSet<int> with the EqualityComparer-suffixed user comparer.
        {
            SortedSet<int> ss = new SortedSet<int>(new MyLib.System.Collections.Generic.EqualityComparer<int>());
            ss.Add(1); ss.Add(2); ss.Add(3);
            int[] got = new int[3];
            int n = 0;
            foreach (int v in ss) got[n++] = v;
            Console.WriteLine("[user_eq_sortedset]=" + got[0] + "," + got[1] + "," + got[2]);
        }

        // 7) A name that merely CONTAINS the prefix ("MySystem..." has "System"
        //    at offset 2) -- the substring form of the old probe.
        {
            int[] a = new int[] { 3, 1, 2 };
            Array.Sort(a, new MySystem.Collections.Generic.Comparer<int>());
            Console.WriteLine("[user_cmp_substr]=" + a[0] + "," + a[1] + "," + a[2]);
        }

        //========== default-singleton regressions ==========
        {
            int[] a = new int[] { 3, 1, 2 };
            Array.Sort(a, Comparer<int>.Default);
            Console.WriteLine("[def_int]=" + a[0] + "," + a[1] + "," + a[2]);
        }
        {
            uint[] a = new uint[] { 4000000000u, 1u, 3000000000u };
            Array.Sort(a, Comparer<uint>.Default);
            Console.WriteLine("[def_uint]=" + a[0] + "," + a[1] + "," + a[2]);
        }
        {
            long[] a = new long[] { 4000000000L, 1L, 3000000000L };
            Array.Sort(a, Comparer<long>.Default);
            Console.WriteLine("[def_long]=" + a[0] + "," + a[1] + "," + a[2]);
        }
        {
            double[] a = new double[] { 3.5, 1.5, 2.5 };
            Array.Sort(a, Comparer<double>.Default);
            Console.WriteLine("[def_double]=" + a[0] + "," + a[1] + "," + a[2]);
        }
        {
            string[] a = new string[] { "c", "a", "b" };
            Array.Sort(a, Comparer<string>.Default);
            Console.WriteLine("[def_string]=" + a[0] + "," + a[1] + "," + a[2]);
        }
        {
            object[] a = new object[] { "c", "a", "b" };
            Array.Sort(a, Comparer<object>.Default);
            Console.WriteLine("[def_object]=" + a[0] + "," + a[1] + "," + a[2]);
        }
        {
            object[] a = new object[] { "c", "a", "b" };
            Array.Sort((Array)a, System.Collections.Comparer.Default);
            Console.WriteLine("[non_generic_cmp]=" + a[0] + "," + a[1] + "," + a[2]);
        }
        {
            int[] a = new int[] { 3, 1, 2 };
            Array.Sort(a, Comparer<int>.Create((x, y) => y.CompareTo(x)));
            Console.WriteLine("[cmp_create]=" + a[0] + "," + a[1] + "," + a[2]);
        }
        {
            int[] a = new int[] { 1, 3, 5, 7, 9 };
            Console.WriteLine("[def_int_bsearch]=" + Array.BinarySearch(a, 5, Comparer<int>.Default));
        }
        {
            Console.WriteLine("[eq_default]=" + EqualityComparer<int>.Default.Equals(4, 4) + "," +
                              EqualityComparer<int>.Default.Equals(4, 5));
        }
    }
}
