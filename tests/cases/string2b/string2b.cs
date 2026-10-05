//------------------------------------------------------------------------------
// string2b.cs -- System.String DEEP reference program (net48 oracle),
// second-generation string batch (string2b). Extends the static-comparison
// (strcompare), static-concat (stringops), positional-search (strdeep) and
// format (strfmt) surfaces with the SPECIFIC probe families the runtime's
// wired surface exposes:
//
//   * Compare MULTI-FORM   -- 2-arg default (culture==ordinal only for the
//     ASCII lowercase pairs probed here) AND 3-arg StringComparison.Ordinal
//     (the runtime maps Compare(a,b,comparison) to the ordinal static icall
//     with the comparison arg DROPPED -- net48 StringComparison.Ordinal is
//     the identical byte contract).
//   * CompareOrdinal       -- raw -1/0/+1 AND the code-unit delta (net48
//     ordinal diff, e.g. "A" vs "a" == -32).
//   * GetHashCode STABILITY -- same string instance, equal-content strings
//     and freshly-built ("he"+"llo") content all produce IDENTICAL hash
//     values in one process (the runtime's HashStringOrdinal is the net48
//     fixed-seed legacy algorithm -- valuesemantics.cpp; cross-process
//     deterministic for plain console apps, so the raw values are pinned).
//   * ToUpper/ToLower       -- ASCII case transforms (current-culture ==
//     invariant for the ASCII probes).
//   * IsNullOrEmpty          -- null / "" / whitespace / non-empty.
//   * Concat 3-4 args        -- il2cpp_string_concat_many fixed-arity shapes
//     (string,string,string) and (string,string,string,string), plus the
//     2-arg string+int convenience.
//   * Contains AT START      -- substring at index 0 (the icall is a single
//     ordinal search; empty value is contained in everything).
//   * Format COMPOSITE       -- multi-placeholder "{0} {1}" with reordering,
//     brace escaping "{{0}}", and 3-arg composite "x{0}y{2}".
//
// Every line prints "label=True/False" or a pinned raw value, so a diff
// failure shows exactly which assertion flipped. net48 is the baseline.
//
// DELIBERATELY NOT PROBED (documented runtime gaps -- do NOT call here):
//   * Compare(a,b,CurrentCulture / OrdinalIgnoreCase) -- the runtime DROPS
//     the comparison mode (ordinal-only; MethodMap.String.cs Compare/Compare
//     Ordinal). net48 would differ for ignore-case inputs, so only the
//     Ordinal mode is exercised.
//   * {N,width:fmt} alignment/format specifiers in string.Format --
//     NotSupportedException in the composite kernel (stringbuilder.cpp).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class String2b
    {
        public static void Main()
        {
            // ===== Compare: 2-arg default (ASCII lowercase: culture==ordinal) =====
            Console.WriteLine("cmp2_lt=" + (string.Compare("abc", "abd") == -1));
            Console.WriteLine("cmp2_eq=" + (string.Compare("abc", "abc") == 0));
            Console.WriteLine("cmp2_gt=" + (string.Compare("abd", "abc") == 1));
            Console.WriteLine("cmp2_prefix=" + (string.Compare("ab", "abc") == -1));   // length tiebreak
            Console.WriteLine("cmp2_empty=" + (string.Compare("", "") == 0));
            Console.WriteLine("cmp2_null_lt=" + (string.Compare(null, "x") == -1));
            Console.WriteLine("cmp2_null_gt=" + (string.Compare("x", null) == 1));
            Console.WriteLine("cmp2_null_null=" + (string.Compare(null, null) == 0));

            // ===== Compare: 3-arg StringComparison.Ordinal (mode dropped -> ordinal) =====
            Console.WriteLine("cmp3_ord_eq=" + (string.Compare("abc", "abc", StringComparison.Ordinal) == 0));
            Console.WriteLine("cmp3_ord_lt=" + (string.Compare("abc", "abd", StringComparison.Ordinal) == -1));
            Console.WriteLine("cmp3_ord_case=" + (string.Compare("A", "a", StringComparison.Ordinal) != 0));
            Console.WriteLine("cmp3_ord_nulls=" + (string.Compare(null, null, StringComparison.Ordinal) == 0));

            // ===== CompareOrdinal: raw code-unit delta + null semantics =====
            Console.WriteLine("cord_eq=" + (string.CompareOrdinal("abc", "abc") == 0));
            Console.WriteLine("cord_lt=" + (string.CompareOrdinal("abc", "abd") == -1));
            Console.WriteLine("cord_gt=" + (string.CompareOrdinal("abd", "abc") == 1));
            Console.WriteLine("cord_delta=" + (string.CompareOrdinal("A", "a") == -32));   // 65 - 97
            Console.WriteLine("cord_len=" + (string.CompareOrdinal("abc", "ab") == 1));
            Console.WriteLine("cord_null_lt=" + (string.CompareOrdinal(null, "x") == -1));
            Console.WriteLine("cord_null_gt=" + (string.CompareOrdinal("x", null) == 1));
            Console.WriteLine("cord_nulls=" + (string.CompareOrdinal(null, null) == 0));

            // ===== GetHashCode: stability (same value every call) =====
            string h1 = "hello";
            Console.WriteLine("ghc_stable=" + (h1.GetHashCode() == h1.GetHashCode()));
            Console.WriteLine("ghc_equal_content=" + ("hello".GetHashCode() == h1.GetHashCode()));
            Console.WriteLine("ghc_built_equal=" + (("he" + "llo").GetHashCode() == h1.GetHashCode()));
            Console.WriteLine("ghc_empty_stable=" + ("".GetHashCode() == "".GetHashCode()));
            Console.WriteLine("ghc_distinct=" + (h1.GetHashCode() != "world".GetHashCode()));
            // Pinned raw values (cross-process deterministic -- valuesemantics.cpp).
            Console.WriteLine("ghc_hello=" + h1.GetHashCode());
            Console.WriteLine("ghc_world=" + "world".GetHashCode());
            Console.WriteLine("ghc_empty=" + "".GetHashCode());

            // ===== ToUpper / ToLower (ASCII) =====
            Console.WriteLine("toupper=" + ("Hello World".ToUpper() == "HELLO WORLD"));
            Console.WriteLine("tolower=" + ("Hello World".ToLower() == "hello world"));
            Console.WriteLine("toupper_digits=" + ("abc123!@#".ToUpper() == "ABC123!@#"));
            Console.WriteLine("tolower_digits=" + ("ABC123!@#".ToLower() == "abc123!@#"));
            Console.WriteLine("toupper_empty=" + ("".ToUpper() == ""));
            Console.WriteLine("toupper_single=" + ("a".ToUpper() == "A"));

            // ===== IsNullOrEmpty =====
            string nll = null;
            Console.WriteLine("isne_null=" + string.IsNullOrEmpty(nll));
            Console.WriteLine("isne_empty=" + string.IsNullOrEmpty(""));
            Console.WriteLine("isne_space=" + (string.IsNullOrEmpty(" ") == false));
            Console.WriteLine("isne_x=" + (string.IsNullOrEmpty("x") == false));

            // ===== Concat 3 / 4 args (il2cpp_string_concat_many) =====
            Console.WriteLine("concat3=" + (string.Concat("a", "b", "c") == "abc"));
            Console.WriteLine("concat3_empty=" + (string.Concat("", "b", "") == "b"));
            Console.WriteLine("concat4=" + (string.Concat("a", "b", "c", "d") == "abcd"));
            Console.WriteLine("concat4_empty=" + (string.Concat("x", "", "", "y") == "xy"));
            Console.WriteLine("concat2_int=" + (string.Concat("n=", 42) == "n=42"));

            // ===== Contains at start =====
            Console.WriteLine("contains_start=" + "abcdef".Contains("abc"));
            Console.WriteLine("contains_mid=" + "abcdef".Contains("bcd"));
            Console.WriteLine("contains_end=" + "abcdef".Contains("def"));
            Console.WriteLine("contains_full=" + "abc".Contains("abc"));
            Console.WriteLine("contains_empty=" + "abc".Contains(""));
            Console.WriteLine("contains_none=" + ("abcdef".Contains("xyz") == false));
            bool threw = false;
            try { "abc".Contains(nll); } catch (ArgumentNullException) { threw = true; }
            Console.WriteLine("contains_null=" + threw);

            // ===== Format composite (multi-placeholder + escaping) =====
            Console.WriteLine("fmt_two=" + (string.Format("{0} {1}", "a", "b") == "a b"));
            Console.WriteLine("fmt_reorder=" + (string.Format("{1} {0}", "a", "b") == "b a"));
            Console.WriteLine("fmt_int=" + (string.Format("x{0}y", 42) == "x42y"));
            Console.WriteLine("fmt_three=" + (string.Format("{0}-{1}-{2}", 1, 2, 3) == "1-2-3"));
            Console.WriteLine("fmt_escape=" + (string.Format("{{0}}", 0) == "{0}"));
            Console.WriteLine("fmt_escape_pair=" + (string.Format("{{0}} {1}", "x", "y") == "{0} y"));
            Console.WriteLine("fmt_double=" + (string.Format("v={0}", 3.5) == "v=3.5"));
            Console.WriteLine("fmt_empty_arg=" + (string.Format("{0}", "") == ""));
        }
    }
}
