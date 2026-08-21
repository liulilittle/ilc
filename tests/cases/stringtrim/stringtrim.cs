//------------------------------------------------------------------------------
// stringtrim.cs -- System.String TRIM domain DEEP e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C82, AFTER the string-domain
// full chain (MethodMap.String.cs Trim/TrimStart/TrimEnd ->
// icall_string_trim/trim_start/trim_end, il2cpp_string_ext.h:112-121;
// stringext.cpp TrimImpl + the literal Char.IsWhiteSpace code-unit set that is
// net48-identical). strdeep/strproc/strcomp2/stringops3 pinned the operational
// surface; THIS case drives the TRIM WHITESPACE-SET dimension exhaustively:
//
//   A. FULL CODE-UNIT SET: every one of the 25 code units .NET Char.IsWhiteSpace
//      classifies as whitespace -- \t \n \v \f \r space, U+0085 NEL, U+00A0
//      NBSP, U+1680 OGHAM, U+2000-U+200A (EN QUAD..HAIR SPACE), U+2028 LS,
//      U+2029 PS, U+202F NNBSP, U+205F MMSP, U+3000 IDEOGRAPHIC SPACE -- each
//      probed as leading (TrimStart/Trim), trailing (TrimEnd/Trim), BOTH sides,
//      and all-whitespace -> "".
//   B. NON-WHITESPACE BOUNDARY (the other side of the contract): near-miss
//      code units that Char.IsWhiteSpace REJECTS and Trim MUST preserve --
//      U+200B ZWSP, U+200C ZWNJ, U+200D ZWJ, U+2060 WORD JOINER, U+FEFF BOM,
//      U+180E MONGOLIAN VOWEL SEPARATOR, U+00AD SOFT HYPHEN -- each probed
//      leading/trailing/both unchanged.
//   C. MIXED RUNS: a run of several DIFFERENT whitespace units at each side,
//      inner whitespace PRESERVED, leading-only / trailing-only / both.
//   D. IDENTITY + ALLOCATION: nothing-to-trim returns the SAME instance
//      (ReferenceEquals) for Trim/TrimStart/TrimEnd; a real trim returns a NEW
//      instance; all-whitespace -> "" (new empty string); Trim() ==
//      TrimStart().TrimEnd() composition for a battery of inputs.
//   E. CHAINS: trimmed result feeding Length / indexer / Substring /
//      ToUpper / equality.
//
// Every line prints "label=True/False"; net48 is the byte reference. The
// wired surface is the no-arg (empty char[]) Trim family -- a NON-EMPTY
// char[] trim set is the recorded deviation D-STR3-1 (stringops3), deliberately
// NOT probed here.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class StringTrim {
    // The 25 code units .NET Char.IsWhiteSpace accepts (net48 == runtime set).
    private static readonly char[] WS = {
        '\t', '\n', '\v', '\f', '\r', ' ',
        '\u0085', '\u00A0', '\u1680',
        '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005',
        '\u2006', '\u2007', '\u2008', '\u2009', '\u200A',
        '\u2028', '\u2029', '\u202F', '\u205F', '\u3000'
    };
    // Near-miss units Char.IsWhiteSpace REJECTS (must be preserved by Trim).
    private static readonly char[] NOTWS = {
        '\u200B', '\u200C', '\u200D', '\u2060', '\uFEFF', '\u180E', '\u00AD'
    };

    public static void Main() {
        // ============ A. full whitespace code-unit set ============
        bool lead = true, trail = true, both = true, allWs = true;
        for (int i = 0; i < WS.Length; i++)
        {
            char w = WS[i];
            if ((w + "abc").TrimStart() != "abc") lead = false;   // leading removed
            if ((w + "abc").Trim() != "abc") lead = false;
            if (("abc" + w).TrimEnd() != "abc") trail = false;    // trailing removed
            if (("abc" + w).Trim() != "abc") trail = false;
            if ((w + "abc" + w).Trim() != "abc") both = false;    // both sides
            string all3 = "" + w + w + w;                         // char+char+char = int; force string
            if (all3.Trim() != "") allWs = false;                 // all-ws -> ""
            if (all3.TrimStart() != "") allWs = false;
            if (all3.TrimEnd() != "") allWs = false;
        }
        Console.WriteLine("ws_set_count=" + (WS.Length == 25));
        Console.WriteLine("ws_leading=" + lead);
        Console.WriteLine("ws_trailing=" + trail);
        Console.WriteLine("ws_both=" + both);
        Console.WriteLine("ws_allws_empty=" + allWs);

        // ============ B. non-whitespace boundary preserved ============
        bool pLead = true, pTrail = true, pBoth = true;
        for (int i = 0; i < NOTWS.Length; i++)
        {
            char n = NOTWS[i];
            if ((n + "abc").TrimStart() != n + "abc") pLead = false;
            if (("abc" + n).TrimEnd() != "abc" + n) pTrail = false;
            if ((n + "abc" + n).Trim() != n + "abc" + n) pBoth = false;
        }
        Console.WriteLine("notws_preserve_lead=" + pLead);
        Console.WriteLine("notws_preserve_trail=" + pTrail);
        Console.WriteLine("notws_preserve_both=" + pBoth);

        // ============ C. mixed runs + inner whitespace preserved ============
        Console.WriteLine("mix_lead=" + (" \t\n\u00A0\u3000abc".Trim() == "abc"));
        Console.WriteLine("mix_trail=" + ("abc\r\f\u0085\u2028 ".Trim() == "abc"));
        Console.WriteLine("mix_both=" + (" \t\nabc\u00A0\u3000\r".Trim() == "abc"));
        Console.WriteLine("mix_inner=" + ("  a  b\tc d  ".Trim() == "a  b\tc d"));
        Console.WriteLine("mix_full_run=" + ("\t\n\v\f\r \u0085\u00A0\u1680\u2000\u200A\u2028\u2029\u202F\u205F\u3000abc".Trim() == "abc"));

        // ============ D. identity + allocation ============
        string clean = "abc";
        Console.WriteLine("id_trim=" + ReferenceEquals(clean, clean.Trim()));
        Console.WriteLine("id_trimstart=" + ReferenceEquals(clean, clean.TrimStart()));
        Console.WriteLine("id_trimend=" + ReferenceEquals(clean, clean.TrimEnd()));
        Console.WriteLine("id_new=" + (!ReferenceEquals(" abc", " abc".Trim())));
        Console.WriteLine("id_new_start=" + (!ReferenceEquals(" abc", " abc".TrimStart())));
        Console.WriteLine("id_new_end=" + (!ReferenceEquals("abc ", "abc ".TrimEnd())));
        Console.WriteLine("id_allws_new=" + (!ReferenceEquals("   ", "   ".Trim())));
        Console.WriteLine("id_empty=" + ("".Trim() == ""));

        // Trim() == TrimStart().TrimEnd() composition battery.
        bool comp = true;
        string[] bat = { "abc", "  abc", "abc  ", "  abc  ", " \t\nabc\u00A0 ", "a b", "   " };
        for (int i = 0; i < bat.Length; i++)
            if (bat[i].Trim() != bat[i].TrimStart().TrimEnd()) comp = false;
        Console.WriteLine("id_compose=" + comp);

        // ============ E. chains ============
        Console.WriteLine("ch_len=" + ("  abc  ".Trim().Length == 3));
        Console.WriteLine("ch_idx=" + ("  abc  ".Trim()[1] == 'b'));
        Console.WriteLine("ch_substr=" + ("  abc  ".Trim().Substring(1) == "bc"));
        Console.WriteLine("ch_upper=" + ("  abc  ".Trim().ToUpper() == "ABC"));
        Console.WriteLine("ch_eq=" + ("  abc  ".Trim() == "abc"));
        Console.WriteLine("ch_ts_te=" + ("  a b  ".TrimStart().TrimEnd() == "a b"));

        Console.WriteLine("done");
    }
}}
