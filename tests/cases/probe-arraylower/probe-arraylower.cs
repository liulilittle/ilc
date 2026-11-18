using System;

class Program
{
    static void Main()
    {
        // A non-zero lower bound: net48 keeps it in the array header, so
        // GetLowerBound reports it and indexing starts there.
        Array a = Array.CreateInstance(typeof(int), new int[] { 3 }, new int[] { 5 });
        Console.WriteLine("lb:" + a.GetLowerBound(0) + ":" + a.GetUpperBound(0) + ":" + a.Length);

        // Indexing must start at the lower bound, not at 0.
        a.SetValue(11, 5);
        a.SetValue(22, 7);
        Console.WriteLine("idx:" + (int)a.GetValue(5) + ":" + (int)a.GetValue(7));

        // A zero lower bound is the ordinary case and must stay 0.
        Array z = Array.CreateInstance(typeof(int), new int[] { 4 });
        Console.WriteLine("zero:" + z.GetLowerBound(0) + ":" + z.GetUpperBound(0));

        // A 2-D array with a non-zero lower bound on each dimension.
        Array m = Array.CreateInstance(typeof(int), new int[] { 2, 3 }, new int[] { 1, 10 });
        Console.WriteLine("md:" + m.GetLowerBound(0) + ":" + m.GetLowerBound(1)
            + ":" + m.GetUpperBound(0) + ":" + m.GetUpperBound(1) + ":" + m.Length);
        m.SetValue(99, 1, 10);
        m.SetValue(77, 2, 12);
        Console.WriteLine("mdidx:" + (int)m.GetValue(1, 10) + ":" + (int)m.GetValue(2, 12));

        // Out-of-range relative to the LOWER bound must throw.
        try
        {
            a.SetValue(1, 4);
            Console.WriteLine("lowbad:no");
        }
        catch (IndexOutOfRangeException)
        {
            Console.WriteLine("lowbad:ok");
        }

        // A negative lower bound is legal in net48.
        Array n = Array.CreateInstance(typeof(int), new int[] { 3 }, new int[] { -2 });
        Console.WriteLine("neg:" + n.GetLowerBound(0) + ":" + n.GetUpperBound(0));
        n.SetValue(5, -2);
        Console.WriteLine("negidx:" + (int)n.GetValue(-2));
    }
}
