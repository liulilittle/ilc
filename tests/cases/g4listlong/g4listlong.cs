using System;
using System.Collections.Generic;
using System.Globalization;

// g4listlong -- G4 gap probe: List<long> (int64 value domain). The wired
// List<T> surface only expands T=int32 (listi4) and the four reference
// suffixes (listobj); every other value-type instantiation is LOUD
// (MethodMap.List.cs:49-63). Expected red until the int64 domain exists.
// Deterministic: InvariantCulture, no time/random/paths.
class Prog
{
    static void Main()
    {
        List<long> l = new List<long>();
        l.Add(10L);
        l.Add(30L);
        l.Add(20L);
        Console.WriteLine("count=" + l.Count);

        Console.WriteLine("get0=" + l[0].ToString(CultureInfo.InvariantCulture));
        l[1] = 31L;
        Console.WriteLine("set1=" + l[1].ToString(CultureInfo.InvariantCulture));

        Console.WriteLine("contains30=" + l.Contains(30L));
        Console.WriteLine("contains31=" + l.Contains(31L));
        Console.WriteLine("indexof20=" + l.IndexOf(20L));

        l.Insert(1, 15L);
        Console.WriteLine("afterinsertcount=" + l.Count);
        Console.WriteLine("inserted=" + l[1].ToString(CultureInfo.InvariantCulture));

        Console.WriteLine("removed=" + l.Remove(31L));
        Console.WriteLine("afterremovecount=" + l.Count);

        l.Sort();
        Console.WriteLine("sorted0=" + l[0].ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("sorted1=" + l[1].ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("sorted2=" + l[2].ToString(CultureInfo.InvariantCulture));

        long[] arr = l.ToArray();
        Console.WriteLine("arrlen=" + arr.Length);
        long a0 = arr[0];
        long al = arr[arr.Length - 1];
        Console.WriteLine("arr0=" + a0.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("arrlast=" + al.ToString(CultureInfo.InvariantCulture));

        foreach (long v in l)
            Console.WriteLine("each=" + v.ToString(CultureInfo.InvariantCulture));

        Console.WriteLine("done");
    }
}
