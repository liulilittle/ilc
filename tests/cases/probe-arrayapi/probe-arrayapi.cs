using System;

class Program
{
    static void Main()
    {
        // Array.Empty<T>: the canonical zero-length array.
        int[] e = Array.Empty<int>();
        Console.WriteLine("empty:" + e.Length);

        // Array.Exists / Find / FindIndex / FindLast / FindAll / TrueForAll
        int[] a = { 3, 8, 1, 9, 4 };
        Console.WriteLine("exists:" + Array.Exists(a, x => x > 8));
        Console.WriteLine("find:" + Array.Find(a, x => x > 3));
        Console.WriteLine("findidx:" + Array.FindIndex(a, x => x > 3));
        Console.WriteLine("findlast:" + Array.FindLast(a, x => x > 3));
        Console.WriteLine("findall:" + Array.FindAll(a, x => x > 3).Length);
        Console.WriteLine("trueall:" + Array.TrueForAll(a, x => x > 0));

        // Array.Resize<T> (ref parameter)
        int[] r = { 1, 2 };
        Array.Resize(ref r, 4);
        Console.WriteLine("resize:" + r.Length + ":" + r[0] + ":" + r[3]);

        // Array.ForEach
        int sum = 0;
        Array.ForEach(a, x => sum += x);
        Console.WriteLine("foreach:" + sum);

        // Array.GetEnumerator
        int n = 0;
        foreach (int v in a) n += v;
        Console.WriteLine("enum:" + n);
        // Array.GetLongLength
        Console.WriteLine("longlen:" + a.GetLongLength(0));

        // Array.get_IsFixedSize / IsReadOnly / IsSynchronized
        Console.WriteLine("flags:" + a.IsFixedSize + ":" + a.IsReadOnly + ":" + a.IsSynchronized);
    }
}