//------------------------------------------------------------------------------
// pinvoke-marshalas-string.cs -- a PARAMETER-level
// [MarshalAs(UnmanagedType.LPStr)] / LPWStr / LPTStr / LPUTF8Str on a `string`
// P/Invoke parameter OVERRIDES the import CharSet in net48.
//
// MEASURED on net48 v4.0.30319 (host CP_ACP=936, zh-CN); the native callee
// copies the marshalled bytes back so difftest compares the oracle against the
// transpiled runtime BYTE FOR BYTE (no hard-coded expectations here):
//   [CharSet.Unicode] + [MarshalAs(LPStr)]     "\u4E2D" -> D6 D0 00   (CP_ACP, NOT UTF-16)
//   [CharSet.Ansi]    + [MarshalAs(LPWStr)]    "\u4E2D" -> 2D 4E 00 00 (UTF-16, NOT CP_ACP)
//   [CharSet.Unicode] + [MarshalAs(LPTStr)]    "\u4E2D" -> 2D 4E 00 00 (UTF-16 for BOTH charsets)
//   [CharSet.Unicode] + [MarshalAs(LPUTF8Str)] "\u4E2D" -> E4 B8 AD 00 (UTF-8 for BOTH charsets)
//   no MarshalAs + CharSet.Unicode              "\u4E2D" -> 2D 4E 00 00 (UTF-16)
//   no MarshalAs + CharSet.Ansi / no CharSet    "\u4E2D" -> D6 D0 00   (CP_ACP)
//
// The probe copies ONE string up to and including its NUL terminator; the
// expected native unit width (1 = char*, 2 = char16_t*) is an INDEPENDENT
// argument, so a wrong marshalling shows up as a different byte sequence
// (e.g. UTF-16 bytes read as a char* string stop at the first zero byte).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

public static class MarshalAsStringPInvokeProbe
{
    private const int Cap = 64;
    private static readonly byte[] s_buf = new byte[Cap];

