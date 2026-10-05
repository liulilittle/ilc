//------------------------------------------------------------------------------
// newarrdeep.cs -- NEWARR DEEP e2e case (net48 oracle, byte-for-byte).
// Created in e2e batch C38, AFTER the D-GC-2 newarr generalization (batch 94,
// commit 9652107): the SZARRAY element gate now accepts EVERY reference
// element -- BCL reference types (System.WeakReference[]), top-level USER
// classes (Payload[]), and NESTED user classes (Outer.Inner[]) -- allocating
// (sizeof(void*), isRefElement=true) so the payload is GC-scanned. The old
// whitelist rejected WeakReference[] / user classes / nested classes.
//
// Probes:
//   1. WeakReference[] -- reference-element newarr + element stores; the
//      array keeps the WeakReference objects alive; a weak ref whose target
//      is ONLY weakly reachable dies at the next collection, a rooted target
//      survives (the D-GC-2 scan contract -- WeakReference[] is now a
//      scanned reference array, so elements survive compaction).
//   2. user class[] (Payload[]) -- element stores + reads + GC survival of
//      elements THROUGH the array (the array IS the root); scalar fields
//      only (array instance fields are a recorded M2 limitation).
//   3. NESTED class[] (Outer.Inner[]) -- the D-GC-2 nested-TypeDef element
//      path (base type read from metadata; closes the nested-type hole).
//   4. object[]/string[] sanity (already wired reference elements).
//   5. GC churn: fresh reference-element arrays under collection + compaction
//      keep elements alive; a dropped array + full collection reclaims the
//      weakly-held targets.
//   6. value-element control: int[] still allocates as a value array (the
//      D-GC-2 gate must NOT misclassify wired value types).
//
// RECORDED GAP (NOT probed -- transpile wall, matrixcase B; D/E fixed in the
// multidim-probe batches): MULTIDIM newarr (`int[,] m = new int[2,3]`) still
// aborts in StackFold.cs:3172 (GenericInstantiationName: TypeSpec signature
// byte 0x14 != GenericInst(0x15)) -- the newobj multidim ctor
// (int32[0...,0...]::.ctor(int32,int32)) reaches the TypeSpec decode before
// the emitter runs. The two SIGNATURE-side walls are already fixed (params:
// MethodSignatureCoder r:i[2]; locals: MethodBodyReader 0x14 -> "r" slot with
// full bounds-list consumption -- MatrixTests/SignatureHashTests/MethodBody
// ReaderTests green). The B wall is a DESIGN blocker, not a point fix: the
// runtime (il2cpp_array.h/array.cpp) has NO multidim representation (single
// length + rank=1, no per-dimension storage; icall_array_get_length throws
// for dimension != 0), so GetLength/Get/Set multi-index semantics need a
// runtime layout change (per-dimension lengths) BEFORE the transpiler can
// fold the newobj/Get/Set calls -- StackFold.cs is the decode gate and the
// runtime owns the semantics. Recorded, not fixed (per the batch protocol);
// the 1-D reference-element surface below is the reachable D-GC-2 surface.
//
// Every line prints "label=True/False". net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public class Payload
    {
        public int V;
    }

    public class Outer
    {
        public class Inner
        {
            public int V;
            // D-GC-1 (this batch): an ARRAY FIELD on a NESTED type -- the
            // field-type emission path (TypeEmitter.DecodeNestedFieldSignature
            // SZARRAY arm). Top-level instance array fields stay rejected by
            // MetadataLoader (read-only domain); the nested-class field decode
            // lives in TypeEmitter, where the D-GC-2 newarr ELEMENT fix did
            // not reach. The field is a GC reference to the array object.
            public Outer.Inner[] children;
        }
    }

    public static class NewarrDeep
    {
        static WeakReference DeadRef()
        {
            return new WeakReference(new object());
        }

        public static void Main()
        {
            // ===== 1. WeakReference[] (BCL reference element, D-GC-2) =====
            WeakReference[] wks = new WeakReference[3];
            Console.WriteLine("wr_len=" + (wks.Length == 3));
            object keep = new object();
            wks[0] = new WeakReference(keep);
            wks[1] = new WeakReference(new object());
            wks[2] = wks[0];
            GC.Collect();
            Console.WriteLine("wr_alive=" + (wks[0].IsAlive && wks[2].IsAlive));
            Console.WriteLine("wr_dead=" + (!wks[1].IsAlive));
            Console.WriteLine("wr_ref=" + ReferenceEquals(wks[0].Target, keep));
            Console.WriteLine("wr_null=" + (wks[1].Target == null));

            // ===== 2. user class[] (Payload[], top-level TypeDef) =====
            Payload[] pals = new Payload[2];
            Payload p0 = new Payload(); p0.V = 7;
            Payload p1 = new Payload(); p1.V = 9;
            pals[0] = p0; pals[1] = p1;
            GC.Collect();
            Console.WriteLine("pl_len=" + (pals.Length == 2));
            Console.WriteLine("pl_ref0=" + ReferenceEquals(pals[0], p0));
            Console.WriteLine("pl_v0=" + (pals[0].V == 7));
            Console.WriteLine("pl_v1=" + (pals[1].V == 9));
            pals[1] = null;
            GC.Collect();
            Console.WriteLine("pl_null=" + (pals[1] == null && pals[0].V == 7));

            // ===== 3. NESTED class[] (Outer.Inner, nested TypeDef element) =====
            Outer.Inner[] inners = new Outer.Inner[3];
            Outer.Inner i0 = new Outer.Inner(); i0.V = 11;
            Outer.Inner i1 = new Outer.Inner(); i1.V = 12;
            inners[0] = i0; inners[1] = i1;
            // D-GC-1: nested-class ARRAY FIELD (children) -- store/load through
            // the field + GC survival of the field-held array. Folded into the
            // in_v line so the net48 oracle output stays byte-identical.
            i0.children = new Outer.Inner[2];
            i0.children[0] = new Outer.Inner(); i0.children[0].V = 13;
            GC.Collect();
            Console.WriteLine("in_len=" + (inners.Length == 3));
            Console.WriteLine("in_ref=" + ReferenceEquals(inners[0], i0));
            Console.WriteLine("in_v=" + (inners[0].V == 11 && inners[1].V == 12 && i0.children[0].V == 13 && i0.children[1] == null));
            Console.WriteLine("in_null=" + (inners[2] == null));

            // ===== 4. object[]/string[] sanity =====
            object[] objs = new object[2];
            string s = "ab";
            objs[0] = s; objs[1] = (object)7;
            GC.Collect();
            Console.WriteLine("obj_ref=" + ReferenceEquals(objs[0], s));
            string[] strs = new string[2];
            strs[0] = "cd";
            Console.WriteLine("str_el=" + (strs[0] == "cd" && strs[1] == null));

            // ===== 5. GC churn: fresh arrays + collection + compaction =====
            bool alive = true;
            object root = new object();
            Outer.Inner holder = new Outer.Inner();   // D-GC-1: field-held array survival
            for (int i = 0; i < 20; i++)
            {
                WeakReference[] tmp = new WeakReference[4];
                tmp[0] = new WeakReference(root);
                Payload[] tp = new Payload[2];
                tp[0] = new Payload();
                Outer.Inner[] ti = new Outer.Inner[2];
                ti[0] = new Outer.Inner();
                holder.children = ti;                  // D-GC-1: store through the field each round
                GC.Collect();
                alive = alive && tmp[0].IsAlive;
            }
            Console.WriteLine("churn_alive=" + alive);
            Console.WriteLine("churn_root=" + (ReferenceEquals(new WeakReference(root).Target, root) && holder.children != null && holder.children[0] != null));

            // ===== 6. dead-target reclaim through a helper =====
            WeakReference d0 = DeadRef();
            WeakReference d1 = DeadRef();
            GC.Collect();
            GC.Collect();
            Console.WriteLine("dead=" + (!d0.IsAlive && !d1.IsAlive));
            Console.WriteLine("deadnull=" + (d0.Target == null && d1.Target == null));

            // ===== 7. value-element control (int[] stays a value array) =====
            int[] nums = new int[3];
            nums[0] = 5; nums[1] = 6; nums[2] = 7;
            Console.WriteLine("int_len=" + (nums.Length == 3));
            Console.WriteLine("int_sum=" + (nums[0] + nums[1] + nums[2] == 18));
            GC.KeepAlive(keep);
            GC.KeepAlive(p0);
            GC.KeepAlive(root);
        }
    }
}
