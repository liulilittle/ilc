//------------------------------------------------------------------------------
// stringsplit.cs -- System.String.Split(char[]) DEEP e2e reference case
// (net48 oracle). Exercises the WIRED Split surface (MethodMap.String.cs
// "Split" -> il2cpp::runtime::icall_string_split, wired at HEAD f8a31ea):
// ONLY the one-arg Split(char[]) / Split(params char[]) shape is wired. A
// null OR EMPTY separator array routes to the WHITESPACE overload (the full
// Char.IsWhiteSpace set, incl. NBSP U+00A0); empty segments are RETAINED
// (StringSplitOptions.None, the net48 contract); segments are String.Empty
// instances, never null.
//
// Coverage:
//   1) single / multiple (params char[]) separators;
//   2) consecutive separators -> EMPTY segment between them;
//   3) leading separator -> leading empty; trailing separator -> trailing
//      empty; all-separator receiver; separator-only receiver;
//   4) empty receiver -> one empty element; no-separator receiver -> one
//      full element;
//   5) EMPTY separator array (new char[0]) == null separator == whitespace
//      overload;
//   6) whitespace set: space / tab / newline / NBSP; consecutive whitespace
//      -> empties; leading+trailing whitespace -> leading/trailing empties;
//      all-whitespace receiver;
//   7) wide-char separator ("\u4E2D") splits a BMP string correctly;
//   8) string[] result: get_Length / indexer / element string equality /
//      empty segment is String.Empty and != null.
//
// NOT covered (NOT wired -> generated-name -> build failure, recorded in the
// batch report): Split(string[], StringSplitOptions) and Split(char[],
// StringSplitOptions) overloads -- the resolver deliberately never rewires
// them to the separator-only icall (RemoveEmptyEntries semantics differ).
//
// Every active line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class StringSplit {
    public static void Main() {
        // --- 1. single separator ---
        string[] p1 = "a,b,c".Split(',');
        Console.WriteLine("s1_len=" + (p1.Length == 3));
        Console.WriteLine("s1_0=" + (p1[0] == "a"));
        Console.WriteLine("s1_2=" + (p1[2] == "c"));

        // --- 2. params multi-separator ---
        string[] p2 = "a,b;c".Split(',', ';');
        Console.WriteLine("s2_len=" + (p2.Length == 3));
        Console.WriteLine("s2_1=" + (p2[1] == "b"));
        string[] p3 = "x1,y 2;z".Split(',', ' ', ';');
        Console.WriteLine("s3_len=" + (p3.Length == 4));
        Console.WriteLine("s3_2=" + (p3[2] == "2"));

        // --- 3. consecutive separators -> retained empty segment ---
        string[] p4 = "a,,b".Split(',');
        Console.WriteLine("s4_len=" + (p4.Length == 3));
        Console.WriteLine("s4_mid=" + (p4[1] == String.Empty));
        Console.WriteLine("s4_mid_notnull=" + (p4[1] != null));

        // --- 4. leading separator -> leading empty ---
        string[] p5 = ",a,b".Split(',');
        Console.WriteLine("s5_len=" + (p5.Length == 3));
        Console.WriteLine("s5_lead=" + (p5[0] == ""));

        // --- 5. trailing separator -> trailing empty ---
        string[] p6 = "a,b,".Split(',');
        Console.WriteLine("s6_len=" + (p6.Length == 3));
        Console.WriteLine("s6_tail=" + (p6[2] == ""));

        // --- 6. all-separator + separator-only receivers ---
        string[] p7 = ",,".Split(',');
        Console.WriteLine("s7_len=" + (p7.Length == 3));
        Console.WriteLine("s7_all=" + (p7[0] == "" && p7[1] == "" && p7[2] == ""));
        string[] p9 = ",".Split(',');
        Console.WriteLine("s9_len=" + (p9.Length == 2));

        // --- 7. empty receiver / no-separator receiver ---
        string[] p8 = "".Split(',');
        Console.WriteLine("s8_len=" + (p8.Length == 1));
        Console.WriteLine("s8_0=" + (p8[0] == ""));
        string[] p10 = "abc".Split(',');
        Console.WriteLine("s10_len=" + (p10.Length == 1));
        Console.WriteLine("s10_0=" + (p10[0] == "abc"));

        // --- 8. wide-char separator ---
        string[] p12 = "a\u4E2Db".Split('\u4E2D');
        Console.WriteLine("s12_len=" + (p12.Length == 2));
        Console.WriteLine("s12=" + (p12[0] == "a" && p12[1] == "b"));

        // --- 9. null separator -> whitespace overload ---
        string[] p13 = "a b c".Split((char[])null);
        Console.WriteLine("s13_len=" + (p13.Length == 3));
        Console.WriteLine("s13_1=" + (p13[1] == "b"));

        // --- 10. EMPTY separator array -> whitespace overload ---
        string[] p14 = "x y".Split(new char[0]);
        Console.WriteLine("s14_len=" + (p14.Length == 2));

        // --- 11. whitespace set: tab / newline ---
        string[] p15 = "a\tb\nc".Split((char[])null);
        Console.WriteLine("s15_len=" + (p15.Length == 3));

        // --- 12. consecutive whitespace -> empties ---
        string[] p16 = "a  b\tc".Split((char[])null);
        Console.WriteLine("s16_len=" + (p16.Length == 5));
        Console.WriteLine("s16_1=" + (p16[1] == ""));

        // --- 13. leading + trailing whitespace -> edge empties ---
        string[] p17 = "  a b  ".Split((char[])null);
        Console.WriteLine("s17_len=" + (p17.Length == 6));
        Console.WriteLine("s17_0=" + (p17[0] == ""));
        Console.WriteLine("s17_5=" + (p17[5] == ""));

        // --- 14. all-whitespace receiver ---
        string[] p18 = "   ".Split((char[])null);
        Console.WriteLine("s18_len=" + (p18.Length == 4));

        // --- 15. NBSP is whitespace (Char.IsWhiteSpace set) ---
        string[] p20 = "a\u00A0b".Split((char[])null);
        Console.WriteLine("s20_len=" + (p20.Length == 2));

        // --- 16. string[] result composition + re-index ---
        string[] p19 = "a,b,c".Split(',');
        Console.WriteLine("s19_join=" + (p19[0] + "|" + p19[1] + "|" + p19[2] == "a|b|c"));
        Console.WriteLine("s19_idx=" + (p19[2][0] == 'c'));
        string[] p21 = "x--y--z".Split('-', '-');
        Console.WriteLine("s21_len=" + (p21.Length == 5));
        Console.WriteLine("s21_1=" + (p21[1] == "" && p21[3] == ""));
    }
}}
