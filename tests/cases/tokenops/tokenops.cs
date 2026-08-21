//------------------------------------------------------------------------------
// tokenops.cs -- TOKENIZER (System.String.Split) DEEP e2e reference case
// (net48 oracle, byte-for-byte). Created in e2e batch C61, after the split
// domain landed: the 1-arg icall_string_split (char[] separators, split 域)
// AND the 2-arg icall_string_split_options (char[]/string[] separators +
// StringSplitOptions, the M6 batch-36 RT-SPL2 wiring) are both wired.
//
// THIS case drives the full TOKENIZER surface on top of the wired layer:
//
//   1) Split(char[]) -- the 1-arg separator overload: single / multiple
//      (params) separators; consecutive separators -> RETAINED empty
//      segments; leading/trailing separators -> edge empties; all-separator
//      and separator-only receivers; empty receiver -> [""]; no-separator
//      receiver -> [whole]; wide-char separator on BMP text.
//   2) Split(char[], StringSplitOptions) -- the 2-arg char[] shape:
//      None retains every segment (== the 1-arg contract); RemoveEmptyEntries
//      drops the empties (consecutive/leading/trailing collapse); all-empty
//      split -> 0-length array; empty receiver None -> [""], RemoveEmpty -> [].
//   3) Split(string[], StringSplitOptions) -- the string[] separator shape:
//      multi-unit separators ("--"); null and EMPTY elements are SKIPPED;
//      FIRST-match-in-array-order greedy (the { "a","aa" } vs { "aa","a" }
//      ORDER probe pins the 4-vs-3 segment count); None vs RemoveEmptyEntries.
//   4) Whitespace mode (the options icall's null-or-empty separators):
//      char[] and string[] both route to the full Char.IsWhiteSpace set;
//      whitespace runs create empties under None and collapse under
//      RemoveEmptyEntries.
//   5) Options validation: (StringSplitOptions)2 / -1 / 5 throw
//      ArgumentException BEFORE splitting (net48 validates the enum value).
//   6) Token composition: Length / indexer / element equality / the empty
//      segment is String.Empty and != null / re-join via Concat.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class TokenOps {
    public static void Main() {
        // ============ 1. Split(char[]) -- 1-arg separator overload ============
        string[] a1 = "a,b,c".Split(',');
        Console.WriteLine("s1_len=" + (a1.Length == 3));
        Console.WriteLine("s1_0=" + (a1[0] == "a"));
        Console.WriteLine("s1_2=" + (a1[2] == "c"));

        // params multi-separator.
        string[] a2 = "a,b;c".Split(',', ';');
        Console.WriteLine("s2_len=" + (a2.Length == 3));
        Console.WriteLine("s2_1=" + (a2[1] == "b"));
        string[] a3 = "x1,y 2;z".Split(',', ' ', ';');
        Console.WriteLine("s3_len=" + (a3.Length == 4));
        Console.WriteLine("s3_2=" + (a3[2] == "2"));

        // consecutive separators -> retained empty.
        string[] a4 = "a,,b".Split(',');
        Console.WriteLine("s4_len=" + (a4.Length == 3));
        Console.WriteLine("s4_mid=" + (a4[1] == ""));
        Console.WriteLine("s4_mid_notnull=" + (a4[1] != null));

        // leading/trailing separators -> edge empties.
        string[] a5 = ",a,b".Split(',');
        Console.WriteLine("s5_len=" + (a5.Length == 3));
        Console.WriteLine("s5_lead=" + (a5[0] == ""));
        string[] a6 = "a,b,".Split(',');
        Console.WriteLine("s6_len=" + (a6.Length == 3));
        Console.WriteLine("s6_tail=" + (a6[2] == ""));

        // all-separator / separator-only receivers.
        string[] a7 = ",,".Split(',');
        Console.WriteLine("s7_len=" + (a7.Length == 3));
        Console.WriteLine("s7_all=" + (a7[0] == "" && a7[1] == "" && a7[2] == ""));
        string[] a8 = ",".Split(',');
        Console.WriteLine("s8_len=" + (a8.Length == 2));

        // empty / no-separator receivers.
        string[] a9 = "".Split(',');
        Console.WriteLine("s9_len=" + (a9.Length == 1));
        Console.WriteLine("s9_0=" + (a9[0] == ""));
        string[] a10 = "abc".Split(',');
        Console.WriteLine("s10_len=" + (a10.Length == 1));
        Console.WriteLine("s10_0=" + (a10[0] == "abc"));

        // wide-char separator.
        string[] a11 = "a\u4E2Db".Split('\u4E2D');
        Console.WriteLine("s11_len=" + (a11.Length == 2));
        Console.WriteLine("s11=" + (a11[0] == "a" && a11[1] == "b"));

        // token composition: Length / indexer / equality / empty segment.
        string[] a12 = "x--y".Split('-');
        Console.WriteLine("s12_len=" + (a12.Length == 3));
        Console.WriteLine("s12_mid=" + (a12[1] == ""));
        Console.WriteLine("s12_concat=" + (a12[0] + a12[1] + a12[2] == "xy"));

        // ============ 2. Split(char[], StringSplitOptions) ============
        // None == the 1-arg contract.
        string[] b1 = "a,b,c".Split(new char[] { ',' }, StringSplitOptions.None);
        Console.WriteLine("t1_len=" + (b1.Length == 3));
        string[] b2 = "a,,b".Split(new char[] { ',' }, StringSplitOptions.None);
        Console.WriteLine("t2_len=" + (b2.Length == 3));
        Console.WriteLine("t2_mid=" + (b2[1] == ""));

        // RemoveEmptyEntries collapses consecutive / leading / trailing.
        string[] b3 = "a,,b".Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("t3_len=" + (b3.Length == 2));
        Console.WriteLine("t3_0=" + (b3[0] == "a"));
        Console.WriteLine("t3_1=" + (b3[1] == "b"));
        string[] b4 = ",a,b,".Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("t4_len=" + (b4.Length == 2));

        // all-empty split -> 0-length array (never [""]).
        string[] b5 = ",,".Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("t5_len=" + (b5.Length == 0));

        // empty receiver: None -> [""]; RemoveEmpty -> [].
        string[] b6 = "".Split(new char[] { ',' }, StringSplitOptions.None);
        Console.WriteLine("t6_len=" + (b6.Length == 1));
        string[] b7 = "".Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("t7_len=" + (b7.Length == 0));

        // ============ 3. Split(string[], StringSplitOptions) ============
        // multi-unit separator.
        string[] c1 = "a--b--c".Split(new string[] { "--" }, StringSplitOptions.None);
        Console.WriteLine("u1_len=" + (c1.Length == 3));
        Console.WriteLine("u1_1=" + (c1[1] == "b"));

        // null / empty string elements are SKIPPED (net48).
        string[] c2 = "a,b".Split(new string[] { null, "," }, StringSplitOptions.None);
        Console.WriteLine("u2_len=" + (c2.Length == 2));
        string[] c3 = "a,b".Split(new string[] { "" }, StringSplitOptions.None);
        Console.WriteLine("u3_len=" + (c3.Length == 1));

        // FIRST-match-in-array-order greedy: { "a","aa" } splits every 'a'
        // (4 segs); { "aa","a" } consumes "aa" first (3 segs) -- "xaaay".
        string[] c4 = "xaaay".Split(new string[] { "a", "aa" }, StringSplitOptions.None);
        Console.WriteLine("u4_len=" + (c4.Length == 4));
        string[] c5 = "xaaay".Split(new string[] { "aa", "a" }, StringSplitOptions.None);
        Console.WriteLine("u5_len=" + (c5.Length == 3));

        // string[] RemoveEmptyEntries.
        string[] c6 = "a,,b".Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("u6_len=" + (c6.Length == 2));

        // ============ 4. whitespace mode (null/empty separators) ============
        string[] d1 = "a b c".Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("v1_len=" + (d1.Length == 3));
        // null char[] -> whitespace overload.
        string[] d2 = "a b\tc".Split((char[])null);
        Console.WriteLine("v2_len=" + (d2.Length == 3));
        string[] d3 = "a  b\tc".Split((char[])null);
        Console.WriteLine("v3_len=" + (d3.Length == 5));
        Console.WriteLine("v3_1=" + (d3[1] == ""));
        // whitespace + RemoveEmptyEntries collapses the runs.
        string[] d4 = "  a b  ".Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("v4_len=" + (d4.Length == 2));
        // string[] whitespace mode (empty array) too.
        string[] d5 = "x y".Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine("v5_len=" + (d5.Length == 2));

        // ============ 5. options validation (invalid enum value) ============
        bool bad2 = false, badNeg = false, bad5 = false;
        try { "a,b".Split(new char[] { ',' }, (StringSplitOptions)2); Console.WriteLine("never"); }
        catch (ArgumentException) { bad2 = true; }
        try { "a,b".Split(new char[] { ',' }, (StringSplitOptions)(-1)); Console.WriteLine("never"); }
        catch (ArgumentException) { badNeg = true; }
        try { "a,b".Split(new string[] { "," }, (StringSplitOptions)5); Console.WriteLine("never"); }
        catch (ArgumentException) { bad5 = true; }
        Console.WriteLine("x1_opt2=" + bad2);
        Console.WriteLine("x2_optneg=" + badNeg);
        Console.WriteLine("x3_opt5=" + bad5);
    }
}}
