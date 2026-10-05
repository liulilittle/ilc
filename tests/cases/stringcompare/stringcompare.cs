//------------------------------------------------------------------------------
// stringcompare.cs -- System.String comparison DEEP e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C67 (2026-08-12), AFTER the
// string comparison domain wiring (MethodMap.String.cs: instance CompareTo,
// static Compare/CompareOrdinal, Equals(+StringComparison), op_Equality/
// op_Inequality -- strcompare/strcmpdeep/strcomp2; the ARRAY sort/search
// comparer surface: Array.Sort<string>/BinarySearch<string> -- arraysortdeep;
// System.Collections.Comparer.Default/Compare -- icomparedeep D-ICMP-2/3).
//
// DEEP DIMENSIONS:
//   1. INSTANCE CompareTo -- the CLAMPED sign-only contract (net48 clamps the
//      raw unit/length diff to -1/0/+1): equal, less/greater, prefix, empty,
//      null, and the case-pair CLAMP RANGE only (culture-aware sign, strcmpdeep
//      convention).
//   2. STATIC CompareOrdinal / Compare(a,b) -- the RAW difference (net48
//      CompareOrdinalHelper pair scan replicated exactly): first differing
//      unit's numeric delta, terminator-as-0 at string boundaries, null
//      handling (null==null->0, null<x->-1, x>null->1). The 3-arg
//      Compare(a,b,StringComparison) drops the mode -> ordinal raw diff.
//   3. COMPARER (System.Collections.Comparer.Default / DefaultInvariant): the
//      runtime singleton (il2cpp_comparer_default) + Compare(object, object):
//      null first, string pairs ORDINAL (documented ILC divergence -- string
//      inputs are ASCII lowercase so net48 culture == ordinal), boxed int
//      compare, the sign symmetry.
//   4. SORT ORDER (Array.Sort<string>): the culture quicksort (arrayext2
//      QuickSortRefs) -- null elements sort FIRST, then ASCII lowercase in
//      ordinal order (culture == ordinal for these inputs); the sorted
//      multiset asserted by index. Follows the arraysortdeep ASCII-lowercase
//      discipline (never a culture-sensitive line).
//   5. SORT-ORDER ties: Array.BinarySearch<string> on the sorted array --
//      hits at exact indices, misses at ~insertion points, null needle,
//      empty array -> -1.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections;

