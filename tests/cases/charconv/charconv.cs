//------------------------------------------------------------------------------
// charconv.cs -- System.Char CONVERSION deep probe (e2e batch C68). The char
// DOMAIN's conversion surface -- char<->int code-unit casts (incl. the
// conv.u2 truncation boundaries), char<->ushort, the UNICODE surrogate/code
// point arithmetic, the string<->char[] and char<->encoding ROUND TRIPS
// (UTF-8 / ASCII / UTF-16LE), and the char static surface wired since the
// char domain landed (MethodMap.Char.cs / il2cpp_char.h). Every probe prints
// a BOOL or an (int) code unit -- never a raw char -- so the diff stays
// byte-stable across console encodings (charstr discipline).
//
// Wire face (all mapped): Encoding.UTF8/ASCII/Unicode GetBytes(string)/
// GetString(byte[])/GetChars(byte[]) (MethodMap.Encoding.cs); new string
// (char[]) / new string(char,int) (MethodMap.String.cs ctor(char[]) /
// ctor_repeat); string indexer; StringBuilder.Append(char); char static
// IsDigit/IsLetter/IsLetterOrDigit/IsLower/IsUpper/IsWhiteSpace/ToUpper/
// ToLower(+Invariant) (D-CHA-2). char.ConvertToUtf32/GetUnicodeCategory/
// IsSurrogate are NOT wired (recorded D-CHC-1) -- the surrogate/code-point
// arithmetic below is expressed with plain int casts (no icall).
//------------------------------------------------------------------------------
using System;
using System.Text;

