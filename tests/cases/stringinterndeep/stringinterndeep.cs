//------------------------------------------------------------------------------
// stringinterndeep.cs -- String.Intern DEEP probe (e2e batch C52, after the
// pooling iron law: stringintern.cpp + il2cpp_string_intern.h + the StringPool
// ldstr path). Beyond internops (identity + concurrency) and internchain
// (cross-method literal identity), this case drives:
//
//   1. POOL REFERENCE IDENTITY under the OPEN-ADDRESSING table: distinct
//      contents get distinct pool entries; identical contents always return
//      the SAME entry (dedup by ordinal content, not by instance); the empty
//      string is pooled exactly once; prefix-sharing contents do NOT alias.
//   2. POOL GROW (load-factor 70%, 64-slot initial table): interning enough
//      DISTINCT contents forces a GrowInternTable rehash; every identity must
//      survive the rehash (re-Intern of an earlier entry returns the same
//      instance), and the pool keeps serving both the find path and the
//      insert path after growth.
//   3. GC SURVIVAL (the pool roots its entries -- net48 semantics): a string
//      that is interned and then has ALL other strong references dropped
//      must survive a full GC.Collect; re-Interning the same content after
//      the collection returns the SAME surviving instance (the pool kept it
//      alive). Contrast: a runtime-built, NEVER-interned string with only a
//      WeakReference is collected (probes the GC actually runs).
//   4. IDENTITY vs CONTENT semantics: Intern of an already-pooled literal
//      returns the literal itself; a runtime-built copy is a DISTINCT
//      instance until pooled; Intern is idempotent.
//
// WIRED SURFACE (the ONLY members used):
//   String.Intern (il2cpp_string_intern)  string.Concat (fresh instances)
//   GC.Collect()  WeakReference(.ctor(object)/IsAlive/Target)
//   (object)ReferenceEquals comparisons
//
// DELIBERATELY NOT PROBED (recorded defects, see GAPS.md):
//   * D-INT-1: String.IsInterned -- NO runtime icall and NO transpiler
//     mapping exist (grep finds no "IsInterned" in src/runtime or
//     src/il2cppc); a call would abort at transpile. IsInterned is therefore
//     OUT of reach.
//   * D-INT-2: interning a string then MUTATING it is impossible (strings
//     are immutable) -- no such probe.
//
// Every line prints "label=True/False" (deterministic on both runtimes;
// net48 is the byte-exact baseline).
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class StringInternDeep
{
    // Reference identity: (object) cast disables string == value semantics.
    static int RefEq(string a, string b) { return (object)a == (object)b ? 1 : 0; }

    // A runtime-built fresh string (Concat always allocates).
    static string Build(string part) { return string.Concat("deep-", part); }

    // Intern 'content' and return ONLY a WeakReference to the pool entry
    // (no other strong reference escapes this frame).
    static WeakReference InternDropped(string content)
    {
        string fresh = Build(content);
        string pooled = string.Intern(fresh);
        return new WeakReference(pooled);
    }

    // Build a fresh, NEVER-interned string and return ONLY a WeakReference.
    static WeakReference BuildDropped(string content)
    {
        return new WeakReference(Build(content));
    }

    public static void Main()
    {
        // ============ 1. reference identity + dedup by content ============
        Console.WriteLine("lit_same=" + (RefEq(string.Intern("deep-lit"), "deep-lit") == 1));
        string f1 = Build("copy");
        string p1 = string.Intern(f1);
        // On a MISS net48 returns the input itself as the pool entry (the
        // input IS pooled), so the first Intern is identity-preserving; a
        // second build of the same content stays a FRESH instance until it
        // is explicitly interned (then it dedups to p1).
        Console.WriteLine("first_miss_returns_input=" + (RefEq(f1, p1) == 1));
        Console.WriteLine("second_build_fresh=" + (RefEq(Build("copy"), p1) == 0));
        Console.WriteLine("pool_dedup=" + (RefEq(string.Intern(Build("copy")), p1) == 1));
        Console.WriteLine("idempotent=" + (RefEq(string.Intern(p1), p1) == 1));
        Console.WriteLine("distinct_content=" + (RefEq(string.Intern("deep-a"), string.Intern("deep-b")) == 0));
        Console.WriteLine("empty_once=" + (RefEq(string.Intern(""), "") == 1));

        // ============ 2. prefix sharing does NOT alias ============
        string pa = string.Intern("deep-alpha");
        string pb = string.Intern("deep-alphabet");
        string pc = string.Intern("deep-alpha");
        Console.WriteLine("pref_distinct=" + (RefEq(pa, pb) == 0));
        Console.WriteLine("pref_roundtrip=" + (RefEq(pa, pc) == 1));

        // ============ 3. pool growth (70% load factor) with identity ============
        // 100 distinct contents force the 64-slot table to grow several
        // times; every identity must survive every rehash.
        string[] first = new string[100];
        first[0] = string.Intern(Build("000"));
        bool growDistinct = true;
        for (int i = 1; i < 100; i++)
        {
            string tag = Build((i < 10 ? "00" : "0") + i);
            first[i] = string.Intern(tag);
            if (RefEq(first[i], first[i - 1]) == 1) growDistinct = false;
        }
        Console.WriteLine("grow_distinct=" + growDistinct);
        bool growDedup = true;
        for (int i = 0; i < 100; i++)
        {
            string tag = Build((i < 10 ? "00" : "0") + i);
            if (RefEq(string.Intern(tag), first[i]) == 0) growDedup = false;
        }
        Console.WriteLine("grow_dedup_after_rehash=" + growDedup);
        Console.WriteLine("grow_reintern_first=" + (RefEq(string.Intern(Build("000")), first[0]) == 1));
        Console.WriteLine("grow_reintern_last=" + (RefEq(string.Intern(Build("099")), first[99]) == 1));

        // ============ 4. GC survival: interned strings are pooled (rooted) ============
        WeakReference wr = InternDropped("alive");
        GC.Collect();
        GC.Collect();
        Console.WriteLine("gc_interned_alive=" + wr.IsAlive);
        string reinterned = string.Intern(Build("alive"));
        Console.WriteLine("gc_reintern_same=" + (RefEq(reinterned, (string)wr.Target) == 1));

        // Control: a NEVER-interned runtime string is collected.
        WeakReference wd = BuildDropped("dead");
        GC.Collect();
        GC.Collect();
        Console.WriteLine("gc_uninterned_dead=" + !wd.IsAlive);

        Console.WriteLine("done");
    }
}}
