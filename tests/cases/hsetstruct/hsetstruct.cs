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
        pt.Add(new Pt(1, 2));
        pt.Add(new Pt(3, 4));
        pt.Add(new Pt(1, 2));
        Console.WriteLine("struct_count=" + pt.Count);
        Console.WriteLine("struct_has=" + pt.Contains(new Pt(3, 4)));
        Console.WriteLine("struct_miss=" + pt.Contains(new Pt(9, 9)));
        string spt = "";
        foreach (Pt v in pt) spt += v.X + "/" + v.Y + ";";
        Console.WriteLine("struct_order=" + spt);
        Console.WriteLine("struct_remove=" + pt.Remove(new Pt(1, 2)) + " count=" + pt.Count);
    }
}
