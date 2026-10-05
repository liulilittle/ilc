//------------------------------------------------------------------------------
// stringreplace.cs -- System.String.Replace DEEP e2e reference case #3 (net48
// oracle, byte-for-byte). Created in e2e batch C85, AFTER the string-domain
// Replace closure (replsplit: Replace(string,string) + Split(char[]); strreplace:
// Replace(char,char) + Replace(string,string) + Split options).
//
// WHERE THIS GOES DEEPER THAN THE PREDECESSORS -- Replace ALL-COMBINATION +
// CULTURE-ORDINAL surface:
//   A. Replace(char,char) POSITION MATRIX: the oldChar at every spot (leading /
//      trailing / middle / consecutive / every occurrence / single-char string /
//      empty receiver), no-occurrence IDENTITY instance, oldChar==newChar with a
//      match -> NEW content-equal instance (strreplace precedent, no identity
//      fast path), oldChar==newChar with NO match -> identity.
//   B. Replace(char,char) ORDINAL/CULTURE: replacement is CODE-UNIT exact and
//      culture-INsensitive -- 'i' does not match 'I' nor the Turkish dotted
//      'İ' (U+0130) nor the dotless 'ı' (U+0131); 'ß' (U+00DF) is one code unit
//      matched exactly.
//   C. Replace(string,string) POSITION MATRIX: oldValue at leading / trailing /
//      middle / whole-string / consecutive / repeated-with-gaps, oldValue LONGER
//      than the input -> IDENTITY, empty receiver, null/empty newValue DELETE,
//      grow (newValue longer) and shrink at multiple spots, non-overlapping
//      greedy (the net48 contract), oldValue==newValue semantics.
//   D. Replace(string,string) ORDINAL/CULTURE: case-sensitive exact substring
//      ("Ab" does not match "ab"), BMP unicode (Chinese) oldValue/newValue,
//      mixed ASCII+unicode chains.
//   E. COMPOSITION + CULTURE TIE: Replace feeding get_Length / indexer /
//      Substring / IndexOf / Contains / equality, chained char-then-string
//      replaces, and ToUpper/ToLower interplay (ASCII-only casing -- both the
//      net48 zh-CN culture mapping and the runtime ASCII mapping agree on
//      a-z/A-Z, so the composed results stay byte-identical).
//
// Every active line prints "label=True/False"; the net48 run is the byte
// reference the transpiled build must match.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class StringReplace {
    public static void Main() {
        // ============ A. Replace(char,char) position matrix ============
        // A1. leading / trailing / middle / consecutive / every occurrence
        Console.WriteLine("rc_lead=" + ("Xabc".Replace('X', 'Y') == "Yabc"));
        Console.WriteLine("rc_trail=" + ("abcX".Replace('X', 'Y') == "abcY"));
        Console.WriteLine("rc_mid=" + ("aXbc".Replace('X', 'Y') == "aYbc"));
        Console.WriteLine("rc_cons=" + ("aXXb".Replace('X', 'Y') == "aYYb"));
        Console.WriteLine("rc_all=" + ("aXbXcX".Replace('X', 'Y') == "aYbYcY"));
        Console.WriteLine("rc_edges=" + ("XaXbX".Replace('X', 'Y') == "YaYbY"));
        // A2. single-char string / empty receiver
        Console.WriteLine("rc_single=" + ("a".Replace('a', 'z') == "z"));
        Console.WriteLine("rc_empty_in=" + ("".Replace('a', 'b') == ""));
        // A3. no occurrence -> IDENTITY instance + same content
        string rcq = "abc";
        Console.WriteLine("rc_nomatch_id=" + ReferenceEquals(rcq, rcq.Replace('z', 'y')));
        Console.WriteLine("rc_nomatch_val=" + (rcq.Replace('z', 'y') == "abc"));
        // A4. oldChar == newChar: match -> NEW instance, content-equal; no match -> identity
        string rce = "aXb";
        Console.WriteLine("rc_same_newinst=" + !ReferenceEquals(rce, rce.Replace('X', 'X')));
        Console.WriteLine("rc_same_val=" + (rce.Replace('X', 'X') == "aXb"));
        string rcn = "abc";
        Console.WriteLine("rc_same_nomatch_id=" + ReferenceEquals(rcn, rcn.Replace('q', 'q')));
        Console.WriteLine("rc_same_nomatch_val=" + (rcn.Replace('q', 'q') == "abc"));
        // A5. digit / whitespace replacement
        Console.WriteLine("rc_digit=" + ("a1b2c3".Replace('1', '9') == "a9b2c3"));
        Console.WriteLine("rc_space=" + ("a b c".Replace(' ', '-') == "a-b-c"));
        Console.WriteLine("rc_tab=" + ("a\tb".Replace('\t', '|') == "a|b"));

        // ============ B. Replace(char,char) ordinal / culture-INsensitive ============
        // B1. 'i' vs 'I' -- code-unit exact, no case folding
        Console.WriteLine("rc_case=" + ("aAa".Replace('A', 'b') == "aba"));
        Console.WriteLine("rc_iI=" + ("Iii".Replace('i', 'x') == "Ixx"));
        Console.WriteLine("rc_II=" + ("Iii".Replace('I', 'x') == "xii"));
        // B2. Turkish dotted/dotless: U+0130 'İ', U+0131 'ı' are distinct code units
        Console.WriteLine("rc_trk_dot=" + ("\u0130x".Replace('i', 'z') == "\u0130x"));
        Console.WriteLine("rc_trk_dotless=" + ("\u0131x".Replace('i', 'z') == "\u0131x"));
        Console.WriteLine("rc_trk_match=" + ("\u0130\u0131".Replace('\u0130', 'a') == "a\u0131"));
        // B3. 'ß' U+00DF is a single code unit, replaced exactly
        Console.WriteLine("rc_ss=" + ("Stra\u00DFe".Replace('\u00DF', 's') == "Strase"));
        // B4. BMP wide char replacement
        Console.WriteLine("rc_wide=" + ("\u4E2D\u6587X".Replace('\u4E2D', 'A') == "A\u6587X"));

        // ============ C. Replace(string,string) position matrix ============
        // C1. leading / trailing / middle / whole-string
        Console.WriteLine("rs_lead=" + ("Xabc".Replace("X", "Y") == "Yabc"));
        Console.WriteLine("rs_trail=" + ("abcX".Replace("X", "Y") == "abcY"));
        Console.WriteLine("rs_mid=" + ("abXcd".Replace("X", "Y") == "abYcd"));
        Console.WriteLine("rs_whole=" + ("abc".Replace("abc", "xyz") == "xyz"));
        // C2. consecutive / repeated with gaps
        Console.WriteLine("rs_cons=" + ("aXXb".Replace("X", "Y") == "aYYb"));
        Console.WriteLine("rs_gaps=" + ("aXbXc".Replace("X", "Y") == "aYbYc"));
        Console.WriteLine("rs_multi=" + ("aXbXcX".Replace("X", "Y") == "aYbYcY"));
        // C3. oldValue longer than the input -> IDENTITY
        string rsl = "abc";
        Console.WriteLine("rs_long_id=" + ReferenceEquals(rsl, rsl.Replace("abcd", "1")));
        Console.WriteLine("rs_long_val=" + (rsl.Replace("abcd", "1") == "abc"));
        // C4. empty receiver with a non-empty oldValue -> empty; newValue matters not
        Console.WriteLine("rs_empty_in=" + ("".Replace("ab", "cd") == ""));
        Console.WriteLine("rs_empty_in_del=" + ("".Replace("ab", "") == ""));
        // C5. delete via null / empty newValue
        Console.WriteLine("rs_del_null=" + ("aXbXc".Replace("X", null) == "abc"));
        Console.WriteLine("rs_del_empty=" + ("aXbXc".Replace("X", "") == "abc"));
        Console.WriteLine("rs_del_edges=" + ("XabcX".Replace("X", "") == "abc"));
        // C6. grow (newValue longer) and shrink at multiple spots
        Console.WriteLine("rs_grow=" + ("ab".Replace("b", "xyz") == "axyz"));
        Console.WriteLine("rs_grow2=" + ("aXb".Replace("X", "12345") == "a12345b"));
        Console.WriteLine("rs_shrink=" + ("aXXb".Replace("XX", "Z") == "aZb"));
        Console.WriteLine("rs_shrink2=" + ("abcdef".Replace("bcd", "z") == "azef"));
        // C7. non-overlapping greedy (the net48 contract)
        Console.WriteLine("rs_nonoverlap=" + ("aaa".Replace("aa", "b") == "ba"));
        Console.WriteLine("rs_nonoverlap2=" + ("aaaa".Replace("aa", "b") == "bb"));
        Console.WriteLine("rs_nonoverlap3=" + ("aaaaa".Replace("aa", "b") == "bba"));
        // C8. oldValue == newValue with a match -> NEW instance; no match -> identity
        string rsv = "aXb";
        Console.WriteLine("rs_same_newinst=" + !ReferenceEquals(rsv, rsv.Replace("X", "X")));
        Console.WriteLine("rs_same_val=" + (rsv.Replace("X", "X") == "aXb"));
        string rsv2 = "abc";
        Console.WriteLine("rs_same_nomatch_id=" + ReferenceEquals(rsv2, rsv2.Replace("z", "z")));
        Console.WriteLine("rs_same_nomatch_val=" + (rsv2.Replace("z", "z") == "abc"));
        // C9. repeated oldValue pattern / no gap
        Console.WriteLine("rs_repeat=" + ("ababab".Replace("ab", "XY") == "XYXYXY"));
        Console.WriteLine("rs_twice=" + ("abcabc".Replace("abc", "Z") == "ZZ"));

        // ============ D. Replace(string,string) ordinal / culture-INsensitive ============
        // D1. case-sensitive exact substring
        Console.WriteLine("rs_case=" + ("AbAb".Replace("Ab", "_") == "__"));
        Console.WriteLine("rs_case2=" + ("AbAb".Replace("ab", "_") == "AbAb"));
        Console.WriteLine("rs_case3=" + ("hello".Replace("He", "x") == "hello"));
        // D2. BMP unicode oldValue / newValue
        Console.WriteLine("rs_uni=" + ("\u4E2D\u6587X\u4E2D\u6587".Replace("X", "-") == "\u4E2D\u6587-\u4E2D\u6587"));
        Console.WriteLine("rs_uni2=" + ("\u4E2D\u6587".Replace("\u4E2D\u6587", "abc") == "abc"));
        Console.WriteLine("rs_uni3=" + ("ab\u4E2Dcd".Replace("\u4E2D", "XY") == "abXYcd"));
        // D3. mixed ASCII+unicode chain
        Console.WriteLine("rs_mixed=" + ("a\u4E2Db\u6587c".Replace("b\u6587", "-") == "a\u4E2D-c"));
        Console.WriteLine("rs_mixed2=" + ("\u4E2D-X-\u6587".Replace("-X-", "/") == "\u4E2D/\u6587"));
        // D4. culture tie: Turkish dotted-I substring is NOT a case-fold match
        Console.WriteLine("rs_trk=" + ("\u0130i".Replace("i", "x") == "\u0130x"));
        Console.WriteLine("rs_trk2=" + ("\u0130i".Replace("\u0130", "a") == "ai"));

        // ============ E. composition + culture tie ============
        // E1. Replace feeding Length / indexer / Substring / IndexOf / Contains
        string e1 = "hello world".Replace('o', '0');
        Console.WriteLine("e_len=" + (e1.Length == 11));
        Console.WriteLine("e_idx0=" + (e1[0] == 'h'));
        Console.WriteLine("e_idx4=" + (e1[4] == '0'));
        Console.WriteLine("e_idx7=" + (e1[7] == 'w'));
        Console.WriteLine("e_sub=" + (e1.Substring(6, 5) == "w0rld"));
        Console.WriteLine("e_idxof=" + (e1.IndexOf('0') == 4));
        Console.WriteLine("e_contains=" + (e1.Contains("w0rld")));
        // E2. chained char-then-string replaces
        Console.WriteLine("e_chain=" + ("a-b:c".Replace('-', '+').Replace(':', '|') == "a+b|c"));
        Console.WriteLine("e_chain2=" + ("a-b-c".Replace("b", "XY").Replace("XY", "Z") == "a-Z-c"));
        // E3. ToUpper / ToLower interplay (ASCII casing, byte-identical both sides)
        Console.WriteLine("e_upper=" + ("abc".Replace('b', 'B').ToUpper() == "ABC"));
        Console.WriteLine("e_lower=" + ("aBC".Replace('B', 'x').ToLower() == "axc"));
        Console.WriteLine("e_up_chain=" + ("x-y-z".ToUpper().Replace('-', '+') == "X+Y+Z"));
        // E4. no-op replacement keeps content identical (via equality + Length)
        Console.WriteLine("e_noop=" + ("abc".Replace('x', 'y') == "abc"));
        Console.WriteLine("e_noop_len=" + ("abc".Replace('x', 'y').Length == 3));
    }
}}
