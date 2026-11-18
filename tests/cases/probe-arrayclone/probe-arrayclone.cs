using System;

class Program
{
    static void Main()
    {
        int[] a = { 1, 2, 3 };
        object c = a.Clone();
        int[] ca = (int[])c;
        Console.WriteLine("i4:" + ca.Length + ":" + ca[1]);

        string[] s = { "x", "y" };
        object sc = s.Clone();
        string[] sca = (string[])sc;
        Console.WriteLine("ref:" + sca.Length + ":" + sca[0]);

        // The clone is independent: mutating it must not touch the source.
        ca[0] = 99;
        Console.WriteLine("indep:" + a[0] + ":" + ca[0]);

        // A clone of a reference array shares the ELEMENTS (shallow), so the
        // slot copy is observable through the element identity.
        string[] s2 = { "p", "q" };
        object s2c = s2.Clone();
        string[] s2ca = (string[])s2c;
        s2ca[0] = "z";
        Console.WriteLine("shallow:" + s2[0] + ":" + s2ca[0]);
    }
}
