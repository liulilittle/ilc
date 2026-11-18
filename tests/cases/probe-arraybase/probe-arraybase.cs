using System;

class Program
{
    static void Main()
    {
        // An array CAST to System.Array, and the `as` form, must be identities:
        // every array IS-A Array. Neither names an element type.
        object o = new int[] { 4, 5, 6 };
        Array a = (Array)o;
        Console.WriteLine("cast:" + a.Length);

        Array b = o as Array;
        Console.WriteLine("as:" + b.Length);

        // A non-array operand must throw / be null.
        object notArray = "s";
        try
        {
            Array c = (Array)notArray;
            Console.WriteLine("bad:" + c.Length);
        }
        catch (InvalidCastException)
        {
            Console.WriteLine("badthrow:ok");
        }

        Console.WriteLine("badnull:" + (notArray as Array == null));

        // Array.Length and indexing through the base class must work.
        object o2 = new long[] { 9000000000L, 7000000000L };
        Array a2 = (Array)o2;
        Console.WriteLine("i8base:" + a2.Length + ":" + a2.GetValue(0));

        // string[] to Array, then clone through the base type.
        object o3 = new string[] { "p", "q" };
        Array a3 = (Array)o3;
        Array a4 = (Array)a3.Clone();
        Console.WriteLine("refbase:" + a4.Length + ":" + a4.GetValue(1));
    }
}
