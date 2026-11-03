// pinvoke-multidim.cs -- MULTI-DIMENSIONAL array parameters across a DllImport
// boundary (the "r:<elem>[<rank>]" signature encoding).
//
// MEASURED net48 contract (oracle .opencode/tmp/mdarr):
//   * net48 ACCEPTS `int[,]` at a DllImport boundary -- it is NOT rejected.
//   * the callee receives a FLAT ROW-MAJOR buffer: int[2,3]{{1,2,3},{4,5,6}}
//     arrives as 1,2,3,4,5,6.
//   * the buffer address is the SAME as the managed array's pinned payload,
//     and a callee write is visible in the managed array immediately: the
//     array is PINNED, not copied (unlike bool[] / struct[]).
//   * a null multidim array gives NULL.
//   * the element width follows the element type (a short[,] keeps 2 bytes).
//
// Before this case the transpiler LOUD-rejected every multidim array parameter,
// so an assembly passing one to native code could not be compiled at all --
// even though net48 supports it and this model stores multidim arrays as an
// Il2CppArray with exactly that row-major flat payload.
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class ProbePinvokeMultidim
    {
        [DllImport("mdlib2", EntryPoint = "md2_addr")]
        private static extern IntPtr Addr(int[,] a);

        [DllImport("mdlib2", EntryPoint = "md2_isnull")]
        private static extern int IsNull(int[,] a);

        [DllImport("mdlib2", EntryPoint = "md2_dump")]
        private static extern int Dump(int[,] a, int n, int[] outp);

        [DllImport("mdlib2", EntryPoint = "md2_store")]
        private static extern int Store(int[,] a, int n);

        [DllImport("mdlib2", EntryPoint = "md2_store")]
        private static extern int StoreInOut([In, Out] int[,] a, int n);

        [DllImport("mdlib2", EntryPoint = "md2_dump16")]
        private static extern int Dump16(short[,] a, int n, short[] outp);

        private static void Record(string id, long value) => Console.WriteLine("[" + id + "]=" + value);

        public static void Main()
        {
            var buf = new int[16];

            // ---- row-major flat order ----
            var a = new int[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
            var n = Dump(a, 6, buf);
            Record("dump.n", n);
            for (var i = 0; i < 6; i++) Record("dump.e" + i, buf[i]);

            // ---- pointer identity: the callee gets the managed payload ----
            var b = new int[2, 3] { { 7, 8, 9 }, { 10, 11, 12 } };
            var nativeAddr = Addr(b);
            var managedAddr = Marshal.UnsafeAddrOfPinnedArrayElement(b, 0);
            Record("addr.native.nonzero", nativeAddr != IntPtr.Zero ? 1 : 0);
            Record("addr.managed.nonzero", managedAddr != IntPtr.Zero ? 1 : 0);
            Record("addr.same", nativeAddr == managedAddr ? 1 : 0);

            // ---- null ----
            Record("null.isnull", IsNull(null));

            // ---- pin semantics: the callee write is visible immediately ----
            var c = new int[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
            Record("store.ret", Store(c, 6));
            Record("store.c00", c[0, 0]);
            Record("store.c01", c[0, 1]);
            Record("store.c11", c[1, 2]);

            // ---- [In, Out] behaves the same (the array is pinned) ----
            var e = new int[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
            Record("inout.ret", StoreInOut(e, 6));
            Record("inout.c00", e[0, 0]);
            Record("inout.c11", e[1, 2]);

            // ---- a narrower element type keeps its width ----
            var sbuf = new short[16];
            var s = new short[2, 2] { { 1000, 2000 }, { 3000, 4000 } };
            var ns = Dump16(s, 4, sbuf);
            Record("short.n", ns);
            for (var i = 0; i < 4; i++) Record("short.e" + i, sbuf[i]);
        }
    }
}
