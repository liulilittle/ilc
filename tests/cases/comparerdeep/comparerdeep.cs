//------------------------------------------------------------------------------
// comparerdeep.cs -- System.Collections.Comparer (Comparer.Default) DEEP e2e
// reference case #2 (net48 oracle, byte-for-byte). Created in e2e batch C85,
// AFTER the comparer domain closure (D-ICMP-1/2/3 wired: icomparedeep +
// boxops2 + stringcompare pin the default-comparer singleton + interface
// dispatch + comparer-driven sorts).
//
// WHERE THIS GOES DEEPER THAN THE PREDECESSORS:
//   icomparedeep section 7 covers Comparer.Default on a small sample
//   (int/double/string/null/Persons); boxops2 section 1 asserts the SIGN of
//   Compare on one pair per primitive. comparerdeep drives the FULL-TYPE GRID
//   of the runtime default comparer (ComparerCompareCore, arraysort.cpp:982)
//   -- the types it dispatches BY VALUE: System.Int32, System.UInt32,
//   System.Int64/System.UInt64, System.Double/System.Single, System.Char,
//   System.Boolean, System.String (ordinal) and the null handling -- with
//   BOUNDARY values (min/max per type), exact -1/0/1 signs, sign symmetry
//   (Compare(a,b) == -Compare(b,a)) and transitivity triples, plus the user
//   IComparable FALLBACK through Comparer.Default and CUSTOM comparer objects
//   (concrete + interface dispatch) driving Array.Sort.
//
// PROBE RULES (all pinned to the wired surface):
//   * Comparer.Default is used INLINE ONLY (Comparer.Default.Compare(...) --
//     the D-SC-2 pattern: storing the singleton in a local types the slot
//     int32_t and fails the C++ build; the inline form folds clean).
//   * No NaN, no cross-type numeric pairs (net48 Int32.CompareTo(object)
//     rejects a boxed Int64 -- a recorded gap), no DateTime/TimeSpan/Guid/
//     Decimal boxes (the fallback dispatch has no reflected CompareTo slot for
//     primitive boxes).
//   * String pairs are ASCII lowercase (the sortdeep precedent) -- net48
//     String.CompareTo is culture-sensitive while the runtime is ordinal; the
//     ASCII-lowercase inputs keep both byte-identical.
//   * Every active line prints a labeled bool (or int); the net48 run is the
//     byte reference the transpiled build must match.
//------------------------------------------------------------------------------
using System;
using System.Collections;

class Item : IComparable {
    public string Name;
    public int Code;
    public Item(string n, int c) { Name = n; Code = c; }
    public int CompareTo(object o) {
        if (o == null) return 1;                       // null sorts before any item
        Item other = (Item)o;
        if (Code < other.Code) return -1;
        if (Code > other.Code) return 1;
        return 0;
    }
}

class ItemNameComparer : IComparer {
    // code first, then name -- a custom total order on Item
    public int Compare(object a, object b) {
        Item x = (Item)a;
        Item y = (Item)b;
        if (x.Code < y.Code) return -1;
        if (x.Code > y.Code) return 1;
        return x.Name.CompareTo(y.Name);
    }
}

class ReverseStringComparer : IComparer {
    // descending ordinal (null sorts LAST in this custom order)
    public int Compare(object a, object b) {
        string x = (string)a;
        string y = (string)b;
        if (x == null) return y == null ? 0 : 1;
        if (y == null) return -1;
        return y.CompareTo(x);
    }
}

