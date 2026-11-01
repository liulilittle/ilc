//------------------------------------------------------------------------------
// probe-pinvoke-byref.cs -- ref/out SCALAR DllImport parameters
// (net48 oracle == transpiled runtime, byte-exact stdout).
//
// MEASURED on Framework64 v4.0.30319 with byrefprobe.dll (this case's own
// SHARED fixture):
//   * `ref T` hands the callee the managed slot's CURRENT value (the mangled
//     local/field address is passed through -- no copy on x64 for blittable
//     scalars);
//   * `out T` is NOT zeroed by the marshal stub: a fresh local reads 0 only
//     because the JIT zeroes every local at method ENTRY (.locals init), and a
//     local that already holds a value passes THAT value to the callee;
//   * the cell widths are the native ones: 1/2/4/8 bytes and the 16-byte Guid;
//   * `ref bool` is a 4-byte BOOL cell and the copy-back NORMALIZES (the callee
//     wrote 2, the managed bool reads true and `b == true` is True).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public sealed class ByRefHolder
    {
        public int Field;
        public static int SField;
    }

    public static class ProbePinvokeByref
    {
        [DllImport("byrefprobe.dll")]
        private static extern int bump_i32(ref int p);
        [DllImport("byrefprobe.dll", EntryPoint = "read_then_fill_i32")]
        private static extern int read_then_fill_i32_ref(ref int p);
        [DllImport("byrefprobe.dll", EntryPoint = "read_then_fill_i32")]
        private static extern int read_then_fill_i32_out(out int p);
        [DllImport("byrefprobe.dll")]
        private static extern void fill_i32(out int p);
        [DllImport("byrefprobe.dll")]
        private static extern uint bump_u32(ref uint p);
        [DllImport("byrefprobe.dll")]
        private static extern long bump_i64(ref long p);
        [DllImport("byrefprobe.dll")]
        private static extern ulong bump_u64(ref ulong p);
        [DllImport("byrefprobe.dll")]
        private static extern int bump_i16(ref short p);
        [DllImport("byrefprobe.dll")]
        private static extern int bump_u16(ref ushort p);
        [DllImport("byrefprobe.dll")]
        private static extern int bump_u8(ref byte p);
        [DllImport("byrefprobe.dll")]
        private static extern int bump_i8(ref sbyte p);
        [DllImport("byrefprobe.dll")]
        private static extern float bump_f32(ref float p);
        [DllImport("byrefprobe.dll")]
        private static extern double bump_f64(ref double p);
        [DllImport("byrefprobe.dll")]
        private static extern int set_bool2(ref bool p);
        [DllImport("byrefprobe.dll")]
        private static extern int read_then_fill_bool(out bool p);
        [DllImport("byrefprobe.dll")]
        private static extern long bump_ptr(ref IntPtr p);
        [DllImport("byrefprobe.dll")]
        private static extern void fill_guid(out Guid g);
        [DllImport("byrefprobe.dll")]
        private static extern int read_guid_first(ref Guid g);
        [DllImport("byrefprobe.dll")]
        private static extern int add_two_i32(ref int a, ref int b);
        [DllImport("byrefprobe.dll", CharSet = CharSet.Ansi)]
        private static extern int ref_and_string(ref int p, string s);
        [DllImport("byrefprobe.dll", CharSet = CharSet.Unicode)]
        private static extern int ref_and_wide(ref int p, string s);

        // STATIC-link fixture (linked into the exe -- the transpiler's own
        // extern "C" declaration shape, not the runtime resolve cast).
        [DllImport("byrefstat")]
        private static extern int stat_bump_i32(ref int p);
        [DllImport("byrefstat")]
        private static extern int stat_bump_i16(ref short p);
        [DllImport("byrefstat")]
        private static extern float stat_bump_f32(ref float p);
        [DllImport("byrefstat")]
        private static extern void stat_fill_guid(out Guid g);

        public static void Main()
        {
            // 1. ref keeps the in-value: callee sees 41, caller sees 42.
            int a = 41;
            Record("ref.i32.ret", bump_i32(ref a));
            Record("ref.i32.after", a);

            // 2. out of a FRESH local: the callee sees the method-entry zero.
            int fresh;
            Record("out.fresh.seen", read_then_fill_i32_out(out fresh));
            Record("out.fresh.after", fresh);

            // 3. out on a local that ALREADY holds a value: passthrough (NOT
            //    re-zeroed at the call site), then the callee's write wins.
            int pre = 123;
            Record("out.pre.seen", read_then_fill_i32_out(out pre));
            Record("out.pre.after", pre);

            // 4. the SAME entry through a managed `ref` signature.
            int refIn = 55;
            Record("ref.same.seen", read_then_fill_i32_ref(ref refIn));
            Record("ref.same.after", refIn);

            // 5. write-only out.
            fill_i32(out int filled);
            Record("out.fill", filled);

            // 6. full-width / unsigned / narrow widths.
            uint u = 4294967294u;
            Record("ref.u32.ret", bump_u32(ref u));
            Record("ref.u32.after", u);
            long l = 4000000001L;
            Record("ref.i64.ret", bump_i64(ref l));
            Record("ref.i64.after", l);
            // (printed through a signed cast: u64 byte-ness, not its ToString)
            ulong q = 18446744073709551614UL;
            Record("ref.u64.ret", (long)bump_u64(ref q));
            Record("ref.u64.after", (long)q);
            short s = 32766;
            Record("ref.i16.ret", bump_i16(ref s));
            Record("ref.i16.after", s);
            ushort us = 65534;
            Record("ref.u16.ret", bump_u16(ref us));
            Record("ref.u16.after", us);
            byte b = 254;
            Record("ref.u8.ret", bump_u8(ref b));
            Record("ref.u8.after", b);
            sbyte sb = -128;
            Record("ref.i8.ret", bump_i8(ref sb));
            Record("ref.i8.after", sb);

            // 7. float (the managed slot is WIDENED; the native cell is 4 bytes).
            float f = 1.5f;
            Record("ref.f32.ret", bump_f32(ref f));
            Record("ref.f32.after", f);
            double d = 2.5;
            Record("ref.f64.ret", bump_f64(ref d));
            Record("ref.f64.after", d);

            // 8. bool: the native BOOL cell is 4 bytes and the copy-back
            //    normalizes (non-zero -> true).
            bool bt = true;
            Record("ref.bool.ret", set_bool2(ref bt));
            Record("ref.bool.after", bt);
            Record("ref.bool.eqtrue", bt == true);
            Record("ref.bool.asint", bt ? 1 : 0);
            bool bo;
            Record("out.bool.seen", read_then_fill_bool(out bo));
            Record("out.bool.after", bo);

            // 9. IntPtr& (pointer-width cell).
            IntPtr p = (IntPtr)0x1000;
            Record("ref.ptr.ret", bump_ptr(ref p));
            Record("ref.ptr.after", p.ToInt64());

            // 10. Guid& (16-byte value cell).
            fill_guid(out Guid g);
            Record("out.guid", g.ToString());
            Guid g2 = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            Record("ref.guid.first", read_guid_first(ref g2));

            // 11. ref of an instance field / of a static field.
            var h = new ByRefHolder();
            h.Field = 7;
            Record("ref.field.ret", bump_i32(ref h.Field));
            Record("ref.field.after", h.Field);
            ByRefHolder.SField = 9;
            Record("ref.sfield.ret", bump_i32(ref ByRefHolder.SField));
            Record("ref.sfield.after", ByRefHolder.SField);

            // 12. two byref params in one call.
            int x = 1, y = 2;
            Record("two.ret", add_two_i32(ref x, ref y));
            Record("two.after", x + "," + y);

            // 13. byref + string (Ansi) / byref + string (UTF-16).
            int n = 10;
            Record("mix.ansi.ret", ref_and_string(ref n, "abcd"));
            Record("mix.ansi.after", n);
            int w = 10;
            Record("mix.wide.ret", ref_and_wide(ref w, "abcd"));
            Record("mix.wide.after", w);

            // 14. STATIC-link shape (extern "C" declaration, direct call).
            int si = 50;
            Record("stat.i32.ret", stat_bump_i32(ref si));
            Record("stat.i32.after", si);
            short ss = 1000;
            Record("stat.i16.ret", stat_bump_i16(ref ss));
            Record("stat.i16.after", ss);
            float sf = 0.5f;
            Record("stat.f32.ret", stat_bump_f32(ref sf));
            Record("stat.f32.after", sf);
            stat_fill_guid(out Guid sg);
            Record("stat.guid", sg.ToString());
        }

        private static void Record(string id, long value)
        {
            Console.WriteLine("[" + id + "]=" + value);
        }

        private static void Record(string id, double value)
        {
            Console.WriteLine("[" + id + "]=" + value);
        }

        private static void Record(string id, bool value)
        {
            Console.WriteLine("[" + id + "]=" + value);
        }

        private static void Record(string id, string value)
        {
            Console.WriteLine("[" + id + "]=" + value);
        }
    }
}