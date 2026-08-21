// internops.cs -- deep String.Intern coverage (e2e batch C20). The net48
// oracle runs the SAME source; the ILC side must produce byte-identical
// identity booleans AND concurrency results.
//
// Focus (per the batch worklist "Intern/IsInterned/并发 -- Intern 互斥后"):
//   * Identity semantics beyond internchain: Intern on a literal returns the
//     literal itself; Intern on a runtime-built copy returns the POOL entry
//     (distinct from the copy); Intern is idempotent; distinct contents get
//     distinct pool entries; prefix-sharing contents do NOT alias (probe
//     correctness -- "alphabet" != "alpha").
//   * Pool probing under load: many distinct entries with a shared prefix are
//     interned and every identity holds (open-addressing probe path, no false
//     matches).
//   * The empty string is pooled exactly once.
//   * CONCURRENCY (the stringintern.cpp recursive-mutex work, HEAD b57650b):
//     several real OS threads call String.Intern concurrently on (a) already
//     interned content (find-only under the lock) and (b) a fresh content
//     built on the main thread (insert path -- a pure pointer store, no
//     worker-thread allocation, no table grow: the pre-interning below keeps
//     the entry count below the 70% grow threshold of the 64-slot table).
//     Every worker writes its result to a DISTINCT slot, so the joined output
//     is deterministic regardless of scheduling. All workers must observe the
//     SAME pool instances (the mutex serializes the pool ops).
//
// Worker threads allocate NOTHING (the allocator pool is single-threaded by
// contract -- IL2CPP_ALLOCATOR_THREAD_SAFE is not defined): every string the
// workers touch is created on the MAIN thread; the workers only look up /
// insert, both of which are allocation-free below the grow threshold.
//
// Reference-output shape (net48 oracle): one line per identity boolean,
// printed in the same order; every expected value is spelled in a comment.
using System;
using System.Threading;

namespace Cases
{
    public static class Internops
    {
        // Reference equality: the (object) cast disables string == value
        // semantics; 1 = the SAME instance, 0 = distinct instances.
        static int RefEq(string a, string b) { return (object)a == (object)b ? 1 : 0; }

        // A runtime-built fresh string (Concat always allocates).
        static string Build(string part) { return string.Concat("bld-", part); }

        // ---- shared worker state (contents created on the MAIN thread) ----
        static string g_hitA;        // pre-interned content, find-only worker
        static string g_hitB;        // second pre-interned content
        static string g_fresh;       // NOT interned until the worker phase
        static string[] g_wres = new string[4];

        static void WorkerHit()
        {
            // Intern already-interned content: pure find path under the mutex.
            string r = string.Intern(g_hitA);
            g_wres[0] = r;
        }

        static void WorkerHit2()
        {
            string r = string.Intern(g_hitB);
            g_wres[1] = r;
        }

        static void WorkerMiss()
        {
            // Insert a content NOT yet pooled (pure pointer store; the string
            // itself was built on the main thread -- zero worker allocations).
            string r = string.Intern(g_fresh);
            g_wres[2] = r;
        }

        static void WorkerMiss2()
        {
            // Same content as WorkerMiss: the SECOND inserter must find the
            // pool entry published by the first (mutex-ordered visibility).
            string r = string.Intern(g_fresh);
            g_wres[3] = r;
        }

