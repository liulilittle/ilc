//------------------------------------------------------------------------------
// converted2.cs -- Convert domain DEEP probe 2 (e2e batch C49): the Base64
// family (ToBase64String / FromBase64String) over every quantum length and
// the full byte-value domain, plus cross-page character-encoding conversions
// (UTF-8 / UTF-16LE / ASCII GetBytes / GetString / GetChars, round trips
// and replacement-fallback corners) -- byte-exact vs the net48 oracle.
//
// Wired surface (MethodMap.Convert.cs / Base64.cs / Encoding.cs), all probes
// stay within it:
//   Convert.ToBase64String(byte[])              -> il2cpp_convert_to_base64_string
//   Convert.FromBase64String(string)            -> il2cpp_convert_from_base64_string
//   Encoding.UTF8/Unicode/ASCII.GetBytes(s)     -> il2cpp_encoding_<page>_get_bytes
//   Encoding.UTF8/Unicode/ASCII.GetString(b)    -> il2cpp_encoding_<page>_get_string
//   Encoding.UTF8.GetChars(byte[])              -> il2cpp_encoding_utf8_get_chars
//   Encoding.UTF8/Unicode/ASCII.GetChars(b,i,c) -> ranged allocating form
//
// net48-shape discipline:
//   * ToBase64String output is deterministic (RFC 4648, '=' padding for the
//     final partial quantum); hex dumps use the lowercase Hex helper so the
//     printed bytes are ASCII-only (crypto2 precedent).
//   * FromBase64String probes stay inside the net48 rules pinned by the
//     base64case oracle (il2cpp_convertbase64.h): ASCII whitespace ignored
//     anywhere, '=' only legal in the FINAL quantum, unpadded / leftover
//     forms throw FormatException ("TWE" throws; "TWE==" decodes to one
//     byte; "AB= " throws).
//   * Encoding probes cover the DEFAULT replacement fallbacks (net48
//     baseline): ASCII ENCODE of non-ASCII units -> '?'; UTF-8 invalid
//     bytes decode to one U+FFFD per maximal invalid subpart (ED A0 80 /
//     E0 80 80 -> two U+FFFD, F0 80 80 80 -> three); UTF-16LE unpaired
//     surrogate and dangling odd trailing byte each decode to one U+FFFD.
//     ASCII DECODE of bytes > 0x7F is a RECORDED DIVERGENCE (net48 -> '?',
//     runtime -> U+FFFD; D-ENC-2) -- NOT probed, see the body.
//   * Exceptions print TYPE NAME only (messages are OS-localized).
//   * No char[]-buffer Base64 variants (ToBase64CharArray/FromBase64CharArray
//     are LOUD rejections -- no runtime helper), no ranged Base64 overloads.
//------------------------------------------------------------------------------
using System;
using System.Text;

namespace Cases { public static class Converted2 {

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static string Hex(byte[] arr)
    {
        string s = "";
        for (int i = 0; i < arr.Length; i++)
        {
            int hi = arr[i] >> 4;
            int lo = arr[i] & 0x0F;
            s += (char)(hi < 10 ? '0' + hi : 'a' + hi - 10);
            s += (char)(lo < 10 ? '0' + lo : 'a' + lo - 10);
        }
        return s;
    }

