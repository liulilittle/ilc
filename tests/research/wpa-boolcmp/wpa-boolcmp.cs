using System;
using System.Collections.Generic;

// wpa-boolcmp -- audit B �? does Array.Sort over a BOOL[] element array ever
// silently dereference the 1-byte cells as int32?
//
// The runtime's comparer-sort core keys on arr->elemSize (arraysort.cpp
// SortIComparerCore: isRefElem / elemSize==4 / else throw) while the
// element-kind-driven sorts fall back to "int32 for anything but 8 bytes"
// (ReadAnyCompareKind). bool[] cells are 1 byte, so every sort shape over a
// bool[] must either be rejected LOUDLY or compare exactly one byte.
//
// Shapes probed here (each guarded so one loud arm does not hide the others):
//   1. Array.Sort<bool>(b)                    -- default comparer
//   2. Array.Sort<bool>(b, cmp)               -- IComparer<bool>
//   3. Array.Sort((Array)b)                   -- non-generic
//   4. Array.Sort((Array)b, (IComparer)cmp)   -- non-generic + comparer
//   5. Array.Sort<bool>(b, 0, b.Length, cmp)  -- range + comparer
class P
{
    sealed class Cmp : IComparer<bool>
    {
        public int Compare(bool x, bool y) { return x == y ? 0 : (x ? 1 : -1); }
    }

    sealed class ObjCmp : System.Collections.IComparer
    {
        public int Compare(object x, object y)
        {
            bool a = (bool)x, b = (bool)y;
            return a == b ? 0 : (a ? 1 : -1);
        }
    }

    static void Run(string name, Action a)
    {
        try { a(); }
        catch (Exception e) { Console.WriteLine("[wpa-boolcmp] " + name + " EX:" + e.GetType().Name); }
    }

    static bool[] Fresh() { return new[] { true, false, true, false, true }; }

    static void Dump(string name, bool[] b)
    {
        var s = "";
        foreach (var v in b) s += v ? "1" : "0";
        Console.WriteLine("[wpa-boolcmp] " + name + " " + s);
    }

    static void Main()
    {
        Run("default", () => { var b = Fresh(); Array.Sort(b); Dump("default", b); });
        Run("icomparer", () => { var b = Fresh(); Array.Sort(b, new Cmp()); Dump("icomparer", b); });
        Run("nongeneric", () => { var b = Fresh(); Array.Sort((Array)b); Dump("nongeneric", b); });
        Run("nongeneric_cmp", () => { var b = Fresh(); Array.Sort((Array)b, (System.Collections.IComparer)new ObjCmp()); Dump("nongeneric_cmp", b); });
        Run("range_cmp", () => { var b = Fresh(); Array.Sort(b, 0, b.Length, new Cmp()); Dump("range_cmp", b); });
        // control: the 1-byte cell layout itself (no sort involved)
        var c = Fresh();
        Console.WriteLine("[wpa-boolcmp] cells " + (c[0] ? 1 : 0) + (c[1] ? 1 : 0) + (c[2] ? 1 : 0) + (c[3] ? 1 : 0) + (c[4] ? 1 : 0));
    }
}
