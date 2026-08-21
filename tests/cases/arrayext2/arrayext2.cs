using System;

//------------------------------------------------------------------------------
// arrayext2.cs -- System.Array extension-2 e2e reference case (net48).
//
// Demonstrates the surface implemented by the arrayext2 runtime domain:
//   Array.Sort(string[])        -- ordinal order; null elements sort FIRST
//   Array.BinarySearch(int[])   -- hit returns the index; a miss returns the
//                                  complement ~insertionPoint (negative)
//   Array.BinarySearch(string[])-- ordinal variant
//   Array.IndexOf / LastIndexOf / FindIndex (int values)
//   Array.Clear(array, index, length)
//   Array.Reverse(array, index, length)
//
// Every printed line is deterministic; the output is the net48 reference the
// transpiled build must match byte for byte. The complement restore (~miss)
// line documents the .NET BinarySearch insertion-point contract.
//------------------------------------------------------------------------------
internal static class ArrayExt2Demo
{
    private static void Main()
    {
        // Sort(string[]): ordinal (case-sensitive) order; null sorts first.
        string[] names = { "pear", null, "apple", "Cherry", "cherry" };
        Array.Sort(names);
        Console.WriteLine("sort: " + Join(names));

        // BinarySearch(int32): hit and miss (complement semantics).
        int[] sorted = { 1, 3, 5, 7, 9 };
        int hit = Array.BinarySearch(sorted, 5);
        int miss = Array.BinarySearch(sorted, 4);
        Console.WriteLine("bs-int hit: " + hit);
        Console.WriteLine("bs-int miss: " + miss + " (~miss=" + (~miss) + ")");

        // BinarySearch(string): ordinal hit and miss.
        string[] fruit = { "apple", "cherry", "pear" };
        Console.WriteLine("bs-string hit: " + Array.BinarySearch(fruit, "cherry"));
        Console.WriteLine("bs-string miss: " + Array.BinarySearch(fruit, "banana"));

        // IndexOf / LastIndexOf / FindIndex (int values).
        int[] dup = { 10, 20, 30, 20, 40 };
        Console.WriteLine("indexof: " + Array.IndexOf(dup, 20) +
                          " / " + Array.LastIndexOf(dup, 20));
        Console.WriteLine("findindex: " + Array.FindIndex(dup, 2, 3, x => x == 20));

        // Clear range.
        int[] clearArr = { 1, 2, 3, 4, 5 };
        Array.Clear(clearArr, 1, 3);
        Console.Write("clear: ");
        PrintInts(clearArr);

        // Reverse range.
        int[] revArr = { 1, 2, 3, 4, 5 };
        Array.Reverse(revArr, 1, 3);
        Console.Write("reverse: ");
        PrintInts(revArr);
    }

    private static string Join(string[] items)
    {
        string result = "";
        for (int i = 0; i < items.Length; ++i)
        {
            if (i > 0)
            {
                result += ",";
            }
            result += items[i] != null ? items[i] : "<null>";
        }
        return result;
    }

    private static void PrintInts(int[] a)
    {
        for (int i = 0; i < a.Length; ++i)
        {
            if (i > 0)
            {
                Console.Write(" ");
            }
            Console.Write(a[i]);
        }
        Console.WriteLine();
    }
}
