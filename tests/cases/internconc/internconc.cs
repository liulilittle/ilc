// internconc.cs -- String.Intern CONCURRENCY deep probe (e2e batch C23,
// "Intern 并发深测: 多线程 Intern 同一池/不同串 -- Intern 互斥后"). Builds on
// internops (C20, 4 workers) and the thread-safe intern pool mutex
// (stringintern.cpp: the recursive g_internMutex serializes find/insert/grow).
// THIS case goes DEEPER on the mutex's concurrency contract:
//
//   * 8 workers on ONE pre-interned content ("hit"): all must observe the SAME
//     pool instance (the mutex serializes the find; no false dedup).
//   * 8 workers, each on a DISTINCT fresh content ("miss"): every insert is a
//     pure pointer store under the mutex; each worker's result is ITS OWN
//     fresh string (distinct from every other worker's), and a subsequent
//     Intern on the main thread returns the SAME pool entries (the publish is
//     visible after Join -- happens-before).
//   * MIXED content: two pre-interned contents + two fresh contents across 8
//     workers -- hits and misses interleave under the mutex; every identity
//     holds (hit->pool, miss->own-fresh) and cross-content never aliases.
//   * RE-INTERN stability after the concurrent phase: Intern on a REBUILT
//     copy returns the concurrent phase's pool entry (no duplicate created).
//
// net48 LITERAL PRE-INTERNING (probe-verified): a string literal that appears
// ANYWHERE in the source is pooled when the assembly loads. The fresh contents
// here ("ic-a".."ic-h", "ic-x", "ic-y") are built with string.Concat and are
// NEVER written as literals in the source, so they stay unpooled until a
// worker interns them -- each is a genuine miss. (internops' separate probe
// confirmed: Build("x") != the literal, and Intern of a rebuilt copy returns
// the pool entry -- rebuild_same=True.)
//
// Determinism contract (internops model): workers NEVER print and NEVER
// allocate. The M-stage managed allocator is single-threaded by contract
// (IL2CPP_ALLOCATOR_THREAD_SAFE is not defined), so EVERY string the workers
// touch is created on the MAIN thread BEFORE the threads start. The worker
// phase stays below the 64-slot / 70% grow threshold (~29 interned literals +
// 12 worker inserts < 44.8), so no insert triggers a grow (no allocation
// inside the mutex on a worker). Workers write results to DISTINCT static
// slots; the main thread reads them AFTER Join (happens-before), so every
// observable is scheduling-independent.
//
// Expected output: all-bool rows, every expected value in a trailing comment.
using System;
using System.Threading;

namespace Cases
{
    public static class Internconc
    {
        // Reference equality: (object) cast disables string == value
        // semantics; 1 = SAME instance, 0 = distinct instances.
        static int RefEq(string a, string b) { return (object)a == (object)b ? 1 : 0; }

        // A runtime-built fresh string (Concat always allocates -- MAIN only).
        static string Build(string part) { return string.Concat("ic-", part); }

        // ---- shared worker contents (ALL created on the main thread) ----
        static string g_hitA;   // pre-interned on main: "conc-hit-a"
        static string g_hitB;   // pre-interned on main: "conc-hit-b"
        static string f0, f1, f2, f3, f4, f5, f6, f7;  // section-2 distinct fresh
        static string g_freshX; // section-3 fresh A: "ic-x" (NOT a literal)
        static string g_freshY; // section-3 fresh B: "ic-y" (NOT a literal)

        // ---- per-worker result slots (8 fixed locals, no array) ----
        static string r0, r1, r2, r3, r4, r5, r6, r7;
        // Section-2 results are SAVED before section-3 overwrites the slots.
        static string s2_r0, s2_r7;

