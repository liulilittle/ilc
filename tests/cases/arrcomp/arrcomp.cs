//------------------------------------------------------------------------------
// arrcomp.cs -- array composition deep chain e2e (net48 reference baseline).
//
// Sections, in order (every line a bool, per e2e convention):
//   1. int[] manual REVERSE -- hand-written two-pointer loop (NOT
//      Array.Reverse; the icall-layer reverse exists but the probe is
//      deliberately manual: plain ld/stelem + loop control);
//   2. element SWAP via temp local;
//   3. Array.Copy with offsets (5-arg overload -> icall_array_copy);
//   4. Length / GetLength (get_Length vs icall_array_get_length);
//   5. Clone -- SKIPPED: not wired (no System.Array.Clone mapping in
//      MethodMap.Array.cs / ArraySort.cs, no runtime entry), documented
//      here so the gap stays visible;
//   6. IndexOf<int32> / BinarySearch<int32> (icall layer);
//   7. reference-element array sort (string[] -> icall_array_sort_strings;
//      all inputs ASCII lowercase so the net48 CURRENT-CULTURE comparer
//      and the runtime ORDINAL sort coincide -- see sortdeep caveat);
//   8. value-element array sort (int[] -> icall_array_sort ascending).
//
// All assertions print True; expected.txt frozen from the net48 reference
// run.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class ArrComp {
    public static void Main() {
        // ---- 1. int[] manual reverse (two-pointer, in place) ----
        int[] r = new int[5];
        for (int i = 0; i < 5; i++) r[i] = i + 1;           // 1,2,3,4,5
        int lo = 0, hi = r.Length - 1;
        while (lo < hi) {
            int t = r[lo];
            r[lo] = r[hi];
            r[hi] = t;
            lo++; hi--;
        }
        Console.WriteLine(r[0] == 5 && r[1] == 4 && r[2] == 3 &&
                          r[3] == 2 && r[4] == 1);

        // ---- 2. element swap via temp ----
        int[] s = new int[3];
        s[0] = 1; s[1] = 2; s[2] = 3;
        int tmp = s[0];
        s[0] = s[2];
        s[2] = tmp;
        Console.WriteLine(s[0] == 3 && s[1] == 2 && s[2] == 1);

        // ---- 3. Array.Copy with offsets (src, srcIdx, dst, dstIdx, len) ----
        int[] src = new int[5];
        for (int i = 0; i < 5; i++) src[i] = i * 10;        // 0,10,20,30,40
        int[] dst = new int[4];
        Array.Copy(src, 2, dst, 1, 2);
        Console.WriteLine(dst[0] == 0 && dst[1] == 20 && dst[2] == 30 &&
                          dst[3] == 0);

        // ---- 4. Length / GetLength ----
        Console.WriteLine(src.Length == 5 && src.GetLength(0) == 5);

        // ---- 5. Clone: not wired (no MethodMap entry / runtime icall) ----
        //     intentionally absent -- the transpiler would LOUD-reject it.

        // ---- 6. IndexOf / BinarySearch (int32) ----
        int[] idx = new int[4];
        idx[0] = 7; idx[1] = 3; idx[2] = 9; idx[3] = 3;
        Console.WriteLine(Array.IndexOf(idx, 3) == 1);      // first match
        Console.WriteLine(Array.IndexOf(idx, 42) == -1);    // miss
        int[] bs = new int[4];
        bs[0] = 1; bs[1] = 4; bs[2] = 7; bs[3] = 9;
        Console.WriteLine(Array.BinarySearch(bs, 7) == 2);  // hit
        Console.WriteLine(Array.BinarySearch(bs, 5) == -3); // miss: ~insertion(2)

        // ---- 7. reference-element array sort (string[]) ----
        string[] names = new string[4];
        names[0] = "pear"; names[1] = "apple";
        names[2] = "cherry"; names[3] = "banana";
        Array.Sort(names);
        Console.WriteLine(names[0] == "apple" && names[1] == "banana" &&
                          names[2] == "cherry" && names[3] == "pear");

        // ---- 8. value-element array sort (int[]) ----
        int[] vals = new int[4];
        vals[0] = 9; vals[1] = 1; vals[2] = 7; vals[3] = 3;
        Array.Sort(vals);
        Console.WriteLine(vals[0] == 1 && vals[1] == 3 &&
                          vals[2] == 7 && vals[3] == 9);
    }
}}
