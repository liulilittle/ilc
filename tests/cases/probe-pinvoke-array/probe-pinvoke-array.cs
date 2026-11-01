//------------------------------------------------------------------------------
// probe-pinvoke-array.cs -- managed ARRAY DllImport parameters
// (net48 oracle == transpiled runtime, byte-exact stdout).
//
// MEASURED on Framework64 v4.0.30319 with arrayprobe.dll (this case's own
// SHARED fixture) -- see the [array.*] records:
//   * a null array -> a NULL native pointer;
//   * a zero-length array -> a NON-NULL pointer to the array start;
//   * blittable element arrays pass the array payload and the callee's writes
//     are visible to the managed side after the call;
//   * the element widths are the managed ones (byte 1, short 2, int 4,
//     long 8, char 2);
//   * a struct array element is the struct's own layout;
//   * a forced GC.Collect() from a native callback between two writes does
//     not disturb the buffer (the array is pinned across the call).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class ProbePinvokeArray
    {
        private const string LIB = "arrayprobe.dll";

        // byte[] -- the four declaration shapes.
        [DllImport(LIB)] private static extern int byte0(byte[] b);
        [DllImport(LIB)] private static extern void set_byte(byte[] b, int i, int v);
        [DllImport(LIB, EntryPoint = "byte0")] private static extern int byte0_in([In] byte[] b);
        [DllImport(LIB, EntryPoint = "set_byte")] private static extern void set_byte_in([In] byte[] b, int i, int v);
        [DllImport(LIB, EntryPoint = "byte0")] private static extern int byte0_out([Out] byte[] b);
        [DllImport(LIB, EntryPoint = "set_byte")] private static extern void set_byte_out([Out] byte[] b, int i, int v);
        [DllImport(LIB, EntryPoint = "byte0")] private static extern int byte0_inout([In, Out] byte[] b);
        [DllImport(LIB, EntryPoint = "set_byte")] private static extern void set_byte_inout([In, Out] byte[] b, int i, int v);

        // int[] / long[] / short[].
        [DllImport(LIB)] private static extern int int0(int[] a);
        [DllImport(LIB)] private static extern void set_int(int[] a, int i, int v);
        [DllImport(LIB)] private static extern long long0(long[] a);
        [DllImport(LIB)] private static extern void set_long(long[] a, int i, long v);
        [DllImport(LIB)] private static extern int short0(short[] a);
        [DllImport(LIB)] private static extern void set_short(short[] a, int i, int v);

        // char[] -- CharSet.Ansi (the C# default) and CharSet.Unicode.

        [DllImport(LIB, EntryPoint = "first4", CharSet = CharSet.Unicode)] private static extern int first4_uni(char[] a);
        [DllImport(LIB, EntryPoint = "set_byte", CharSet = CharSet.Unicode)] private static extern void set_byte_uni(char[] a, int i, int v);

        // --- extra measurement entries ---
        [DllImport(LIB, EntryPoint = "first4", CharSet = CharSet.Auto)] private static extern int first4_auto(char[] a);
        [DllImport(LIB, EntryPoint = "f0")] private static extern float f0(float[] a);
        [DllImport(LIB, EntryPoint = "set_f")] private static extern void set_f(float[] a, int i, float v);
        [DllImport(LIB, EntryPoint = "ip0")] private static extern long ip0(IntPtr[] a);
        [DllImport(LIB, EntryPoint = "set_ip")] private static extern void set_ip(IntPtr[] a, int i, long v);
        [DllImport(LIB, EntryPoint = "guid_byte0")] private static extern int guid_byte0(Guid[] a);
        [DllImport(LIB, EntryPoint = "guid_set_byte")] private static extern void guid_set_byte(Guid[] a, int off, int v);
        [DllImport(LIB, EntryPoint = "rect_sum")] private static extern int rect_sum_inout([In, Out] Rect4[] a, int n);
        [DllImport(LIB, EntryPoint = "rect_set_first_left")] private static extern void rect_set_first_left_inout([In, Out] Rect4[] a, int v);
        [DllImport(LIB, EntryPoint = "rect_sum")] private static extern int rect_sum_out([Out] Rect4[] a, int n);
        [DllImport(LIB, EntryPoint = "rect_set_first_left")] private static extern void rect_set_first_left_out([Out] Rect4[] a, int v);
        // struct array (4 x int32 element).
        [DllImport(LIB)] private static extern int rect_sum(Rect4[] a, int n);
        [DllImport(LIB, EntryPoint = "rect_set_first_left")] private static extern void rect_set_first_left(Rect4[] a, int v);

        // null / zero-length.
        [DllImport(LIB, EntryPoint = "is_null")] private static extern int is_null(byte[] b);
        [DllImport(LIB, EntryPoint = "same_ptr")] private static extern int same_ptr(byte[] a, byte[] b);

        // GC forced between the callee's two writes (the callback collects).
        [DllImport(LIB)] private static extern int gc_then_write(byte[] b, GcAction cb);

        public delegate void GcAction();

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect4
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static int _gcCalls;

        public static void Main()
        {
            // 1. byte[]: default (no [In]/[Out]).
            var b1 = new byte[] { 0x11, 0x22, 0x33, 0x44 };
            Record("byte.default.seen", byte0(b1));
            set_byte(b1, 2, 0x99);
            Record("byte.default.after", b1[2]);

            // 2. byte[]: [In].
            var b2 = new byte[] { 0x21, 0x22, 0x23, 0x24 };
            Record("byte.in.seen", byte0_in(b2));
            set_byte_in(b2, 1, 0x98);
            Record("byte.in.after", b2[1]);

            // 3. byte[]: [Out] (the callee's observation is NOT pinned by the
            //    oracle contract -- only the write-back is compared).
            var b3 = new byte[] { 0x31, 0x32, 0x33, 0x34 };
            set_byte_out(b3, 3, 0x97);
            Record("byte.out.after", b3[3]);

            // 4. byte[]: [In, Out].
            var b4 = new byte[] { 0x41, 0x42, 0x43, 0x44 };
            Record("byte.inout.seen", byte0_inout(b4));
            set_byte_inout(b4, 0, 0x96);
            Record("byte.inout.after", b4[0]);

            // 5. int[].
            var ia = new int[] { 100, 200, 300 };
            Record("int.seen", int0(ia));
            set_int(ia, 1, -7);
            Record("int.after", ia[1]);

            // 6. long[].
            var la = new long[] { 4000000001L, -2L };
            Record("long.seen", long0(la));
            set_long(la, 1, 9000000002L);
            Record("long.after", la[1]);

            // 7. short[].
            var sa = new short[] { 1234, -2 };
            Record("short.seen", short0(sa));
            set_short(sa, 0, -30000);
            Record("short.after", sa[0]);

            // 8. char[] element width: UTF-16 (CharSet.Unicode / Auto; a
            //    CharSet.Ansi char[] is converted to narrow ANSI by net48 and
            //    is LOUD-rejected by the transpiler -- measured 0x44434241).
            var cu = new char[] { 'A', 'B', 'C', 'D' };
            Record("char.uni.first4", first4_uni(cu));
            set_byte_uni(cu, 0, 0x5A);
            Record("char.uni.after0", (int)cu[0]);
            Record("char.uni.after1", (int)cu[1]);

            // 9. struct array.
            var ra = new Rect4[2];
            ra[0].Left = 1; ra[0].Top = 2; ra[0].Right = 3; ra[0].Bottom = 4;
            ra[1].Left = 10; ra[1].Top = 20; ra[1].Right = 30; ra[1].Bottom = 40;
            Record("rect.seen.sum", rect_sum(ra, 2));
            rect_set_first_left(ra, 777);
            Record("rect.after.left", ra[0].Left);

            // 10. null array.
            Record("null.isnull", is_null(null));
            Record("null.same", same_ptr(null, null));

            // 11. zero-length array: non-null pointer to the array start.
            var z1 = new byte[0];
            Record("zero.byte.isnull", is_null(z1));
            var z2 = new byte[0];
            Record("zero.byte.distinct", same_ptr(z1, z2));

            // --- extra measurements ---
            Record("byte.out.seen", byte0_out(new byte[] { 0x35, 2, 3, 4 }));
            var ca2 = new char[] { 'A', 'B', 'C', 'D' };
            Record("char.auto.first4", first4_auto(ca2));
            var fa = new float[] { 1.5f, -2.25f };
            Record("extra.float.seen", (long)(f0(fa) * 100));
            set_f(fa, 1, 7.5f);
            Record("extra.float.after", (long)(fa[1] * 100));
            var pa = new IntPtr[] { (IntPtr)0x1234, (IntPtr)0x5678 };
            Record("extra.ptr.seen", ip0(pa));
            set_ip(pa, 1, 0x7fffffff12345678L);
            Record("extra.ptr.after", pa[1].ToInt64());
            var ga = new Guid[2];
            ga[0] = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
            Record("extra.guid.seen", guid_byte0(ga));
            guid_set_byte(ga, 3, 0xAB);
            Record("extra.guid.after3", (int)ga[0].ToByteArray()[3]);
            var r2 = new Rect4[1];
            r2[0].Left = 5;
            Record("extra.rect.inout.seen", rect_sum_inout(r2, 1));
            rect_set_first_left_inout(r2, 888);
            Record("extra.rect.inout.after", r2[0].Left);
            var r3 = new Rect4[1];
            r3[0].Left = 6;
            Record("extra.rect.out.seen", rect_sum_out(r3, 1));
            rect_set_first_left_out(r3, 999);
            Record("extra.rect.out.after", r3[0].Left);
            // 12. forced collection from a native callback between the
            //     callee's two writes -- the write-back must not be corrupted.
            var g = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            _gcCalls = 0;
            Record("gc.seen", gc_then_write(g, OnGc));
            Record("gc.after0", g[0]);
            Record("gc.after1", g[1]);
            Record("gc.calls", _gcCalls);
        }

        private static void OnGc()
        {
            _gcCalls++;
            GC.Collect();
            // Allocation pressure so a compacting collector has something to do.
            for (var i = 0; i < 64; i++)
            {
                var tmp = new byte[256];
                tmp[0] = (byte)i;
            }
            GC.Collect();
        }

        private static void Record(string id, long value)
        {
            Console.WriteLine("[" + id + "]=" + value);
        }

        private static void Record(string id, string value)
        {
            Console.WriteLine("[" + id + "]=" + value);
        }
    }
}
