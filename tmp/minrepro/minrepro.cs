using System;

static class Program
{
    static void Check(string name, bool ok)
    {
        if (!ok) Console.WriteLine("FAIL " + name);
        else Console.WriteLine("PASS " + name);
    }

    static void Main()
    {
        int[] arr = new int[] { 5, 3, 1 };
        int i = Array.FindIndex(arr, x => x > 2);
        Check("findindex", i == 0);
        bool e = Array.Exists(arr, x => x == 3);
        Check("exists", e);
        Console.WriteLine("ALL-PASS");
    }
}
