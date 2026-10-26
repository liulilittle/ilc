//------------------------------------------------------------------------------
// arrgen2 -- residual Array/Buffer/ArrayList LOUD closure probe.
//
// Covers the compile-time-analyzable shapes the arrgen1b batch left LOUD:
//   * Array.Copy long-index overloads
//   * ArrayList(ICollection)
//   * reference-element generic instantiations (SZARRAY element family)
//   * IntPtr/UIntPtr 8-byte value cells
//   * DateTime/TimeSpan tick-cell scans/sorts
//   * narrow-integral Sort/IndexOf/LastIndexOf/BinarySearch (+ comparer forms)
//   * narrow ConvertAll (kind-parameterized dynamic kernel)
//   * multi-dimensional GetValue/SetValue (int/long/params forms)
//   * Buffer.MemoryCopy ulong
// net48 is the byte oracle.
//------------------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;

class ByteCmp : IComparer<byte>
{
    public int Compare(byte x, byte y) { return x - y; }
}

class Prog
{
    static int Main()
    {
        // ---- Array.Copy long overloads ----
        int[] cs = { 1, 2, 3, 4, 5 };
        int[] cd = new int[5];
        Array.Copy(cs, cd, 3L);
        Console.WriteLine("copy3long=" + cd[0] + "," + cd[2] + "," + cd[3]);
        int[] cd2 = new int[5];
        Array.Copy(cs, 1L, cd2, 2L, 2L);
        Console.WriteLine("copy5long=" + cd2[2] + "," + cd2[3] + "," + cd2[0]);
        bool copyHuge = false;
        try { Array.Copy(cs, cd, (long)int.MaxValue + 1); }
        catch (ArgumentOutOfRangeException) { copyHuge = true; }
        Console.WriteLine("copy_huge=" + copyHuge);

        // ---- ArrayList(ICollection) ----
        ArrayList al1 = new ArrayList(new int[] { 7, 8, 9 });
        Console.WriteLine("al_arr=" + al1.Count + "," + al1.Capacity + "," + (int)al1[1]);
        List<int> srcList = new List<int>();
        srcList.Add(4); srcList.Add(5);
        ArrayList al2 = new ArrayList(srcList);
        Console.WriteLine("al_list=" + al2.Count + "," + al2.Capacity + "," + (int)al2[0]);
        ArrayList al3 = new ArrayList(new int[0]);
        Console.WriteLine("al_empty=" + al3.Count + "," + al3.Capacity);
        bool alNull = false;
        try { new ArrayList((ICollection)null); }
        catch (ArgumentNullException) { alNull = true; }
        Console.WriteLine("al_null=" + alNull);

        // ---- reference-element generic instantiations (0x1D SZARRAY) ----
        int[][] rows = { new int[] { 1 }, new int[] { 2, 3 }, new int[] { 4, 5, 6 } };
        Console.WriteLine("ref_find=" + Array.Find(rows, r => r.Length == 2)[1]);
        Console.WriteLine("ref_exists=" + Array.Exists(rows, r => r.Length == 3));
        Console.WriteLine("ref_findindex=" + Array.FindIndex(rows, r => r.Length == 3));
        int[][] found = Array.FindAll(rows, r => r.Length >= 2);
        Console.WriteLine("ref_findall=" + found.Length + "," + found[0][0]);
        Array.Resize(ref rows, 5);
        Console.WriteLine("ref_resize=" + rows.Length + "," + (rows[4] == null));
        string[] names = { "bb", "a", "ccc" };
        Array.Sort(names, (x, y) => x.Length - y.Length);
        Console.WriteLine("ref_sort=" + names[0] + names[1] + names[2]);
        Console.WriteLine("ref_indexof=" + Array.IndexOf(rows, found[0]));

        // ---- IntPtr / UIntPtr ----
        // net48 IntPtr/UIntPtr do NOT implement IComparable: Sort and
        // BinarySearch raise InvalidOperationException, while IndexOf uses
        // EqualityComparer<T>.Default (bit equality) and works.
        IntPtr[] ips = { (IntPtr)5, (IntPtr)1, (IntPtr)9, (IntPtr)3 };
        bool ipSort = false;
        try { Array.Sort(ips); } catch (InvalidOperationException) { ipSort = true; }
        Console.WriteLine("ip_sort_ex=" + ipSort);
        Console.WriteLine("ip_index=" + Array.IndexOf(ips, (IntPtr)9));
        bool ipBs = false;
        try { Array.BinarySearch(ips, (IntPtr)5); } catch (InvalidOperationException) { ipBs = true; }
        Console.WriteLine("ip_bsearch_ex=" + ipBs);
        Console.WriteLine("ip_bs_empty=" + Array.BinarySearch(Array.Empty<IntPtr>(), (IntPtr)5));
        Console.WriteLine("ip_empty=" + Array.Empty<IntPtr>().Length);
        Array.Reverse(ips);
        Console.WriteLine("ip_rev=" + (long)ips[0]);
        Array.Resize(ref ips, 6);
        Console.WriteLine("ip_resize=" + ips.Length + "," + (long)ips[5]);
        UIntPtr[] ups = { (UIntPtr)5, (UIntPtr)1, (UIntPtr)9 };
        bool upSort = false;
        try { Array.Sort(ups); } catch (InvalidOperationException) { upSort = true; }
        Console.WriteLine("up_sort_ex=" + upSort);
        bool upBs = false;
        try { Array.BinarySearch(ups, (UIntPtr)9); } catch (InvalidOperationException) { upBs = true; }
        Console.WriteLine("up_bsearch_ex=" + upBs);

        // ---- DateTime / TimeSpan ----
        DateTime d0 = new DateTime(2001, 1, 1);
        DateTime d1 = new DateTime(2002, 1, 1);
        DateTime d2 = new DateTime(2003, 1, 1);
        DateTime[] ds = { d2, d0, d1 };
        Array.Sort(ds);
        Console.WriteLine("dt_sort=" + ds[0].Year + "," + ds[2].Year);
        Console.WriteLine("dt_index=" + Array.IndexOf(ds, d1));
        Console.WriteLine("dt_index3=" + Array.IndexOf(ds, d1, 1));
        Console.WriteLine("dt_index4=" + Array.IndexOf(ds, d1, 0, 3));
        Console.WriteLine("dt_last=" + Array.LastIndexOf(ds, d1));
        Console.WriteLine("dt_last3=" + Array.LastIndexOf(ds, d1, 2));
        Console.WriteLine("dt_last4=" + Array.LastIndexOf(ds, d1, 2, 3));
        Console.WriteLine("dt_bs=" + Array.BinarySearch(ds, d1));
        Console.WriteLine("dt_bs4=" + Array.BinarySearch(ds, 0, 3, d1));
        Console.WriteLine("dt_empty=" + Array.Empty<DateTime>().Length);
        Array.Reverse(ds);
        Console.WriteLine("dt_rev=" + ds[0].Year);
        Array.Resize(ref ds, 4);
        Console.WriteLine("dt_resize=" + ds.Length + "," + ds[3].Year);
        TimeSpan[] ts = { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1) };
        Array.Sort(ts);
        Console.WriteLine("ts_sort=" + ts[0].Seconds);
        Console.WriteLine("ts_index=" + Array.IndexOf(ts, TimeSpan.FromSeconds(3)));

