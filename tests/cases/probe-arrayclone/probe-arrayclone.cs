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

        // A clone of a long[]/double[] array: the cast back to the concrete
        // array type needs the element-array class synthesized on demand
        // (il2cpp_array_class_for_element_name), not just looked up.
        long[] l = { 9000000000L, 7000000000L };
        object lc = l.Clone();
        long[] lca = (long[])lc;
        Console.WriteLine("i8:" + lca.Length + ":" + lca[0] + ":" + lca[1]);

        double[] f = { 1.5, 2.5 };
        object fc = f.Clone();
        double[] fca = (double[])fc;
        Console.WriteLine("f8:" + fca.Length + ":" + fca[0] + ":" + fca[1]);
    }
}
