// p-mvarbv.cs -- GN-0 probe: generic METHOD BY-VALUE MVAR call side
// (GENERICS-FULL-ARG-GAPS.md 1.5/5, P-mvarbv). Locks the status quo; no
// implementation changes in this batch (GN-5 decision is gated on this
// probe's result).
//
// The gap: the M6 user generic-method model expands only T -> int32/uint32;
// any other by-value argument is LOUD-rejected at the call site
// (MethodRefResolver.cs:708-718 EnsureInt32DefaultInstantiation). net48 runs
// all lines byte-exact -- int32 works, long/double/struct are the rejected
// cells. If the transpiled side LOUD-throws on the first non-int32 call, the
// case FAILs at transpile -- that FAIL locks the status quo.
//------------------------------------------------------------------------------
using System;

namespace Cases {

struct Pt {
    public int X;
    public Pt(int x) { X = x; }
}

public static class PMvarBv {
    static T Id<T>(T x) { return x; }

    public static void Main() {
        // int32: the supported cell (M6 T->int32/uint32)
        Console.WriteLine(Id<int>(5));         // 5

        // long: the 1.5 rejected cell
        long l = Id<long>(7L);
        Console.WriteLine(l == 7L);            // True (net48)

        // double: the 1.5 rejected cell
        double d = Id<double>(3.5);
        Console.WriteLine(d == 3.5);           // True (net48)

        // user struct: the 1.5 rejected cell
        Pt p = Id<Pt>(new Pt(9));
        Console.WriteLine(p.X == 9);           // True (net48)
    }
}}