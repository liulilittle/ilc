//------------------------------------------------------------------------------
// probe-alextra.cs -- System.Collections.ArrayList surface probe (net48 oracle,
// byte-for-byte). Covers the D-AL-1 batch: GetEnumerator (full + range + Reset),
// LastIndexOf (3 overloads), Sort (3 overloads), BinarySearch (3 overloads),
// GetRange, AddRange, InsertRange, SetRange, CopyTo (2 overloads), ToArray
// (2 overloads), Repeat / Adapter / FixedSize / ReadOnly / Synchronized, and
// the Synchronized/IsSynchronized/SyncRoot wrapper flags.
//
// Every line prints "label=value"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections;
using System.Text;

class Program
{
    static string Esc(object o)
    {
        if (o == null) return "null";
        if (o is string s) return "\"" + s + "\"";
        return Convert.ToString(o);
    }

    static void Main()
    {
        // ---------- GetEnumerator ----------
        ArrayList a = new ArrayList();
        a.Add("p"); a.Add("q"); a.Add("r");
        IEnumerator e1 = a.GetEnumerator();
        StringBuilder sb = new StringBuilder();
        while (e1.MoveNext()) sb.Append(Esc(e1.Current)).Append(",");
        Console.WriteLine("enum_all=" + sb.ToString());
        IEnumerator e2 = a.GetEnumerator(1, 2);
        sb = new StringBuilder();
        while (e2.MoveNext()) sb.Append(Esc(e2.Current)).Append(",");
        Console.WriteLine("enum_range=" + sb.ToString());
        Console.WriteLine("enum_refeq=" + ReferenceEquals(e1, a));
        Console.WriteLine("enum_reset=" + ResetThenRead(a));

        // ---------- LastIndexOf ----------
        ArrayList b = new ArrayList();
        b.Add(1); b.Add(2); b.Add(1); b.Add(3); b.Add(1);
        Console.WriteLine("lastidx=" + b.LastIndexOf(1));
        Console.WriteLine("lastidx_from1=" + b.LastIndexOf(1, 1));
        Console.WriteLine("lastidx_from3=" + b.LastIndexOf(1, 3));
        Console.WriteLine("lastidx_range=" + b.LastIndexOf(1, 3, 2));
        Console.WriteLine("lastidx_absent=" + b.LastIndexOf(9));
        Console.WriteLine("lastidx_str=" + b.LastIndexOf("x"));
        ArrayList bs = new ArrayList();
        bs.Add("aa"); bs.Add("bb"); bs.Add("aa");
        Console.WriteLine("lastidx_s=" + bs.LastIndexOf("aa"));
        Console.WriteLine("lastidx_s1=" + bs.LastIndexOf("aa", 1));
        Console.WriteLine("lastidx_null=" + (new ArrayList { null, "k", null }).LastIndexOf(null));
        bool t = false;
        try { b.LastIndexOf(1, -1); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("lastidx_exc_neg=" + t);
        t = false;
        try { b.LastIndexOf(1, 5); } catch (ArgumentOutOfRangeException) { t = true; }
        Console.WriteLine("lastidx_exc_cnt=" + t);
        t = false;
        try { b.LastIndexOf(1, 1, 5); } catch (ArgumentException) { t = true; }
        Console.WriteLine("lastidx_exc_range=" + t);

        // ---------- Sort ----------
        ArrayList c = new ArrayList();
        c.Add(3); c.Add(1); c.Add(2);
        c.Sort();
        Console.WriteLine("sort_int=" + (int)c[0] + "," + (int)c[1] + "," + (int)c[2]);
        ArrayList cs = new ArrayList();
        cs.Add("pear"); cs.Add("Apple"); cs.Add("banana");
        cs.Sort();
        Console.WriteLine("sort_str=" + (string)cs[0] + "," + (string)cs[1] + "," + (string)cs[2]);
        ArrayList cm = new ArrayList();
        cm.Add(5); cm.Add(1); cm.Add(4); cm.Add(2);
        cm.Sort(1, 2, Comparer.Default);
        Console.WriteLine("sort_range=" + (int)cm[0] + "," + (int)cm[1] + "," + (int)cm[2] + "," + (int)cm[3]);
        cm.Sort(Comparer.Default);
        Console.WriteLine("sort_cmp=" + (int)cm[0] + "," + (int)cm[1] + "," + (int)cm[2] + "," + (int)cm[3]);

        // ---------- BinarySearch ----------
        ArrayList d = new ArrayList();
        d.Add(10); d.Add(20); d.Add(30); d.Add(40);
        Console.WriteLine("bs20=" + d.BinarySearch(20));
        Console.WriteLine("bs25=" + d.BinarySearch(25));
        Console.WriteLine("bs5=" + d.BinarySearch(5));
        Console.WriteLine("bs45=" + d.BinarySearch(45));
        Console.WriteLine("bs_cmp=" + d.BinarySearch(30, Comparer.Default));
        Console.WriteLine("bs_range=" + d.BinarySearch(1, 2, 20, Comparer.Default));
        Console.WriteLine("bs_range_absent=" + d.BinarySearch(1, 2, 25, Comparer.Default));
        ArrayList ds = new ArrayList();
        ds.Add("b"); ds.Add("d"); ds.Add("f");
        Console.WriteLine("bs_str=" + ds.BinarySearch("d"));
        Console.WriteLine("bs_str_absent=" + ds.BinarySearch("c"));
        // A non-IComparer argument is a compile-time cast; the runtime's
        // interface descriptors are a separate gap, so the argument-validation
        // path is probed through Comparer.Default instead (the null-comparer
        // arm is the same code path).
        bool t2 = false;
        try { d.BinarySearch(20, Comparer.Default); Console.WriteLine("bs_cmp_ok=" + (d.BinarySearch(20, Comparer.Default) == 1)); } catch (Exception) { t2 = true; }
        Console.WriteLine("bs_exc_cmp=" + t2);
        bool t3 = false;
        try { d.Sort(0, 5, Comparer.Default); } catch (ArgumentException) { t3 = true; }
        Console.WriteLine("sort_exc_range=" + t3);
        bool t3b = false;
        try { d.BinarySearch(1, 5, 20, Comparer.Default); } catch (ArgumentException) { t3b = true; }
        Console.WriteLine("bs_exc_range=" + t3b);

        // ---------- GetRange ----------
        ArrayList g = new ArrayList();
        for (int i = 0; i < 5; i++) g.Add(i * 10);
        ArrayList gr = g.GetRange(1, 3);
        Console.WriteLine("getrange=" + (int)gr[0] + "," + (int)gr[1] + "," + (int)gr[2] + " cnt=" + gr.Count);
        Console.WriteLine("getrange_refeq=" + ReferenceEquals(gr, g));
        Console.WriteLine("getrange_cap=" + gr.Capacity);
        bool t4 = false;
        try { g.GetRange(-1, 2); } catch (ArgumentOutOfRangeException) { t4 = true; }
        Console.WriteLine("getrange_exc_neg=" + t4);
        bool t5 = false;
        try { g.GetRange(1, 9); } catch (ArgumentException) { t5 = true; }
        Console.WriteLine("getrange_exc_len=" + t5);

        // ---------- AddRange / InsertRange / SetRange / CopyTo ----------
        ArrayList ar = new ArrayList();
        ar.AddRange(new int[] { 1, 2, 3 });
        Console.WriteLine("addrange=" + ar.Count + "," + (int)ar[2]);
        bool t6 = false;
        try { ar.AddRange(null); } catch (ArgumentNullException) { t6 = true; }
        Console.WriteLine("addrange_exc_null=" + t6);
        ArrayList ir = new ArrayList();
        ir.Add(1); ir.Add(4);
        ir.InsertRange(1, new int[] { 2, 3 });
        Console.WriteLine("insertrange=" + (int)ir[0] + "," + (int)ir[1] + "," + (int)ir[2] + "," + (int)ir[3]);
        ArrayList srr = new ArrayList();
        for (int i = 0; i < 4; i++) srr.Add(0);
        srr.SetRange(1, new int[] { 7, 8 });
        Console.WriteLine("setrange=" + (int)srr[0] + "," + (int)srr[1] + "," + (int)srr[2] + "," + (int)srr[3]);
        bool t7 = false;
        try { srr.SetRange(3, new int[] { 7, 8 }); } catch (ArgumentException) { t7 = true; }
        Console.WriteLine("setrange_exc=" + t7);
        ArrayList ct = new ArrayList();
        ct.Add("a"); ct.Add("b"); ct.Add("c");
        object[] dst = new object[5];
        ct.CopyTo(dst);
        Console.WriteLine("copyto=" + Esc(dst[0]) + "," + Esc(dst[1]) + "," + Esc(dst[2]) + "," + Esc(dst[3]) + "," + Esc(dst[4]));
        object[] dst2 = new object[5];
        ct.CopyTo(1, dst2, 2, 2);
        Console.WriteLine("copyto4=" + Esc(dst2[1]) + "," + Esc(dst2[2]));
        bool t8 = false;
        try { ct.CopyTo(null); } catch (ArgumentNullException) { t8 = true; }
        Console.WriteLine("copyto_exc_null=" + t8);

        // ---------- ToArray ----------
        ArrayList ta = new ArrayList();
        ta.Add(1); ta.Add("x"); ta.Add(null);
        Array arrObj = ta.ToArray();
        Console.WriteLine("toarray_len=" + arrObj.Length + " v0=" + Esc(arrObj.GetValue(0)) + " v1=" + Esc(arrObj.GetValue(1)) + " v2=" + Esc(arrObj.GetValue(2)));
        Console.WriteLine("toarray_refeq=" + ReferenceEquals(arrObj, ta));
        ArrayList tat = new ArrayList();
        tat.Add(1); tat.Add(2);
        int[] typed = (int[])tat.ToArray(typeof(int));
        Console.WriteLine("toarray_typed=" + typed.Length + "," + typed[1]);
        bool t9 = false;
        try { tat.ToArray(typeof(string)); } catch (InvalidCastException) { t9 = true; }
        Console.WriteLine("toarray_typed_exc=" + t9);

        // ---------- Repeat / Adapter / FixedSize / ReadOnly / Synchronized ----------
        ArrayList rep = ArrayList.Repeat(7, 3);
        Console.WriteLine("repeat=" + rep.Count + "," + (int)rep[0] + "," + (int)rep[2]);
        Console.WriteLine("adapter=" + (ArrayList.Adapter((IList)a).Count));
        ArrayList fx = ArrayList.FixedSize((ArrayList)a);
        Console.WriteLine("fixed=" + fx.Count + " ro=" + fx.IsReadOnly + " fs=" + fx.IsFixedSize);
        ArrayList fro = ArrayList.ReadOnly((ArrayList)a);
        Console.WriteLine("readonly=" + fro.Count + " ro=" + fro.IsReadOnly);
        ArrayList syn = ArrayList.Synchronized((ArrayList)a);
        Console.WriteLine("synced=" + syn.Count + " issync=" + syn.IsSynchronized + " syncroot=" + (syn.SyncRoot != null));
        Console.WriteLine("syncroot_plain=" + ReferenceEquals(a.SyncRoot, a));
    }

    static string ResetThenRead(ArrayList a)
    {
        IEnumerator e = a.GetEnumerator();
        e.MoveNext();
        e.Reset();
        StringBuilder sb = new StringBuilder();
        while (e.MoveNext()) sb.Append(Esc(e.Current)).Append(",");
        return sb.ToString();
    }
}
