//------------------------------------------------------------------------------
// arraysortdeep.cs -- System.Array sorting-family DEEP reference program #2
// (net48 oracle). Extends sortdeep with the comparer-behavior / mixed-type /
// boundary dimensions of the wired arraysort domain (MethodMap.ArraySort.cs +
// arraysort.h): every line a BOOL, net48 byte-exact.
//
// WIRED SURFACE exercised here:
//   * Array.Sort<int32>   (il2cpp_array_sort_int32)   ascending in-place
//   * Array.Sort<string>  (il2cpp_array_sort_string)  ordinal, null first
//   * Array.IndexOf<int32/string>    (array_index_of_int32/_string)
//   * Array.BinarySearch<int32/string> (array_binary_search_int32/_string)
//   * Array.CopyTo(Array, int)       (il2cpp_array_copy_to)
//
// NEW vs sortdeep:
//   * sort INPUT SHAPES: already-sorted / reverse-sorted / all-equal / mixed
//     sign including int.MinValue / int.MaxValue / single / empty
//   * duplicate blocks: equal keys keep an UNSPECIFIED but consistent order
//     (net48 introsort) -- asserted via the sorted multiset, never a
//     stability claim
//   * string sort: mixed lengths, duplicate strings, NULL duplicates (nulls
//     pack first, ordinal for the rest)
//   * IndexOf<string>: ordinal (case-sensitive), null needle/element,
//     first-match among duplicates
//   * BinarySearch<string>: sorted-array hits/misses/insertion points
//   * CopyTo: exact-fit tail, offset copy, overwrite after offset
//
// COMPARER CAVEATS (same discipline as sortdeep):
//   * Comparison<T> overloads are NOT wired (LOUD -- no delegate-to-function-
//     pointer ABI); no such line appears. "Comparer" here means the DEFAULT
//     comparer behavior: net48 Array.Sort(string[]) is CURRENT-CULTURE while
//     the runtime is ORDINAL -- all string inputs are ASCII lowercase so the
//     two orders coincide (never a culture-sensitive line).
//   * BinarySearch on duplicates returns an UNSPECIFIED index (.NET contract);
//     the duplicate assertion uses the predicate form, never an exact index.
//   * uint[]/long[] sources cannot be built (transpiler creates only
//     int32/string/object arrays) -- no long/uint line appears.
//------------------------------------------------------------------------------
using System;

class Program
{
    static bool Ascending(int[] a)
    {
        for (int i = 1; i < a.Length; ++i)
            if (a[i - 1] > a[i]) return false;
        return true;
    }

    static bool AscendingStr(string[] a)
    {
        for (int i = 1; i < a.Length; ++i)
        {
            if (a[i - 1] == null) continue;   // nulls packed first
            if (a[i] == null) return false;
            if (string.CompareOrdinal(a[i - 1], a[i]) > 0) return false;
        }
        return true;
    }