        // ============ section 1: 8 workers, ONE content (hit) ============
        static void HitW0() { r0 = string.Intern(g_hitA); }
        static void HitW1() { r1 = string.Intern(g_hitA); }
        static void HitW2() { r2 = string.Intern(g_hitA); }
        static void HitW3() { r3 = string.Intern(g_hitA); }
        static void HitW4() { r4 = string.Intern(g_hitA); }
        static void HitW5() { r5 = string.Intern(g_hitA); }
        static void HitW6() { r6 = string.Intern(g_hitA); }
        static void HitW7() { r7 = string.Intern(g_hitA); }

        static void RunHits()
        {
            Thread t0 = new Thread(HitW0); t0.Start();
            Thread t1 = new Thread(HitW1); t1.Start();
            Thread t2 = new Thread(HitW2); t2.Start();
            Thread t3 = new Thread(HitW3); t3.Start();
            Thread t4 = new Thread(HitW4); t4.Start();
            Thread t5 = new Thread(HitW5); t5.Start();
            Thread t6 = new Thread(HitW6); t6.Start();
            Thread t7 = new Thread(HitW7); t7.Start();
            t0.Join(); t1.Join(); t2.Join(); t3.Join();
            t4.Join(); t5.Join(); t6.Join(); t7.Join();
        }

        // ============ section 2: 8 workers, DISTINCT content each (miss) ============
        static void MissW0() { r0 = string.Intern(f0); }
        static void MissW1() { r1 = string.Intern(f1); }
        static void MissW2() { r2 = string.Intern(f2); }
        static void MissW3() { r3 = string.Intern(f3); }
        static void MissW4() { r4 = string.Intern(f4); }
        static void MissW5() { r5 = string.Intern(f5); }
        static void MissW6() { r6 = string.Intern(f6); }
        static void MissW7() { r7 = string.Intern(f7); }

        static void RunMisses()
        {
            Thread t0 = new Thread(MissW0); t0.Start();
            Thread t1 = new Thread(MissW1); t1.Start();
            Thread t2 = new Thread(MissW2); t2.Start();
            Thread t3 = new Thread(MissW3); t3.Start();
            Thread t4 = new Thread(MissW4); t4.Start();
            Thread t5 = new Thread(MissW5); t5.Start();
            Thread t6 = new Thread(MissW6); t6.Start();
            Thread t7 = new Thread(MissW7); t7.Start();
            t0.Join(); t1.Join(); t2.Join(); t3.Join();
            t4.Join(); t5.Join(); t6.Join(); t7.Join();
        }

        // ============ section 3: mixed (2 pre-interned + 2 fresh) ============
        static void MixW0() { r0 = string.Intern(g_hitA); }   // hit
        static void MixW1() { r1 = string.Intern(g_hitB); }   // hit
        static void MixW2() { r2 = string.Intern(g_freshX); } // miss
        static void MixW3() { r3 = string.Intern(g_freshY); } // miss
        static void MixW4() { r4 = string.Intern(g_hitA); }   // hit
        static void MixW5() { r5 = string.Intern(g_freshX); } // miss
        static void MixW6() { r6 = string.Intern(g_hitB); }   // hit
        static void MixW7() { r7 = string.Intern(g_freshY); } // miss

        static void RunMix()
        {
            Thread t0 = new Thread(MixW0); t0.Start();
            Thread t1 = new Thread(MixW1); t1.Start();
            Thread t2 = new Thread(MixW2); t2.Start();
            Thread t3 = new Thread(MixW3); t3.Start();
            Thread t4 = new Thread(MixW4); t4.Start();
            Thread t5 = new Thread(MixW5); t5.Start();
            Thread t6 = new Thread(MixW6); t6.Start();
            Thread t7 = new Thread(MixW7); t7.Start();
            t0.Join(); t1.Join(); t2.Join(); t3.Join();
            t4.Join(); t5.Join(); t6.Join(); t7.Join();
        }