        // ---- narrow-integral sort/scan ----
        byte[] by = { 3, 1, 2 };
        byte b2 = 2;
        byte b1 = 1;
        Array.Sort(by);
        Console.WriteLine("by_sort=" + by[0] + by[1] + by[2]);
        Console.WriteLine("by_index=" + Array.IndexOf(by, b2));
        Console.WriteLine("by_last=" + Array.LastIndexOf(by, b2));
        Console.WriteLine("by_bs=" + Array.BinarySearch(by, b2));
        sbyte[] sb = { -1, 2, -3 };
        Array.Sort(sb);
        Console.WriteLine("sb_sort=" + sb[0] + "," + sb[2]);
        short[] sh = { 300, -5, 12 };
        Array.Sort(sh);
        Console.WriteLine("sh_sort=" + sh[0] + "," + sh[2]);
        ushort[] us = { 300, 5, 12 };
        Array.Sort(us);
        Console.WriteLine("us_sort=" + us[0] + "," + us[2]);
        char[] ch = { 'c', 'a', 'b' };
        Array.Sort(ch);
        Console.WriteLine("ch_sort=" + ch[0] + ch[1] + ch[2]);
        bool[] bo = { true, false, true };
        Array.Sort(bo);
        Console.WriteLine("bo_sort=" + bo[0] + "," + bo[2]);
        byte[] byc = { 3, 1, 2 };
        Array.Sort(byc, (x, y) => y - x);
        Console.WriteLine("by_cmp=" + byc[0] + byc[1] + byc[2]);
        Array.Sort(byc, Comparer<byte>.Create((x, y) => x - y));
        Console.WriteLine("by_icmp=" + byc[0] + byc[1] + byc[2]);
        Array.Sort<byte>(by, 1, 2);
        Console.WriteLine("by_range=" + by[0] + by[1] + by[2]);
        Console.WriteLine("by_bs4=" + Array.BinarySearch<byte>(by, 0, 3, b1));
        Console.WriteLine("by_bs3=" + Array.BinarySearch<byte>(by, b2, new ByteCmp()));
        Console.WriteLine("by_index3=" + Array.IndexOf<byte>(by, b1, 1));
        Console.WriteLine("by_index4=" + Array.IndexOf<byte>(by, b1, 0, 3));

