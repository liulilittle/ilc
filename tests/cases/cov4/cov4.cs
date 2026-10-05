using System;
class P
{
    static void Main()
    {
        int[] arr = { 4, 8, 15, 16, 23, 42 };
        Console.WriteLine("f1=" + (Array.FindIndex(arr, x => x == 15) == 2));
        Console.WriteLine("f2=" + Array.FindIndex(arr, x => x > 100));
        Console.WriteLine("f3=" + string.Join(",", Array.FindAll(arr, x => x % 2 == 0)));
        Console.WriteLine("f4=" + (Array.Find(arr, x => x > 20) == 23));
        Console.WriteLine("f5=" + Array.TrueForAll(arr, x => x > 0));
        decimal df = new decimal(3.5f);
        Console.WriteLine("dc1=" + df);
        Console.WriteLine("dc2=" + (decimal.ToInt32(3.9m) == 3));
        Console.WriteLine("dc3=" + ((int)7.99m == 7));
    }
}