        public static void Main()
        {
            // Pre-intern the hit contents on the main thread. Build the fresh
            // contents on the MAIN thread too (zero worker allocations). The
            // interned-literal set (~29) + the worker inserts (8+2) stay below
            // the 64-slot / 70% grow threshold (44.8), so the worker phase
            // performs NO allocation anywhere (no grow inside the mutex on a
            // worker -- the internops constraint).
            g_hitA = string.Intern("conc-hit-a");
            g_hitB = string.Intern("conc-hit-b");
            f0 = Build("a"); f1 = Build("b"); f2 = Build("c"); f3 = Build("d");
            f4 = Build("e"); f5 = Build("f"); f6 = Build("g"); f7 = Build("h");
            g_freshX = Build("x"); // "ic-x" (never a source literal)
            g_freshY = Build("y"); // "ic-y" (never a source literal)

            // ==== section 1: 8 workers intern ONE pre-interned content ====
            RunHits();
            Console.WriteLine("hits_all_same=" + (RefEq(r0, r1) == 1 && RefEq(r1, r2) == 1 && RefEq(r2, r3) == 1
                && RefEq(r3, r4) == 1 && RefEq(r4, r5) == 1 && RefEq(r5, r6) == 1 && RefEq(r6, r7) == 1));
            Console.WriteLine("hits_are_pool=" + (RefEq(r0, g_hitA) == 1 && RefEq(r7, g_hitA) == 1));

            // ==== section 2: 8 workers intern DISTINCT fresh content ====
            RunMisses();
            Console.WriteLine("misses_all_distinct=" + (RefEq(r0, r1) == 0 && RefEq(r1, r2) == 0
                && RefEq(r2, r3) == 0 && RefEq(r3, r4) == 0 && RefEq(r4, r5) == 0
                && RefEq(r5, r6) == 0 && RefEq(r6, r7) == 0));
            // A miss interns the input itself -> each worker's result is ITS
            // OWN fresh string (not another worker's, not a pre-existing pool).
            Console.WriteLine("miss0_own=" + (RefEq(r0, f0) == 1));
            Console.WriteLine("miss7_own=" + (RefEq(r7, f7) == 1));
            s2_r0 = r0; s2_r7 = r7;   // save before section 3 overwrites slots

            // ==== section 3: mixed content, 8 workers ====
            RunMix();
            // The two HIT contents are the pool instances (not the fresh ones).
            Console.WriteLine("mix_hitA_pool=" + (RefEq(r0, g_hitA) == 1 && RefEq(r4, g_hitA) == 1));
            Console.WriteLine("mix_hitB_pool=" + (RefEq(r1, g_hitB) == 1 && RefEq(r6, g_hitB) == 1));
            // The two MISS contents are the fresh strings themselves.
            Console.WriteLine("mix_missX_own=" + (RefEq(r2, g_freshX) == 1 && RefEq(r5, g_freshX) == 1));
            Console.WriteLine("mix_missY_own=" + (RefEq(r3, g_freshY) == 1 && RefEq(r7, g_freshY) == 1));
            // Cross-content never aliases.
            Console.WriteLine("mix_no_alias=" + (RefEq(r0, r1) == 0 && RefEq(r0, r2) == 0 && RefEq(r2, r3) == 0));

            // ==== section 4: re-intern stability after the concurrent phase ====
            // The pool entries published by the workers are still THE entries.
            Console.WriteLine("rein_hitA=" + (RefEq(string.Intern("conc-hit-a"), r0) == 1));
            Console.WriteLine("rein_hitB=" + (RefEq(string.Intern("conc-hit-b"), r1) == 1));
            Console.WriteLine("rein_freshX=" + (RefEq(string.Intern(g_freshX), r2) == 1));
            Console.WriteLine("rein_freshY=" + (RefEq(string.Intern(g_freshY), r3) == 1));
            // A main-thread Intern of a REBUILT copy returns the worker's entry.
            Console.WriteLine("rein_rebuild=" + (RefEq(string.Intern(Build("h")), s2_r7) == 1));
            Console.WriteLine("rein_rebuild0=" + (RefEq(string.Intern(Build("a")), s2_r0) == 1));
        }
    }
}
