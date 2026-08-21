//------------------------------------------------------------------------------
// sortdeep.cs -- System.Array sorting-family deep reference program (net48).
//
// Verifies the reachable sort-domain wiring end-to-end, every line a BOOL:
//
//   * Array.Sort<int32>        (icall_array_sort)          ascending in-place
//   * Array.Sort<string>       (icall_array_sort_strings)  ordinal, null first
//   * Array.CopyTo(Array, int) (il2cpp_array_copy_to -- the ArraySort-domain
//                              direct call, one of several in
//                              MethodMap.ArraySort.cs reachable behind the
//                              TryMapArray ?? TryMapArraySort chain)
//   * Array.IndexOf<int32>     first-match / -1
//   * Array.BinarySearch<int32/string>  hit index / ~insertion point
//   * Duplicates + empty arrays + CopyTo error paths (AOOE/AE/ANE/ATME)
//
// NOTE: same-array CopyTo overlap is only legal at dstIndex 0 (the capacity
// check src.Length <= dst.Length - dstIndex makes any shift-right throw), so
// no overlap line is exercised -- the memmove safety is Array.Copy territory.
// Two lines are deliberately NOT included:
//   (1) A byte[]/int64[]-element CopyTo line: newarr System.Byte /
//       System.Int64 are not supported by the committed transpiler
//       (CodeGenerator ArrayElementType audit C4 -- only int32/string/object
//       arrays transpile). The int32 element-exact assertions above cover the
//       raw-element-bytes copy semantics.
//   (2) A null-RECEIVER CopyTo line (null source): net48 throws
//       NullReferenceException (instance-call receiver deref), but the
//       runtime's il2cpp_array_copy_to throws ArgumentNullException (its
//       null-arg validation) -- the direct call cannot reproduce receiver-NRE
//       semantics. The null-DESTINATION line below is the equivalent clean
//       parameter-null check.
//
// net48 BASELINE caveats:
//   (1) Array.Sort(string[]) on .NET Framework 4.8 uses the CURRENT-CULTURE
//       comparer, but the runtime's wired string sort is ORDINAL. Observed on
//       zh-CN: {"pear",null,"apple","Cherry","cherry"} -> net48
//       [null,apple,cherry,Cherry,pear] vs ordinal [null,Cherry,apple,cherry,
//       pear]. All string inputs below are ASCII lowercase (+ null) so the two
//       orders coincide; the divergence is a recorded gap (not exercised).
//   (2) BinarySearch on a duplicate value returns AN UNSPECIFIED matching
//       index (.NET contract); the duplicate assertions use the predicate
//       form (index >= 0 && a[index] == value), never an exact index.
//
// LATENT surface (wired but not exercisable here):
//   Array.Sort<long/uint>, Array.IndexOf<string/long>, Array.BinarySearch<long>
//   now resolve to the arraysort.h direct calls (il2cpp_array_sort_int64/
//   uint32, il2cpp_array_index_of_int64/_string, il2cpp_array_binary_search_
//   int64 -- MethodMap.ArraySort.cs, unit-pinned), but no line here can
//   exercise them: the transpiler creates only int32/string/object arrays
//   (CodeGenerator ArrayElementType audit C4), so long[]/uint[] sources
//   cannot be built. Array.Sort<T>(T[], Comparison<T>) stays unwireable
//   (no delegate-to-function-pointer ABI) -- LOUD.
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
        // ================= Array.Sort<int32> =================
        int[] a = { 5, 3, 9, 1, 3, 7, 0, -2 };
        Array.Sort(a);
        Console.WriteLine(a.Length == 8);                       // length unchanged
        Console.WriteLine(Ascending(a));                        // fully ascending
        Console.WriteLine(a[0] == -2 && a[1] == 0 && a[2] == 1 &&   // exact order,
                          a[3] == 3 && a[4] == 3 && a[5] == 5 &&   // duplicates kept
                          a[6] == 7 && a[7] == 9);
        int[] one = { 42 };
        Array.Sort(one);
        Console.WriteLine(one[0] == 42);                        // single element
        bool ok = false;
        try { Array.Sort(new int[0]); ok = true; } catch (Exception) { }
        Console.WriteLine(ok);                                  // empty array no-op

        // ================= Array.Sort<string> =================
        string[] s = { "banana", "apple", "banana" };
        Array.Sort(s);
        Console.WriteLine(s[0] == "apple" && s[1] == "banana" && s[2] == "banana"); // dup kept
        string[] sn = { "pear", null, "apple" };
        Array.Sort(sn);
        Console.WriteLine(sn[0] == null && sn[1] == "apple" && sn[2] == "pear");    // null first
        ok = false;
        try { Array.Sort(new string[0]); ok = true; } catch (Exception) { }
        Console.WriteLine(ok);                                  // empty string[] no-op

        // ================= Array.CopyTo(Array, int) =================
        int[] src = { 1, 2, 3 };
        int[] dst = { 0, 0, 0, 0, 0 };
        src.CopyTo(dst, 1);                                     // value copy at index 1
        Console.WriteLine(dst[0] == 0 && dst[1] == 1 && dst[2] == 2 &&
                          dst[3] == 3 && dst[4] == 0);
        int[] full = new int[3];
        src.CopyTo(full, 0);                                    // whole copy at index 0
        Console.WriteLine(full[0] == 1 && full[1] == 2 && full[2] == 3);
        ok = false;
        try { new int[0].CopyTo(new int[3], 3); ok = true; } catch (Exception) { }
        Console.WriteLine(ok);                                  // empty src legal at dst.Length
        ok = false;
        try { src.CopyTo(dst, -1); } catch (ArgumentOutOfRangeException) { ok = true; }
        Console.WriteLine(ok);                                  // negative index
        ok = false;
        try { src.CopyTo(new int[2], 0); } catch (ArgumentException) { ok = true; }
        Console.WriteLine(ok);                                  // destination too small
        ok = false;
        try { src.CopyTo((int[])null, 0); } catch (ArgumentNullException) { ok = true; }
        Console.WriteLine(ok);                                  // null destination
        ok = false;
        try { src.CopyTo(new string[4], 0); } catch (ArrayTypeMismatchException) { ok = true; }
        Console.WriteLine(ok);                                  // element type mismatch

        // ================= Array.IndexOf<int32> =================
        int[] dup = { 10, 20, 30, 20, 40 };
        Console.WriteLine(Array.IndexOf(dup, 20) == 1);         // hit: first match
        Console.WriteLine(Array.IndexOf(dup, 99) == -1);        // miss
        Console.WriteLine(Array.IndexOf(new int[0], 5) == -1);  // empty array

        // ================= Array.BinarySearch<int32> =================
        int[] sorted = { 1, 3, 5, 7, 9 };
        Console.WriteLine(Array.BinarySearch(sorted, 5) == 2);  // hit
        Console.WriteLine(Array.BinarySearch(sorted, 1) == 0);  // hit at first
        Console.WriteLine(Array.BinarySearch(sorted, 9) == 4);  // hit at last
        Console.WriteLine(Array.BinarySearch(sorted, 4) == -3); // miss: ~insertion(2)
        Console.WriteLine(Array.BinarySearch(sorted, 0) == -1); // below all: ~0
        Console.WriteLine(Array.BinarySearch(sorted, 10) == -6);// above all: ~5
        int[] d3 = { 1, 3, 3, 3, 5 };
        int di = Array.BinarySearch(d3, 3);                     // duplicate: any hit legal
        Console.WriteLine(di >= 0 && d3[di] == 3);
        Console.WriteLine(Array.BinarySearch(new int[0], 5) == -1); // empty: -1 (~0)

        // ================= Array.BinarySearch<string> =================
        string[] fruit = { "pear", "apple", "cherry" };
        Array.Sort(fruit);
        Console.WriteLine(Array.BinarySearch(fruit, "cherry") == 1); // hit
        Console.WriteLine(Array.BinarySearch(fruit, "banana") == -2); // miss: ~1
        Console.WriteLine(Array.BinarySearch(fruit, "zebra") == -4);  // above all: ~3
        string[] fnull = { "apple", null, "pear" };
        Array.Sort(fnull);
        Console.WriteLine(fnull[0] == null && fnull[1] == "apple" &&
                          fnull[2] == "pear");                  // null sorts first
        Console.WriteLine(Array.BinarySearch(fnull, null) == 0);    // null hits null
    }
}
