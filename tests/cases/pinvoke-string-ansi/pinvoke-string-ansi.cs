//------------------------------------------------------------------------------
// pinvoke-string-ansi.cs -- CharSet.Ansi / CharSet.None `string` P/Invoke
// parameters marshal through the SYSTEM ANSI code page (CP_ACP), not UTF-8.
//
// net48 v4.0.30319 (host CP_ACP=936, zh-CN), native `int32_t f(const char*)`:
//   * "\u4E2D"        Ansi -> [D6 D0]     2 bytes (CP936 lead + trail)
//   * "\u4E2D\u6587"  Ansi -> [D6 D0 CE C4]
//   * "Abc"           Ansi -> [41 62 63]  ASCII identity
//   * "\uD83D\uDE00"  Ansi -> [3F 3F]     each surrogate HALF becomes '?'
//   * ""              Ansi -> non-NULL, first byte 0, length 0
//   * null            Ansi -> NULL pointer (both a literal null and a runtime
//                           null field)
// CharSet.Unicode is the UTF-16 path: the callee sees 2-byte code UNITS (no
// conversion), so its unit count differs from the Ansi BYTE count.
//
// Every record is diffed byte-exactly against this case's own SHARED native
// fixture by difftest.ps1 -- the oracle, not a hard-coded expectation.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

public static class StringAnsiPInvokeProbe
{
    // The default [DllImport] CharSet is Ansi.
    [DllImport("ansiprobe.dll")]
    private static extern int ansi_is_null(string s);
    [DllImport("ansiprobe.dll")]
    private static extern int ansi_len(string s);
    [DllImport("ansiprobe.dll")]
    private static extern int ansi_first(string s);
    [DllImport("ansiprobe.dll")]
    private static extern int ansi_dump(string s, byte[] outBytes, int cap);
    [DllImport("ansiprobe.dll", CharSet = CharSet.Unicode)]
    private static extern int uni_len(string s);

    // Explicit CharSet.Ansi -- must behave exactly like the default.
    [DllImport("ansiprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "ansi_is_null")]
    private static extern int ansi_is_null_x(string s);
    [DllImport("ansiprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "ansi_len")]
    private static extern int ansi_len_x(string s);
    [DllImport("ansiprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "ansi_first")]
    private static extern int ansi_first_x(string s);
    [DllImport("ansiprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "ansi_dump")]
    private static extern int ansi_dump_x(string s, byte[] outBytes, int cap);

    // Default null static field: a RUNTIME null (not a statically-null literal),
    // so it exercises the allocator's null input instead of the emitter's
    // NullExpr shortcut.
    private static string s_null;

    private static readonly byte[] s_buf = new byte[64];

    public static void Main()
    {
        // Default (no CharSet) + non-ASCII.
        RecordViewDefault("cjk", "\u4E2D");
        // Explicit CharSet.Ansi + non-ASCII -- must equal the default.
        RecordViewExplicitAnsi("cjk.ansi", "\u4E2D");
        RecordViewDefault("cjk2", "\u4E2D\u6587");
        RecordViewExplicitAnsi("cjk2.ansi", "\u4E2D\u6587");

        // CharSet.Unicode: UTF-16 UNIT count (no conversion).
        Record("cjk.uni.len", uni_len("\u4E2D"));
        Record("cjk2.uni.len", uni_len("\u4E2D\u6587"));

        // ASCII is an identity in every code page (regression guard).
        RecordViewDefault("ascii", "Abc");

        // Empty string: non-NULL, first byte 0, length 0.
        RecordViewDefault("empty", "");

        // null: literal (compile-time) and runtime field.
        RecordViewDefault("null.literal", null);
        RecordViewDefault("null.field", s_null);

        // Astral surrogate pair: net48 produces TWO '?' (each half alone).
        RecordViewDefault("emoji", "\uD83D\uDE00");
        Record("emoji.uni.len", uni_len("\uD83D\uDE00"));

        // Mixed ASCII + CJK + surrogate pair.
        RecordViewDefault("mix", "A\u4E2D\uD83D\uDE00Z");
    }

    private static void RecordViewDefault(string id, string value)
    {
        var buf = s_buf;
        Record(id + ".null", ansi_is_null(value));
        var n = ansi_len(value);
        Record(id + ".len", n);
        Record(id + ".first", ansi_first(value));
        var m = n < buf.Length ? n : buf.Length;
        if (m < 0)
        {
            m = 0;
        }
        ansi_dump(value, buf, buf.Length);
        Record(id + ".bytes", Hex(buf, m));
    }

    private static void RecordViewExplicitAnsi(string id, string value)
    {
        var buf = s_buf;
        Record(id + ".null", ansi_is_null_x(value));
        var n = ansi_len_x(value);
        Record(id + ".len", n);
        Record(id + ".first", ansi_first_x(value));
        var m = n < buf.Length ? n : buf.Length;
        if (m < 0)
        {
            m = 0;
        }
        ansi_dump_x(value, buf, buf.Length);
        Record(id + ".bytes", Hex(buf, m));
    }

    private static string Hex(byte[] bytes, int count)
    {
        const string digits = "0123456789ABCDEF";
        var chars = new char[count == 0 ? 0 : count * 3 - 1];
        for (var i = 0; i < count; i++)
        {
            chars[i * 3] = digits[(bytes[i] >> 4) & 0xF];
            chars[i * 3 + 1] = digits[bytes[i] & 0xF];
            if (i + 1 < count)
            {
                chars[i * 3 + 2] = ' ';
            }
        }
        return new string(chars);
    }

    private static void Record(string id, int value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }

    private static void Record(string id, string value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }
}
