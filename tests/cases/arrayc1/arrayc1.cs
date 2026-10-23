using System;
using System.Collections.Generic;

// arrayc1 -- Array static family: Find/FindAll/FindIndex/FindLast/FindLastIndex/
// Exists/TrueForAll/ForEach, Resize (grow+shrink), Sort overload matrix,
// ConvertAll<int,string>, BinarySearch complement, Reverse/Clear/Copy/
// ConstrainedCopy/IndexOf/LastIndexOf, Empty.
class Prog
{
    static void Main()
    {
        int[] a = new int[] { 5, 3, 9, 1, 7 };
        Console.WriteLine("find=" + Array.Find(a, x => x > 4));
        Console.WriteLine("findlast=" + Array.FindLast(a, x => x > 4));
        Console.WriteLine("findidx=" + Array.FindIndex(a, x => x == 9));
        Console.WriteLine("findlastidx=" + Array.FindLastIndex(a, x => x == 7));
        Console.WriteLine("exists=" + Array.Exists(a, x => x == 3));
        Console.WriteLine("trueforall=" + Array.TrueForAll(a, x => x > 0));
        int sum = 0;
        Array.ForEach(a, x => sum += x);
        Console.WriteLine("foreach=" + sum);
        int[] fa = Array.FindAll(a, x => x > 3);
        Console.WriteLine("findall_len=" + fa.Length + " first=" + fa[0]);

        int[] r = new int[] { 1, 2, 3 };
        Array.Resize(ref r, 5);
        Console.WriteLine("resize5=" + r.Length + " " + r[0] + r[2] + r[4]);
        Array.Resize(ref r, 2);
        Console.WriteLine("resize2=" + r.Length + " " + r[1]);

        int[] s = new int[] { 5, 3, 9, 1 };
        Array.Sort(s);
        Console.WriteLine("sort=" + s[0] + s[1] + s[2] + s[3]);
        Array.Sort(s, (x, y) => y.CompareTo(x));
        Console.WriteLine("sort_cmp=" + s[0] + s[1] + s[2] + s[3]);
        int[] s2 = new int[] { 4, 2, 8, 6 };
        Array.Sort(s2, 1, 2);
        Console.WriteLine("sort_range=" + s2[0] + s2[1] + s2[2] + s2[3]);
        string[] ss = new string[] { "bb", "aa", "cc" };
        Array.Sort(ss);
        Console.WriteLine("sort_str=" + ss[0] + ss[1] + ss[2]);
        int[] keys = new int[] { 2, 1 };
        string[] items = new string[] { "two", "one" };
        Array.Sort(keys, items);
        Console.WriteLine("sort_kk=" + items[0] + items[1]);

        int[] bs = new int[] { 1, 3, 5, 7 };
        Console.WriteLine("bin3=" + Array.BinarySearch(bs, 3));
        Console.WriteLine("bin4=" + Array.BinarySearch(bs, 4));
        Console.WriteLine("bin9=" + Array.BinarySearch(bs, 9));

        string[] conv = Array.ConvertAll<int, string>(new int[] { 1, 2, 3 }, x => "v" + x);
        Console.WriteLine("conv_len=" + conv.Length + " " + conv[0] + conv[2]);
        int[] conv2 = Array.ConvertAll<string, int>(new string[] { "1", "2" }, int.Parse);
        Console.WriteLine("conv2=" + (conv2[0] + conv2[1]));

        int[] rv = new int[] { 1, 2, 3, 4 };
        Array.Reverse(rv);
        Console.WriteLine("rev=" + rv[0] + rv[1] + rv[2] + rv[3]);
        Array.Reverse(rv, 0, 2);
        Console.WriteLine("rev_range=" + rv[0] + rv[1] + rv[2] + rv[3]);

        int[] cl = new int[] { 1, 2, 3 };
        Array.Clear(cl, 0, 2);
        Console.WriteLine("clear=" + cl[0] + cl[1] + cl[2]);

        int[] src = new int[] { 7, 8, 9 };
        int[] dst = new int[5];
        Array.Copy(src, dst, 3);
        Console.WriteLine("copy=" + dst[0] + dst[1] + dst[2] + dst[3]);
        int[] dst2 = new int[5];
        Array.ConstrainedCopy(src, 1, dst2, 2, 2);
        Console.WriteLine("ccopy=" + dst2[0] + dst2[2] + dst2[3]);
        Console.WriteLine("idx=" + Array.IndexOf(src, 8));
        Console.WriteLine("lastidx=" + Array.LastIndexOf(new int[] { 1, 2, 1 }, 1));
        Console.WriteLine("empty=" + Array.Empty<int>().Length);
    }
}
