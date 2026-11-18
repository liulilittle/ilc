using System;
public struct Pt { public int X; public int Y; }
public static class P {
    static void Main() {
        StructFn sf = new StructFn(OnStruct); Pt p = sf(3, 4);
        TakeStructFn ts = new TakeStructFn(OnTakeStruct); int sum = ts(new Pt { X = 7, Y = 8 });
        StrFn s0 = new StrFn(OnStr0); string s = s0();
        Console.WriteLine("p=" + p.X + "," + p.Y + " sum=" + sum + " s=" + s);
    }
    public delegate Pt StructFn(int x, int y);
    public delegate int TakeStructFn(Pt p);
    public delegate string StrFn();
    static Pt OnStruct(int x, int y) { return new Pt { X = x * 2, Y = y * 2 }; }
    static int OnTakeStruct(Pt p) { return p.X + p.Y; }
    static string OnStr0() { return "hi"; }
}
