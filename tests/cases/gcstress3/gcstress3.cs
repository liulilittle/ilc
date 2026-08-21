//------------------------------------------------------------------------------
// gcstress3.cs -- GC STRESS e2e reference case 3 (net48 oracle, byte-for-byte).
// Created in e2e batch C37 (2026-08-11), GC-system verification. gcops (C24)
// covered the GC icall basics (Collect / GetTotalMemory / GetGeneration /
// WeakReference live/dead/set_Target); gcstress (C19) covered a rooted
// 2000-node chain + churn; gcstatic3 (C30) stressed REGISTERED STATIC ROOTS.
// THIS case targets the FRAME-ROOT path under mass churn + compaction:
//
//   1. ROOTED TARGET survival across a full collection (frame-root protocol
//      keeps the Main local alive; the WeakReference is rewritten).
//   2. MASS SHORT-LIVED OBJECTS + COMPACTION LOOPS -- 40 rounds of varied
//      garbage (Payload objects + byte[]/int[]/string/boxed-int locals, all
//      unreachable at the next Collect) with a full collection every round.
//      The rooted target must survive every compaction and its weak ref must
//      keep pointing at it (the compacting sweeper rewrites the reference
//      each pass). Content (V / V2) proves it is the SAME object, not a
//      stale copy.
//   3. MULTI-ROOT REWRITE -- five rooted Payload objects with weak refs all
//      survive 10 further collections under fresh churn; ReferenceEquals to
//      the originals proves every root was rewritten in place.
//   4. MASS DEAD TARGET RECLAIM -- six weak refs planted in helper frames
//      (no reachable strong reference) are all dead after two full collects,
//      and Target reads back null.
//   5. WeakReference set_Target round trip -- re-arm and clear.
//   6. GENERATION PROMOTION -- the long-rooted target leaves Gen0
//      (GC.GetGeneration(keep) >= 1) after the pressure wave.
//
// NOTE (M2 limitations, recorded, not probed):
//   * The TypeEmitter nested-type field decoder accepts SCALAR instance
//     fields only -- an ARRAY instance field (any element type, SZARRAY 0x1D)
//     throws "field type byte 0x1D not supported in M2" at transpile (static
//     fields skip the decoder). The rooted Payload therefore carries scalar
//     fields only; all array churn is local variables.
//   * D-GC-2 (fixed 2026-08-11): newarr GENERALIZES reference elements now --
//     WeakReference[] and Payload[] transpile as (sizeof(void*), ref) and are
//     exercised below (folded into the existing bools so the 19-line oracle is
//     byte-exact). Value-slot arrays of unlisted BCL value types (double,
//     DateTime, ...) and user structs stay loudly rejected (audit C4).
//
// DETERMINISM: net48 GC.Collect() is a blocking full collection; the ILC
// collector (icall_gc_collect) is a synchronous full collection with Gen0
// compaction. Dead-target probes follow the weakrefcase helper-frame pattern
// (a Main local would keep the target alive to the end of Main); churn
// allocations are never read afterwards, so their lifetimes end before the
// next Collect. Every line is a labeled bool -- a diff failure shows exactly
// which assertion flipped.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Gcstress3
    {
        public class Payload
        {
            public int V;
            public int V2;
            public Payload(int v) { V = v; V2 = v * 3; }
        }

        // A target that dies as soon as this method returns (only the weak
        // reference escapes), so the next full collection reclaims it.
        static WeakReference DeadPayload(int v)
        {
            return new WeakReference(new Payload(v));
        }

        public static void Main()
        {
            // ===== 1. rooted target survives a full collection =====
            Payload keep = new Payload(1);
            WeakReference wk = new WeakReference(keep);
            GC.Collect();
            Console.WriteLine(wk.IsAlive);                              // True
            Console.WriteLine(ReferenceEquals(wk.Target, keep));        // True

            // D-GC-2 (newarr whitelist): reference-element arrays of a BCL
            // reference type (System.WeakReference[]) and of a user class
            // (Payload[]) must transpile with (sizeof(void*), ref) and stay
            // GC-scanned. Folded into a local so the byte-exact output is
            // untouched (no new WriteLine).
            bool arrOk = true;
            WeakReference[] wks = new WeakReference[3];
            Payload[] pals = new Payload[3];
            wks[0] = wk;
            wks[1] = new WeakReference(keep);
            wks[2] = wks[1];
            pals[0] = keep;
            pals[1] = keep;
            pals[2] = new Payload(2);
            arrOk = arrOk && wks[0].IsAlive && wks[2].IsAlive
                    && ReferenceEquals(wks[2].Target, keep)
                    && ReferenceEquals(pals[0], keep) && pals[2].V == 2;

            // ===== 2. mass short-lived churn + 40 compaction loops =====
            // Every allocation below is garbage by the time its Collect runs:
            // the tmp/b/ia/s/bx locals are never read again, so their
            // lifetimes end before the next collection. The rooted 'keep'
            // must survive all 40 passes with its weak ref rewritten each time.
            // D-GC-2: fresh WeakReference[]/Payload[] newarrs every round also
            // exercise the reference-element array path under churn.
            bool alive = true && arrOk;
            for (int i = 0; i < 40; i++)
            {
                for (int j = 0; j < 8 + (i & 3); j++)
                {
                    Payload tmp = new Payload(i * 100 + j);
                }
                byte[] b = new byte[1024 + i * 64];
                int[] ia = new int[512 + i];
                string s = "churn-" + i;
                object bx = (object)i;
                GC.Collect();
                alive = alive && wk.IsAlive;
            }
            Console.WriteLine(alive);                                   // True (survived 40 compactions)
            Console.WriteLine(ReferenceEquals(wk.Target, keep));        // True (root rewritten)
            Console.WriteLine(keep.V == 1);                             // True (content intact)
            Console.WriteLine(keep.V2 == 3);                            // True (payload intact)

            // ===== 3. multi-root rewrite under further churn =====
            Payload r0 = new Payload(0);
            WeakReference wr0 = new WeakReference(r0);
            Payload r1 = new Payload(1);
            WeakReference wr1 = new WeakReference(r1);
            Payload r2 = new Payload(2);
            WeakReference wr2 = new WeakReference(r2);
            Payload r3 = new Payload(3);
            WeakReference wr3 = new WeakReference(r3);
            Payload r4 = new Payload(4);
            WeakReference wr4 = new WeakReference(r4);
            for (int i = 0; i < 10; i++)
            {
                Payload t = new Payload(9000 + i);                      // unreachable churn
                GC.Collect();
            }
            Console.WriteLine(wr0.IsAlive && wr1.IsAlive && wr2.IsAlive &&
                              wr3.IsAlive && wr4.IsAlive);              // True (all five alive)
            Console.WriteLine(ReferenceEquals(wr0.Target, r0) &&
                              ReferenceEquals(wr1.Target, r1) &&
                              ReferenceEquals(wr2.Target, r2) &&
                              ReferenceEquals(wr3.Target, r3) &&
                              ReferenceEquals(wr4.Target, r4));         // True (all rewritten in place)
            Console.WriteLine(r4.V == 4);                               // True (per-root content)
            Console.WriteLine(r2.V2 == 6);                              // True

            // ===== 4. mass dead-target reclaim (helper-frame weak refs) =====
            WeakReference d0 = DeadPayload(0);
            WeakReference d1 = DeadPayload(1);
            WeakReference d2 = DeadPayload(2);
            WeakReference d3 = DeadPayload(3);
            WeakReference d4 = DeadPayload(4);
            WeakReference d5 = DeadPayload(5);
            GC.Collect();
            GC.Collect();
            Console.WriteLine(!d0.IsAlive && !d1.IsAlive && !d2.IsAlive &&
                              !d3.IsAlive && !d4.IsAlive && !d5.IsAlive); // True (all reclaimed)
            Console.WriteLine(d0.Target == null);                       // True (Target reads null)
            Console.WriteLine(d5.Target == null);                       // True

            // ===== 5. WeakReference set_Target round trip =====
            WeakReference w = new WeakReference(null);
            Console.WriteLine(!w.IsAlive);                              // True (null target)
            Payload p = new Payload(77);
            w.Target = p;
            Console.WriteLine(w.IsAlive);                               // True (re-armed)
            Console.WriteLine(ReferenceEquals(w.Target, p));            // True
            w.Target = null;
            Console.WriteLine(!w.IsAlive);                              // True (cleared)
            GC.KeepAlive(p);

            // ===== 6. generation promotion after the pressure wave =====
            Console.WriteLine(GC.GetGeneration(keep) >= 1);             // True (promoted out of Gen0)

            // ===== 7. heap sanity: rooted allocation raises the live total =====
            Console.WriteLine(GC.GetTotalMemory(false) > 0);            // True (heap primed)
            GC.KeepAlive(keep);
        }
    }
}
