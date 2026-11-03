// pinvoke-refstring.cs -- `ref string` / `out string` across a DllImport
// boundary (the "B:r:System.String" signature encoding).
//
// MEASURED net48 contract (oracle .opencode/tmp/refstr):
//   * the native view is a POINTER TO the string pointer (char** / char16_t**),
//     NOT a char*. Declaring the callee parameter as char* makes it read the
//     pointer BYTES (F0 61 82 11 19 02 00 00) and crash.
//   * Ansi converts through CP_ACP: "ABC" -> 41 42 43, U+4E2D U+6587 ->
//     D6 D0 CE C4 (4 bytes for 2 characters). Unicode passes UTF-16 through.
//   * a NULL managed string gives *cell == nullptr while the CELL itself is a
//     valid address.
//   * the callee may WRITE through the cell: overwriting the content is
//     visible in the managed string after the call; writing NULL nulls the
//     managed ref slot.
//
// Before this case the transpiler LOUD-rejected `B:r:System.String`, so any
// assembly passing a string by reference to native code could not compile at
// all.
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class ProbePinvokeRefString
    {
        [DllImport("reflib2", EntryPoint = "ref2_slot_isnull", CharSet = CharSet.Ansi)]
        private static extern int SlotIsNull(ref string s);

        [DllImport("reflib2", EntryPoint = "ref2_value_isnull", CharSet = CharSet.Ansi)]
        private static extern int ValueIsNull(ref string s);

        [DllImport("reflib2", EntryPoint = "ref2_len", CharSet = CharSet.Ansi)]
        private static extern int Len(ref string s);

        [DllImport("reflib2", EntryPoint = "ref2_bytes", CharSet = CharSet.Ansi)]
        private static extern int Bytes(ref string s, int n, byte[] outp);

        [DllImport("reflib2", EntryPoint = "ref2_overwrite", CharSet = CharSet.Ansi)]
        private static extern int Overwrite(ref string s);

        [DllImport("reflib2", EntryPoint = "ref2_clear", CharSet = CharSet.Ansi)]
        private static extern int Clear(ref string s);

        [DllImport("reflib2", EntryPoint = "ref2_clear", CharSet = CharSet.Ansi)]
        private static extern int ClearOut(out string s);

        // CharSet.Unicode control: the cell element MUST be 2 bytes wide.
        [DllImport("reflib2", EntryPoint = "ref2w_len", CharSet = CharSet.Unicode)]
        private static extern int LenW(ref string s);

        [DllImport("reflib2", EntryPoint = "ref2w_bytes", CharSet = CharSet.Unicode)]
        private static extern int BytesW(ref string s, int n, ushort[] outp);

        private static void Record(string id, long value) => Console.WriteLine("[" + id + "]=" + value);

        public static void Main()
        {
            var buf = new byte[16];

            // ---- Ansi: what the callee sees ----
            string s1 = "ABC";
            Record("ansi.slot.isnull", SlotIsNull(ref s1));
            Record("ansi.value.isnull", ValueIsNull(ref s1));
            Record("ansi.len", Len(ref s1));
            Record("ansi.bytes.n", Bytes(ref s1, 8, buf));
            for (var i = 0; i < 3; i++) Record("ansi.bytes.b" + i, buf[i]);
            Record("ansi.read.unchanged", s1 == "ABC" ? 1 : 0);

            // ---- CP_ACP conversion for non-ASCII ----
            string cjk = "\u4E2D\u6587";
            Record("ansi.cjk.len", Len(ref cjk));
            var nc = Bytes(ref cjk, 8, buf);
            Record("ansi.cjk.n", nc);
            for (var i = 0; i < nc; i++) Record("ansi.cjk.b" + i, buf[i]);

            // ---- null managed string: the CELL exists, its value is NULL ----
            string sn = null;
            Record("ansi.null.slot.isnull", SlotIsNull(ref sn));
            Record("ansi.null.value.isnull", ValueIsNull(ref sn));
            Record("ansi.null.len", Len(ref sn));
            Record("ansi.null.after", sn == null ? 1 : 0);

            // ---- the callee overwrites the content: managed sees it ----
            string sw = "orig";
            Record("ansi.write.ret", Overwrite(ref sw));
            Record("ansi.write.isnull", sw == null ? 1 : 0);
            Record("ansi.write.len", sw == null ? -1 : sw.Length);
            Record("ansi.write.ch0", sw == null ? -1 : sw[0]);
            Record("ansi.write.ch1", sw == null ? -1 : sw[1]);
            Record("ansi.write.eqorig", sw == "orig" ? 1 : 0);

            // ---- the callee NULLs the cell: managed slot becomes null ----
            string sc = "orig";
            Record("ansi.clear.ret", Clear(ref sc));
            Record("ansi.clear.isnull", sc == null ? 1 : 0);

            // ---- out string: same ABI, the cell starts from the (null) slot ----
            string so;
            Record("ansi.out.clear.ret", ClearOut(out so));
            Record("ansi.out.isnull", so == null ? 1 : 0);

            // ---- CharSet.Unicode: 2-byte cell, no code-page conversion ----
            var wbuf = new ushort[8];
            string uw = "AB";
            Record("uni.len", LenW(ref uw));
            var nw = BytesW(ref uw, 4, wbuf);
            Record("uni.bytes.n", nw);
            for (var i = 0; i < nw; i++) Record("uni.bytes.u" + i, wbuf[i]);

            // U+4E2D U+6587 stay verbatim (20013 / 25991), NOT CP_ACP bytes.
            string ucj = "\u4E2D\u6587";
            Record("uni.cjk.len", LenW(ref ucj));
            var nwj = BytesW(ref ucj, 4, wbuf);
            Record("uni.cjk.n", nwj);
            for (var i = 0; i < nwj; i++) Record("uni.cjk.u" + i, wbuf[i]);
        }
    }
}
