using System;
class P
{
    static void Main()
    {
        DateTime dt = new DateTime(2024, 3, 15, 10, 20, 30, 250);
        Console.WriteLine("d1=" + (dt.DayOfYear == 75));
        Console.WriteLine("d2=" + (dt.Millisecond == 250));
        int r;
        Math.DivRem(17, 5, out r);
        Console.WriteLine("m1=" + (r == 2));
        Console.WriteLine("m2=" + (Math.DivRem(17, 5, out r) == 3));
        long rl;
        Console.WriteLine("m3=" + (Math.DivRem(-17L, 5L, out rl) == -3L) + " rl=" + rl);
        int[] a1 = { 1, 2, 3 };
        int[] a2 = new int[3];
        Array.Copy(a1, a2, 3);
        Console.WriteLine("a1=" + string.Join(",", a2));
        Console.WriteLine("c1=" + Convert.ToBoolean("true"));
        Console.WriteLine("c2=" + (Convert.ToInt32(true) == 1));
        Console.WriteLine("c3=" + (Convert.ToBoolean("False") == false));
    }
}
