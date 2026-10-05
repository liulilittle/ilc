//------------------------------------------------------------------------------
// sortcase2.cs -- sortdeep supplement e2e: negative full range / big array /
//                duplicate values / null string / BinarySearch boundaries /
//                CopyTo partial copies (all bool).
//
//   * The big array is shuffled with a deterministic Fisher-Yates
//     (j = (i*7919)%1000, the multiply does not overflow), independent of the
//     runtime Random icall; a 0..999 full permutation -> after sorting the
//     result must equal i element by element.
//   * Duplicate elements use a "value-sequence assertion": C# Array.Sort is
//     not stable, so index-marker assertions are impossible (net48 and the
//     runtime may both swap equal elements freely); the complete ascending
//     value sequence is the only valid result.
//   * string sorting follows the sortdeep-recorded limits: all-ASCII lowercase
//     + null + empty string (net48 current-culture and runtime ordinal agree
//     on this input domain).
//------------------------------------------------------------------------------

using System;

class Program
{
    static bool Ascending(int[] a)
    {
        for (int i = 1; i < a.Length; ++i)
        {
            if (a[i - 1] > a[i])
            {
                return false;
            }
        }
        return true;
    }

    static void Main()
    {
        // ============ negative sorting (int full-range endpoints) ============
        int[] neg = { -3, 2147483647, -2147483648, 0, 7, -1000, -1, 42 };
        Array.Sort(neg);
        Console.WriteLine(Ascending(neg));                      // fully ascending
        Console.WriteLine(neg[0] == -2147483648 && neg[1] == -1000 &&
                          neg[6] == 42 && neg[7] == 2147483647); // min/max endpoints land

        // ============ big array: 1000 shuffled elements -> ascending ============
        int n = 1000;
        int[] big = new int[n];
        for (int i = 0; i < n; ++i)
        {
            big[i] = i;
        }
        for (int i = n - 1; i > 0; --i)                         // deterministic shuffle (no overflow)
        {
            int j = (i * 7919) % n;
            int t = big[i];
            big[i] = big[j];
            big[j] = t;
        }
        Console.WriteLine(!Ascending(big));                     // the input really is shuffled
        Array.Sort(big);
        Console.WriteLine(Ascending(big));                      // ascending
        bool exact = true;
        for (int i = 0; i < n; ++i)
        {
            if (big[i] != i)
            {
                exact = false;                                  // 0..999 full permutation has a unique solution
            }
        }
        Console.WriteLine(exact);                               // value sequence == 0..999

        // ============ duplicate elements: value-sequence assertion (Sort is not stable) ============
        int[] dup = { 3, 1, 2, 3, 1, 2, 3, 1, 2 };
        Array.Sort(dup);
        Console.WriteLine(dup[0] == 1 && dup[1] == 1 && dup[2] == 1 &&
                          dup[3] == 2 && dup[4] == 2 && dup[5] == 2 &&
                          dup[6] == 3 && dup[7] == 3 && dup[8] == 3); // complete ascending sequence
        int[] all7 = new int[100];
        for (int i = 0; i < 100; ++i)
        {
            all7[i] = 7;
        }
        Array.Sort(all7);
        Console.WriteLine(Ascending(all7) && all7[0] == 7 && all7[99] == 7); // all duplicates

        // ============ string sorting: null / empty handling ============
        string[] sn = { "banana", null, "", "apple", null, "cherry", "" };
        Array.Sort(sn);
        Console.WriteLine(sn[0] == null && sn[1] == null && sn[2] == "" &&
                          sn[3] == "" && sn[4] == "apple" && sn[5] == "banana" &&
                          sn[6] == "cherry");                   // nulls first, empties next

        // ============ BinarySearch boundaries (int) ============
        int[] b = { -2147483648, -5, -1, 0, 3, 42, 2147483647 };
        Console.WriteLine(Array.BinarySearch(b, -2147483648) == 0); // hits the first element
        Console.WriteLine(Array.BinarySearch(b, 2147483647) == 6);  // hits the last element
        Console.WriteLine(Array.BinarySearch(b, -5) == 1);          // hits a negative value
        Console.WriteLine(Array.BinarySearch(b, 1) == -5);          // mid miss ~4
        Console.WriteLine(Array.BinarySearch(b, 2147483646) == -7); // upper miss ~6
        int[] s2 = { 3, 5 };
        Console.WriteLine(Array.BinarySearch(s2, 2) == -1);         // below everything ~0
        Console.WriteLine(Array.BinarySearch(s2, 9) == -3);         // above everything ~2
        int[] s1 = { 7 };
        Console.WriteLine(Array.BinarySearch(s1, 7) == 0);          // single-element hit
        Console.WriteLine(Array.BinarySearch(s1, 8) == -2);         // single-element miss
        Console.WriteLine(Array.BinarySearch(new int[0], 5) == -1); // empty array ~0

        // ============ BinarySearch<string> boundaries ============
        string[] fb = { null, "apple", "banana", "cherry", "pear" };
        Console.WriteLine(Array.BinarySearch(fb, null) == 0);       // null hit
        Console.WriteLine(Array.BinarySearch(fb, "banana") == 2);   // hit
        Console.WriteLine(Array.BinarySearch(fb, "ab") == -2);      // miss ~1
        Console.WriteLine(Array.BinarySearch(fb, "zebra") == -6);   // above everything ~5
        Console.WriteLine(Array.BinarySearch(new string[0], "x") == -1); // empty array

        // ============ CopyTo partial copies ============
        int[] csrc = { 1, 2, 3, 4 };
        int[] cdst = new int[8];
        csrc.CopyTo(cdst, 2);                                   // lands in the middle, head/tail stay 0
        Console.WriteLine(cdst[0] == 0 && cdst[1] == 0 && cdst[2] == 1 &&
                          cdst[3] == 2 && cdst[4] == 3 && cdst[5] == 4 &&
                          cdst[6] == 0 && cdst[7] == 0);
        csrc.CopyTo(cdst, 4);                                   // lands flush with the right edge
        Console.WriteLine(cdst[4] == 1 && cdst[5] == 2 && cdst[6] == 3 &&
                          cdst[7] == 4);
        int[] fresh = new int[4];
        new int[0].CopyTo(fresh, 2);                            // empty src at a legal index is a no-op
        Console.WriteLine(fresh[0] == 0 && fresh[1] == 0 && fresh[2] == 0 &&
                          fresh[3] == 0);                       // target untouched
        bool ok = false;
        try { csrc.CopyTo(cdst, 5); } catch (ArgumentException) { ok = true; }
        Console.WriteLine(ok);                                  // does not fit -> AE
        ok = false;
        try { csrc.CopyTo(cdst, -1); } catch (ArgumentOutOfRangeException) { ok = true; }
        Console.WriteLine(ok);                                  // negative index -> AOOE
    }
}