namespace Cases { public static class CharConv {
    public static void Main()
    {
        // ================= S1: char <-> int code-unit casts =================
        Console.WriteLine("ci_A=" + ((int)'A' == 65));
        Console.WriteLine("ci_z=" + ((int)'z' == 122));
        Console.WriteLine("ci_0=" + ((int)'0' == 48));
        Console.WriteLine("ci_nul=" + ((int)'\0' == 0));
        Console.WriteLine("ci_max=" + ((int)char.MaxValue == 65535));
        Console.WriteLine("ci_min=" + ((int)char.MinValue == 0));
        Console.WriteLine("ic_65=" + ((char)65 == 'A'));
        Console.WriteLine("ic_97=" + ((char)97 == 'a'));
        Console.WriteLine("ic_48=" + ((char)48 == '0'));
        Console.WriteLine("ic_0=" + ((char)0 == '\0'));
        Console.WriteLine("ic_max=" + ((char)65535 == char.MaxValue));
        Console.WriteLine("round_A=" + ((char)(int)'A' == 'A'));
        Console.WriteLine("round_cn=" + ((char)(int)'\u4E2D' == '\u4E2D'));

        // ================= S2: int -> char truncation (conv.u2 low 16 bits) =================
        Console.WriteLine("trunc_10000=" + (unchecked((char)0x10000) == (char)0));
        Console.WriteLine("trunc_10441=" + (unchecked((char)0x10441) == (char)0x441));
        Console.WriteLine("trunc_1FFFF=" + (unchecked((char)0x1FFFF) == (char)0xFFFF));
        Console.WriteLine("trunc_imax=" + (unchecked((char)int.MaxValue) == (char)0xFFFF));
        Console.WriteLine("trunc_imin=" + (unchecked((char)int.MinValue) == (char)0));
        Console.WriteLine("trunc_m1=" + (unchecked((char)(-1)) == (char)0xFFFF));
        Console.WriteLine("trunc_m65536=" + (unchecked((char)(-65536)) == (char)0));

        // ================= S3: char <-> ushort / wider round trips =================
        Console.WriteLine("cu_A=" + ((ushort)'A' == 65));
        Console.WriteLine("cu_max=" + ((int)(ushort)char.MaxValue == 65535));
        Console.WriteLine("uc_4e2d=" + ((char)(ushort)0x4E2D == '\u4E2D'));
        Console.WriteLine("uc_ff01=" + ((char)(ushort)0xFF01 == '\uFF01'));
        Console.WriteLine("wi_12345=" + ((int)(char)12345 == 12345));

        // ================= S4: Unicode surface -- surrogate arithmetic =================
        char hi = (char)0xD83D;                  // high surrogate of U+1F600
        char lo = (char)0xDE00;                  // low surrogate of U+1F600
        Console.WriteLine("hi_unit=" + ((int)hi == 0xD83D));
        Console.WriteLine("lo_unit=" + ((int)lo == 0xDE00));
        Console.WriteLine("hi_is_hi=" + ((int)hi >= 0xD800 && (int)hi <= 0xDBFF));
        Console.WriteLine("hi_not_lo=" + (((int)hi >= 0xDC00 && (int)hi <= 0xDFFF) == false));
        Console.WriteLine("lo_is_lo=" + ((int)lo >= 0xDC00 && (int)lo <= 0xDFFF));
        Console.WriteLine("lo_not_hi=" + (((int)lo >= 0xD800 && (int)lo <= 0xDBFF) == false));
        Console.WriteLine("sur_max=" + ((int)'\uDFFF' == 0xDFFF));
        Console.WriteLine("bmp_d7ff=" + ((int)'\uD7FF' == 0xD7FF));
        Console.WriteLine("bmp_e000=" + ((int)'\uE000' == 0xE000));
        // high+low pair -> code point U+1F600 (pure int arithmetic)
        int cp = 0x10000 + (((int)hi - 0xD800) << 10) + ((int)lo - 0xDC00);
        Console.WriteLine("pair_cp=" + (cp == 0x1F600));
        Console.WriteLine("pair_cp10=" + (cp == 128512));
        // decompose the code point back into the pair (round trip)
        int v = cp - 0x10000;
        char h2 = (char)(0xD800 + (v >> 10));
        char l2 = (char)(0xDC00 + (v & 0x3FF));
        Console.WriteLine("decompose=" + (h2 == hi && l2 == lo));
        // BMP single-unit round trip through the encoding layer's code unit
        Console.WriteLine("u16_surrogate=" + ((int)'\uD83D' == 0xD83D));

        // ================= S5: encoding round trips (char <-> bytes) =================
        // UTF-8 BMP (A + U+00E9 latin small e acute): 41 C3 A9
        byte[] b1 = Encoding.UTF8.GetBytes("A\u00E9");
        Console.WriteLine("u8_bmp_len=" + (b1.Length == 3));
        Console.WriteLine("u8_bmp_b0=" + (b1[0] == 0x41));
        Console.WriteLine("u8_bmp_b1=" + (b1[1] == 0xC3));
        Console.WriteLine("u8_bmp_b2=" + (b1[2] == 0xA9));
        Console.WriteLine("u8_bmp_rt=" + (Encoding.UTF8.GetString(b1) == "A\u00E9"));
        // UTF-8 astral (surrogate pair -> 4 bytes F0 9F 98 80)
        string ast = "\uD83D\uDE00";
        byte[] b2 = Encoding.UTF8.GetBytes(ast);
        Console.WriteLine("u8_ast_len=" + (b2.Length == 4));
        Console.WriteLine("u8_ast_b0=" + (b2[0] == 0xF0));
        Console.WriteLine("u8_ast_b1=" + (b2[1] == 0x9F));
        Console.WriteLine("u8_ast_b2=" + (b2[2] == 0x98));
        Console.WriteLine("u8_ast_b3=" + (b2[3] == 0x80));
        Console.WriteLine("u8_ast_rt=" + (Encoding.UTF8.GetString(b2) == ast));
        // UTF-8 CJK (zhong wen): E4 B8 AD E6 96 87
        byte[] b3 = Encoding.UTF8.GetBytes("\u4E2D\u6587");
        Console.WriteLine("u8_cjk_len=" + (b3.Length == 6));
        Console.WriteLine("u8_cjk=" + (b3[0] == 0xE4 && b3[1] == 0xB8 && b3[2] == 0xAD &&
                                      b3[3] == 0xE6 && b3[4] == 0x96 && b3[5] == 0x87));
        Console.WriteLine("u8_cjk_rt=" + (Encoding.UTF8.GetString(b3) == "\u4E2D\u6587"));
        // ASCII round trip
        byte[] b4 = Encoding.ASCII.GetBytes("AbC123");
        Console.WriteLine("ascii_len=" + (b4.Length == 6));
        Console.WriteLine("ascii_b=" + (b4[0] == 0x41 && b4[1] == 0x62 && b4[2] == 0x43 &&
                                       b4[3] == 0x31 && b4[4] == 0x32 && b4[5] == 0x33));
        Console.WriteLine("ascii_rt=" + (Encoding.ASCII.GetString(b4) == "AbC123"));
        // UTF-16LE round trip ("AB" -> 41 00 42 00)
        byte[] b5 = Encoding.Unicode.GetBytes("AB");
        Console.WriteLine("u16_len=" + (b5.Length == 4));
        Console.WriteLine("u16_b=" + (b5[0] == 0x41 && b5[1] == 0x00 && b5[2] == 0x42 && b5[3] == 0x00));
        Console.WriteLine("u16_rt=" + (Encoding.Unicode.GetString(b5) == "AB"));
        // UTF-16LE astral: 3D D8 00 DE
        byte[] b6 = Encoding.Unicode.GetBytes(ast);
        Console.WriteLine("u16_ast_len=" + (b6.Length == 4));
        Console.WriteLine("u16_ast_b=" + (b6[0] == 0x3D && b6[1] == 0xD8 && b6[2] == 0x00 && b6[3] == 0xDE));
        Console.WriteLine("u16_ast_rt=" + (Encoding.Unicode.GetString(b6) == ast));
        // UTF-8 GetChars -> char[] (back to the char domain)
        char[] ch = Encoding.UTF8.GetChars(b1);
        Console.WriteLine("u8_chars_len=" + (ch.Length == 2));
        Console.WriteLine("u8_chars=" + (ch[0] == 'A' && ch[1] == '\u00E9'));

        // ================= S6: char <-> string / char[] / StringBuilder =================
        Console.WriteLine("cs_ctor=" + (new string(new char[] { 'h', 'i' }) == "hi"));
        Console.WriteLine("cs_repeat=" + (new string('\u4E2D', 3) == "\u4E2D\u4E2D\u4E2D"));
        Console.WriteLine("cs_repeat0=" + (new string('x', 0) == ""));
        string s6 = "abc";
        Console.WriteLine("idx0=" + (s6[0] == 'a'));
        Console.WriteLine("idx2=" + (s6[2] == 'c'));
        string s7 = new string(new char[] { 'A', '\u4E2D', (char)0xD83D, (char)0xDE00 });
        Console.WriteLine("cs_len=" + (s7.Length == 4));
        Console.WriteLine("cs_u0=" + (s7[0] == 'A'));
        Console.WriteLine("cs_u1=" + ((int)s7[1] == 0x4E2D));
        Console.WriteLine("cs_u2=" + ((int)s7[2] == 0xD83D));
        Console.WriteLine("cs_u3=" + ((int)s7[3] == 0xDE00));
        // char -> StringBuilder -> string -> UTF-8 (full char-to-bytes path)
        StringBuilder sb9 = new StringBuilder();
        sb9.Append('A').Append('\u4E2D');
        string s8 = sb9.ToString();
        Console.WriteLine("sb_len=" + (s8.Length == 2));
        Console.WriteLine("sb_u0=" + ((int)s8[0] == 65));
        Console.WriteLine("sb_u1=" + ((int)s8[1] == 20013));
        byte[] b7 = Encoding.UTF8.GetBytes(s8);
        Console.WriteLine("sb_bytes=" + (b7.Length == 4 && b7[0] == 0x41 &&
                                        b7[1] == 0xE4 && b7[2] == 0xB8 && b7[3] == 0xAD));

        // ================= S7: char static surface pinned to code units =================
        Console.WriteLine("su_k=" + ((int)char.ToUpper('k') == 75));
        Console.WriteLine("sl_K=" + ((int)char.ToLower('K') == 107));
        Console.WriteLine("su_5=" + (char.ToUpper('5') == '5'));
        Console.WriteLine("sr_Q=" + (char.ToLower(char.ToUpper('Q')) == 'q'));
        Console.WriteLine("isd_8=" + (char.IsDigit('8')));
        Console.WriteLine("isl_a=" + (char.IsLetter('a')));
        Console.WriteLine("islod_q=" + (char.IsLetterOrDigit('q')));
        Console.WriteLine("islower_b=" + (char.IsLower('b')));
        Console.WriteLine("isupper_B=" + (char.IsUpper('B')));
        Console.WriteLine("isws_sp=" + (char.IsWhiteSpace(' ')));
    }
}}
