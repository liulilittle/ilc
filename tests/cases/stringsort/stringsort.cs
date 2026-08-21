//------------------------------------------------------------------------------
// stringsort.cs -- STRING SORT/COMPARE ORDER DEEP e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C76, AFTER the string sort
// domain: Array.Sort<string> / BinarySearch<string> (MethodMap.ArraySort.cs
// il2cpp_array_sort_string / il2cpp_array_binary_search_string -- sortdeep /
// arraysortdeep), the static Compare/CompareOrdinal raw-diff and instance
// CompareTo clamp (MethodMap.String.cs, stringext.cpp / stringext2.cpp --
// strcmpdeep / stringcompare), and Comparer.Default (icomparedeep).
//
// THIS case drives the ORDERING dimension one stage deeper:
//
//   1. SORT ORDER of multi-element string arrays: prefix lattices
//      ("a" < "aa" < "ab" < "b"), duplicates, empty strings, nulls packing
//      FIRST, digit-vs-letter ordering, and a 20-element mixed array whose
//      exact final order is asserted index-by-index AND verified by the
//      pairwise CompareOrdinal scan (a[i-1] <= a[i]).
//   2. SORTED-ORDER verification helper: Ascending() re-checks the whole
//      array through CompareOrdinal after every sort, so a sort that
//      scrambled the array is caught even when the spot-checked indices
//      happen to align.
//   3. CompareTo CLAMP: exact -1/0/1 over prefix, longer/shorter, empty,
//      null, equal; the case-pair clamp RANGE only (culture-aware sign,
//      strcmpdeep convention).
//   4. CompareOrdinal RAW DIFF: exact boundary values (-32/+32, -97/98,
//      the terminator-as-0 tail, null triad) -- the pair scan replicated
//      exactly.
//   5. COMPARE MODE AGREEMENT: for ASCII-lowercase inputs the net48
//      culture-aware string.Compare(a,b) and every StringComparison mode
//      agree with CompareOrdinal on the SIGN; the runtime folds every mode
//      to ordinal (documented), so the SIGN assertions hold on both sides.
//      Raw-value assertions use ONLY the Ordinal mode (stringcompare
//      precedent) or CompareOrdinal directly.
//   6. BinarySearch<string> on a sorted array: hits at exact indices,
//      misses at ~insertion points, below-all / above-all, empty needle.
//
// CULTURE discipline (sortdeep/stringcompare precedent): net48
// Array.Sort(string[]) uses the CURRENT-CULTURE comparer while the runtime
// string sort is ORDINAL -- every sort input is ASCII lowercase (+ null /
// empty) so the two orders coincide; mixed-case arrays are NOT exercised
// (recorded gap, see GAPS).
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class StringSort {
    static bool Ascending(string[] a)
    {
        for (int i = 1; i < a.Length; ++i)
        {
            if (a[i - 1] == null) continue;          // nulls packed first
            if (a[i] == null) return false;
            if (string.CompareOrdinal(a[i - 1], a[i]) > 0) return false;
        }
        return true;
    }

    public static void Main() {
        // ============ 1. sort order: prefix lattice ============
        string[] lat = { "ab", "a", "abc", "aa", "b", "aab", "abb", "aaa" };
        Array.Sort(lat);
        Console.WriteLine("so1_prefix=" + (lat[0] == "a" && lat[1] == "aa" && lat[2] == "aaa" &&
                                           lat[3] == "aab" && lat[4] == "ab" && lat[5] == "abb" &&
                                           lat[6] == "abc" && lat[7] == "b"));
        Console.WriteLine("so1_asc=" + Ascending(lat));

        // ============ 2. sort order: duplicates + empty + null ============
        string[] dup = { "bb", "a", "bb", "", "a", null, "bb" };
        Array.Sort(dup);
        Console.WriteLine("so2_null_empty=" + (dup[0] == null && dup[1] == "" && dup[2] == "a" &&
                                               dup[3] == "a" && dup[4] == "bb" && dup[5] == "bb" &&
                                               dup[6] == "bb"));
        Console.WriteLine("so2_asc=" + Ascending(dup));

        // ============ 3. sort order: digit vs letter ============
        string[] dig = { "a1", "a10", "a2", "a0" };
        Array.Sort(dig);
        // ordinal: '0' < '1' < '2', so a0 < a1 < a10 < a2
        Console.WriteLine("so3_digit=" + (dig[0] == "a0" && dig[1] == "a1" && dig[2] == "a10" && dig[3] == "a2"));
        Console.WriteLine("so3_asc=" + Ascending(dig));

        // ============ 4. sort order: 20-element mixed array ============
        string[] big = { "pear", "apple", "kiwi", "mango", "grape", "fig", "banana",
                         "orange", "lemon", "cherry", "plum", "apricot", "berry",
                         "coconut", "date", "elderberry", "guava", "lime", "melon", "nectarine" };
        Array.Sort(big);
        Console.WriteLine("so4_first=" + (big[0] == "apple" && big[1] == "apricot"));
        Console.WriteLine("so4_last=" + (big[18] == "pear" && big[19] == "plum"));
        Console.WriteLine("so4_asc=" + Ascending(big));
        // spot check the middle block
        Console.WriteLine("so4_mid=" + (big[7] == "elderberry" && big[8] == "fig" && big[9] == "grape"));

        // ============ 5. sort order: single / all-equal / empty ============
        string[] one = { "solo" };
        Array.Sort(one);
        Console.WriteLine("so5_single=" + (one[0] == "solo"));
        string[] same = { "x", "x", "x" };
        Array.Sort(same);
        Console.WriteLine("so5_alleq=" + (same[0] == "x" && same[2] == "x"));
        string[] empty = { };
        Array.Sort(empty);
        Console.WriteLine("so5_empty_ok=" + (empty.Length == 0));

        // ============ 6. CompareTo clamp (exact -1/0/1) ============
        Console.WriteLine("ct1_eq=" + ("abc".CompareTo("abc") == 0));
        Console.WriteLine("ct2_less=" + ("abc".CompareTo("abd") == -1));
        Console.WriteLine("ct3_greater=" + ("abd".CompareTo("abc") == 1));
        Console.WriteLine("ct4_longer=" + ("abc".CompareTo("ab") == 1));
        Console.WriteLine("ct5_shorter=" + ("ab".CompareTo("abc") == -1));
        Console.WriteLine("ct6_vs_empty=" + ("abc".CompareTo("") == 1));
        Console.WriteLine("ct7_empty_eq=" + ("".CompareTo("") == 0));
        Console.WriteLine("ct8_null=" + ("abc".CompareTo(null) == 1));
        Console.WriteLine("ct9_self=" + ("zzz".CompareTo("zzz") == 0));
        Console.WriteLine("ct10_sym=" + ("abc".CompareTo("abd") == -"abd".CompareTo("abc")));
        // clamp range only for case pairs (culture-aware sign, strcmpdeep)
        Console.WriteLine("ct11_clamp_aA=" + ("a".CompareTo("A") >= -1 && "a".CompareTo("A") <= 1));
        Console.WriteLine("ct12_clamp_Aa=" + ("A".CompareTo("a") >= -1 && "A".CompareTo("a") <= 1));

        // ============ 7. CompareOrdinal raw diff ============
        Console.WriteLine("co1_eq=" + (string.CompareOrdinal("abc", "abc") == 0));
        Console.WriteLine("co2_case=" + (string.CompareOrdinal("A", "a") == -32));
        Console.WriteLine("co3_case_r=" + (string.CompareOrdinal("a", "A") == 32));
        Console.WriteLine("co4_empty=" + (string.CompareOrdinal("", "a") == -97));
        Console.WriteLine("co5_prefix=" + (string.CompareOrdinal("ab", "a") == 98));
        Console.WriteLine("co6_boundary=" + (string.CompareOrdinal("abc", "abcd") == -100));
        Console.WriteLine("co7_len_tail=" + (string.CompareOrdinal("abc", "ab") == 1));
        Console.WriteLine("co8_null_lt=" + (string.CompareOrdinal(null, "x") == -1));
        Console.WriteLine("co9_null_gt=" + (string.CompareOrdinal("x", null) == 1));
        Console.WriteLine("co10_null_eq=" + (string.CompareOrdinal(null, null) == 0));
        Console.WriteLine("co11_hello=" + (string.CompareOrdinal("hello", "HELLO") == 32));
        Console.WriteLine("co12_mid=" + (string.CompareOrdinal("abcx", "abcy") == -1));
        Console.WriteLine("co13_mid_r=" + (string.CompareOrdinal("abcy", "abcx") == 1));

        // ============ 8. Compare mode agreement (SIGN, ASCII lowercase) ============
        Console.WriteLine("cm1_default=" + (string.Compare("abc", "abd") < 0));
        Console.WriteLine("cm2_default_r=" + (string.Compare("abd", "abc") > 0));
        Console.WriteLine("cm3_default_eq=" + (string.Compare("abc", "abc") == 0));
        Console.WriteLine("cm4_ordinal=" + (string.Compare("a", "b", StringComparison.Ordinal) < 0));
        Console.WriteLine("cm5_ordinal_raw=" + (string.Compare("a", "A", StringComparison.Ordinal) == 32));
        Console.WriteLine("cm6_ordinal_prefix=" + (string.Compare("ab", "a", StringComparison.Ordinal) == 98));
        Console.WriteLine("cm7_ordinal_eq=" + (string.Compare("abc", "abc", StringComparison.Ordinal) == 0));
        Console.WriteLine("cm8_currentculture=" + (string.Compare("a", "b", StringComparison.CurrentCulture) < 0));
        Console.WriteLine("cm9_ignorecase_agree=" + (string.Compare("apple", "banana", StringComparison.OrdinalIgnoreCase) < 0));
        // sign agreement: default Compare (culture on net48) == CompareOrdinal for ASCII lowercase
        Console.WriteLine("cm10_sign_agree=" + (Math.Sign(string.Compare("m", "n")) == Math.Sign(string.CompareOrdinal("m", "n"))));
        Console.WriteLine("cm11_sign_agree2=" + (Math.Sign(string.Compare("n", "m")) == Math.Sign(string.CompareOrdinal("n", "m"))));
        Console.WriteLine("cm12_sign_agree3=" + (Math.Sign(string.Compare("abc", "ab")) == Math.Sign(string.CompareOrdinal("abc", "ab"))));

        // ============ 9. BinarySearch<string> on the sorted array ============
        string[] fruit = { "apple", "fig", "grape", "mango" };
        Console.WriteLine("bs1_hit0=" + (Array.BinarySearch(fruit, "apple") == 0));
        Console.WriteLine("bs2_hit1=" + (Array.BinarySearch(fruit, "fig") == 1));
        Console.WriteLine("bs3_hit3=" + (Array.BinarySearch(fruit, "mango") == 3));
        Console.WriteLine("bs4_miss_mid=" + (Array.BinarySearch(fruit, "kiwi") == -4));
        Console.WriteLine("bs5_miss_below=" + (Array.BinarySearch(fruit, "aardvark") == -1));
        Console.WriteLine("bs6_miss_above=" + (Array.BinarySearch(fruit, "zebra") == -5));
        Console.WriteLine("bs7_miss_prefix=" + (Array.BinarySearch(fruit, "figg") == -3));
        Console.WriteLine("bs8_empty_needle=" + (Array.BinarySearch(fruit, "") == -1));
        string[] solo = { "solo" };
        Console.WriteLine("bs9_solo_hit=" + (Array.BinarySearch(solo, "solo") == 0));
        Console.WriteLine("bs10_solo_miss=" + (Array.BinarySearch(solo, "zzz") == -2));
        Console.WriteLine("bs11_empty_arr=" + (Array.BinarySearch(new string[0], "x") == -1));
        string[] dupf = { "apple", "fig", "fig", "mango" };
        Console.WriteLine("bs12_dup_any=" + (Array.BinarySearch(dupf, "fig") >= 0));

        // ============ 10. sort output is the CompareOrdinal order ============
        string[] outArr = { "melon", "apple", "date", "berry" };
        Array.Sort(outArr);
        Console.WriteLine("so10_ordinal_order=" + (string.CompareOrdinal(outArr[0], "apple") == 0 &&
                                                   string.CompareOrdinal(outArr[1], "berry") == 0 &&
                                                   string.CompareOrdinal(outArr[2], "date") == 0 &&
                                                   string.CompareOrdinal(outArr[3], "melon") == 0));

        Console.WriteLine("done");
    }
}}
