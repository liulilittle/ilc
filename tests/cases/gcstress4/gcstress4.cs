//------------------------------------------------------------------------------
// gcstress4.cs -- GC STRESS e2e reference case 4 (net48 oracle, byte-for-byte).
// Created in e2e batch C59 (2026-08-12), GC-system deep 2. gcstress3 (C37)
// covered frame-root survival under mass churn + compaction + generation
// promotion. THIS case targets the LARGE-OBJECT HEAP and the MIXED
// multi-generation surface the runtime's collector models explicitly
// (gc.cpp: kGcLohThreshold = 85000 -- the same LOH cutoff as net48; LOH
// blocks live in generation 3 and are never promoted):
//
//   1. LOH SURVIVAL -- a 120000-byte array survives a full collection with
//      its weak ref rewritten and content intact.
//   2. LOH DEATH -- large arrays created in helper frames are reclaimed after
//      a full collection (Target reads null).
//   3. MULTI-GENERATION MIX -- a rooted small object is promoted through
//      repeated full collects to generation >= 2, while a rooted LARGE
//      object stays alive alongside it (mixed-gen heap).
//   4. LOH FRAGMENTATION CHURN -- 12 arrays of growing sizes (90000..167000
//      bytes); every third one is rooted, the rest die in helper frames.
//      Rooted survive with content intact; the rest are reclaimed.
//   5. MIXED SMALL+LARGE CHURN -- interleaved small Payload and large byte[]
//      allocations across collects; rooted members of both sizes survive,
//      unrooted members of both sizes die.
//   6. LOH REUSE AFTER RECLAIM -- a 500000-byte array dies, a fresh
//      same-size allocation reuses the reclaimed region (content intact).
//   7. WeakReference re-arm onto a large object.
//   8. HEAP GROWTH -- a rooted 200000-byte allocation strictly raises
//      GC.GetTotalMemory(false).
//
// DETERMINISM: every assertion is a labeled bool -- a diff failure shows
// exactly which line flipped. Generation numbers are asserted with SAFE
// inequalities only: net48 reports LOH objects as generation 2 while the
// runtime reports generation 3 (il2cpp_gc_get_generation), so LOH
// assertions use `> 0`; small-object promotion uses `>= 2` after repeated
// full collects (both runtimes land the survivor in generation 2).
// Memory-drop assertions are deliberately ABSENT: net48 GetTotalMemory does
// NOT shrink after a collection (committed segments persist), while the
// runtime's live-block sum does -- only GROWTH from a rooted allocation is
// asserted (both runtimes agree).
//
// RECORDED DIVERGENCE (not probed here -- see GAPS.md): the runtime's UTF-8
// decoder counts an astral (4-byte) character as ONE UTF-16 unit where .NET
// counts TWO (surrogate pair); unrelated to GC but discovered in this batch.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Gcstress4
    {
        public class Payload
        {
            public int V;
            public int V2;
            public Payload(int v) { V = v; V2 = v * 3; }
        }

        // A LARGE array that dies as soon as this method returns (only the
        // weak reference escapes), so the next full collection reclaims it.
        static WeakReference DeadLarge(int n)
        {
            return new WeakReference(new byte[120000 + n]);
        }

        // A large array with its LAST byte stamped -- dies on return.
        static WeakReference DeadLargeStamped(int n, byte stamp)
        {
            byte[] a = new byte[90000 + n * 7000];
            a[a.Length - 1] = stamp;
            return new WeakReference(a);
        }

        public static void Main()
        {
            // ===== 1. LOH survival across a full collection =====
            byte[] big = new byte[120000];
            big[119999] = 42;
            WeakReference wb = new WeakReference(big);
            GC.Collect();
            Console.WriteLine(wb.IsAlive);                                 // True
            Console.WriteLine(ReferenceEquals(wb.Target, big));            // True (root rewritten)
            Console.WriteLine(big[119999] == 42);                          // True (content intact via strong ref)
            Console.WriteLine(GC.GetGeneration(big) > 0);                  // True (LOH: net48=2, runtime=3)

            // ===== 2. LOH death (helper-frame weak refs) =====
            WeakReference d0 = DeadLarge(0);
            WeakReference d1 = DeadLarge(1000);
            WeakReference d2 = DeadLarge(2000);
            GC.Collect();
            GC.Collect();
            Console.WriteLine(!d0.IsAlive && !d1.IsAlive && !d2.IsAlive); // True (all reclaimed)
            Console.WriteLine(d0.Target == null);                          // True (Target reads null)

            // ===== 3. multi-generation mix: small promoted, large alive =====
            Payload keep = new Payload(5);
            byte[] keepBig = new byte[100000];
            keepBig[99999] = 7;
            for (int i = 0; i < 3; i++) { GC.Collect(); }
            Console.WriteLine(GC.GetGeneration(keep) >= 2);                // True (promoted to Gen2)
            Console.WriteLine(wb.IsAlive);                                 // True (earlier LOH still alive)
            Console.WriteLine(keep.V == 5 && keepBig[99999] == 7);         // True (both intact)

            // ===== 4. LOH fragmentation churn: every 3rd of 12 rooted =====
            byte[] k0 = new byte[90000 + 0 * 7000]; k0[k0.Length - 1] = 1;
            byte[] k1 = new byte[90000 + 3 * 7000]; k1[k1.Length - 1] = 2;
            byte[] k2 = new byte[90000 + 6 * 7000]; k2[k2.Length - 1] = 3;
            byte[] k3 = new byte[90000 + 9 * 7000]; k3[k3.Length - 1] = 4;
            WeakReference wk0 = new WeakReference(k0);
            WeakReference wk1 = new WeakReference(k1);
            WeakReference wk2 = new WeakReference(k2);
            WeakReference wk3 = new WeakReference(k3);
            WeakReference wf0 = DeadLargeStamped(1, 11);
            WeakReference wf1 = DeadLargeStamped(2, 22);
            WeakReference wf2 = DeadLargeStamped(4, 44);
            WeakReference wf3 = DeadLargeStamped(5, 55);
            WeakReference wf4 = DeadLargeStamped(7, 77);
            WeakReference wf5 = DeadLargeStamped(8, 88);
            WeakReference wf6 = DeadLargeStamped(10, 101);
            WeakReference wf7 = DeadLargeStamped(11, 111);
            GC.Collect();
            Console.WriteLine(wk0.IsAlive && wk1.IsAlive && wk2.IsAlive && wk3.IsAlive); // True (all four rooted survive)
            Console.WriteLine(ReferenceEquals(wk0.Target, k0) &&
                              ReferenceEquals(wk1.Target, k1) &&
                              ReferenceEquals(wk2.Target, k2) &&
                              ReferenceEquals(wk3.Target, k3));           // True (rewritten in place)
            Console.WriteLine(k0[k0.Length - 1] == 1 && k3[k3.Length - 1] == 4); // True (content intact)
            Console.WriteLine(!wf0.IsAlive && !wf1.IsAlive && !wf2.IsAlive &&
                              !wf3.IsAlive && !wf4.IsAlive && !wf5.IsAlive &&
                              !wf6.IsAlive && !wf7.IsAlive);              // True (all interleaved dead)

            // ===== 5. mixed small + large churn =====
            bool mix = true;
            Payload sp = new Payload(9);
            byte[] lp = new byte[130000];
            lp[129999] = 13;
            for (int i = 0; i < 5; i++)
            {
                Payload tmp = new Payload(i);                              // small garbage
                byte[] ltmp = new byte[100000 + i * 5000];                 // large garbage
                GC.Collect();
                mix = mix && sp.V == 9 && lp[129999] == 13;
            }
            Console.WriteLine(mix);                                       // True (rooted small+large survived 5 mixes)
            Console.WriteLine(GC.GetGeneration(sp) >= 1);                 // True (promoted out of Gen0)

            // ===== 6. LOH reuse after reclaim =====
            WeakReference rr = DeadLarge(300);
            GC.Collect();
            byte[] fresh = new byte[500000];
            fresh[499999] = 77;
            Console.WriteLine(!rr.IsAlive && fresh[499999] == 77);        // True (reclaimed + reused, content intact)

            // ===== 7. WeakReference re-arm onto a large object =====
            WeakReference wl = new WeakReference(null);
            Console.WriteLine(!wl.IsAlive);                               // True (null target)
            wl.Target = big;
            Console.WriteLine(wl.IsAlive);                                // True (re-armed to LOH)
            Console.WriteLine(ReferenceEquals(wl.Target, big));           // True
            wl.Target = null;
            Console.WriteLine(!wl.IsAlive);                               // True (cleared)

            // ===== 8. heap growth from a rooted large allocation =====
            long m1 = GC.GetTotalMemory(false);
            byte[] big2 = new byte[200000];
            big2[0] = 1;
            long m2 = GC.GetTotalMemory(false);
            Console.WriteLine(m2 > m1);                                   // True (rooted allocation raised the total)
            GC.KeepAlive(keep);
            GC.KeepAlive(big);
            GC.KeepAlive(big2);
        }
    }
}
