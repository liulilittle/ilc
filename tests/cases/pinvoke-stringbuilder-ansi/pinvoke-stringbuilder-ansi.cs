//------------------------------------------------------------------------------
// pinvoke-stringbuilder-ansi.cs -- `StringBuilder` DllImport parameters under
// CharSet.Ansi (net48 oracle == transpiled runtime, byte-exact stdout).
//
// MEASURED on net48 v4.0.30319 (host CP_ACP=936, zh-CN) with sbansiprobe.dll:
//
//   * the callee receives a CP_ACP BYTE buffer, not the UTF-16 chunk:
//       "ABC"          -> 41 42 43            (3 bytes)
//       U+4E2D U+6587  -> D6 D0 CE C4         (4 bytes for 2 characters)
//   * byte capacity == StringBuilder.Capacity -- ONE byte per capacity unit, so
//     a 2-unit CJK character consumes TWO capacity units.
//   * the buffer IS NUL-terminated after the content (unlike char[] under Ansi,
//     which is not), so a strlen-style callee sees exactly the encoded length.
//   * copy-back DECODES the byte string through CP_ACP: the two bytes D6 D0
//     become ONE managed character (measured Length 1, sb[0] == U+4E2D).
//   * net48 grows the builder when the encoded content does not fit.
//   * a null builder yields NULL.
//   * CharSet.Unicode is the control path: the UTF-16 chunk is aliased
//     directly, so U+4E2D U+6587 reads as 2D 4E 87 65.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Cases
{
    public static class ProbePinvokeStringBuilderAnsi
    {
        [DllImport("sbansiprobe.dll", EntryPoint = "sb_len", CharSet = CharSet.Ansi)]
        private static extern int Len(StringBuilder sb);
        [DllImport("sbansiprobe.dll", EntryPoint = "sb_dump", CharSet = CharSet.Ansi)]
        private static extern int Dump(StringBuilder sb, int nbytes, int cap, byte[] outBuf);
        [DllImport("sbansiprobe.dll", EntryPoint = "sb_isnull", CharSet = CharSet.Ansi)]
        private static extern int IsNull(StringBuilder sb);
        [DllImport("sbansiprobe.dll", EntryPoint = "sb_write_ascii", CharSet = CharSet.Ansi)]
        private static extern int WriteAscii(StringBuilder sb, int cap);
        [DllImport("sbansiprobe.dll", EntryPoint = "sb_write_cjk", CharSet = CharSet.Ansi)]
        private static extern int WriteCjk(StringBuilder sb, int cap);
        [DllImport("sbansiprobe.dll", EntryPoint = "sb_write_latin", CharSet = CharSet.Ansi)]
        private static extern int WriteLatin(StringBuilder sb, int cap);

        // Control: CharSet.Unicode aliases the UTF-16 chunk (no conversion).
        [DllImport("sbansiprobe.dll", EntryPoint = "sb_dump", CharSet = CharSet.Unicode)]
        private static extern int DumpW(StringBuilder sb, int nbytes, int cap, byte[] outBuf);

        public static void Main()
        {
            byte[] o = new byte[128];

            // --- what the callee receives: CP_ACP bytes ---
            // Only the CONTENT plus its NUL is asserted. Beyond the terminator
            // the buffer is free space: net48 leaves uninitialised heap there
            // (observed 73 00 00 00 28 00 00 00 and 02 00 00 00 40 F0 FA 5D),
            // so dumping past it would pin garbage. Same lesson as the char[]
            // Ansi case (b5-gaps section 36).
            var ascii = new StringBuilder("ABC", 8);
            Record("sb.ascii.len", Len(ascii));
            Show("sb.ascii", Dump(ascii, Len(ascii) + 1, 128, o), o);

            var cjk = new StringBuilder("\u4E2D\u6587", 8);
            Record("sb.cjk.len", Len(cjk));
            Show("sb.cjk", Dump(cjk, Len(cjk) + 1, 128, o), o);

            var empty = new StringBuilder(8);
            Record("sb.empty.len", Len(empty));
            Record("sb.null.isnull", IsNull(null));

            // --- CharSet.Unicode control: the chunk passes through as UTF-16 ---
            var uni = new StringBuilder("\u4E2D\u6587", 8);
            Show("sb.uni", DumpW(uni, 8, 128, o), o);

            // --- copy-back: ASCII bytes become ASCII characters ---
            var w1 = new StringBuilder("ABC", 16);
            Record("sb.write.ascii.ret", WriteAscii(w1, w1.Capacity));
            Record("sb.write.ascii.len", w1.Length);
            Console.WriteLine("[sb.write.ascii.text]=" + w1.ToString());

            // --- copy-back: a 2-byte CP_ACP sequence becomes ONE character ---
            var w2 = new StringBuilder("ABC", 16);
            Record("sb.write.cjk.ret", WriteCjk(w2, w2.Capacity));
            Record("sb.write.cjk.len", w2.Length);
            Record("sb.write.cjk.ch0", w2.Length > 0 ? (int)w2[0] : -1);

            // --- copy-back: a byte with no CP936 mapping decodes to the fallback ---
            var w3 = new StringBuilder("ABC", 16);
            Record("sb.write.latin.ret", WriteLatin(w3, w3.Capacity));
            Record("sb.write.latin.len", w3.Length);
            Record("sb.write.latin.ch0", w3.Length > 0 ? (int)w3[0] : -1);
            Record("sb.write.latin.ch1", w3.Length > 1 ? (int)w3[1] : -1);
        }

        private static void Show(string id, int n, byte[] buf)
        {
            if (n < 0) { Record(id + ".null", 1); return; }
            var s = new StringBuilder();
            for (int i = 0; i < n; i++) s.Append(buf[i].ToString("X2")).Append(' ');
            Console.WriteLine("[" + id + "]=[" + s.ToString().Trim() + "] n=" + n);
        }

        private static void Record(string id, long value)
        {
            Console.WriteLine("[" + id + "]=" + value);
        }
    }
}