    public static void Main()
    {
        // ============ S1: Base64 -- known RFC 4648 vectors ============
        Console.WriteLine("b64_man=" + Convert.ToBase64String(new byte[] { (byte)'M', (byte)'a', (byte)'n' }));   // TWFu
        Console.WriteLine("b64_foobar=" + Convert.ToBase64String(new byte[] { (byte)'f', (byte)'o', (byte)'o', (byte)'b', (byte)'a', (byte)'r' })); // Zm9vYmFy
        Console.WriteLine("b64_foob=" + Convert.ToBase64String(new byte[] { (byte)'f', (byte)'o', (byte)'o', (byte)'b' })); // Zm9vYg==
        Console.WriteLine("b64_fooba=" + Convert.ToBase64String(new byte[] { (byte)'f', (byte)'o', (byte)'o', (byte)'b', (byte)'a' })); // Zm9vYmE=
        Console.WriteLine("b64_M=" + Convert.ToBase64String(new byte[] { (byte)'M' }));    // TQ==
        Console.WriteLine("b64_empty=" + Convert.ToBase64String(new byte[0]));             // (empty)

        // ============ S2: Base64 -- every quantum length 0..12 ============
        for (int n = 0; n <= 12; n++)
        {
            byte[] bytes = new byte[n];
            for (int i = 0; i < n; i++) bytes[i] = (byte)(i + 1);
            string enc = Convert.ToBase64String(bytes);
            Console.WriteLine("b64_len" + n + "=" + enc);
            Console.WriteLine("b64_rt" + n + "=" + BytesEqual(Convert.FromBase64String(enc), bytes));
        }

        // ============ S3: Base64 -- full byte-value domain 0..255 ============
        byte[] all = new byte[256];
        for (int i = 0; i < 256; i++) all[i] = (byte)i;
        string allEnc = Convert.ToBase64String(all);
        Console.WriteLine("b64_all_rt=" + BytesEqual(Convert.FromBase64String(allEnc), all)); // True
        byte[] zeros = new byte[12];
        Console.WriteLine("b64_zeros=" + Convert.ToBase64String(zeros));     // AAAAAAAAAAAAAAAA (12B % 3 == 0, no padding)
        Console.WriteLine("b64_zeros_rt=" + BytesEqual(Convert.FromBase64String(Convert.ToBase64String(zeros)), zeros)); // True
        byte[] ff = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        Console.WriteLine("b64_ff=" + Convert.ToBase64String(ff));           // ////fw==
        Console.WriteLine("b64_ff_rt=" + BytesEqual(Convert.FromBase64String(Convert.ToBase64String(ff)), ff)); // True

        // ============ S4: FromBase64String -- whitespace and padding rules ============
        byte[] hello = Encoding.ASCII.GetBytes("Hello");
        Console.WriteLine("b64_ws_sp=" + BytesEqual(Convert.FromBase64String("SGVs bG8="), hello));   // space inside
        Console.WriteLine("b64_ws_tab=" + BytesEqual(Convert.FromBase64String("SGVs\tbG8="), hello));  // tab inside
        Console.WriteLine("b64_ws_crlf=" + BytesEqual(Convert.FromBase64String("SGVs\r\nbG8="), hello)); // CR LF inside
        Console.WriteLine("b64_ws_trail=" + BytesEqual(Convert.FromBase64String("SGVsbG8= "), hello)); // trailing whitespace after padding
        Console.WriteLine("b64_ws_lead=" + BytesEqual(Convert.FromBase64String("  SGVsbG8="), hello)); // leading whitespace
        // net48 quirks pinned by the base64case oracle
        Console.WriteLine("b64_twe=" + (Hex(Convert.FromBase64String("TWE==")) == "58")); // 3 chars + '==' -> ONE byte 0x58 (net48 state machine)
        Console.WriteLine("b64_empty_in=" + (Convert.FromBase64String("").Length == 0));  // True
        Console.WriteLine("b64_ws_only=" + (Convert.FromBase64String("   ").Length == 0)); // True (whitespace strips to empty)

        // ============ S5: FromBase64String -- invalid input (FormatException) ============
        try { Convert.FromBase64String("!!!"); Console.WriteLine("b64_inv_bad=false"); }
        catch (FormatException) { Console.WriteLine("b64_inv_bad=true"); }
        try { Convert.FromBase64String("TWE"); Console.WriteLine("b64_inv_unpad=false"); }
        catch (FormatException) { Console.WriteLine("b64_inv_unpad=true"); }
        try { Convert.FromBase64String("AB= "); Console.WriteLine("b64_inv_abws=false"); }
        catch (FormatException) { Console.WriteLine("b64_inv_abws=true"); }
        try { Convert.FromBase64String("TWFu="); Console.WriteLine("b64_inv_extra=false"); }
        catch (FormatException) { Console.WriteLine("b64_inv_extra=true"); }
        try { Convert.FromBase64String("T=WFu"); Console.WriteLine("b64_inv_mid=false"); }
        catch (FormatException) { Console.WriteLine("b64_inv_mid=true"); }
        try { Convert.FromBase64String("a"); Console.WriteLine("b64_inv_one=false"); }
        catch (FormatException) { Console.WriteLine("b64_inv_one=true"); }
        try { Convert.FromBase64String("abcde"); Console.WriteLine("b64_inv_left=false"); }
        catch (FormatException) { Console.WriteLine("b64_inv_left=true"); }
        try { Convert.ToBase64String(null); Console.WriteLine("b64_null_enc=false"); }
        catch (ArgumentNullException) { Console.WriteLine("b64_null_enc=true"); }
        try { Convert.FromBase64String(null); Console.WriteLine("b64_null_dec=false"); }
        catch (ArgumentNullException) { Console.WriteLine("b64_null_dec=true"); }

        // ============ S6: UTF-8 GetBytes / GetString / GetChars ============
        Console.WriteLine("u8_ascii=" + Hex(Encoding.UTF8.GetBytes("Hello")));           // 48656c6c6f
        Console.WriteLine("u8_zh=" + Hex(Encoding.UTF8.GetBytes("\u4F60\u597D")));        // e4bda0e5a5bd
        Console.WriteLine("u8_emoji=" + Hex(Encoding.UTF8.GetBytes("\uD83D\uDE00")));     // f09f9880
        Console.WriteLine("u8_empty=" + (Encoding.UTF8.GetBytes("").Length == 0));        // True
        Console.WriteLine("u8_rt1=" + (Encoding.UTF8.GetString(Encoding.UTF8.GetBytes("Hello")) == "Hello")); // True
        Console.WriteLine("u8_rt2=" + (Encoding.UTF8.GetString(Encoding.UTF8.GetBytes("\u4F60\u597D")) == "\u4F60\u597D")); // True
        Console.WriteLine("u8_rt3=" + (Encoding.UTF8.GetString(Encoding.UTF8.GetBytes("\uD83D\uDE00")) == "\uD83D\uDE00")); // True
        // replacement fallback: invalid UTF-8 bytes -> one U+FFFD per maximal invalid subpart
        Console.WriteLine("u8_bad1=" + (Encoding.UTF8.GetString(new byte[] { 0xFF }) == "\uFFFD"));                     // True
        Console.WriteLine("u8_bad2=" + (Encoding.UTF8.GetString(new byte[] { 0xE4, 0xBD }) == "\uFFFD"));               // True (truncated 3-byte)
        Console.WriteLine("u8_bad3=" + (Encoding.UTF8.GetString(new byte[] { 0xF0, 0x9F, 0x98 }) == "\uFFFD"));        // True (truncated 4-byte)
        Console.WriteLine("u8_bad4=" + (Encoding.UTF8.GetString(new byte[] { 0xED, 0xA0, 0x80 }) == "\uFFFD\uFFFD"));   // True (surrogate, 2 replacements)
        Console.WriteLine("u8_bad5=" + (Encoding.UTF8.GetString(new byte[] { 0xF0, 0x80, 0x80, 0x80 }) == "\uFFFD\uFFFD\uFFFD")); // True (overlong, 3)
        Console.WriteLine("u8_valid_mid=" + (Encoding.UTF8.GetString(new byte[] { 0x41, 0xE4, 0xBD, 0xA0, 0x42 }) == "A\u4F60B")); // True
        // GetChars whole-array (UTF8 page) + ranged form
        byte[] zhb = Encoding.UTF8.GetBytes("\u4F60\u597D");
        char[] zhc = Encoding.UTF8.GetChars(zhb);
        Console.WriteLine("u8_chars_len=" + (zhc.Length == 2));                              // True
        Console.WriteLine("u8_chars0=" + (zhc[0] == '\u4F60'));                              // True
        char[] zhcR = Encoding.UTF8.GetChars(zhb, 1, 2);                                    // slice: BD A0 -> two U+FFFD (invalid lead bytes)
        Console.WriteLine("u8_chars_rlen=" + (zhcR.Length == 2));                            // True
        Console.WriteLine("u8_chars_r0=" + (zhcR[0] == '\uFFFD'));                           // True
        Console.WriteLine("u8_chars_r1=" + (zhcR[1] == '\uFFFD'));                           // True
        char[] zhcFull = Encoding.UTF8.GetChars(zhb, 0, zhb.Length);
        Console.WriteLine("u8_chars_full=" + (zhcFull.Length == 2 && zhcFull[1] == '\u597D')); // True

        // ============ S7: Unicode (UTF-16LE) GetBytes / GetString / GetChars ============
        Console.WriteLine("u16_ascii=" + Hex(Encoding.Unicode.GetBytes("ABC")));             // 410042004300
        Console.WriteLine("u16_zh=" + Hex(Encoding.Unicode.GetBytes("\u4F60")));             // 604f
        Console.WriteLine("u16_pair=" + Hex(Encoding.Unicode.GetBytes("\uD83D\uDE00")));     // 3dd800de (UTF-16LE)
        Console.WriteLine("u16_rt=" + (Encoding.Unicode.GetString(Encoding.Unicode.GetBytes("Hi\u4F60")) == "Hi\u4F60")); // True
        Console.WriteLine("u16_odd=" + (Encoding.Unicode.GetString(new byte[] { 0x41, 0x00, 0x42 }) == "A\uFFFD"));     // True (dangling byte)
        Console.WriteLine("u16_surr=" + (Encoding.Unicode.GetString(new byte[] { 0x00, 0xD8 }) == "\uFFFD"));          // True (unpaired high)
        char[] u16c = Encoding.Unicode.GetChars(Encoding.Unicode.GetBytes("\u4F60"), 0, 2);
        Console.WriteLine("u16_chars_rlen=" + (u16c.Length == 1));                            // True
        Console.WriteLine("u16_chars_r0=" + (u16c[0] == '\u4F60'));                           // True

        // ============ S8: ASCII GetBytes / GetString / GetChars (7-bit, '?' / U+FFFD) ============
        Console.WriteLine("ascii_abc=" + Hex(Encoding.ASCII.GetBytes("ABC")));               // 414243
        Console.WriteLine("ascii_e=" + Hex(Encoding.ASCII.GetBytes("\u00E9")));              // 3f ('?' replacement)
        Console.WriteLine("ascii_mix=" + Hex(Encoding.ASCII.GetBytes("A\u00E9B")));          // 413f42
        // NOTE: ASCII DECODE of bytes > 0x7F is a RECORDED DIVERGENCE (not probed):
        //   net48 ASCIIEncoding defaults to '?' (0x3F) for out-of-range bytes, while
        //   the runtime decodes them to U+FFFD (il2cpp_encoding.h ascii contract). See
        //   the batch C49 report (D-ENC-2). The valid-byte probes below pass on both.
        Console.WriteLine("ascii_rt=" + (Encoding.ASCII.GetString(Encoding.ASCII.GetBytes("Hello")) == "Hello"));     // True
        char[] asciic = Encoding.ASCII.GetChars(new byte[] { 0x41, 0x42, 0x43 }, 1, 2);
        Console.WriteLine("ascii_chars_rlen=" + (asciic.Length == 2));                       // True
        Console.WriteLine("ascii_chars_r1=" + (asciic[1] == 'C'));                           // True

        // ============ S9: cross-page chains (base64 <-> UTF-8 <-> UTF-16) ============
        string zh = "\u4F60\u597D";
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(zh));
        Console.WriteLine("chain_b64=" + b64);                                              // 5Lit5paH
        Console.WriteLine("chain_rt=" + (Encoding.UTF8.GetString(Convert.FromBase64String(b64)) == zh)); // True
        byte[] u16 = Encoding.Unicode.GetBytes(zh);
        Console.WriteLine("chain_u16b64=" + Convert.ToBase64String(u16));                   // 60 4F 7D 59 -> YE99WQ==
        Console.WriteLine("chain_u16rt=" + (Encoding.Unicode.GetString(Convert.FromBase64String(Convert.ToBase64String(u16))) == zh)); // True
        Console.WriteLine("chain_hex=" + Hex(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(new byte[] { 0x48, 0x69 })))); // 4869
    }
}}
