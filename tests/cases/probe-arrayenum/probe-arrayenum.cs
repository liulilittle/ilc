using System;
using System.Collections;

class Program
{
    static void Main()
    {
        // Array.GetEnumerator: the non-generic IEnumerator over the array.
        int[] a = { 10, 20, 30 };
        IEnumerator e = a.GetEnumerator();
        int sum = 0;
        while (e.MoveNext())
        {
            sum += (int)e.Current;
        }
        Console.WriteLine("sum:" + sum);

        // Reset must rewind to before the first element.
        e.Reset();
        int n = 0;
        while (e.MoveNext()) n++;
        Console.WriteLine("reset:" + n);

        // A reference-element array enumerates its elements.
        string[] s = { "p", "q" };
        IEnumerator se = s.GetEnumerator();
        string joined = "";
        while (se.MoveNext()) joined += (string)se.Current;
        Console.WriteLine("ref:" + joined);

        // An empty array yields nothing.
        int[] empty = new int[0];
        IEnumerator ee = empty.GetEnumerator();
        Console.WriteLine("empty:" + ee.MoveNext());

        // foreach over an array uses the same enumerator path.
        int total = 0;
        foreach (int v in a) total += v;
        Console.WriteLine("foreach:" + total);
        // The NON-GENERIC IEnumerator.Current path: reading through the
        // `object` carrier (not the typed i4 accessor) must box a value
        // element. This is the path that routes through
        // il2cpp_ienum_get_current_obj, so it is the one that catches a
        // reference-only element read.
        IEnumerator oe = a.GetEnumerator();
        object first = null;
        if (oe.MoveNext()) first = oe.Current;
        Console.WriteLine("objcur:" + first + ":" + first.GetType().Name);

        // A reference-element array through the same object carrier.
        IEnumerator orf = s.GetEnumerator();
        object rfirst = null;
        if (orf.MoveNext()) rfirst = orf.Current;
        Console.WriteLine("objref:" + rfirst);
    }
}