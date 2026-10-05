//------------------------------------------------------------------------------
// replsplit.cs -- System.String.Replace(string,string) + Split(char[]) DEEP
// e2e reference case (net48 oracle, byte-for-byte).
//
// Exercises the runtime surfaces wired at HEAD 8572fa6 / f8a31ea:
//   Replace(string,string) -> icall_string_replace_s_s (il2cpp_string_ext.h):
//     net48 semantics (csc-probe-pinned) -- non-overlapping greedy replace,
//     null/EMPTY newValue DELETES the matches, no-match returns the IDENTITY
//     instance (ReferenceEquals), EMPTY oldValue throws ArgumentException,
//     null oldValue throws ArgumentNullException, and there is NO
//     oldValue==newValue identity fast path (a real match always allocates).
//   Split(char[]) / Split(params char[]) -> icall_string_split: a null OR
//     EMPTY separator array routes to the WHITESPACE overload (the full
//     Char.IsWhiteSpace set incl. NBSP U+00A0); empty segments are RETAINED
//     (StringSplitOptions.None -- the net48 contract); segments are
//     String.Empty instances, never null.
//
// Every active line prints "label=True/False" (or "label=<int>"); the net48
// run is the byte reference the transpiled build must match. Exception probes
// catch the EXACT .NET exception types the runtime throws (ArgumentNull /
// ArgumentException) so the try/catch flags stay byte-identical.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class ReplSplit {
    public static void Main() {
        // ===================== A. Replace(string,string) =====================
        // --- A1. multiple occurrences, all replaced ---
        Console.WriteLine("r_multi=" + ("aXbXc".Replace("X", "1") == "a1b1c"));
        Console.WriteLine("r_multi2=" + ("abcabc".Replace("bc", "BC") == "aBCaBC"));

        // --- A2. no match -> IDENTITY instance + unchanged content ---
        string q = "abc";
        Console.WriteLine("r_nomatch_id=" + ReferenceEquals(q, q.Replace("z", "1")));
        Console.WriteLine("r_nomatch_val=" + (q.Replace("z", "1") == "abc"));

        // --- A3. oldValue longer than the input -> identity, no match ---
        string w = "abc";
        Console.WriteLine("r_oldlonger_id=" + ReferenceEquals(w, w.Replace("abcd", "1")));
        Console.WriteLine("r_oldlonger_val=" + (w.Replace("abcd", "1") == "abc"));

        // --- A4. null newValue -> delete all matches ---
        Console.WriteLine("r_del_null=" + ("aXbXc".Replace("X", null) == "abc"));
        // --- A5. EMPTY newValue -> delete all matches ---
        Console.WriteLine("r_del_empty=" + ("aXbXc".Replace("X", "") == "abc"));

        // --- A6. grow / shrink ---
        Console.WriteLine("r_grow=" + ("ab".Replace("b", "xyz") == "axyz"));
        Console.WriteLine("r_shrink=" + ("aXb".Replace("X", "") == "ab"));

        // --- A7. NON-overlapping semantics (net48 greedy left-to-right) ---
        Console.WriteLine("r_overlap1=" + ("aaa".Replace("aa", "b") == "ba"));
        Console.WriteLine("r_overlap2=" + ("aaaa".Replace("aa", "b") == "bb"));

        // --- A8. old==new WITH a match -> NEW instance (no identity fast path) ---
        string e = "aXb";
        Console.WriteLine("r_same_newinst=" + !ReferenceEquals(e, e.Replace("X", "X")));
        Console.WriteLine("r_same_val=" + (e.Replace("X", "X") == "aXb"));
        // old==new with NO match -> identity (no allocation)
        string e2 = "abc";
        Console.WriteLine("r_same_nomatch_id=" + ReferenceEquals(e2, e2.Replace("z", "z")));

        // --- A9. empty receiver with a non-empty oldValue ---
        Console.WriteLine("r_empty_in=" + ("".Replace("a", "b") == ""));

        // --- A10. Unicode (UTF-16 code-unit replacement) ---
        Console.WriteLine("r_unicode=" + ("caf\u00E9".Replace("\u00E9", "e") == "cafe"));

        // --- A11. chained replaces + leading/trailing deletion + all-replaced ---
        Console.WriteLine("r_chain=" + ("a-b-c".Replace("-", "+").Replace("+", " ") == "a b c"));
        Console.WriteLine("r_lead=" + ("Xabc".Replace("X", "") == "abc"));
        Console.WriteLine("r_tail=" + ("abcX".Replace("X", "") == "abc"));
        Console.WriteLine("r_all=" + ("XXX".Replace("X", "y") == "yyy"));
        Console.WriteLine("r_mixed=" + ("aXbXc".Replace("X", "XYZ") == "aXYZbXYZc"));

        // --- A12. exceptions (exact net48 types, byte-stable flags) ---
        bool ae = false;
        try { "abc".Replace("", "x"); } catch (ArgumentException) { ae = true; }
        Console.WriteLine("r_ae=" + ae);                    // empty oldValue -> ArgumentException
        bool ae2 = false;
        try { "".Replace("", "x"); } catch (ArgumentException) { ae2 = true; }
        Console.WriteLine("r_ae_emptyrecv=" + ae2);         // empty oldValue throws even on empty receiver
        int ae_kind = 0;
        try { "abc".Replace("", "x"); }
        catch (ArgumentNullException) { ae_kind = 1; }
        catch (ArgumentException) { ae_kind = 2; }
        Console.WriteLine("r_ae_kind=" + ae_kind);          // empty oldValue -> ArgumentException (2), NOT ANE (1)
        int ane_kind = 0;
        try { "abc".Replace((string)null, "x"); }
        catch (ArgumentNullException) { ane_kind = 1; }
        catch (ArgumentException) { ane_kind = 2; }
        Console.WriteLine("r_ane_kind=" + ane_kind);        // null oldValue -> ArgumentNullException (1)

        // ===================== B. Split(char[]) explicit separators ==========
        // --- B1. single separator ---
        string[] p1 = "a,b,c".Split(',');
        Console.WriteLine("sp_single_len=" + (p1.Length == 3));
        Console.WriteLine("sp_single_0=" + (p1[0] == "a"));
        Console.WriteLine("sp_single_2=" + (p1[2] == "c"));

        // --- B2. params multi-separator ---
        string[] p2 = "a,b;c".Split(',', ';');
        Console.WriteLine("sp_multi_len=" + (p2.Length == 3));
        Console.WriteLine("sp_multi_1=" + (p2[1] == "b"));
        string[] p3 = "x1,y 2;z".Split(',', ' ', ';');
        Console.WriteLine("sp_multi2_len=" + (p3.Length == 4));
        Console.WriteLine("sp_multi2_2=" + (p3[2] == "2"));

        // --- B3. consecutive separators -> RETAINED empty segment ---
        string[] p4 = "a,,b".Split(',');
        Console.WriteLine("sp_consec_len=" + (p4.Length == 3));
        Console.WriteLine("sp_consec_mid=" + (p4[1] == String.Empty));
        Console.WriteLine("sp_consec_notnull=" + (p4[1] != null));

        // --- B4. leading / trailing / all-separator / separator-only ---
        string[] p5 = ",a,b".Split(',');
        Console.WriteLine("sp_lead_len=" + (p5.Length == 3));
        Console.WriteLine("sp_lead_0=" + (p5[0] == ""));
        string[] p6 = "a,b,".Split(',');
        Console.WriteLine("sp_trail_len=" + (p6.Length == 3));
        Console.WriteLine("sp_trail_2=" + (p6[2] == ""));
        string[] p7 = ",,".Split(',');
        Console.WriteLine("sp_allsep=" + (p7.Length == 3 && p7[0] == "" && p7[1] == "" && p7[2] == ""));
        string[] p9 = ",".Split(',');
        Console.WriteLine("sp_onlysep_len=" + (p9.Length == 2));

        // --- B5. empty receiver / no-separator receiver ---
        string[] p8 = "".Split(',');
        Console.WriteLine("sp_empty_len=" + (p8.Length == 1));
        Console.WriteLine("sp_empty_0=" + (p8[0] == ""));
        string[] p10 = "abc".Split(',');
        Console.WriteLine("sp_nosep_len=" + (p10.Length == 1));
        Console.WriteLine("sp_nosep_0=" + (p10[0] == "abc"));

        // --- B6. repeated params separator -> empty segments ---
        string[] p21 = "x--y--z".Split('-', '-');
        Console.WriteLine("sp_rep_len=" + (p21.Length == 5));
        Console.WriteLine("sp_rep_1=" + (p21[1] == "" && p21[3] == ""));

        // ===================== C. Split(null / empty) = whitespace overload =
        // --- C1. null separator array -> whitespace split ---
        string[] p13 = "a b c".Split((char[])null);
        Console.WriteLine("sp_null_len=" + (p13.Length == 3));
        Console.WriteLine("sp_null_1=" + (p13[1] == "b"));
        string[] p13b = "abc".Split((char[])null);
        Console.WriteLine("sp_null_nospace_len=" + (p13b.Length == 1));

        // --- C2. EMPTY separator array -> whitespace split (net48 quirk) ---
        string[] p14 = "x y".Split(new char[0]);
        Console.WriteLine("sp_emptysep_len=" + (p14.Length == 2));

        // --- C3. whitespace set: tab / newline ---
        string[] p15 = "a\tb\nc".Split((char[])null);
        Console.WriteLine("sp_ws_tabnl_len=" + (p15.Length == 3));

        // --- C4. consecutive whitespace -> retained empties (net48: the two
        //     consecutive spaces yield ONE empty segment; total 4) ---
        string[] p16 = "a  b\tc".Split((char[])null);
        Console.WriteLine("sp_ws_consec_len=" + (p16.Length == 4));
        Console.WriteLine("sp_ws_consec_1=" + (p16[1] == ""));

        // --- C5. leading + trailing whitespace -> edge empties ---
        string[] p17 = "  a b  ".Split((char[])null);
        Console.WriteLine("sp_ws_edges_len=" + (p17.Length == 6));
        Console.WriteLine("sp_ws_edges_0=" + (p17[0] == ""));
        Console.WriteLine("sp_ws_edges_5=" + (p17[5] == ""));

        // --- C6. all-whitespace receiver ---
        string[] p18 = "   ".Split((char[])null);
        Console.WriteLine("sp_ws_all_len=" + (p18.Length == 4));

        // --- C7. NBSP U+00A0 is whitespace ---
        string[] p20 = "a\u00A0b".Split((char[])null);
        Console.WriteLine("sp_nbsp_len=" + (p20.Length == 2));

        // ===================== D. wide char + result composition ============
        // --- D1. BMP wide-char separator ---
        string[] p12 = "a\u4E2Db".Split('\u4E2D');
        Console.WriteLine("sp_wide_len=" + (p12.Length == 2));
        Console.WriteLine("sp_wide=" + (p12[0] == "a" && p12[1] == "b"));

        // --- D2. string[] composition + re-index ---
        string[] p19 = "a,b,c".Split(',');
        Console.WriteLine("sp_join=" + (p19[0] + "|" + p19[1] + "|" + p19[2] == "a|b|c"));
        Console.WriteLine("sp_idx=" + (p19[2][0] == 'c'));
    }
}}
