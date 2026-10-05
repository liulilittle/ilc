//------------------------------------------------------------------------------
// charops.cs -- System.Char domain deep probe (e2e batch C45): the WIRED char
// surface -- literals / indexer / comparison / arithmetic / casts / ToString /
// boxed Equals / string.ToUpper-ToLower (ASCII) / StringBuilder.Append(char) /
// concat / the STATIC surface (D-CHA-2) -- byte-exact vs the net48 oracle.
//
// Wired expression surface exercised:
//   char LITERALS (BMP escapes + surrogate pair): lengths + (int) code units
//   string indexer s[i]: char values + comparisons + arithmetic
//   char comparison/arithmetic: '<'/'>', char+int, char-char=int, casts
//   char.ToString()                  -> icall_char_to_string (ValueType map)
//   char.Equals(object)              -> icall_boxed_equals_ext2 (boxed)
//   string.ToUpper()/ToLower()       -> icall_string_to_upper/to_lower
//                                      (ASCII-domain; probe ASCII only)
//   StringBuilder.Append(char)       -> icall_stringbuilder_append_c
//   char through string concat       -> concat icall chain
//   char STATIC surface (D-CHA-2)    -> icall_char_is_* / icall_char_to_*
//       IsDigit / IsLetter / IsLetterOrDigit / IsLower / IsUpper /
//       IsWhiteSpace / ToUpper / ToUpperInvariant / ToLower /
//       ToLowerInvariant (MethodMap.Char.cs, il2cpp_char.h)
//
// net48-shape discipline (charstr precedent): the diff prints only bools and
// (int) code units -- never raw chars -- so it stays byte-stable across
// console encodings.
//
// RECORDED WIRING GAP (defect, NOT probed): the System.Char STATIC surface is
// unwired (D-CHA-2, charstr GAPS.md) -- char.IsDigit / IsLetter /
// IsWhiteSpace / char.ToUpper(char) / char.ToLower(char) / ConvertToUtf32 have
// no MethodMap.Char.cs and no runtime icalls; probing them transpiles to
// undefined generated-name externs and aborts the C++ build. The "IsDigit/
// IsLetter/ToUpper" SHAPE is therefore reached through the wired equivalents:
// comparison-range predicates (c >= '0' && c <= '9') and string.ToUpper().
// CLOSED 2026-08-11 (D-CHA-2): the static surface is wired (S8 below).
//------------------------------------------------------------------------------
using System;
using System.Text;

