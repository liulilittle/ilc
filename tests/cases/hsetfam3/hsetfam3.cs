using System;
using System.Collections.Generic;

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
    static void Main()
    {
                HashSet<Pt> pt = new HashSet<Pt>();
        pt.Add(new Pt(1, 2)); pt.Add(new Pt(3, 4)); pt.Add(new Pt(1, 2));
        string spt = "";
        foreach (Pt v in pt) spt += v.ToString() + ";";
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
    }
}
