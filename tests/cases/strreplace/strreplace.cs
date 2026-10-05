//------------------------------------------------------------------------------
// strreplace.cs -- System.String.Replace + Split DEEP 2 e2e reference case
// (net48 oracle, byte-for-byte). Created in e2e batch C18 (2026-08-10).
//
// EXTENDS replsplit (Replace(string,string) + Split(char[])) on the FULLY
// WIRED icall surface (unlocked at HEAD: MethodMap.String.cs Replace/Split
// + stringext.cpp icall_string_replace (char,char) / icall_string_replace_s_s
// (string,string) / icall_string_split (char[]) / icall_string_split_options
// (char[]|string[], StringSplitOptions)):
//
//   1) Replace MULTI-FORM: char/char (identity on no-match, new-instance when
//      oldChar==newChar WITH a match, all-occurrence, unicode, chained, empty
//      receiver) AND string/string (multi-char oldValue, whole-string match,
//      repeated newValue, case-sensitivity, result re-use via Length/indexer,
//      delete via null/empty newValue, exceptions).
//   2) Split options ALL FORMS (StringSplitOptions):
//      * char[] + None: empty segments RETAINED;
//      * char[] + RemoveEmptyEntries: empties DROPPED (all-separator -> [],
//        empty receiver -> [], whitespace collapse);
//      * null/empty separator array + options -> WHITESPACE overload;
//      * INVALID options (2 / -1 / 5) -> ArgumentException (enum validated
//        BEFORE splitting);
//      * string[] + None/RemoveEmptyEntries: multi-unit separators, FIRST-
//        match-in-array-order ({ "a","aa" } vs { "aa","a" }), null and EMPTY
//        elements SKIPPED, leading/trailing/all-separator shapes.
//
// Every active line prints "label=True/False" (or "<int>"); the net48 run is
// the byte reference the transpiled build must match.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class StrReplace {
    public static void Main()
    {
        // ===================== A. Replace(char, char) =====================
        // A1. all occurrences replaced / middle-of-string
        Console.WriteLine("rc_multi=" + ("aXbXc".Replace('X', '1') == "a1b1c"));
        Console.WriteLine("rc_mid=" + ("hello world".Replace('o', '0') == "hell0 w0rld"));
        // A2. no occurrence -> IDENTITY instance
        string rcq = "abc";
        Console.WriteLine("rc_nomatch_id=" + ReferenceEquals(rcq, rcq.Replace('z', 'y')));
        Console.WriteLine("rc_nomatch_val=" + (rcq.Replace('z', 'y') == "abc"));
        // A3. oldChar == newChar WITH a match -> NEW (content-equal) instance
        string rce = "aXb";
        Console.WriteLine("rc_same_newinst=" + !ReferenceEquals(rce, rce.Replace('X', 'X')));
        Console.WriteLine("rc_same_val=" + (rce.Replace('X', 'X') == "aXb"));
        // A4. all-same receiver / single char / empty receiver
        Console.WriteLine("rc_all=" + ("aaaa".Replace('a', 'b') == "bbbb"));
        Console.WriteLine("rc_single=" + ("a".Replace('a', 'z') == "z"));
        Console.WriteLine("rc_empty_in=" + ("".Replace('a', 'b') == ""));
        // A5. case-sensitive (only exact code-unit matches)
        Console.WriteLine("rc_case=" + ("aAa".Replace('A', 'b') == "aba"));
        // A6. unicode (UTF-16 code-unit) + chained
        Console.WriteLine("rc_unicode=" + ("caf\u00E9".Replace('\u00E9', 'e') == "cafe"));
        Console.WriteLine("rc_chain=" + ("a-b-c".Replace('-', ' ').Replace(' ', '+') == "a+b+c"));

        // ===================== B. Replace(string, string) ==================
        // B1. multi-char oldValue / whole-string match
        Console.WriteLine("rs_multi=" + ("hello".Replace("ll", "LL") == "heLLo"));
        Console.WriteLine("rs_whole=" + ("abc".Replace("abc", "xyz") == "xyz"));
        // B2. repeated newValue (grow) / delete (shrink)
        Console.WriteLine("rs_grow=" + ("a".Replace("a", "xyz") == "xyz"));
        Console.WriteLine("rs_del=" + ("aXbXc".Replace("X", "") == "abc"));
        Console.WriteLine("rs_del_null=" + ("aXbXc".Replace("X", null) == "abc"));
        // B3. case-sensitive string match
        Console.WriteLine("rs_case=" + ("AbAb".Replace("Ab", "_") == "__"));
        // B4. oldValue as strict substring (not prefix), result re-use
        Console.WriteLine("rs_mid=" + ("xxabcxx".Replace("abc", "Z") == "xxZxx"));
        string rsz = "abab".Replace("ab", "cd");
        Console.WriteLine("rs_len=" + (rsz.Length == 4));
        Console.WriteLine("rs_idx=" + (rsz[1] == 'd'));
        // B5. unicode string oldValue/newValue + non-overlapping greedy
        Console.WriteLine("rs_unicode=" + ("\u4E2D\u6587X\u4E2D\u6587".Replace("X", "-") == "\u4E2D\u6587-\u4E2D\u6587"));
        Console.WriteLine("rs_overlap=" + ("aaaa".Replace("aa", "b") == "bb"));
        // B6. exceptions (exact net48 types)
        int rs_kind = 0;
        try { "abc".Replace("", "x"); } catch (ArgumentException) { rs_kind = 2; }
        Console.WriteLine("rs_empty_old=" + rs_kind);
        int rs_ane = 0;
        try { "abc".Replace((string)null, "x"); } catch (ArgumentNullException) { rs_ane = 1; }
        Console.WriteLine("rs_null_old=" + rs_ane);

        // ============ C. Split(char[], StringSplitOptions) ============
        char[] sepC = { ',' };
        // C1. None retains empty segments (2-arg explicit options form)
        string[] c1 = "a,,b".Split(sepC, StringSplitOptions.None);
        Console.WriteLine("so_none_len=" + (c1.Length == 3));
        Console.WriteLine("so_none_mid=" + (c1[1] == String.Empty));
        Console.WriteLine("so_none_midnn=" + (c1[1] != null));
        // C2. RemoveEmptyEntries drops them
        string[] c2 = "a,,b".Split(sepC, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("so_rem_len=" + (c2.Length == 2));
        Console.WriteLine("so_rem_0=" + (c2[0] == "a"));
        Console.WriteLine("so_rem_1=" + (c2[1] == "b"));
        // C3. leading/trailing separators
        string[] c3 = ",a,".Split(sepC, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("so_rem_edges_len=" + (c3.Length == 1));
        string[] c4 = ",a,".Split(sepC, StringSplitOptions.None);
        Console.WriteLine("so_none_edges_len=" + (c4.Length == 3));
        // C4. all-separator: None keeps 3 empties, Remove -> 0-length
        string[] c5 = ",,".Split(sepC, StringSplitOptions.None);
        Console.WriteLine("so_none_allsep=" + (c5.Length == 3 && c5[0] == "" && c5[2] == ""));
        string[] c6 = ",,".Split(sepC, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("so_rem_allsep_len=" + (c6.Length == 0));
        // C5. empty receiver: None -> [""], Remove -> []
        string[] c7 = "".Split(sepC, StringSplitOptions.None);
        Console.WriteLine("so_none_empty_len=" + (c7.Length == 1));
        Console.WriteLine("so_none_empty_0=" + (c7[0] == ""));
        string[] c8 = "".Split(sepC, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("so_rem_empty_len=" + (c8.Length == 0));
        // C6. null separator array + options -> WHITESPACE overload
        string[] c9 = "a b  c".Split((char[])null, StringSplitOptions.None);
        Console.WriteLine("so_null_ws_len=" + (c9.Length == 4));
        string[] c10 = "a b  c".Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("so_null_rem_len=" + (c10.Length == 3));
        Console.WriteLine("so_null_rem_2=" + (c10[2] == "c"));
        // C7. whitespace + RemoveEmptyEntries collapses consecutive spaces
        string[] c11 = "  a  b  ".Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("so_ws_collapse_len=" + (c11.Length == 2));
        Console.WriteLine("so_ws_collapse_0=" + (c11[0] == "a"));
        // C8. INVALID options -> ArgumentException (validated before split)
        int so_inv2 = 0;
        try { "a,b".Split(sepC, (StringSplitOptions)2); } catch (ArgumentException) { so_inv2 = 1; }
        Console.WriteLine("so_inv2=" + so_inv2);
        int so_invm1 = 0;
        try { "a,b".Split(sepC, (StringSplitOptions)(-1)); } catch (ArgumentException) { so_invm1 = 1; }
        Console.WriteLine("so_invm1=" + so_invm1);

        // ============ D. Split(string[], StringSplitOptions) ============
        // D1. multi-unit separators, None
        string[] d1 = "a##b##c".Split(new string[] { "##" }, StringSplitOptions.None);
        Console.WriteLine("ss_multi_len=" + (d1.Length == 3));
        Console.WriteLine("ss_multi_1=" + (d1[1] == "b"));
        // D2. FIRST-match-in-array-order: { "a","aa" } vs { "aa","a" }
        string[] d2 = "xaaay".Split(new string[] { "a", "aa" }, StringSplitOptions.None);
        string[] d3 = "xaaay".Split(new string[] { "aa", "a" }, StringSplitOptions.None);
        Console.WriteLine("ss_order_a=" + (d2.Length == 4));
        Console.WriteLine("ss_order_b=" + (d3.Length == 3));
        // D3. null / EMPTY elements are SKIPPED
        string[] d4 = "abc".Split(new string[] { null }, StringSplitOptions.None);
        Console.WriteLine("ss_null_elem_len=" + (d4.Length == 1));
        Console.WriteLine("ss_null_elem_0=" + (d4[0] == "abc"));
        string[] d5 = "abc".Split(new string[] { "" }, StringSplitOptions.None);
        Console.WriteLine("ss_empty_elem_len=" + (d5.Length == 1));
        Console.WriteLine("ss_empty_elem_0=" + (d5[0] == "abc"));
        // D4. leading/trailing/all-separator with string[]
        string[] d6 = "##a##".Split(new string[] { "##" }, StringSplitOptions.None);
        Console.WriteLine("ss_edges_len=" + (d6.Length == 3));
        Console.WriteLine("ss_edges_0=" + (d6[0] == ""));
        Console.WriteLine("ss_edges_2=" + (d6[2] == ""));
        string[] d7 = "##a##".Split(new string[] { "##" }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("ss_edges_rem_len=" + (d7.Length == 1));
        Console.WriteLine("ss_edges_rem_0=" + (d7[0] == "a"));
        string[] d8 = "##".Split(new string[] { "##" }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("ss_allsep_rem_len=" + (d8.Length == 0));
        string[] d9 = "##".Split(new string[] { "##" }, StringSplitOptions.None);
        Console.WriteLine("ss_allsep_none_len=" + (d9.Length == 2));
        // D5. distinct multi separators / null string[] array -> whitespace
        string[] d10 = "a, b;c".Split(new string[] { ", ", ";" }, StringSplitOptions.None);
        Console.WriteLine("ss_two_len=" + (d10.Length == 3));
        Console.WriteLine("ss_two_1=" + (d10[1] == "b"));
        string[] d11 = "x y".Split((string[])null, StringSplitOptions.None);
        Console.WriteLine("ss_null_arr_len=" + (d11.Length == 2));
        // D6. RemoveEmptyEntries with string[] collapses consecutive seps
        string[] d12 = "a###b".Split(new string[] { "##", "#" }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("ss_rem_collapse_len=" + (d12.Length == 2));
        Console.WriteLine("ss_rem_collapse_1=" + (d12[1] == "b"));
    }
}}
