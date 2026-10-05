using System;
using System.Collections.Generic;
using System.Globalization;

// g4listdouble -- G4 List<double> (double value domain) e2e case.
// GREEN since the double AOT expansion landed (MethodMap.List.cs
// doubleReceiver + listobj *_double icalls + BclAllocExpr alloc arm);
// the G17/"R" format assertions moved to the dblfmt_r probe.
// Deterministic: InvariantCulture, no time/random/paths.
class Prog
{
    static void Main()
    {
        List<double> l = new List<double>();
        l.Add(1.5);
        l.Add(3.25);
        l.Add(2.0);
        Console.WriteLine("count=" + l.Count);

        Console.WriteLine("get0=" + l[0].ToString(CultureInfo.InvariantCulture));
        l[1] = 3.5;
        Console.WriteLine("set1=" + l[1].ToString(CultureInfo.InvariantCulture));

        Console.WriteLine("contains3_25=" + l.Contains(3.25));
        Console.WriteLine("contains3_5=" + l.Contains(3.5));
        Console.WriteLine("indexof2=" + l.IndexOf(2.0));

        l.Insert(1, 1.25);
        Console.WriteLine("afterinsertcount=" + l.Count);
        Console.WriteLine("inserted=" + l[1].ToString(CultureInfo.InvariantCulture));

        Console.WriteLine("removed=" + l.Remove(3.5));
        Console.WriteLine("afterremovecount=" + l.Count);

        l.Sort();
        Console.WriteLine("sorted0=" + l[0].ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("sorted1=" + l[1].ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("sorted2=" + l[2].ToString(CultureInfo.InvariantCulture));

        double[] arr = l.ToArray();
        Console.WriteLine("arrlen=" + arr.Length);
        // NOTE: a direct arr[0].ToString(...) receiver hits the known
        // ldelema->intptr_t receiver-shape gap (see g4listlong's note);
        // the local temp keeps this probe focused on the List<double> domain.
        double a0 = arr[0];
        Console.WriteLine("arr0=" + a0.ToString(CultureInfo.InvariantCulture));
        double al = arr[arr.Length - 1];
        Console.WriteLine("arrlast=" + al.ToString(CultureInfo.InvariantCulture));

        foreach (double v in l)
            Console.WriteLine("each=" + v.ToString(CultureInfo.InvariantCulture));

        // NOTE: the ToString("R") / G17 precision assertions moved to the
        // dedicated dblfmt_r probe (the runtime throws "Format specifier was
        // invalid" for R -- D-DBLF-1, orthogonal to the List<double> domain).

        Console.WriteLine("done");
    }
}
