//------------------------------------------------------------------------------
// icompbox.cs -- BOXED-VALUE IComparable.CompareTo e2e reference case (net48
// oracle, byte-for-byte). Created in the R→G→C batch, G2 (D3).
//
// Probes the IComparable interface dispatch on RECEIVER-AGNOSTIC calls -- a
// boxed primitive reached through an interface-typed slot
// (`((IComparable)boxed).CompareTo(x)`, the GAP-BOX-3 record). The dispatcher
// lowers receiver-agnostically to il2cpp_icomparable_compare_to (arraysort.h);
// the runtime resolves a boxed BUILTIN receiver through the raw value table
// (Int32/Int64/UInt64/Single/Double/Char/Boolean + String value arms) before
// the reflected vtable path, so the box needs no typeinfo vtable.
//
// net48 semantics pinned here:
//   * boxed value .CompareTo(same-layout value) -> 0 / sign vs neighbors;
//   * .CompareTo(null) -> 1 (a value type is greater than null);
//   * .CompareTo(foreign type) -> ArgumentException ("Object must be of type ...");
//   * a STRING receiver still culture-compares (the string arm of the table).
//
// Byte-stability: every line prints "label=True/False" or a fixed exception
// marker. net48 is the reference baseline; every line must be byte-identical.
//------------------------------------------------------------------------------
using System;

namespace Cases {
public static class ICompBox {
    public static void Main() {
        // ---- 1. boxed Int32 through an IComparable slot ----
        IComparable ci = 42;
        Console.WriteLine(ci.CompareTo(42) == 0);    // True
        Console.WriteLine(ci.CompareTo(41) > 0);     // True
        Console.WriteLine(ci.CompareTo(43) < 0);     // True
        Console.WriteLine(ci.CompareTo(null) > 0);   // True: a value is > null

        // ---- 2. boxed Int64 / Double / Char / Boolean / String ----
        IComparable cl = 100000000000L;
        Console.WriteLine(cl.CompareTo(99999999999L) > 0);  // True
        IComparable cd = 2.5;
        Console.WriteLine(cd.CompareTo(2.5) == 0);          // True
        Console.WriteLine(cd.CompareTo(2.4) > 0);           // True
        IComparable cc = 'z';
        Console.WriteLine(cc.CompareTo('a') > 0);           // True
        IComparable cb = true;
        Console.WriteLine(cb.CompareTo(false) > 0);         // True
        IComparable cs = "abc";
        Console.WriteLine(cs.CompareTo("abc") == 0);        // True
        Console.WriteLine(cs.CompareTo("abd") < 0);         // True

        // ---- 3. foreign-type operand -> ArgumentException ----
        try {
            Console.WriteLine(ci.CompareTo("nope") == 0);
            Console.WriteLine("no-throw");
        } catch (ArgumentException) {
            Console.WriteLine("arg-ex");
        }
        try {
            Console.WriteLine(cd.CompareTo(42) == 0);
            Console.WriteLine("no-throw");
        } catch (ArgumentException) {
            Console.WriteLine("arg-ex");
        }

        // ---- 4. interface-typed receiver from a user method parameter ----
        Console.WriteLine(CompareVia(ci, 41) > 0);          // True
        Console.WriteLine(CompareVia(cs, "abd") < 0);       // True
    }

    static int CompareVia(IComparable a, object b) { return a.CompareTo(b); }
}}