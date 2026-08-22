using System;
using System.Text;

static class P
{
    static void Main()
    {
        string a = "Held".Insert(3, "l");
        Console.WriteLine(a);
        Console.WriteLine(a.Length);
        long v = BitConverter.DoubleToInt64Bits(2.0);
        Console.WriteLine(v.ToString());
        Console.WriteLine((4616189618054758400L).ToString());
    }
}