class Program
{
    static void Main()
    {
        // ============ 1. instance CompareTo: clamped sign-only ============
        Console.WriteLine("ct_eq=" + ("abc".CompareTo("abc") == 0));
        Console.WriteLine("ct_less=" + ("abc".CompareTo("abd") == -1));
        Console.WriteLine("ct_greater=" + ("abd".CompareTo("abc") == 1));
        Console.WriteLine("ct_prefix_longer=" + ("abc".CompareTo("ab") == 1));
        Console.WriteLine("ct_prefix_shorter=" + ("ab".CompareTo("abc") == -1));
        Console.WriteLine("ct_empty_vs=" + ("abc".CompareTo("") == 1));
        Console.WriteLine("ct_empty_eq=" + ("".CompareTo("") == 0));
        Console.WriteLine("ct_vs_empty=" + ("".CompareTo("abc") == -1));
        Console.WriteLine("ct_null=" + ("abc".CompareTo(null) == 1));
        Console.WriteLine("ct_clamp_range_aA=" + ("a".CompareTo("A") >= -1 && "a".CompareTo("A") <= 1));
        Console.WriteLine("ct_clamp_range_Aa=" + ("A".CompareTo("a") >= -1 && "A".CompareTo("a") <= 1));
        Console.WriteLine("ct_sign_sym=" + ("abc".CompareTo("abd") == -"abd".CompareTo("abc")));
        Console.WriteLine("ct_self=" + ("zzz".CompareTo("zzz") == 0));

        // ============ 2. static CompareOrdinal / Compare: raw difference ============
        Console.WriteLine("ord_eq=" + (string.CompareOrdinal("abc", "abc") == 0));
        Console.WriteLine("ord_case=" + (string.CompareOrdinal("A", "a") == -32));
        Console.WriteLine("ord_case_r=" + (string.CompareOrdinal("a", "A") == 32));
        Console.WriteLine("ord_empty=" + (string.CompareOrdinal("", "a") == -97));
        Console.WriteLine("ord_prefix=" + (string.CompareOrdinal("ab", "a") == 98));
        Console.WriteLine("ord_len_tail=" + (string.CompareOrdinal("abc", "ab") == 1));
        Console.WriteLine("ord_boundary=" + (string.CompareOrdinal("abc", "abcd") == -100));
        Console.WriteLine("ord_null_lt=" + (string.CompareOrdinal(null, "x") == -1));
        Console.WriteLine("ord_null_gt=" + (string.CompareOrdinal("x", null) == 1));
        Console.WriteLine("ord_null_eq=" + (string.CompareOrdinal(null, null) == 0));
        Console.WriteLine("ord_hello=" + (string.CompareOrdinal("hello", "HELLO") == 32));
        Console.WriteLine("cmp_ord=" + (string.Compare("abc", "abd") == -1));
        Console.WriteLine("cmp_ord_raw=" + (string.Compare("a", "A", StringComparison.Ordinal) == 32));
        Console.WriteLine("cmp_ord_boundary=" + (string.Compare("ab", "a", StringComparison.Ordinal) == 98));
        Console.WriteLine("cmp_null=" + (string.Compare(null, "x", StringComparison.Ordinal) == -1));

        // ============ 3. Comparer.Default (inline field form) ============
        // NOTE: Comparer.Default / DefaultInvariant are STATIC FIELDS on net48
        // (ldsfld, not get_ calls) -- the runtime defines the singleton symbol
        // System_Collections_Comparer_sf_Default only; DefaultInvariant has NO
        // runtime symbol (recorded D-SC-1, not asserted). The inline field form
        // matches icomparedeep (the fold inlines the field into the call).
        Console.WriteLine("cmpd_str_lt=" + (Comparer.Default.Compare("a", "b") < 0));
        Console.WriteLine("cmpd_str_gt=" + (Comparer.Default.Compare("b", "a") > 0));
        Console.WriteLine("cmpd_str_eq=" + (Comparer.Default.Compare("a", "a") == 0));
        Console.WriteLine("cmpd_null_lt=" + (Comparer.Default.Compare(null, "a") < 0));
        Console.WriteLine("cmpd_null_gt=" + (Comparer.Default.Compare("a", null) > 0));
        Console.WriteLine("cmpd_null_eq=" + (Comparer.Default.Compare(null, null) == 0));
        Console.WriteLine("cmpd_int_lt=" + (Comparer.Default.Compare(3, 5) < 0));
        Console.WriteLine("cmpd_int_gt=" + (Comparer.Default.Compare(5, 3) > 0));
        Console.WriteLine("cmpd_int_eq=" + (Comparer.Default.Compare(3, 3) == 0));
        Console.WriteLine("cmpd_sym=" + (Comparer.Default.Compare("m", "n") == -Comparer.Default.Compare("n", "m")));

        // ============ 4. sort order: Array.Sort<string> (null first, ordinal rest) ============
        string[] s1 = { "pear", null, "apple", "cherry", "banana" };
        Array.Sort(s1);
        Console.WriteLine("sort_null0=" + (s1[0] == null));
        Console.WriteLine("sort_apple=" + ((string)s1[1] == "apple"));
        Console.WriteLine("sort_banana=" + ((string)s1[2] == "banana"));
        Console.WriteLine("sort_cherry=" + ((string)s1[3] == "cherry"));
        Console.WriteLine("sort_pear=" + ((string)s1[4] == "pear"));
        string[] s2 = { "bb", "a", "ccc", "dddd" };
        Array.Sort(s2);
        Console.WriteLine("sort_len=" + ((string)s2[0] == "a" && (string)s2[1] == "bb" &&
                                         (string)s2[2] == "ccc" && (string)s2[3] == "dddd"));
        string[] s3 = { "ab", "a", "abc", "aa", "ab" };
        Array.Sort(s3);
        Console.WriteLine("sort_dup_prefix=" + ((string)s3[0] == "a" && (string)s3[1] == "aa" &&
                                                (string)s3[2] == "ab" && (string)s3[3] == "ab" &&
                                                (string)s3[4] == "abc"));
        string[] s4 = { "x", "x", "x" };
        Array.Sort(s4);
        Console.WriteLine("sort_alleq=" + ((string)s4[0] == "x" && (string)s4[2] == "x"));
        string[] s5 = { "m", null, null, "a" };
        Array.Sort(s5);
        Console.WriteLine("sort_nulldup=" + (s5[0] == null && s5[1] == null && (string)s5[2] == "a" && (string)s5[3] == "m"));

        // ============ 5. binary search on the sorted array ============
        string[] fruit = { "apple", "fig", "grape", "mango" };
        Console.WriteLine("bs_hit0=" + (Array.BinarySearch(fruit, "apple") == 0));
        Console.WriteLine("bs_hit1=" + (Array.BinarySearch(fruit, "fig") == 1));
        Console.WriteLine("bs_hit3=" + (Array.BinarySearch(fruit, "mango") == 3));
        Console.WriteLine("bs_miss_mid=" + (Array.BinarySearch(fruit, "kiwi") == -4));
        Console.WriteLine("bs_miss_below=" + (Array.BinarySearch(fruit, "aardvark") == -1));
        Console.WriteLine("bs_miss_above=" + (Array.BinarySearch(fruit, "zebra") == -5));
        Console.WriteLine("bs_null_miss=" + (Array.BinarySearch(fruit, null) == -1));
        string[] solo = { "solo" };
        Console.WriteLine("bs_solo_hit=" + (Array.BinarySearch(solo, "solo") == 0));
        Console.WriteLine("bs_solo_miss=" + (Array.BinarySearch(solo, "zzz") == -2));
        Console.WriteLine("bs_empty=" + (Array.BinarySearch(new string[0], "x") == -1));
    }
}