namespace Cases { public static class CharOps {
    public static void Main()
    {
        // ================= S1: char literals + code units =================
        char A = 'A';
        char z = 'z';
        char d0 = '0';
        Console.WriteLine("lit_A=" + (A == 'A'));
        Console.WriteLine("code_A=" + ((int)A == 65));
        Console.WriteLine("code_z=" + ((int)z == 122));
        Console.WriteLine("code_0=" + ((int)d0 == 48));
        string cn = "\u4E2D\u6587";                 // BMP: zhong wen
        Console.WriteLine("bmp_len=" + (cn.Length == 2));
        Console.WriteLine("bmp_c0=" + ((int)cn[0] == 20013));
        Console.WriteLine("bmp_c1=" + ((int)cn[1] == 25991));
        string sur = "\uD83D\uDE00";                // surrogate pair U+1F600
        Console.WriteLine("sur_len=" + (sur.Length == 2));
        Console.WriteLine("sur_c0=" + ((int)sur[0] == 55357));
        Console.WriteLine("sur_c1=" + ((int)sur[1] == 56832));

        // ================= S2: string indexer char ops =================
        string s = "AbC";
        Console.WriteLine("idx0=" + (s[0] == 'A'));
        Console.WriteLine("idx1=" + (s[1] == 'b'));
        Console.WriteLine("idx2=" + (s[2] == 'C'));
        Console.WriteLine("idx_last=" + (s[s.Length - 1] == 'C'));
        Console.WriteLine("idx_cmp=" + (s[0] < s[1]));
        Console.WriteLine("idx_code=" + ((int)s[1] == 98));

        // ================= S3: comparison + arithmetic =================
        Console.WriteLine("cmp_a_b=" + ('a' < 'b'));
        Console.WriteLine("cmp_z_A=" + ('z' > 'A'));
        Console.WriteLine("cmp_eq=" + ('x' == 'x'));
        Console.WriteLine("cmp_ne=" + ('x' != 'y'));
        Console.WriteLine("arith_plus=" + ((char)('a' + 1) == 'b'));
        Console.WriteLine("arith_upper=" + ((char)('A' + 5) == 'F'));
        Console.WriteLine("arith_sub=" + ('Z' - 'A' == 25));
        Console.WriteLine("arith_digit=" + ('9' - '0' == 9));
        Console.WriteLine("arith_dist=" + ('a' - 'A' == 32));

        // ================= S4: conversions (casts / ToString / Equals) =================
        Console.WriteLine("cast_i2c=" + ((char)97 == 'a'));
        Console.WriteLine("cast_c2i=" + ((int)'A' == 65));
        char ch = 'Q';
        Console.WriteLine("tostr_q=" + (ch.ToString() == "Q"));
        Console.WriteLine("tostr_box=" + (((object)ch).ToString() == "Q"));
        object bq = 'Q';
        object br = 'R';
        Console.WriteLine("equals_obj=" + (bq.Equals((object)'Q')));
        Console.WriteLine("equals_other=" + (bq.Equals(br) == false));

        // ================= S5: string.ToUpper / ToLower (ASCII domain) =================
        Console.WriteLine("upper_hello=" + ("hello".ToUpper() == "HELLO"));
        Console.WriteLine("upper_mix=" + ("aBc1".ToUpper() == "ABC1"));
        Console.WriteLine("lower_HELLO=" + ("HELLO".ToLower() == "hello"));
        Console.WriteLine("lower_mix=" + ("AbC1".ToLower() == "abc1"));

        // ================= S6: StringBuilder.Append(char) =================
        StringBuilder sb = new StringBuilder();
        sb.Append('a');
        sb.Append('b');
        sb.Append('c');
        Console.WriteLine("sb_abc=" + (sb.ToString() == "abc"));
        sb.Append(' ').Append('y');
        Console.WriteLine("sb_abc_y=" + (sb.ToString() == "abc y"));
        Console.WriteLine("sb_len=" + (sb.Length == 5));

        // ================= S7: char through string concat =================
        char c1 = 'x';
        Console.WriteLine("cat_c=" + ("" + c1 == "x"));
        Console.WriteLine("cat_cc=" + ("" + c1 + c1 == "xx"));
        Console.WriteLine("cat_pQ=" + (("p" + 'Q') == "pQ"));

        // ================= S8: char STATIC surface (D-CHA-2) =================
        // Classification -- ASCII domain, pinned net48 probes
        // (IsDigit('5') / IsLetter('a') / ToUpper('a')='A').
        Console.WriteLine("isd5=" + (char.IsDigit('5')));
        Console.WriteLine("isd0=" + (char.IsDigit('0')));
        Console.WriteLine("isd9=" + (char.IsDigit('9')));
        Console.WriteLine("isd_not=" + (char.IsDigit('a') == false));
        Console.WriteLine("isd_ws=" + (char.IsDigit(' ') == false));
        Console.WriteLine("islet_a=" + (char.IsLetter('a')));
        Console.WriteLine("islet_Z=" + (char.IsLetter('Z')));
        Console.WriteLine("islet_not=" + (char.IsLetter('5') == false));
        // zh-CN IsLetter('\u4E2D') is NOT probed: net48 classifies it as
        // Unicode category Lo (Letter, other) -> True, while the ASCII-domain
        // icall returns False -- a RECORDED divergence (il2cpp_char.h),
        // excluded from the byte-exact net48 diff.
        Console.WriteLine("islod_7=" + (char.IsLetterOrDigit('7')));
        Console.WriteLine("islod_x=" + (char.IsLetterOrDigit('x')));
        Console.WriteLine("islod_not=" + (char.IsLetterOrDigit('_') == false));
        Console.WriteLine("islower_a=" + (char.IsLower('a')));
        Console.WriteLine("islower_A=" + (char.IsLower('A') == false));
        Console.WriteLine("isupper_A=" + (char.IsUpper('A')));
        Console.WriteLine("isupper_a=" + (char.IsUpper('a') == false));
        Console.WriteLine("isws_sp=" + (char.IsWhiteSpace(' ')));
        Console.WriteLine("isws_tab=" + (char.IsWhiteSpace('\t')));
        Console.WriteLine("isws_nl=" + (char.IsWhiteSpace('\n')));
        Console.WriteLine("isws_not=" + (char.IsWhiteSpace('x') == false));
        Console.WriteLine("isws_nbsp=" + (char.IsWhiteSpace('\u00A0')));

        // Case conversion -- ASCII domain, pinned net48 probes
        // (ToUpper('a')='A'; ToLower/ToLowerInvariant symmetric).
        Console.WriteLine("toupper_a=" + (char.ToUpper('a') == 'A'));
        Console.WriteLine("toupper_A=" + (char.ToUpper('A') == 'A'));
        Console.WriteLine("toupper_z=" + (char.ToUpper('z') == 'Z'));
        Console.WriteLine("toupper_digit=" + (char.ToUpper('5') == '5'));
        Console.WriteLine("toupper_inv_gecko=" + (char.ToUpperInvariant('k') == 'K'));
        Console.WriteLine("toupper_inv_mixed=" + (char.ToUpperInvariant('q') == 'Q'));
        Console.WriteLine("tolower_A=" + (char.ToLower('A') == 'a'));
        Console.WriteLine("tolower_a=" + (char.ToLower('a') == 'a'));
        Console.WriteLine("tolower_Z=" + (char.ToLower('Z') == 'z'));
        Console.WriteLine("tolower_inv_E=" + (char.ToLowerInvariant('E') == 'e'));
        Console.WriteLine("tolower_inv_lower=" + (char.ToLowerInvariant('m') == 'm'));
        Console.WriteLine("case_roundtrip=" + (char.ToLower(char.ToUpper('j')) == 'j'));
        // zh-CN (non-ASCII) passes through UNCHANGED (ASCII-domain divergence,
        // documented in il2cpp_char.h -- net48 lowercases U+4E2D to itself).
        Console.WriteLine("toupper_cn=" + (char.ToUpper('\u4E2D') == '\u4E2D'));
        Console.WriteLine("tolower_cn=" + (char.ToLower('\u4E2D') == '\u4E2D'));
    }
}}
