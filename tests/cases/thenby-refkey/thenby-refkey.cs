//------------------------------------------------------------------------------
// thenby-refkey -- the REFERENCE-source FLOAT key probe (the ref-source
// residual of b5-gaps section 14).
//
// LinqKeyFamilySuffix folds float (0x0C) and double (0x0D) into the SAME
// "d" suffix, so OrderBy(keySelector) over a reference-element array source
// (object[]/class[]) with a Func<T,float> key rides
// il2cpp_linq_order_by_key_ref_d (SortRefsKeyCopy<double> +
// InvokeKeyRefD = double(*)(Il2CppObject*)). A Func<T,float> leaf returns a
// 4-byte float per the MS x64 ABI (XMM0 low 32 bits); reading that return
// through a double fn-pointer is an ABI misread (the upper XMM0 half is
// callee residue), so the float ordering can silently differ from net48.
//
// Key design (thenby-floatprobe手法): the float keys are genuinely fractional
// (float field values, not cvtsi2sd widenings) and duplicate, so a float-key
// order and its double-key control DIFFER, duplicate keys force the stable
// tie rule to be visible, and a float read as a 64-bit double (residue bits)
// cannot reproduce the net48 order. Every element carries a distinct Id, so
// byte-equality against the net48 oracle validates the exact order including
// stability. The int32 and double keys ride as CONTROLS -- they are wired
// and must stay byte-identical across the fix.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public sealed class Box
    {
        public int Id;
        public float W;
        public double D;
    }

    public static class ThenByRefKeyProbe
    {
        private static Box[] MakeSource()
        {
            // 11 distinct markers; the float weights repeat (W pattern with
            // duplicates) and the double control key spreads differently.
            int[] ids = { 17, 4, 13, 8, 25, 11, 2, 19, 6, 23, 9 };
            var src = new Box[ids.Length];
            for (int i = 0; i < ids.Length; ++i)
            {
                src[i] = new Box
                {
                    Id = ids[i],
                    W = ids[i] % 4 == 0 ? 2.5f
                      : ids[i] % 4 == 1 ? -1.5f
                      : ids[i] % 4 == 2 ? 0.75f
                      : -1.5f,
                    D = (ids[i] % 4) * 1.5 - 2.25,
                };
            }
            return src;
        }

        private static string Join(Box[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i].Id;
            }
            return s;
        }

        public static void Main()
        {
            Box[] src = MakeSource();

            // Direct OrderBy / OrderByDescending over the reference source,
            // float key + double / int32 controls.
            Console.WriteLine("[ref_f_aa]=" + Join(src.OrderBy(b => b.W).ToArray()));
            Console.WriteLine("[ref_f_da]=" + Join(src.OrderByDescending(b => b.W).ToArray()));
            Console.WriteLine("[ref_d_aa]=" + Join(src.OrderBy(b => b.D).ToArray()));
            Console.WriteLine("[ref_i4_aa]=" + Join(src.OrderBy(b => b.Id).ToArray()));
        }
    }
}
