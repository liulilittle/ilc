//------------------------------------------------------------------------------
// nesteddeep.cs -- NESTED TYPE DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C39, AFTER the D-GC-1 fix (batch 96,
// commit 0f58996): a NESTED class's ARRAY instance field (SZARRAY 0x1D /
// ARRAY 0x14) now decodes in TypeEmitter.DecodeNestedFieldSignature
// (TypeEmitter.cs:298) as a GC-reference field. newarrdeep proved the minimal
// D-GC-1 surface (one Inner[] field on a one-level nested class). THIS case
// drives the nested-type FULL CHAIN:
//
//   1. FIELD FULL CHAIN on a nested class -- int32 / string / object /
//      self-typed class-ref (0x12, TypeDef handle) / SZARRAY instance fields
//      (int[] / string[] / Inner[]) -- every Reference field lands in the
//      layout refFieldBitmap (TypeEmitter.LayoutInstanceFields) so the
//      collector scans it, and the array fields ride the D-GC-1 decode.
//   2. NESTED METHODS -- instance methods reading/writing nested fields,
//      calling other nested methods on the same instance, plus a STATIC
//      method on a nested class (resolved through the short-name owner).
//   3. NESTED OBJECT GRAPH -- a linked list of nested-class instances rooted
//      by a TOP-LEVEL static field (Cases.NestedDeep.g_head -> Inner), read
//      through a Next.Next.Next field chain, surviving GC collections; the
//      tail's WeakReference stays alive.
//   4. ARRAY FIELDS THROUGH GC -- field-held int[]/string[]/Inner[] arrays
//      with element writes read back after a compaction pressure wave.
//   5. TWO-LEVEL NESTING -- a class nested two levels (Inner.Deep): newobj /
//      field read / method call / self-typed ref field (TypeDef self-ref).
//      NOTE (D-NEST-1): a ref field that CROSSES the second '+' boundary
//      (Inner declaring a Deep-typed field) is NOT probed -- see GAPS.md.
//   6. STATIC MEMBERS on a nested class -- a static int field (file-scope
//      C++ static Inner_sf_*, FieldCppName) read/written from Main, plus the
//      static method Twin.
//   7. RELEASE -- dropping the static root makes the old chain collectible
//      (the WeakReference dies) -- the nested graph is scanned, not pinned.
//
// Determinism: every probe is a bool comparison over exact field/method
// results; the GC order is deterministic and net48 ground truth is stable.
// net48 is the reference baseline; every line prints "label=True/False".
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class NestedDeep
    {
        // The nested type under test: full field chain + methods + statics.
        public class Inner
        {
            // ---- field full chain (all decode arms) ----
            public int V;            // 0x13 Int32
            public string Tag;       // 0x0E System.String
            public object Data;      // 0x1C System.Object
            public Inner Next;       // 0x12 self-typed class ref (TypeDef)
            public int[] Nums;       // D-GC-1: 0x1D SZARRAY instance field
            public string[] Strs;    // D-GC-1: 0x1D SZARRAY instance field
            public Inner[] Kids;     // D-GC-1: 0x1D SZARRAY instance field

            // ---- nested methods ----
            public int Inc(int d) { V += d; return V; }
            public int Scale(int m) { return V * m; }
            // D-NEST-2 (GAPS.md): `Tag + ":" + V` -- Roslyn lowers `string +
            // intField` to ldflda V; call Int32::ToString(), and the fold must
            // load the FIELD VALUE through the field address (the "deref:i"
            // arm -- icall_string_concat_int(Tag+":", (*reinterpret_cast<
            // int32_t*>(&V)))) -- never static_cast<Il2CppObject*>(&V).
            public string Describe() { return Tag + ":" + V; }
            public int ChainAdd(int d) { return Scale(2) + Inc(d); }
            public static int Twin(int x) { return x + x; }

            // ---- nested static field (file-scope C++ static) ----
            public static int Made;

            // ---- two-level nesting (Inner.Deep) ----
            public class Deep
            {
                public int D;
                public Deep Self;    // self-typed ref field (TypeDef self-ref)
                public Deep(int d) { D = d; }
                public int GetD() { return D; }
                public void Push(int d) { D += d; }
            }
        }

        // ---- top-level static roots holding nested-class instances ----
        static Inner g_head;
        static Inner.Deep g_deep;

        // Build a chain 1 -> 2 -> ... -> n (head = 1). The ONLY strong
        // reference escapes through the return value (planted into g_head).
        static Inner BuildChain(int n)
        {
            Inner head = null;
            for (int i = n; i >= 1; i--)
            {
                Inner node = new Inner();
                node.V = i;
                node.Tag = "n" + i;
                node.Next = head;
                head = node;
            }
            return head;
        }

        static WeakReference DeadNode()
        {
            Inner dead = new Inner();
            dead.V = 55;
            return new WeakReference(dead);
        }

        public static void Main()
        {
            // ===== 1. field full chain (value + reference + D-GC-1 arrays) =====
            Inner a = new Inner();
            a.V = 5;
            a.Tag = "alpha";
            a.Data = (object)7;
            a.Nums = new int[] { 1, 2, 3 };
            a.Strs = new string[] { "x", "y" };
            Inner k0 = new Inner(); k0.V = 11;
            Inner k1 = new Inner(); k1.V = 22;
            a.Kids = new Inner[2];
            a.Kids[0] = k0; a.Kids[1] = k1;
            GC.Collect();
            Console.WriteLine("f_v=" + (a.V == 5));
            Console.WriteLine("f_tag=" + (a.Tag == "alpha"));
            Console.WriteLine("f_data=" + ((int)a.Data == 7));
            Console.WriteLine("f_nums=" + (a.Nums[0] == 1 && a.Nums[2] == 3));
            Console.WriteLine("f_strs=" + (a.Strs[1] == "y"));
            Console.WriteLine("f_kids=" + (a.Kids[0].V == 11 && a.Kids[1].V == 22));
            Console.WriteLine("f_kidref=" + ReferenceEquals(a.Kids[0], k0));

            // ===== 2. nested methods (instance state + static method) =====
            Inner m = new Inner(); m.V = 4; m.Tag = "beta";
            Console.WriteLine("m_inc=" + (m.Inc(6) == 10));
            Console.WriteLine("m_scale=" + (m.Scale(3) == 30));
            Console.WriteLine("m_desc=" + (m.Describe() == "beta:10"));
            Inner m2 = new Inner(); m2.V = 2;
            Console.WriteLine("m_chain=" + (m2.ChainAdd(3) == 9));
            Console.WriteLine("m_static=" + (Inner.Twin(21) == 42));

            // ===== 3. nested object graph rooted by a static field =====
            g_head = BuildChain(4);   // 1 -> 2 -> 3 -> 4
            WeakReference wtail = new WeakReference(g_head.Next.Next.Next);
            GC.Collect();
            GC.Collect();
            Console.WriteLine("g_v=" + (g_head.V == 1));
            Console.WriteLine("g_chain=" + (g_head.Next.V == 2 && g_head.Next.Next.V == 3 && g_head.Next.Next.Next.V == 4));
            Console.WriteLine("g_tail=" + wtail.IsAlive);
            Console.WriteLine("g_tag=" + (g_head.Next.Next.Tag == "n3"));

            // ===== 4. array fields through a compaction pressure wave =====
            Inner arr = new Inner();
            arr.Nums = new int[] { 9, 8, 7 };
            arr.Strs = new string[] { "p", "q" };
            arr.Kids = new Inner[1];
            arr.Kids[0] = new Inner(); arr.Kids[0].V = 33;
            for (int i = 0; i < 5; i++) { byte[] junk = new byte[2048]; GC.Collect(); }
            Console.WriteLine("arr_nums=" + (arr.Nums[0] == 9 && arr.Nums[1] == 8 && arr.Nums[2] == 7));
            Console.WriteLine("arr_strs=" + (arr.Strs[1] == "q"));
            Console.WriteLine("arr_kids=" + (arr.Kids[0].V == 33));
            Console.WriteLine("arr_ref=" + (arr.Nums.Length == 3 && arr.Strs.Length == 2));

            // ===== 5. two-level nesting (Inner.Deep) =====
            Inner.Deep d = new Inner.Deep(77);
            Console.WriteLine("deep_v=" + (d.D == 77));
            Console.WriteLine("deep_m=" + (d.GetD() == 77));
            d.Push(5);
            Console.WriteLine("deep_push=" + (d.GetD() == 82));
            d.Self = d;
            GC.Collect();
            Console.WriteLine("deep_self=" + (d.Self.GetD() == 82));
            g_deep = d;
            Console.WriteLine("deep_root=" + (g_deep.GetD() == 82));

            // ===== 6. nested static field + static method =====
            Inner.Made = 0;
            Inner.Made += 9;
            Inner.Made++;
            Console.WriteLine("nested_static=" + (Inner.Made == 10));
            Console.WriteLine("nested_staticfn=" + (Inner.Twin(7) == 14));

            // ===== 7. release: dropped static root -> old chain collected =====
            g_head = new Inner(); g_head.V = 99;
            GC.Collect();
            GC.Collect();
            Console.WriteLine("rel_old_tail=" + (!wtail.IsAlive));
            Console.WriteLine("rel_new_head=" + (g_head.V == 99 && g_head.Next == null));
            // control: a dead nested node with no root is collected
            WeakReference wd = DeadNode();
            GC.Collect();
            Console.WriteLine("rel_dead_node=" + (!wd.IsAlive));

            GC.KeepAlive(a);
            GC.KeepAlive(k0);
            GC.KeepAlive(m);
            GC.KeepAlive(arr);
            GC.KeepAlive(d);
        }
    }
}