        // ---- narrow ConvertAll ----
        byte[] csrc = { 1, 2, 3 };
        int[] ctoi = Array.ConvertAll(csrc, x => (int)x * 10);
        Console.WriteLine("ca_u1_i4=" + ctoi[0] + "," + ctoi[2]);
        byte[] ctob = Array.ConvertAll(ctoi, x => (byte)(x / 10));
        Console.WriteLine("ca_i4_u1=" + ctob[0] + "," + ctob[2]);
        sbyte[] csb = { -1, 2 };
        sbyte[] csb2 = Array.ConvertAll(csb, x => (sbyte)(x * 2));
        Console.WriteLine("ca_i1_i1=" + csb2[0] + "," + csb2[1]);

        // ---- multi-dimensional GetValue/SetValue ----
        int[,] m2 = new int[2, 3];
        m2[0, 1] = 11;
        m2[1, 2] = 22;
        Array ma = m2;
        Console.WriteLine("md_gv2=" + (int)ma.GetValue(0, 1) + "," + (int)ma.GetValue(1, 2));
        ma.SetValue(33, 1, 0);
        Console.WriteLine("md_sv2=" + (int)ma.GetValue(1, 0));
        Console.WriteLine("md_gv2l=" + (int)ma.GetValue(0L, 1L));
        ma.SetValue(44, 0L, 2L);
        Console.WriteLine("md_sv2l=" + (int)ma.GetValue(0, 2));
        Console.WriteLine("md_gvi=" + (int)ma.GetValue(new int[] { 0, 1 }));
        ma.SetValue(55, new int[] { 1, 1 });
        Console.WriteLine("md_svi=" + (int)ma.GetValue(1, 1));
        Console.WriteLine("md_gvil=" + (int)ma.GetValue(new long[] { 1, 2 }));
        ma.SetValue(66, new long[] { 0, 0 });
        Console.WriteLine("md_svil=" + (int)ma.GetValue(0, 0));
        int[,,] m3 = new int[2, 2, 2];
        Array ma3 = m3;
        ma3.SetValue(7, 0, 1, 1);
        Console.WriteLine("md3=" + (int)ma3.GetValue(0, 1, 1));
        Console.WriteLine("md3l=" + (int)ma3.GetValue(0L, 1L, 1L));
        bool mdRank = false;
        try { ma.GetValue(0, 0, 0); }
        catch (ArgumentException) { mdRank = true; }
        Console.WriteLine("md_rank=" + mdRank);
        bool mdOob = false;
        try { ma.GetValue(2, 0); }
        catch (IndexOutOfRangeException) { mdOob = true; }
        Console.WriteLine("md_oob=" + mdOob);

        Console.WriteLine("done");
        return 0;
    }
}
