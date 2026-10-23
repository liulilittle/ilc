using System;
using System.Collections.Generic;

// hsetfam -- HashSet<T> ELEMENT FAMILY coverage + net48 enumeration order.
// Every element family must produce the net48 slot order (insertion order with
// LIFO free-slot reuse) and the net48 membership semantics (value equality for
// value types, ordinal content for strings, reference identity for classes).
public struct Pt
{
    public int X;
    public int Y;
    public Pt(int x, int y) { X = x; Y = y; }
    public override int GetHashCode() { return X * 31 + Y; }
    public override bool Equals(object o)
    {
        if (!(o is Pt)) return false;
        Pt p = (Pt)o;
        return X == p.X && Y == p.Y;
    }
    public override string ToString() { return X + "/" + Y; }
}

class Prog
{
    static void DumpI4(string tag, HashSet<int> s)
    {
        string o = "";
        foreach (int v in s) o += v + ",";
        Console.WriteLine(tag + "=" + s.Count + "|" + o);
    }

    static void Main()
    {
        HashSet<int> i4 = new HashSet<int>();
        i4.Add(30); i4.Add(10); i4.Add(20); i4.Add(10);
        DumpI4("i4", i4);
        Console.WriteLine("i4_has=" + i4.Contains(20) + "/" + i4.Contains(40));

        HashSet<long> i8 = new HashSet<long>();
        i8.Add(9000000000L); i8.Add(-5L); i8.Add(9000000000L);
        string s8 = "";
        foreach (long v in i8) s8 += v + ",";
        Console.WriteLine("i8=" + i8.Count + "|" + s8);
        Console.WriteLine("i8_has=" + i8.Contains(-5L));

        HashSet<uint> u4 = new HashSet<uint>();
        u4.Add(4000000000u); u4.Add(1u); u4.Add(4000000000u);
        string su = "";
        foreach (uint v in u4) su += v + ",";
        Console.WriteLine("u4=" + u4.Count + "|" + su);
        Console.WriteLine("u4_has=" + u4.Contains(4000000000u));

        HashSet<byte> b1 = new HashSet<byte>();
        b1.Add(200); b1.Add(3); b1.Add(200);
        string sb = "";
        foreach (byte v in b1) sb += v + ",";
        Console.WriteLine("byte=" + b1.Count + "|" + sb);

        HashSet<sbyte> sb1 = new HashSet<sbyte>();
        sb1.Add(-100); sb1.Add(7); sb1.Add(-100);
        string ssb = "";
        foreach (sbyte v in sb1) ssb += v + ",";
        Console.WriteLine("sbyte=" + sb1.Count + "|" + ssb);

        HashSet<short> sh = new HashSet<short>();
        sh.Add(-30000); sh.Add(5); sh.Add(-30000);
        string ssh = "";
        foreach (short v in sh) ssh += v + ",";
        Console.WriteLine("short=" + sh.Count + "|" + ssh);

        HashSet<ushort> us = new HashSet<ushort>();
        us.Add(60000); us.Add(2); us.Add(60000);
        string sus = "";
        foreach (ushort v in us) sus += v + ",";
        Console.WriteLine("ushort=" + us.Count + "|" + sus);

        HashSet<char> ch = new HashSet<char>();
        ch.Add('z'); ch.Add('a'); ch.Add('z');
        string sch = "";
        foreach (char v in ch) sch += v + ",";
        Console.WriteLine("char=" + ch.Count + "|" + sch);

        HashSet<bool> bo = new HashSet<bool>();
        bo.Add(true); bo.Add(false); bo.Add(true);
        string sbo = "";
        foreach (bool v in bo) sbo += v + ",";
        Console.WriteLine("bool=" + bo.Count + "|" + sbo);

        HashSet<float> fl = new HashSet<float>();
        fl.Add(1.5f); fl.Add(-0.25f); fl.Add(1.5f);
        string sfl = "";
        foreach (float v in fl) sfl += v + ",";
        Console.WriteLine("float=" + fl.Count + "|" + sfl);

        HashSet<double> db = new HashSet<double>();
        db.Add(2.5); db.Add(-1.25); db.Add(2.5);
        string sdb = "";
        foreach (double v in db) sdb += v + ",";
        Console.WriteLine("double=" + db.Count + "|" + sdb);

        HashSet<decimal> de = new HashSet<decimal>();
        de.Add(1.5m); de.Add(2.75m); de.Add(1.50m);
        string sde = "";
        foreach (decimal v in de) sde += v + ",";
        Console.WriteLine("decimal=" + de.Count + "|" + sde);
        Console.WriteLine("decimal_has=" + de.Contains(1.500m));

        HashSet<string> st = new HashSet<string>();
        st.Add("bb"); st.Add("aa"); st.Add("bb");
        string sst = "";
        foreach (string v in st) sst += v + ",";
        Console.WriteLine("string=" + st.Count + "|" + sst);
        Console.WriteLine("string_content=" + st.Contains(new System.Text.StringBuilder("aa").ToString()));

        HashSet<Pt> pt = new HashSet<Pt>();
        pt.Add(new Pt(1, 2)); pt.Add(new Pt(3, 4)); pt.Add(new Pt(1, 2));
        string spt = "";
        foreach (Pt v in pt) spt += v + ";";
        Console.WriteLine("struct=" + pt.Count + "|" + spt);
        Console.WriteLine("struct_has=" + pt.Contains(new Pt(3, 4)));

        HashSet<DateTime> dt = new HashSet<DateTime>();
        DateTime d1 = new DateTime(2020, 1, 2);
        dt.Add(d1); dt.Add(new DateTime(2021, 5, 6)); dt.Add(d1);
        string sdt = "";
        foreach (DateTime v in dt) sdt += v.ToString("yyyy-MM-dd") + ",";
        Console.WriteLine("datetime=" + dt.Count + "|" + sdt);

        HashSet<Guid> gd = new HashSet<Guid>();
        Guid g1 = new Guid("11111111-2222-3333-4444-555555555555");
        gd.Add(g1); gd.Add(Guid.Empty); gd.Add(g1);
        string sgd = "";
        foreach (Guid v in gd) sgd += v.ToString() + ",";
        Console.WriteLine("guid=" + gd.Count + "|" + sgd);

        // remove/re-add LIFO slot reuse across a family
        HashSet<int> re = new HashSet<int>();
        re.Add(1); re.Add(2); re.Add(3); re.Add(4);
        re.Remove(2); re.Remove(4);
        re.Add(99); re.Add(98);
        DumpI4("reuse", re);
    }
}
