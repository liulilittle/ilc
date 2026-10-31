using System;

namespace Cases
{
    public static class ProbeIntPtr
    {
        static void WriteIntPtr(out IntPtr p) { p = (IntPtr)0x100000001L; }
        static void WriteUIntPtr(out UIntPtr p) { p = (UIntPtr)0x8000000000000001UL; }
        static void WriteRefIntPtr(ref IntPtr p) { p = (IntPtr)0x200000002L; }

        public static void Main()
        {
            // [a] assignment-free IntPtr local written through `out` (ldloca):
            // the x64 value above 2^31 must survive (was truncated to 1).
            IntPtr p;
            WriteIntPtr(out p);
            Console.WriteLine("[a.out.intptr]=" + p.ToInt64());

            // [b] assignment-free UIntPtr local, high bit set: the slot must
            // be the UNSIGNED carrier (uintptr_t), not a signed intptr_t.
            UIntPtr u;
            WriteUIntPtr(out u);
            Console.WriteLine("[b.out.uintptr]=" + u.ToUInt64());

            // [c] ref IntPtr -- the same ldloca write shape (`ref` needs a
            // definitely-assigned local, so it starts at Zero).
            IntPtr r = IntPtr.Zero;
            WriteRefIntPtr(ref r);
            Console.WriteLine("[c.ref.intptr]=" + r.ToInt64());

            // [d]/[e] arithmetic through the IntPtr helpers.
            IntPtr a = IntPtr.Add(p, 1);
            Console.WriteLine("[d.add]=" + (a.ToInt64() - p.ToInt64()));
            IntPtr s = IntPtr.Subtract(p, 1);
            Console.WriteLine("[e.sub]=" + (p.ToInt64() - s.ToInt64()));

            // [f]/[g] comparison, [h] ToString.
            // NOTE: `IntPtr.ToString()` itself has NO icall mapping in this
            // transpiler (the call falls to an undefined generated name --
            // MethodMap.IntPtr.cs is outside this task's file scope), so the
            // ToString coverage goes through the local's ToInt64() value.
            Console.WriteLine("[f.eq]=" + (p == (IntPtr)0x100000001L));
            Console.WriteLine("[g.ne]=" + (p != (IntPtr)0x100000001L));
            Console.WriteLine("[h.tostring]=" + p.ToInt64().ToString());

            // [i]/[j] UIntPtr unsigned conversion (the high bit must read as
            // a LARGE value, not a negative one). net48 UIntPtr has no
            // CompareTo/relational operators (those are .NET Core 3.0+).
            Console.WriteLine("[i.ugt1]=" + (u.ToUInt64() > 1UL));
            Console.WriteLine("[j.u64]=" + u.ToUInt64());

            // [k] UIntPtr.Add keeps the high bit.
            UIntPtr ua = UIntPtr.Add(u, 1);
            Console.WriteLine("[k.uadd]=" + ua.ToUInt64());

            // [l] IntPtr.Zero equality.
            Console.WriteLine("[l.zero]=" + (IntPtr.Zero == IntPtr.Zero));
        }
    }
}