        public static void Main()
        {
            // ==== identity / pool semantics (single-threaded) ====
            // Intern on a literal returns the literal instance itself.
            Console.WriteLine("lit_same=" + RefEq(string.Intern("i-lit"), "i-lit")); // 1
            // Intern on a runtime copy returns the POOL entry; the copy itself
            // stays a distinct instance (interning does not rewrite it).
            string fresh1 = Build("copy");
            string pooled1 = string.Intern(fresh1);
            Console.WriteLine("copy_distinct_pool=" + RefEq(fresh1, pooled1));             // 0
            Console.WriteLine("pool_matches_interned=" + RefEq(pooled1, string.Intern("bld-copy"))); // 1
            // Intern is idempotent on the pool entry AND on a re-built copy.
            Console.WriteLine("idempotent=" + RefEq(string.Intern(pooled1), pooled1));              // 1
            Console.WriteLine("reintern_copy=" + RefEq(string.Intern(Build("copy")), pooled1));     // 1
            // Distinct contents -> distinct pool entries.
            Console.WriteLine("distinct=" + RefEq(string.Intern("bld-copy"), string.Intern("bld-diff"))); // 0
            // The input string object is NOT rewritten by interning.
            Console.WriteLine("input_unchanged=" + RefEq(fresh1, pooled1)); // 0

            // ==== probe correctness: prefix + suffix sharing must NOT alias ====
            // "alphabet" contains "alpha" as a prefix; the pool probe must
            // keep walking past the shorter entry, not stop on it.
            string a1 = string.Intern("alpha");
            string a2 = string.Intern("alphabet");
            string a3 = string.Intern("alphabetal");
            Console.WriteLine("pref_distinct=" + RefEq(a1, a2));  // 0
            Console.WriteLine("pref2_distinct=" + RefEq(a2, a3)); // 0
            Console.WriteLine("suf_distinct=" + RefEq(string.Intern("beta"), a3)); // 0

            // ==== many entries, shared prefix (probe load) ====
            // 21 distinct entries "probe-NNNN" -- open-addressing probes chain
            // through equal-hash collisions; every identity must hold.
            string first = string.Intern("probe-0000");
            string prev = first;
            int probeOk = 1;
            for (int i = 1; i <= 20; ++i)
            {
                string tag = "probe-" + (i < 10 ? "000" : "00") + i;
                string cur = string.Intern(tag);
                if (RefEq(cur, prev) != 0) probeOk = 0;  // adjacent entries distinct
                if (RefEq(cur, first) != 0) probeOk = 0; // and none aliases the first
                prev = cur;
            }
            Console.WriteLine("probe_distinct_all=" + probeOk); // 1
            Console.WriteLine("probe_roundtrip=" + RefEq(string.Intern("probe-0000"), first)); // 1
            Console.WriteLine("probe_reintern=" + RefEq(string.Intern("probe-0017"), prev == null ? null : "probe-0017")); // 1

            // ==== the empty string is pooled exactly once ====
            Console.WriteLine("empty_same=" + RefEq(string.Intern(""), ""));                    // 1
            Console.WriteLine("empty_reintern=" + RefEq(string.Intern(""), string.Intern(""))); // 1

            // ==== long content ====
            string longLit = "0123456789abcdefghijklmnopqrstuvwxyz0123456789";
            string longPool = string.Intern(Build("long"));
            Console.WriteLine("long_distinct=" + RefEq(longPool, longLit));                     // 0
            Console.WriteLine("long_interned=" + RefEq(string.Intern("bld-long"), longPool));   // 1

            // ==== CONCURRENCY (Intern pool mutex) ====
            // Pre-intern the hit contents on the main thread; build the fresh
            // content here too (no worker-thread allocation anywhere). The
            // pre-interning above (~25 entries) + the 1 worker insert stay far
            // below the 64-slot / 70% grow threshold, so the worker phase
            // performs NO allocation (no grow inside the mutex on a worker).
            g_hitA = string.Intern("conc-hit-a");
            g_hitB = string.Intern("conc-hit-b");
            g_fresh = Build("conc-miss"); // NOT interned yet

            Thread ta = new Thread(WorkerHit);
            Thread tb = new Thread(WorkerHit2);
            Thread tc = new Thread(WorkerMiss);
            Thread td = new Thread(WorkerMiss2);
            ta.Start(); tb.Start(); tc.Start(); td.Start();
            ta.Join(); tb.Join(); tc.Join(); td.Join();

            // Workers on already-interned content see the pool instance.
            Console.WriteLine("conc_hitA=" + RefEq(g_wres[0], g_hitA));   // 1
            Console.WriteLine("conc_hitB=" + RefEq(g_wres[1], g_hitB));   // 1
            // Both inserters of the fresh content see the SAME pool entry
            // (mutex-ordered: the second finds what the first published), and
            // a main-thread re-intern returns that same entry.
            Console.WriteLine("conc_miss_pair=" + RefEq(g_wres[2], g_wres[3]));                  // 1
            Console.WriteLine("conc_miss_is_fresh=" + RefEq(g_wres[2], g_fresh));                // 1 (miss -> the input becomes the entry)
            Console.WriteLine("conc_miss_reintern=" + RefEq(string.Intern(g_fresh), g_wres[2])); // 1
            // Cross-content: the two hit pools are still distinct.
            Console.WriteLine("conc_distinct=" + RefEq(g_wres[0], g_wres[1]));                   // 0
        }
    }
}
