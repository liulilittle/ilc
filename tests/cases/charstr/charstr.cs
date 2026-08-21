// charstr.cs -- char/string boundary e2e reference case (net48 baseline).
//
// Probes the WIRED char/string surface only:
//
//   1) Unicode escape LITERALS: BMP escapes, surrogate PAIRS, and an unpaired
//      surrogate -- lengths and (int) code-unit values (the raw chars are NOT
//      printed, so the diff stays byte-stable across console encodings).
//   2) string INDEXER char ops: s[i], s.Length, indexer char in comparisons,
//      char arithmetic on the indexed char, char-char subtraction.
//   3) char comparison + arithmetic: '<'/'>', char+int, char-char = int,
//      char<->int casts, uppercase/lowercase distance.
//   4) char through string concat and StringBuilder.Append(char)/Append(string)
//      (the wired icall_stringbuilder_append_c / _s surface).
//
// Recorded gaps (GAPS.md): the System.Char STATIC surface is NOT wired --
// char.IsDigit / IsLetter / IsWhiteSpace / ConvertToUtf32 have no MethodMap
// entry anywhere in src/il2cppc (a transpile would fall to generated-name
// emission and fail at build). Only the instances-and-literals surface above
// is exercised.

using System;
using System.Text;
namespace Cases { public static class CharStr {
    public static void Main() {
        // ---- Unicode escape literals: lengths + code units ----
        string a = "\u0041";                        // "A"
        Console.WriteLine(a.Length);                // 1
        Console.WriteLine((int)a[0]);               // 65
        Console.WriteLine(a == "A");                // True
        string cn = "\u4E2D\u6587";                 // "zhong wen" (BMP)
        Console.WriteLine(cn.Length);               // 2
        Console.WriteLine((int)cn[0]);              // 20013
        Console.WriteLine((int)cn[1]);              // 25991
        string sur = "\uD83D\uDE00";                // surrogate pair (U+1F600)
        Console.WriteLine(sur.Length);              // 2
        Console.WriteLine((int)sur[0]);             // 55357
        Console.WriteLine((int)sur[1]);             // 56832
        // NOTE: an UNPAIRED-surrogate literal ("\uD800") is NOT probed: it
        // aborts the string-pool EMISSION ("Unable to translate Unicode
        // character \\uD800 ... to specified code page" -> EMPTY string-pool.h
        // -> every il2cpp::g_strPool[i] reference fails to compile). See
        // GAPS.md D-CHA-1.

        // ---- string indexer char ops ----
        string s = "Hello";
        Console.WriteLine(s.Length);                // 5
        Console.WriteLine(s[0]);                    // H
        Console.WriteLine(s[s.Length - 1]);         // o
        Console.WriteLine(s[0] == 'H');             // True
        Console.WriteLine(s[1] == 'e');             // True
        Console.WriteLine((char)(s[0] + 1));        // I
        Console.WriteLine(s[0] < s[1]);             // True ('H'=72 < 'e'=101)
        Console.WriteLine((int)(s[4] - s[0]));      // 'o'-'H' = 39
        Console.WriteLine((int)s[0]);               // 72

        // ---- char comparison + arithmetic ----
        Console.WriteLine('a' < 'b');               // True
        Console.WriteLine((char)('a' + 1));         // b
        Console.WriteLine((char)('A' + 5));         // F
        Console.WriteLine('Z' - 'A');               // 25
        Console.WriteLine('9' - '0');               // 9
        Console.WriteLine((int)'A');                // 65
        Console.WriteLine((char)97);                // a
        Console.WriteLine('A' == 'A');              // True
        Console.WriteLine('a' != 'A');              // True

        // ---- char through string concat + StringBuilder ----
        char c1 = 'x';
        Console.WriteLine("" + c1);                 // x
        Console.WriteLine("" + c1 + c1);            // xx
        Console.WriteLine(("p" + 'Q') == "pQ");     // True
        StringBuilder sb = new StringBuilder();
        sb.Append('a');
        sb.Append('b');
        sb.Append("c");
        Console.WriteLine(sb.ToString() == "abc");  // True
        Console.WriteLine(sb.Length);               // 3
        sb.Append(' ').Append('y');
        Console.WriteLine(sb.ToString() == "abc y");// True
        sb.AppendLine("z");
        Console.WriteLine(sb.ToString() == "abc y" + Environment.NewLine + "z" + Environment.NewLine); // True
    }
}}