namespace Cases { public static class ComparerDeep {
    public static void Main() {
        // ============ A. FULL-TYPE GRID: boundaries + sign ============
        // A1. System.Int32 -- min/max/zero/negative boundaries
        Console.WriteLine("i_min0=" + (Comparer.Default.Compare((object)int.MinValue, (object)0) < 0));
        Console.WriteLine("i_0max=" + (Comparer.Default.Compare((object)0, (object)int.MaxValue) < 0));
        Console.WriteLine("i_maxmin=" + (Comparer.Default.Compare((object)int.MaxValue, (object)int.MinValue) > 0));
        Console.WriteLine("i_neg=" + (Comparer.Default.Compare((object)(-5), (object)3) < 0));
        Console.WriteLine("i_self=" + (Comparer.Default.Compare((object)7, (object)7) == 0));
        // exact signs
        Console.WriteLine("i_sgn_m1=" + (Comparer.Default.Compare((object)3, (object)5) == -1));
        Console.WriteLine("i_sgn_p1=" + (Comparer.Default.Compare((object)5, (object)3) == 1));
        Console.WriteLine("i_sgn_0=" + (Comparer.Default.Compare((object)4, (object)4) == 0));

        // A2. System.UInt32
        Console.WriteLine("u_0max=" + (Comparer.Default.Compare((object)0u, (object)4000000000u) < 0));
        Console.WriteLine("u_max0=" + (Comparer.Default.Compare((object)4000000000u, (object)0u) > 0));
        Console.WriteLine("u_eq=" + (Comparer.Default.Compare((object)4000000000u, (object)4000000000u) == 0));
        Console.WriteLine("u_sgn=" + (Comparer.Default.Compare((object)1u, (object)2u) == -1));

        // A3. System.Int64 -- NOTE: only NON-NEGATIVE long probes. The runtime
        // reads the boxed Int64 payload as uint64_t and compares UNSIGNED
        // (arraysort.cpp ComparerCompareCore), while net48 Int64.CompareTo is
        // SIGNED -- a negative-long probe diverges (D-CMP-LONG, GAPS). The
        // non-negative surface is byte-exact.
        Console.WriteLine("l_minmax=" + (Comparer.Default.Compare((object)0L, (object)long.MaxValue) < 0));
        Console.WriteLine("l_neg=" + (Comparer.Default.Compare((object)5L, (object)9L) < 0));
        Console.WriteLine("l_pos=" + (Comparer.Default.Compare((object)9L, (object)5L) > 0));
        Console.WriteLine("l_eq=" + (Comparer.Default.Compare((object)1234567890123L, (object)1234567890123L) == 0));
        Console.WriteLine("l_max0=" + (Comparer.Default.Compare((object)long.MaxValue, (object)0L) > 0));

        // A4. System.UInt64
        Console.WriteLine("ul_0max=" + (Comparer.Default.Compare((object)0UL, (object)ulong.MaxValue) < 0));
        Console.WriteLine("ul_max0=" + (Comparer.Default.Compare((object)ulong.MaxValue, (object)0UL) > 0));
        Console.WriteLine("ul_sgn=" + (Comparer.Default.Compare((object)5UL, (object)18446744073709551615UL) == -1));

        // A5. System.Double
        Console.WriteLine("d_neg0=" + (Comparer.Default.Compare((object)(-0.0), (object)0.0) == 0));
        Console.WriteLine("d_fract=" + (Comparer.Default.Compare((object)0.5, (object)1.5) < 0));
        Console.WriteLine("d_rev=" + (Comparer.Default.Compare((object)1.5, (object)0.5) > 0));
        Console.WriteLine("d_eq=" + (Comparer.Default.Compare((object)3.25, (object)3.25) == 0));
        Console.WriteLine("d_minmax=" + (Comparer.Default.Compare((object)double.MinValue, (object)double.MaxValue) < 0));
        Console.WriteLine("d_sgn=" + (Comparer.Default.Compare((object)1.5, (object)3.25) == -1));

        // A6. System.Single
        Console.WriteLine("f_neg=" + (Comparer.Default.Compare((object)(-1.5f), (object)1.5f) < 0));
        Console.WriteLine("f_pos=" + (Comparer.Default.Compare((object)1.5f, (object)(-1.5f)) > 0));
        Console.WriteLine("f_eq=" + (Comparer.Default.Compare((object)9.5f, (object)9.5f) == 0));
        Console.WriteLine("f_minmax=" + (Comparer.Default.Compare((object)float.MinValue, (object)float.MaxValue) < 0));

        // A7. System.Char
        Console.WriteLine("c_az=" + (Comparer.Default.Compare((object)'A', (object)'Z') < 0));
        Console.WriteLine("c_za=" + (Comparer.Default.Compare((object)'Z', (object)'A') > 0));
        Console.WriteLine("c_nul=" + (Comparer.Default.Compare((object)'\0', (object)'A') < 0));
        Console.WriteLine("c_eq=" + (Comparer.Default.Compare((object)'a', (object)'a') == 0));
        Console.WriteLine("c_max=" + (Comparer.Default.Compare((object)'\uFFFF', (object)'A') > 0));

        // A8. System.Boolean
        Console.WriteLine("b_ft=" + (Comparer.Default.Compare((object)false, (object)true) < 0));
        Console.WriteLine("b_tf=" + (Comparer.Default.Compare((object)true, (object)false) > 0));
        Console.WriteLine("b_eq=" + (Comparer.Default.Compare((object)true, (object)true) == 0));

        // A9. System.String -- ASCII lowercase (ordinal-safe inputs)
        Console.WriteLine("s_ab=" + (Comparer.Default.Compare((object)"apple", (object)"banana") < 0));
        Console.WriteLine("s_ba=" + (Comparer.Default.Compare((object)"banana", (object)"apple") > 0));
        Console.WriteLine("s_empty=" + (Comparer.Default.Compare((object)"", (object)"a") < 0));
        Console.WriteLine("s_prefix=" + (Comparer.Default.Compare((object)"ab", (object)"abc") < 0));
        Console.WriteLine("s_eq=" + (Comparer.Default.Compare((object)"same", (object)"same") == 0));
        Console.WriteLine("s_a_aa=" + (Comparer.Default.Compare((object)"a", (object)"aa") < 0));

        // ============ B. null grid (null sorts first everywhere) ============
        Console.WriteLine("n_nn=" + (Comparer.Default.Compare(null, null) == 0));
        Console.WriteLine("n_int=" + (Comparer.Default.Compare(null, (object)5) < 0));
        Console.WriteLine("n_int_r=" + (Comparer.Default.Compare((object)5, null) > 0));
        Console.WriteLine("n_str=" + (Comparer.Default.Compare(null, (object)"a") < 0));
        Console.WriteLine("n_bool=" + (Comparer.Default.Compare(null, (object)true) < 0));
        Console.WriteLine("n_char=" + (Comparer.Default.Compare((object)'x', null) > 0));
        Console.WriteLine("n_long=" + (Comparer.Default.Compare((object)(-3L), null) > 0));
        Console.WriteLine("n_double=" + (Comparer.Default.Compare(null, (object)1.5) < 0));

        // ============ C. sign symmetry: Compare(a,b) == -Compare(b,a) ============
        Console.WriteLine("sym_int=" + (Comparer.Default.Compare((object)3, (object)5) == -Comparer.Default.Compare((object)5, (object)3)));
        Console.WriteLine("sym_long=" + (Comparer.Default.Compare((object)(-9L), (object)9L) == -Comparer.Default.Compare((object)9L, (object)(-9L))));
        Console.WriteLine("sym_double=" + (Comparer.Default.Compare((object)0.5, (object)1.5) == -Comparer.Default.Compare((object)1.5, (object)0.5)));
        Console.WriteLine("sym_char=" + (Comparer.Default.Compare((object)'a', (object)'z') == -Comparer.Default.Compare((object)'z', (object)'a')));
        Console.WriteLine("sym_bool=" + (Comparer.Default.Compare((object)false, (object)true) == -Comparer.Default.Compare((object)true, (object)false)));
        Console.WriteLine("sym_str=" + (Comparer.Default.Compare((object)"a", (object)"z") == -Comparer.Default.Compare((object)"z", (object)"a")));

        // ============ D. transitivity triples ============
        Console.WriteLine("tr_int=" + (Comparer.Default.Compare((object)1, (object)2) < 0
                                     && Comparer.Default.Compare((object)2, (object)3) < 0
                                     && Comparer.Default.Compare((object)1, (object)3) < 0));
        Console.WriteLine("tr_str=" + (Comparer.Default.Compare((object)"a", (object)"b") < 0
                                     && Comparer.Default.Compare((object)"b", (object)"c") < 0
                                     && Comparer.Default.Compare((object)"a", (object)"c") < 0));
        Console.WriteLine("tr_double=" + (Comparer.Default.Compare((object)0.1, (object)0.2) < 0
                                        && Comparer.Default.Compare((object)0.2, (object)0.3) < 0
                                        && Comparer.Default.Compare((object)0.1, (object)0.3) < 0));

        // ============ E. user IComparable fallback through Comparer.Default ============
        Item alpha = new Item("alpha", 1);
        Item beta = new Item("beta", 2);
        Item gamma = new Item("gamma", 3);
        Console.WriteLine("u_lt=" + (Comparer.Default.Compare((object)alpha, (object)beta) < 0));
        Console.WriteLine("u_gt=" + (Comparer.Default.Compare((object)beta, (object)alpha) > 0));
        Console.WriteLine("u_eq=" + (Comparer.Default.Compare((object)alpha, (object)alpha) == 0));
        Console.WriteLine("u_tr=" + (Comparer.Default.Compare((object)beta, (object)gamma) < 0
                                   && Comparer.Default.Compare((object)alpha, (object)gamma) < 0));
        Console.WriteLine("u_sym=" + (Comparer.Default.Compare((object)alpha, (object)beta) == -Comparer.Default.Compare((object)beta, (object)alpha)));
        Item alpha2 = new Item("alpha2", 1);
        Console.WriteLine("u_same=" + (Comparer.Default.Compare((object)alpha, (object)alpha2) == 0));
        Console.WriteLine("u_null=" + (Comparer.Default.Compare(null, (object)alpha) < 0));
        Console.WriteLine("u_null_r=" + (Comparer.Default.Compare((object)alpha, null) > 0));

        // ============ F. custom IComparer: concrete + interface dispatch ============
        ItemNameComparer inc = new ItemNameComparer();
        Console.WriteLine("inc_lt=" + (inc.Compare(beta, alpha) > 0));
        Console.WriteLine("inc_gt=" + (inc.Compare(alpha, beta) < 0));
        Item beta2 = new Item("beta", 2);
        Console.WriteLine("inc_same=" + (inc.Compare(beta, beta2) == 0));
        Console.WriteLine("inc_tr=" + (inc.Compare(alpha, gamma) < 0));
        IComparer incI = inc;
        Console.WriteLine("inc_iface=" + (incI.Compare(gamma, alpha) > 0));
        ReverseStringComparer rsc = new ReverseStringComparer();
        Console.WriteLine("rsc_lt=" + (rsc.Compare("z", "a") < 0));
        Console.WriteLine("rsc_gt=" + (rsc.Compare("a", "z") > 0));
        Console.WriteLine("rsc_eq=" + (rsc.Compare("b", "b") == 0));
        Console.WriteLine("rsc_null=" + (rsc.Compare(null, "a") > 0));
        Console.WriteLine("rsc_null2=" + (rsc.Compare("a", null) < 0));
        IComparer rscI = rsc;
        Console.WriteLine("rsc_iface=" + (rscI.Compare("a", "b") > 0));

        // ============ G. comparer-driven Array.Sort usage ============
        int[] ia = new int[] { 9, 1, 7, 3, 5 };
        Array.Sort(ia, Comparer.Default);
        Console.WriteLine("sort_i=" + (ia[0] == 1 && ia[1] == 3 && ia[2] == 5 && ia[3] == 7 && ia[4] == 9));
        object[] items = new object[] { (object)gamma, (object)alpha, (object)beta2, (object)beta, (object)alpha2 };
        Array.Sort(items, new ItemNameComparer());
        Console.WriteLine("sort_items=" + (((Item)items[0]).Code == 1 && ((Item)items[1]).Code == 1
                                         && ((Item)items[2]).Code == 2 && ((Item)items[3]).Code == 2
                                         && ((Item)items[4]).Code == 3));
        string[] ss = new string[] { "pear", "apple", "cherry", "banana" };
        Array.Sort(ss, new ReverseStringComparer());
        Console.WriteLine("sort_str=" + ((string)ss[0] == "pear" && (string)ss[1] == "cherry"
                                       && (string)ss[2] == "banana" && (string)ss[3] == "apple"));
    }
}}
