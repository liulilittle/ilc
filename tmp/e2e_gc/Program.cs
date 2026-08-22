using System;

class Program
{
    static void Main()
    {
        GC.Collect();
        long a = GC.GetTotalAllocatedBytes();
        object o = new string('x', 100);
        GC.Collect();
        long b = GC.GetTotalAllocatedBytes();
        Console.WriteLine(a >= 0 && b >= a ? "ALL-PASS" : "FAIL");
        int mg = GC.MaxGeneration;
        Console.WriteLine("MaxGeneration=" + mg + " ok=" + (mg >= 0));
    }
}
