//------------------------------------------------------------------------------
// gccompact.cs -- GC COMPACTION DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C79, AFTER the GC compaction domain
// closure: the M3 compacting collector (gc.cpp -- Gen0 survivors are COPIED
// to a promote segment and UpdateReferencesAfterCompaction rewrites every
// root / weak slot / reference field through the forward map; gcstress3
// proved the weak-ref rewrite + 40-round churn survival, gcstatic3 the
// static-root rewrite, gcstress4 the LOH surface).
//
// THIS case drives the COMPACTION MECHANICS one stage deeper, specifically
// the "object MOVED -> references UPDATED" invariant across sustained
// compaction loops:
//
//   1. COMPACTION LOOP with an OBJECT GRAPH: a 20-node doubly-rooted chain
//      (n0..n19, each Next -> successor) is rooted in Main; 30 compaction
//      rounds of mixed dead allocation churn (byte[]/int[]/string/boxed int/
//      fresh Node) each followed by a full collection. Every round proves:
//      all 20 weak refs stay alive AND ReferenceEquals their original node
//      (the weak slot is REWRITTEN to the moved object, not re-pointed to a
//      copy), every chain.Next link still ReferenceEquals the successor
//      (reference FIELDS are updated), and every node value is intact.
//   2. OBJECT MOVEMENT via GCHandle: a strong GCHandle to a chain node
//      survives the compaction loop and its Target still ReferenceEquals the
//      node (the handle slot follows the Gen0 compaction -- the
//      il2cpp_gchandle.h contract), and a second handle to a DISTINCT node
//      keeps them distinct.
//   3. CROSS-REFERENCE UPDATE: node A.Next -> B and B.Prev -> A (mutual
//      back-edges). After further compaction, both directions still
//      ReferenceEquals their originals -- the collector must update BOTH
//      slots when either object moves.
//   4. REFERENCE-ARRAY UPDATE: an object[] of chain nodes survives the loop
//      with every element still ReferenceEquals its node (reference-element
//      array slots follow compaction).
//   5. DEAD TARGET RECLAIM under compaction: nodes created in helper frames
//      (only weak refs escape) are reclaimed after two full collects -- the
//      compaction sweep must not retain them.
//   6. GENERATION PROMOTION: after the pressure wave the rooted chain head
//      is promoted out of Gen0 (GC.GetGeneration >= 1), and a fresh object
//      is back in Gen0 (== 0).
//   7. HEAP SANITY: GetTotalMemory is primed (> 0) and a rooted 1 MiB
//      allocation strictly raises it.
//
// DETERMINISM (gcstress3/gcops conventions): every assertion is a labeled
// bool; GC.Collect() is a blocking full collection on net48 and a synchronous
// full collection with Gen0 compaction on the ILC runtime. Dead-target
// probes follow the helper-frame pattern (a Main local would root the
// target). Generation numbers use SAFE inequalities only. The compacting
// sweep makes every rooted object move on the ILC side; net48 compacts the
// small-object heap too -- the ReferenceEquals assertions hold on BOTH.
//
// FIELD-SHAPE DISCIPLINE (gcstress3 D-GC-1 precedent): Node carries scalar
// (int V) and reference (Node Next / Node Prev) fields only -- the TypeEmitter
// nested-type field decoder rejects ARRAY instance fields (0x1D) loudly, so
// all array churn stays in locals.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases { public static class Gccompact {
    public class Node {
        public int V;
        public Node Next;
        public Node Prev;
        public Node(int v) { V = v; }
    }

    // A node that dies as soon as this method returns (only the weak
    // reference escapes), so the next full collect reclaims it.
    static WeakReference DeadNode(int v)
    {
        return new WeakReference(new Node(v));
    }

    // A node created in a helper frame with a value field -- dies on return.
    static WeakReference DeadNodeLinked(int v)
    {
        Node a = new Node(v);
        Node b = new Node(v + 1);
        a.Next = b;
        b.Prev = a;
        return new WeakReference(a);
    }

    public static void Main() {
        // ===== 1. compaction loop with a 20-node rooted chain =====
        Node[] nodes = new Node[20];
        for (int i = 0; i < 20; i++) nodes[i] = new Node(i);
        for (int i = 0; i < 19; i++) nodes[i].Next = nodes[i + 1];
        for (int i = 1; i < 20; i++) nodes[i].Prev = nodes[i - 1];
        WeakReference[] wrs = new WeakReference[20];
        for (int i = 0; i < 20; i++) wrs[i] = new WeakReference(nodes[i]);
        Node head = nodes[0];
        Node tail = nodes[19];

        bool loopOk = true;
        for (int r = 0; r < 30; r++)
        {
            // mixed dead churn: every allocation is garbage by the next
            // Collect (locals below are never read again)
            byte[] b = new byte[512 + r * 32];
            int[] ia = new int[256 + r];
            string s = "gccompact-" + r;
            object bx = (object)r;
            Node tmp = new Node(9000 + r);
            GC.Collect();
            // every weak ref alive and still pointing at ITS original node
            for (int i = 0; i < 20; i++)
            {
                if (!wrs[i].IsAlive || !ReferenceEquals(wrs[i].Target, nodes[i])) loopOk = false;
            }
            // every forward link intact
            for (int i = 0; i < 19; i++)
            {
                if (!ReferenceEquals(nodes[i].Next, nodes[i + 1])) loopOk = false;
            }
            // every backward link intact
            for (int i = 1; i < 20; i++)
            {
                if (!ReferenceEquals(nodes[i].Prev, nodes[i - 1])) loopOk = false;
            }
        }
        Console.WriteLine("cl_loop=" + loopOk);                              // True
        Console.WriteLine("cl_head_alive=" + wrs[0].IsAlive);                // True
        Console.WriteLine("cl_tail_alive=" + wrs[19].IsAlive);               // True
        Console.WriteLine("cl_head_rewritten=" + ReferenceEquals(wrs[0].Target, head));  // True
        Console.WriteLine("cl_tail_rewritten=" + ReferenceEquals(wrs[19].Target, tail)); // True
        Console.WriteLine("cl_head_value=" + (head.V == 0));                 // True
        Console.WriteLine("cl_mid_value=" + (nodes[10].V == 10));            // True
        Console.WriteLine("cl_tail_value=" + (tail.V == 19));                // True
        Console.WriteLine("cl_head_next=" + ReferenceEquals(head.Next, nodes[1])); // True

        // ===== 2. object movement via strong GCHandle (slot follows compaction) =====
        Node hn0 = nodes[5];
        Node hn1 = nodes[15];
        GCHandle gh0 = GCHandle.Alloc(hn0);
        GCHandle gh1 = GCHandle.Alloc(hn1);
        for (int r = 0; r < 10; r++)
        {
            object garbage = new Node(100 + r);
            GC.Collect();
        }
        Console.WriteLine("gh_alive=" + gh0.IsAllocated);                    // True
        Console.WriteLine("gh_target0=" + ReferenceEquals(gh0.Target, hn0)); // True
        Console.WriteLine("gh_target1=" + ReferenceEquals(gh1.Target, hn1)); // True
        Console.WriteLine("gh_distinct=" + !ReferenceEquals(gh0.Target, gh1.Target)); // True
        Console.WriteLine("gh_value=" + (((Node)gh0.Target).V == 5));        // True
        gh0.Free();
        gh1.Free();
        Console.WriteLine("gh_freed=" + !gh0.IsAllocated);                   // True

        // ===== 3. cross-reference update (both directions after compaction) =====
        Node cA = new Node(31);
        Node cB = new Node(32);
        cA.Next = cB;
        cB.Prev = cA;
        for (int r = 0; r < 15; r++)
        {
            Node g = new Node(200 + r);
            GC.Collect();
        }
        Console.WriteLine("xr_ab=" + ReferenceEquals(cA.Next, cB));          // True
        Console.WriteLine("xr_ba=" + ReferenceEquals(cB.Prev, cA));          // True
        Console.WriteLine("xr_values=" + (cA.V == 31 && cB.V == 32));        // True

        // ===== 4. reference-array update across compaction =====
        object[] arr = new object[20];
        for (int i = 0; i < 20; i++) arr[i] = nodes[i];
        for (int r = 0; r < 10; r++)
        {
            byte[] g = new byte[1024 + r];
            GC.Collect();
        }
        bool arrOk = true;
        for (int i = 0; i < 20; i++)
        {
            if (!ReferenceEquals(arr[i], nodes[i])) arrOk = false;
        }
        Console.WriteLine("ar_elements=" + arrOk);                           // True
        Console.WriteLine("ar_head=" + ReferenceEquals(arr[0], head));       // True

        // ===== 5. dead target reclaim under compaction =====
        WeakReference d0 = DeadNode(500);
        WeakReference d1 = DeadNode(501);
        WeakReference d2 = DeadNodeLinked(600);
        GC.Collect();
        GC.Collect();
        Console.WriteLine("dt_reclaimed=" + (!d0.IsAlive && !d1.IsAlive && !d2.IsAlive)); // True
        Console.WriteLine("dt_null=" + (d0.Target == null));                 // True

        // ===== 6. generation promotion =====
        Console.WriteLine("gen_promoted=" + (GC.GetGeneration(head) >= 1));  // True
        object fresh = new object();
        Console.WriteLine("gen_fresh=" + (GC.GetGeneration(fresh) == 0));    // True
        GC.KeepAlive(fresh);

        // ===== 7. heap sanity =====
        Console.WriteLine("heap_primed=" + (GC.GetTotalMemory(false) > 0));  // True
        long m0 = GC.GetTotalMemory(false);
        byte[] big = new byte[1024 * 1024];
        long m1 = GC.GetTotalMemory(false);
        Console.WriteLine("heap_grow=" + (m1 > m0));                         // True
        GC.KeepAlive(big);
        GC.KeepAlive(head);
        GC.KeepAlive(tail);
        Console.WriteLine("done");
    }
}}
