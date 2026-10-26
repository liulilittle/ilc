//------------------------------------------------------------------------------
// decimalobj.cs -- decimal BOXED-member probe: Decimal.Equals(object) /
// Decimal.CompareTo(object) (net48 decimal.cs:930 / :1001), byte-exact vs the
// net48 oracle. Wired by the icall_decimal_equals_object /
// icall_decimal_compare_object runtime pair (boxingext2.cpp) + the
// MethodMap.Decimal.cs 0x1C arms.
//
// Semantics under test:
//   * Equals(object): null -> false, a non-Decimal object -> false, a boxed
//     Decimal -> 16-byte VALUE equality (scale-insensitive: 1.5m == 1.50m).
//   * CompareTo(object): null -> 1, a non-Decimal object throws
//     ArgumentException, a boxed Decimal -> the -1/0/1 value compare.
//
// Discipline (decimalcmp precedent): every decimal value is a NAMED local;
// the object argument is a named object local (a boxed decimal, null, or a
// non-decimal string). No other LOUD decimal surface.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class DecimalObj {
    public static void Main() {
        decimal a = 1.5m;
        decimal b = 1.50m;
        decimal c = 2m;
        object oa = a;       // boxed Decimal 1.5
        object ob = b;       // boxed Decimal 1.50 (same value, different scale)
        object oc = c;       // boxed Decimal 2
        object onull = null; // null
        object ostr = "x";   // a non-Decimal object

        // ---------- Equals(object) ----------
        Console.WriteLine("eq_same=" + a.Equals(oa));
        Console.WriteLine("eq_scale=" + a.Equals(ob));
        Console.WriteLine("eq_diff=" + a.Equals(oc));
        Console.WriteLine("eq_null=" + a.Equals(onull));
        Console.WriteLine("eq_nonobj=" + a.Equals(ostr));

        // ---------- CompareTo(object) ----------
        Console.WriteLine("cmp_same=" + a.CompareTo(oa));
        Console.WriteLine("cmp_scale=" + a.CompareTo(ob));
        Console.WriteLine("cmp_diff=" + a.CompareTo(oc));
        Console.WriteLine("cmp_null=" + a.CompareTo(onull));
        try {
            Console.WriteLine("cmp_nonobj=" + a.CompareTo(ostr));
        } catch (ArgumentException) {
            Console.WriteLine("cmp_nonobj=ArgumentException");
        }
    }
}}