    // --- parameter-level MarshalAs overrides the import CharSet -------------
    [DllImport("marshalasprobe.dll", CharSet = CharSet.Unicode, EntryPoint = "probe")]
    private static extern int probe_uni_lpstr([MarshalAs(UnmanagedType.LPStr)] string s, int unit, byte[] outBytes, int cap);
    [DllImport("marshalasprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "probe")]
    private static extern int probe_ansi_lpwstr([MarshalAs(UnmanagedType.LPWStr)] string s, int unit, byte[] outBytes, int cap);
    [DllImport("marshalasprobe.dll", CharSet = CharSet.Unicode, EntryPoint = "probe")]
    private static extern int probe_uni_lpwstr([MarshalAs(UnmanagedType.LPWStr)] string s, int unit, byte[] outBytes, int cap);
    [DllImport("marshalasprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "probe")]
    private static extern int probe_ansi_lpstr([MarshalAs(UnmanagedType.LPStr)] string s, int unit, byte[] outBytes, int cap);
    [DllImport("marshalasprobe.dll", CharSet = CharSet.Unicode, EntryPoint = "probe")]
    private static extern int probe_uni_lptstr([MarshalAs(UnmanagedType.LPTStr)] string s, int unit, byte[] outBytes, int cap);
    [DllImport("marshalasprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "probe")]
    private static extern int probe_ansi_lptstr([MarshalAs(UnmanagedType.LPTStr)] string s, int unit, byte[] outBytes, int cap);
    [DllImport("marshalasprobe.dll", CharSet = CharSet.Unicode, EntryPoint = "probe")]
    private static extern int probe_uni_utf8([MarshalAs(UnmanagedType.LPUTF8Str)] string s, int unit, byte[] outBytes, int cap);
    [DllImport("marshalasprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "probe")]
    private static extern int probe_ansi_utf8([MarshalAs(UnmanagedType.LPUTF8Str)] string s, int unit, byte[] outBytes, int cap);
    // No CharSet at all (defaults to Ansi) + LPStr.
    [DllImport("marshalasprobe.dll", EntryPoint = "probe")]
    private static extern int probe_none_lpstr([MarshalAs(UnmanagedType.LPStr)] string s, int unit, byte[] outBytes, int cap);

    // --- controls: no parameter-level MarshalAs (import CharSet only) -------
    [DllImport("marshalasprobe.dll", CharSet = CharSet.Unicode, EntryPoint = "probe")]
    private static extern int probe_uni_none(string s, int unit, byte[] outBytes, int cap);
    [DllImport("marshalasprobe.dll", CharSet = CharSet.Ansi, EntryPoint = "probe")]
    private static extern int probe_ansi_none(string s, int unit, byte[] outBytes, int cap);

    // --- Seq=0 guard: a [return: MarshalAs(...)] row coexisting with a
    // parameter-level override must NOT shift the parameter projection.
    [DllImport("marshalasprobe.dll", CharSet = CharSet.Unicode, EntryPoint = "probe")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static extern int probe_ret_uni_lpstr([MarshalAs(UnmanagedType.LPStr)] string s, int unit, byte[] outBytes, int cap);

    // --- per-parameter independence: LPStr + LPWStr + (no MarshalAs) in ONE
    // call, all on a CharSet.Unicode import.
    [DllImport("marshalasprobe.dll", CharSet = CharSet.Unicode, EntryPoint = "probe3")]
    private static extern int probe3_mixed(
        [MarshalAs(UnmanagedType.LPStr)] string a, int aw,
        [MarshalAs(UnmanagedType.LPWStr)] string b, int bw,
        string c, int cw,
        byte[] outBytes, int cap);

    // A RUNTIME null (not a compile-time literal) exercises the allocator's
    // null input on the override paths.
    private static string s_null;

    public static void Main()
    {
        // The decisive override records (2 CJK code units / 3 CP_ACP bytes).
        Record("uni.lpstr.cjk", probe_uni_lpstr("\u4E2D", 1, s_buf, Cap));
        Record("ansi.lpwstr.cjk", probe_ansi_lpwstr("\u4E2D", 2, s_buf, Cap));
        // Plain pairs (the MarshalAs agrees with / contradicts the CharSet).
        Record("uni.lpwstr.cjk", probe_uni_lpwstr("\u4E2D", 2, s_buf, Cap));
        Record("ansi.lpstr.cjk", probe_ansi_lpstr("\u4E2D", 1, s_buf, Cap));
        // LPTStr is UTF-16 on Windows for BOTH import charsets.
        Record("uni.lptstr.cjk", probe_uni_lptstr("\u4E2D", 2, s_buf, Cap));
        Record("ansi.lptstr.cjk", probe_ansi_lptstr("\u4E2D", 2, s_buf, Cap));
        // LPUTF8Str is UTF-8 for BOTH import charsets.
        Record("uni.utf8.cjk", probe_uni_utf8("\u4E2D", 1, s_buf, Cap));
        Record("ansi.utf8.cjk", probe_ansi_utf8("\u4E2D", 1, s_buf, Cap));
        // No CharSet + LPStr -> CP_ACP.
        Record("none.lpstr.cjk", probe_none_lpstr("\u4E2D", 1, s_buf, Cap));
        // Controls: no MarshalAs -> import CharSet decides.
        Record("uni.none.cjk", probe_uni_none("\u4E2D", 2, s_buf, Cap));
        Record("ansi.none.cjk", probe_ansi_none("\u4E2D", 1, s_buf, Cap));

        // ASCII is an identity in the byte encodings, and UTF-16 in the wide one.
        Record("uni.lpstr.ascii", probe_uni_lpstr("Abc", 1, s_buf, Cap));
        Record("ansi.lpwstr.ascii", probe_ansi_lpwstr("Abc", 2, s_buf, Cap));

        // Astral surrogate pair through CP_ACP: one '?' per surrogate HALF.
        Record("uni.lpstr.emoji", probe_uni_lpstr("\uD83D\uDE00", 1, s_buf, Cap));

        // NULL managed string -> the callee's pointer is NULL.
        Record("uni.lpstr.null.field", probe_uni_lpstr(s_null, 1, s_buf, Cap));
        Record("ansi.lpwstr.null.field", probe_ansi_lpwstr(s_null, 2, s_buf, Cap));
        Record("uni.lpstr.null.literal", probe_uni_lpstr(null, 1, s_buf, Cap));

        // Empty string -> a non-NULL, single-NUL buffer.
        Record("uni.lpstr.empty", probe_uni_lpstr("", 1, s_buf, Cap));

        // [return: MarshalAs] on the same method: the parameter override must
        // still be honored (Seq=0 is NOT the first parameter).
        Record("ret.uni.lpstr.cjk", probe_ret_uni_lpstr("\u4E2D", 1, s_buf, Cap));

        // Per-parameter independence: a=LPStr("A\u4E2D"), b=LPWStr("\u4E2DB"),
        // c=no MarshalAs on a Unicode import ("\u4E2D").
        Record("multi.mixed", probe3_mixed("A\u4E2D", 1, "\u4E2DB", 2, "\u4E2D", 2, s_buf, Cap));
    }

    private static void Record(string id, int rc)
    {
        if (rc < 0)
        {
            Console.WriteLine("[" + id + "]=NULL");
            return;
        }
        Console.WriteLine("[" + id + "]=" + Hex(s_buf, rc));
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
}
