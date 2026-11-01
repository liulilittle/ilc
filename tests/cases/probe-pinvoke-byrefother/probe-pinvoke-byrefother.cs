// probe-pinvoke-byrefother.cs -- `ref`/`out` of a FOREIGN-assembly value type
// (mscorlib System.Guid, System.Drawing.Point) and of a LOCAL blittable struct,
// all as DllImport parameters. net48 oracle == transpiled runtime, byte-exact.
//
// The audit's `byref-other` census group (docs/audit/winforms-pinvoke-current.md
// line 126) is 13 parameters of the System.Windows.Forms / System.Drawing
// P/Invoke surface: 8 x `Guid&`, 1 x `ref NativeMethods+LVHITTESTINFO`
// (SendMessage), 4 x `ref object` ([MarshalAs(Interface)] OleCreatePropertyFrame
// x2, VarFormat, [MarshalAs(IUnknown)] SendMessage). This case pins the two
// statically marshallable families -- the Guid& / blittable-struct& slot
// ADDRESS -- and the NOT-statically-marshallable `ref object` family stays
// rejected (COM/VARIANT marshalling, measured in the task NOTES).
//
// MEASURED on Framework64 v4.0.30319 with byrefother.dll (this case's own
// SHARED fixture):
//   * a Guid& / struct& param is the address of the MANAGED slot (blittable =>
//     pinned, no copy): the callee reads the caller's current value and its
//     writes are visible with no copy-back step;
//   * `out` is NOT re-zeroed at the call site: a fresh local reads zero because
//     .locals init zeroes at method ENTRY, while a local that already holds a
//     value passes THAT value;
//   * a callee that writes only 8 of a Guid's 16 bytes leaves the other 8 as
//     the caller had them (zeros for a fresh out-local, the old value for ref).
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Cases
{
    public struct LvhLike
    {
        public int X;
        public int Y;
        public int Flags;
        public int Item;
        public int Sub;
    }

    public static class ProbePinvokeByrefOther
    {
        [DllImport("byrefother.dll")]
        private static extern int out_guid_peek_zero(out Guid g);
        [DllImport("byrefother.dll")]
        private static extern int ref_guid_first(ref Guid g);
        [DllImport("byrefother.dll")]
        private static extern int guid_sum(ref Guid g);
        [DllImport("byrefother.dll")]
        private static extern void out_guid_full(out Guid g);
        [DllImport("byrefother.dll")]
        private static extern void guid_write_half(ref Guid g);
        [DllImport("byrefother.dll")]
        private static extern void out_guid_half(out Guid g);

        [DllImport("byrefother.dll")]
        private static extern int lvh_peek(ref LvhLike p);
        [DllImport("byrefother.dll")]
        private static extern void lvh_fill(ref LvhLike p);
        [DllImport("byrefother.dll")]
        private static extern int lvh_sum(ref LvhLike p);

        [DllImport("byrefother.dll")]
        private static extern void point_fill(ref Point p);
        [DllImport("byrefother.dll")]
        private static extern int point_sum(ref Point p);

        [DllImport("byrefother.dll")]
        private static extern int mix_ref_guid_int_out(ref Guid g, int n, out int v);
        [DllImport("byrefother.dll")]
        private static extern int mix_ref_guid_ptr(ref Guid g, uint flags, out IntPtr q);

        public static void Main()
        {
            // 1. `out Guid` of a FRESH local: the callee sees the method-entry
            //    zero image, and a half write leaves the high half at zero.
            Guid fresh;
            Record("guid.out.fresh.seenzero", out_guid_peek_zero(out fresh));
            out_guid_half(out fresh);
            Record("guid.out.half.after", fresh.ToString());
            Record("guid.out.half.sum", guid_sum(ref fresh));

            // 2. `ref Guid`: the callee observes the caller's value.
            Guid known = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            Record("guid.ref.first", ref_guid_first(ref known));
            Record("guid.ref.presum", guid_sum(ref known));

            // 3. half write through `ref` keeps the high 8 bytes of the old value.
            guid_write_half(ref known);
            Record("guid.ref.half.after", known.ToString());

            // 4. full 16-byte write through `out`.
            out_guid_full(out Guid full);
            Record("guid.out.full.after", full.ToString());

            // 5. a Guid& whose 16 bytes are all non-zero (width proof: the
            //    callee's byte sum sees every byte, not a truncated 8-byte copy).
            Guid allff = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
            Record("guid.ref.fullsum", guid_sum(ref allff));

            // 6. `ref` of a LOCAL blittable struct (the LVHITTESTINFO shape):
            //    the callee reads the caller's fields and writes all of them.
            LvhLike lvh = new LvhLike();
            lvh.X = 1;
            lvh.Y = 2;
            lvh.Flags = 3;
            Record("lvh.ref.peek", lvh_peek(ref lvh));
            lvh_fill(ref lvh);
            Record("lvh.ref.sum", lvh_sum(ref lvh));
            Record("lvh.ref.x", lvh.X);
            Record("lvh.ref.sub", lvh.Sub);

            // 7. `ref` of a FOREIGN-assembly blittable struct (System.Drawing.
            //    Point is a TypeRef to System.Drawing, never a local TypeDef).
            Point pt = default(Point);
            Record("point.ref.before", point_sum(ref pt));
            point_fill(ref pt);
            Record("point.ref.after", point_sum(ref pt));

            // 8. The multi-parameter shape of GdipGetEncoderParameterList /
            //    ...Size (Guid& + int + out int) -- the Guid slot must not
            //    disturb its neighbours in the SAME argument list.
            Guid mg = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            int outv;
            Record("mix.guidints.seen", mix_ref_guid_int_out(ref mg, 1234, out outv));
            Record("mix.guidints.outv", outv);
            Record("mix.guidints.guid", mg.ToString());

            // 9. The SHGetKnownFolderPath shape (Guid& + uint + out IntPtr).
            Guid mg2 = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            IntPtr q;
            Record("mix.guidptr.seen", mix_ref_guid_ptr(ref mg2, 7, out q));
            Record("mix.guidptr.q", (long)q);
            Record("mix.guidptr.guid", mg2.ToString());
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
