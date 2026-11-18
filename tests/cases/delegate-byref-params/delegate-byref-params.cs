using System;
public static class P {
    static void Main() {
        RefFn rf = new RefFn(OnRef); int v = 5; rf(ref v);
        OutFn of = new OutFn(OnOut); int o; of(out o);
        TwoRefFn tr = new TwoRefFn(OnTwoRef); int a = 1, b = 2; tr(ref a, ref b);
        Console.WriteLine("v=" + v + " o=" + o + " a=" + a + " b=" + b);
    }
    public delegate void RefFn(ref int x);
    public delegate void OutFn(out int x);
    public delegate void TwoRefFn(ref int a, ref int b);
    static void OnRef(ref int x) { x = x + 10; }
    static void OnOut(out int x) { x = 99; }
    static void OnTwoRef(ref int a, ref int b) { int t = a; a = b; b = t; }
}