    static void Main()
    {
        // ============ int sort: already-sorted / reverse-sorted ============
        int[] pre = { 1, 2, 3, 4, 5 };
        Array.Sort(pre);
        Console.WriteLine(Ascending(pre));
        int[] rev = { 9, 7, 5, 3, 1 };
        Array.Sort(rev);
        Console.WriteLine(rev[0] == 1 && rev[1] == 3 && rev[2] == 5 && rev[3] == 7 && rev[4] == 9);
        // ============ int sort: all-equal / duplicate blocks ============
        int[] eq = { 7, 7, 7, 7 };
        Array.Sort(eq);
        Console.WriteLine(eq[0] == 7 && eq[3] == 7);
        int[] dup = { 2, 1, 2, 1, 2, 1 };
        Array.Sort(dup);
        Console.WriteLine(dup[0] == 1 && dup[1] == 1 && dup[2] == 1 &&
                          dup[3] == 2 && dup[4] == 2 && dup[5] == 2);
        // ============ int sort: extreme boundaries ============
        int[] bnd = { 5, int.MinValue, -1, 0, int.MaxValue, -5 };
        Array.Sort(bnd);
        Console.WriteLine(bnd[0] == int.MinValue && bnd[1] == -5 && bnd[2] == -1 &&
                          bnd[3] == 0 && bnd[4] == 5 && bnd[5] == int.MaxValue);
        // ============ int sort: single / empty ============
        int[] one = { 5 };
        Array.Sort(one);
        Console.WriteLine(one[0] == 5);
        bool ok = true;
        try { Array.Sort(new int[0]); } catch (Exception) { ok = false; }
        Console.WriteLine(ok);
        // ============ string sort: lengths / duplicates / case (ordinal) ============
        string[] strs = { "bb", "a", "ccc", "bb", "dddd" };
        Array.Sort(strs);
        Console.WriteLine(strs[0] == "a" && strs[1] == "bb" && strs[2] == "bb" &&
                          strs[3] == "ccc" && strs[4] == "dddd");
        string[] same = { "ab", "ab", "ab" };
        Array.Sort(same);
        Console.WriteLine(same[0] == "ab" && same[2] == "ab");
        string[] mixed = { "z", "a", "m", "b" };
        Array.Sort(mixed);
        Console.WriteLine(mixed[0] == "a" && mixed[1] == "b" && mixed[2] == "m" && mixed[3] == "z");
        string[] pref = { "ab", "a", "abc", "ab", "aa" };
        Array.Sort(pref);
        Console.WriteLine(pref[0] == "a" && pref[1] == "aa" && pref[2] == "ab" &&
                          pref[3] == "ab" && pref[4] == "abc");
        // ============ string sort: null duplicates (nulls packed first) ============
        string[] n1 = { "b", null, "a", null };
        Array.Sort(n1);
        Console.WriteLine(n1[0] == null && n1[1] == null && n1[2] == "a" && n1[3] == "b");
        string[] n2 = { null, null };
        Array.Sort(n2);
        Console.WriteLine(n2[0] == null && n2[1] == null);
        string[] n3 = { "solo", null };
        Array.Sort(n3);
        Console.WriteLine(n3[0] == null && n3[1] == "solo");
        Console.WriteLine(AscendingStr(strs) && AscendingStr(pref) && AscendingStr(n1));

        // ============ IndexOf<int>: boundaries ============
        int[] arr = { 10, 20, 30, 40, 50 };
        Console.WriteLine(Array.IndexOf(arr, 10) == 0);       // first
        Console.WriteLine(Array.IndexOf(arr, 50) == 4);       // last
        Console.WriteLine(Array.IndexOf(arr, 25) == -1);      // miss
        Console.WriteLine(Array.IndexOf(arr, int.MinValue) == -1);
        Console.WriteLine(Array.IndexOf(arr, int.MaxValue) == -1);
        Console.WriteLine(Array.IndexOf(new int[] { 7 }, 7) == 0);  // single hit
        Console.WriteLine(Array.IndexOf(new int[] { 7 }, 8) == -1); // single miss
        Console.WriteLine(Array.IndexOf(new int[0], 5) == -1);      // empty
        int[] dupi = { 5, 9, 5, 3, 5 };
        Console.WriteLine(Array.IndexOf(dupi, 5) == 0);       // FIRST match

        // ============ IndexOf<string>: ordinal / null / case ============
        string[] ss = { "apple", "banana", null, "cherry" };
        Console.WriteLine(Array.IndexOf(ss, "apple") == 0);
        Console.WriteLine(Array.IndexOf(ss, "cherry") == 3);
        Console.WriteLine(Array.IndexOf(ss, null) == 2);      // null needle
        Console.WriteLine(Array.IndexOf(ss, "APPLE") == -1);  // case-sensitive
        Console.WriteLine(Array.IndexOf(ss, "fig") == -1);
        string[] sd = { "x", "y", "x" };
        Console.WriteLine(Array.IndexOf(sd, "x") == 0);       // first-match dup

        // ============ BinarySearch<int>: boundaries ============
        int[] bs = { -10, -3, 0, 4, 9, 15, 21 };
        Console.WriteLine(Array.BinarySearch(bs, -10) == 0);  // hit at first
        Console.WriteLine(Array.BinarySearch(bs, 21) == 6);   // hit at last
        Console.WriteLine(Array.BinarySearch(bs, 0) == 2);    // hit in middle
        Console.WriteLine(Array.BinarySearch(bs, 4) == 3);
        Console.WriteLine(Array.BinarySearch(bs, 7) == -5);   // miss: ~4
        Console.WriteLine(Array.BinarySearch(bs, -5) == -2);  // miss: ~1
        Console.WriteLine(Array.BinarySearch(bs, int.MinValue) == -1);  // below all
        Console.WriteLine(Array.BinarySearch(bs, int.MaxValue) == -8);  // above all: ~7
        Console.WriteLine(Array.BinarySearch(new int[] { 5 }, 5) == 0); // single hit
        Console.WriteLine(Array.BinarySearch(new int[] { 5 }, 4) == -1); // single below
        Console.WriteLine(Array.BinarySearch(new int[] { 5 }, 6) == -2); // single above
        Console.WriteLine(Array.BinarySearch(new int[0], 3) == -1);      // empty
        int[] bsd = { 1, 3, 3, 3, 5 };
        int di = Array.BinarySearch(bsd, 3);                  // duplicate: any hit legal
        Console.WriteLine(di >= 0 && bsd[di] == 3);

        // ============ BinarySearch<string>: sorted-array search ============
        string[] fruit = { "apple", "fig", "grape", "mango" };
        Console.WriteLine(Array.BinarySearch(fruit, "fig") == 1);
        Console.WriteLine(Array.BinarySearch(fruit, "apple") == 0);
        Console.WriteLine(Array.BinarySearch(fruit, "mango") == 3);
        Console.WriteLine(Array.BinarySearch(fruit, "kiwi") == -4);     // miss: ~3
        Console.WriteLine(Array.BinarySearch(fruit, "aardvark") == -1); // below all: ~0
        Console.WriteLine(Array.BinarySearch(fruit, "zebra") == -5);    // above all: ~4
        Console.WriteLine(Array.BinarySearch(fruit, "figg") == -3);     // mid: ~2
        Console.WriteLine(Array.BinarySearch(fruit, "apricot") == -2);  // mid: ~1
        Console.WriteLine(Array.BinarySearch(new string[] { "solo" }, "solo") == 0);
        Console.WriteLine(Array.BinarySearch(new string[0], "x") == -1);

        // ============ CopyTo: boundaries ============
        int[] src = { 1, 2, 3 };
        int[] dst = new int[5];
        src.CopyTo(dst, 2);                                   // offset copy
        Console.WriteLine(dst[0] == 0 && dst[1] == 0 && dst[2] == 1 &&
                          dst[3] == 2 && dst[4] == 3);
        int[] exact = new int[3];
        src.CopyTo(exact, 0);                                 // exact-fit whole
        Console.WriteLine(exact[0] == 1 && exact[1] == 2 && exact[2] == 3);
        int[] tail = new int[4];
        src.CopyTo(tail, 1);                                  // tail-fit
        Console.WriteLine(tail[0] == 0 && tail[1] == 1 && tail[2] == 2 && tail[3] == 3);
        int[] big = new int[8];
        src.CopyTo(big, 5);                                   // deep offset
        Console.WriteLine(big[5] == 1 && big[6] == 2 && big[7] == 3 && big[0] == 0);
        int[] ovr = { 0, 0, 0, 0, 0 };
        src.CopyTo(ovr, 0);                                   // overwrite from 0
        Console.WriteLine(ovr[0] == 1 && ovr[1] == 2 && ovr[2] == 3 && ovr[3] == 0);
        ok = true;
        try { new int[0].CopyTo(new int[3], 0); } catch (Exception) { ok = false; }
        Console.WriteLine(ok);                                // empty src legal
        ok = true;
        try { src.CopyTo(new int[3], 0); } catch (Exception) { ok = false; }
        Console.WriteLine(ok);                                // exact capacity legal
        ok = false;
        try { src.CopyTo(new int[3], 1); } catch (ArgumentException) { ok = true; }
        Console.WriteLine(ok);                                // destination too small
        ok = false;
        try { src.CopyTo(new int[5], -1); } catch (ArgumentOutOfRangeException) { ok = true; }
        Console.WriteLine(ok);                                // negative index
        ok = false;
        try { src.CopyTo((int[])null, 0); } catch (ArgumentNullException) { ok = true; }
        Console.WriteLine(ok);                                // null destination
    }
}
