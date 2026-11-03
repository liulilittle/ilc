//------------------------------------------------------------------------------
// pinvoke-chararray-ansi.cs -- `char[]` DllImport parameters under
// CharSet.Ansi (net48 oracle == transpiled runtime, byte-exact stdout).
//
// MEASURED on net48 v4.0.30319 (host CP_ACP=936 zh-CN) with chararrprobe.dll
// (this case's own SHARED fixture):
//   * the whole array is converted to a CP_ACP byte buffer, but -- and this is
//     the trap -- net48 does NOT NUL-terminate it:
//       {'\u4E2D','\u6587'} -> D6 D0 CE C4   (2 bytes per unit, 4 total)
//       {'A','b','c'}       -> 41 62 63      (3 total)
//     Reading past the converted bytes with strlen returned 4, 4, 6 and 8 on
//     four runs of the SAME binary, so every probe here uses an EXPLICIT byte
//     count derived from Encoding.Default.GetByteCount -- never strlen.
//   * a null array   -> NULL.
//   * an EMPTY array -> a NON-NULL pointer. Its length is NOT stable (same
//     uninitialised-memory reason), so only NULL-ness is asserted.
//   * surrogate halves are unrepresentable in CP936 and become '?' (3F 3F).
//   * copy-back: default / [In] -> NONE; [In,Out] -> yes; [Out] -> the callee
//     receives a ZEROED buffer and the result is copied back.
//   * CharSet.Unicode is the control path: a 2-byte UTF-16 element, zero-copy,
//     so the same input produces 2D 4E 87 65.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class ProbePinvokeCharArrayAnsi
    {
        // ---- CharSet.Ansi: the array is converted to a narrow CP_ACP buffer ----
        [DllImport("chararrprobe.dll", EntryPoint = "ca_read", CharSet = CharSet.Ansi)]
        private static extern int a_read(char[] c, int nbytes, int cap, byte[] outBuf);
        [DllImport("chararrprobe.dll", EntryPoint = "ca_isnull", CharSet = CharSet.Ansi)]
        private static extern int a_isnull(char[] c);
        [DllImport("chararrprobe.dll", EntryPoint = "ca_write", CharSet = CharSet.Ansi)]
        private static extern int a_write_default(char[] c, int cap);
        [DllImport("chararrprobe.dll", EntryPoint = "ca_write", CharSet = CharSet.Ansi)]
        private static extern int a_write_inout([In, Out] char[] c, int cap);
        [DllImport("chararrprobe.dll", EntryPoint = "ca_write", CharSet = CharSet.Ansi)]
        private static extern int a_write_out([Out] char[] c, int cap);
        // Read-only: proves the [Out] seed policy (zeroed) vs [In,Out] (input).
        [DllImport("chararrprobe.dll", EntryPoint = "ca_peek", CharSet = CharSet.Ansi)]
        private static extern int a_peek_default(char[] c, int nbytes);
        [DllImport("chararrprobe.dll", EntryPoint = "ca_peek", CharSet = CharSet.Ansi)]
        private static extern int a_peek_inout([In, Out] char[] c, int nbytes);
        [DllImport("chararrprobe.dll", EntryPoint = "ca_peek", CharSet = CharSet.Ansi)]
        private static extern int a_peek_out([Out] char[] c, int nbytes);

        // ---- CharSet.Unicode: the control path (2-byte element, zero-copy) ----
        [DllImport("chararrprobe.dll", EntryPoint = "ca_read", CharSet = CharSet.Unicode)]
        private static extern int u_read(char[] c, int nbytes, int cap, byte[] outBuf);

        public static void Main()
        {
            byte[] buf = new byte[64];

            // The expected byte count comes from the SAME encoder net48 uses for
            // CharSet.Ansi (Encoding.Default == CP_ACP), so the assertion does not
            // depend on reading past the conversion.
            Dump("ansi.cjk", a_read(new char[] { '\u4E2D', '\u6587' }, 4, 64, buf), buf);
            Dump("ansi.ascii", a_read(new char[] { 'A', 'b', 'c' }, 3, 64, buf), buf);
            Dump("ansi.single", a_read(new char[] { 'A' }, 1, 64, buf), buf);
            Dump("ansi.emoji", a_read(new char[] { '\uD83D', '\uDE00' }, 2, 64, buf), buf);

            // --- null vs empty: only NULL-ness is stable ---
            Record("ansi.null.isnull", a_isnull(null));
            Record("ansi.empty.isnull", a_isnull(new char[0]));

            // --- CharSet.Unicode control: UTF-16 units pass through untouched ---
            Dump("uni.cjk", u_read(new char[] { '\u4E2D', '\u6587' }, 4, 64, buf), buf);

            // --- SEED policy, observed without writing: [Out] must see zeros ---
            Record("ansi.peek.default", a_peek_default(new char[] { 'A', 'B', 'C' }, 3));
            Record("ansi.peek.inout", a_peek_inout(new char[] { 'A', 'B', 'C' }, 3));
            Record("ansi.peek.out", a_peek_out(new char[] { 'A', 'B', 'C' }, 3));

            // --- copy-back policy (measured) ---
            char[] def = new char[] { 'A', 'B', 'C' };
            int defRet = a_write_default(def, def.Length);
            Record("ansi.write.default.ret", defRet);
            Record("ansi.write.default.a0", def[0]);
            Record("ansi.write.default.a1", def[1]);

            char[] io = new char[] { 'A', 'B', 'C' };
            int ioRet = a_write_inout(io, io.Length);
            Record("ansi.write.inout.ret", ioRet);
            Record("ansi.write.inout.a0", io[0]);
            Record("ansi.write.inout.a1", io[1]);
            Record("ansi.write.inout.a2", io[2]);

            char[] oo = new char[] { 'A', 'B', 'C' };
            int ooRet = a_write_out(oo, oo.Length);
            Record("ansi.write.out.ret", ooRet);
            Record("ansi.write.out.a0", oo[0]);
            Record("ansi.write.out.a1", oo[1]);
            Record("ansi.write.out.a2", oo[2]);
        }

        private static void Dump(string id, int n, byte[] buf)
        {
            if (n < 0) { Record(id + ".null", 1); return; }
            var s = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++) s.Append(buf[i].ToString("X2")).Append(' ');
            Console.WriteLine("[" + id + "]=[" + s.ToString().Trim() + "] n=" + n);
        }

        private static void Record(string id, long value)
        {
            Console.WriteLine("[" + id + "]=" + value);
        }
    }
}
