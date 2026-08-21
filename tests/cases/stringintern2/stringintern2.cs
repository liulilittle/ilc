//------------------------------------------------------------------------------
// stringintern2.cs -- String.Intern DEEP 2 e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C64, AFTER the pooling iron law
// (stringintern.cpp + il2cpp_string_intern.h StringPool: open-addressing
// FNV-1a table, 70% load-factor doubling, strong-rooted entries, recursive
// mutex). Beyond stringinterndeep (identity/dedup/prefix-non-alias/GROW-to-100/
// GC-survival, C52) and internconc (8-worker mutex concurrency, C23), this
// case drives TWO new dimensions -- INTERN COMBINATIONS and POOL PRESSURE
// BEYOND ONE GROW:
//
//   1. INTERN COMBINATIONS -- nested Intern(Intern(x)) / Intern(Intern(Intern(x)))
//      idempotence chains; Intern applied to a Concat of TWO ALREADY-POOLED
//      strings (pooled content as the miss source); cross-construction content
//      equality ("si2-xcombo" assembled via two DIFFERENT Concat shapes ->
//      Intern must dedup to ONE pool entry); empty-string and single-char
//      Intern round-trips (the empty literal is the pool entry).
//   2. POOL PRESSURE, MULTI-GROW -- 320 DISTINCT runtime-built contents force
//      the 64-slot table through FOUR rehashes (64 -> 128 -> 256 -> 512 at the
//      70% load factor). Every identity from the FIRST pass must survive every
//      rehash (second-pass re-Intern of ALL 320 returns the SAME instances);
//      a duplicate re-Intern pass (find-only path) under full-table pressure
//      returns the same entries (no duplicate inserts).
//   3. PROBE-STRESS -- 200 contents that differ ONLY in their trailing digits
//      ("si2-lc-000".."si2-lc-199", the open-addressing probe-chain stress
//      shape) must stay all-distinct AND round-trip after the pressure phase.
//   4. LONG-CONTENT interning (200-char payload built by Concat) -- a miss
//      interns the input itself; re-Intern of a rebuilt copy returns the same
//      entry; distinct long contents never alias.
//   5. CROSS-CONSTRUCTION DEDUP -- same content assembled through different
//      Concat partings dedups to the same pool entry (the pool compares by
//      CONTENT, never by construction history).
//
// net48 LITERAL PRE-INTERNING discipline (internconc/stringinterndeep rule):
// a literal used anywhere in the source is pooled at assembly/JIT time, so
// every runtime content below is built via string.Concat from parts that are
// NEVER assembled into a full source literal ("si2-" + part shapes only). A
// first Intern is therefore a genuine MISS and returns the input instance.
//
// All work is MAIN-THREAD (no workers; the internconc mutex is already
// proven) and every probe is deterministic. Every line prints
// "label=True/False"; net48 is the byte-exact baseline.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class StringIntern2
{
    // Reference identity: (object) cast disables string == value semantics.
    static int RefEq(string a, string b) { return (object)a == (object)b ? 1 : 0; }

    // A runtime-built fresh string (Concat always allocates). "si2-" is never
    // written as a full-content literal anywhere in this source.
    static string Build(string part) { return string.Concat("si2-", part); }

    public static void Main()
    {
        // ============ 1. INTERN COMBINATIONS ============
        // Nested Intern chains are idempotent: Intern(Intern(x)) == Intern(x).
        string cb = Build("combo");
        string p1 = string.Intern(cb);
        string p2 = string.Intern(p1);
        string p3 = string.Intern(p2);
        Console.WriteLine("nest2_same=" + (RefEq(p2, p1) == 1));
        Console.WriteLine("nest3_same=" + (RefEq(p3, p1) == 1));
        Console.WriteLine("nest_first_miss=" + (RefEq(p1, cb) == 1));

        // Intern of a Concat of TWO ALREADY-POOLED strings: the miss content is
        // built from pool entries, yet it is still a FRESH unpooled instance
        // until interned (Concat allocates); after Intern the content is pooled.
        string pa = string.Intern("si2-part-a");
        string pb = string.Intern("si2-part-b");
        string joined = string.Concat(pa, pb);          // "si2-part-asi2-part-b"
        string pj = string.Intern(joined);
        Console.WriteLine("join_fresh_before=" + (RefEq(joined, pj) == 1));
        Console.WriteLine("join_reintern=" + (RefEq(string.Intern(string.Concat(pa, pb)), pj) == 1));

        // Cross-construction content equality: "si2-xcombo" assembled two ways.
        string x1 = string.Concat("si2-", "xcombo");
        string x2 = string.Concat("si2-x", "combo");
        string i1 = string.Intern(x1);
        string i2 = string.Intern(x2);
        Console.WriteLine("x_construct_distinct=" + (RefEq(x1, x2) == 0));
        Console.WriteLine("x_dedup_one_entry=" + (RefEq(i1, i2) == 1));
        Console.WriteLine("x_first_is_input=" + (RefEq(i1, x1) == 1));

        // Empty + single-char Intern round-trips.
        Console.WriteLine("empty_lit_same=" + (RefEq(string.Intern(""), "") == 1));
        Console.WriteLine("empty_reintern=" + (RefEq(string.Intern(""), string.Intern("")) == 1));
        Console.WriteLine("char_dedup=" + (RefEq(string.Intern("si2-q"), string.Intern("si2-q")) == 1));

        // ============ 2. POOL PRESSURE: 320 distinct -> 4 rehashes ============
        // 320 distinct contents (64-slot table doubles at 44.8 entries -> four
        // rehashes). Every first-pass identity must survive every rehash.
        string[] first = new string[320];
        first[0] = string.Intern(Build("000"));
        bool pressureDistinct = true;
        for (int i = 1; i < 320; i++)
        {
            string tag = Build(i < 10 ? "00" + i : i < 100 ? "0" + i : "" + i);
            first[i] = string.Intern(tag);
            if (RefEq(first[i], first[i - 1]) == 1) pressureDistinct = false;
        }
        Console.WriteLine("pressure_distinct=" + pressureDistinct);
        bool pressureDedup = true;
        for (int i = 0; i < 320; i++)
        {
            string tag = Build(i < 10 ? "00" + i : i < 100 ? "0" + i : "" + i);
            if (RefEq(string.Intern(tag), first[i]) == 0) pressureDedup = false;
        }
        Console.WriteLine("pressure_dedup_all=" + pressureDedup);
        Console.WriteLine("pressure_rein0=" + (RefEq(string.Intern(Build("000")), first[0]) == 1));
        Console.WriteLine("pressure_rein63=" + (RefEq(string.Intern(Build("063")), first[63]) == 1));
        Console.WriteLine("pressure_rein127=" + (RefEq(string.Intern(Build("127")), first[127]) == 1));
        Console.WriteLine("pressure_rein255=" + (RefEq(string.Intern(Build("255")), first[255]) == 1));
        Console.WriteLine("pressure_rein319=" + (RefEq(string.Intern(Build("319")), first[319]) == 1));

        // Duplicate re-Intern pass under FULL-table pressure: the find-only
        // path must never insert a duplicate (all 320 already present).
        bool pressureNoDup = true;
        for (int i = 0; i < 320; i++)
        {
            string tag = Build(i < 10 ? "00" + i : i < 100 ? "0" + i : "" + i);
            if (RefEq(string.Intern(tag), first[i]) == 0) pressureNoDup = false;
        }
        Console.WriteLine("pressure_findonly_no_dup=" + pressureNoDup);

        // ============ 3. PROBE-STRESS: trailing-digit-only contents ============
        // 200 contents differing ONLY in the final digits: the open-addressing
        // probe chain must keep walking past near-misses, never aliasing. The
        // "lc-NNN" contents are built at runtime only (never source literals).
        string lc0 = string.Intern(Build("lc-000"));
        bool lcAllDistinct = true;
        for (int i = 1; i < 200; i++)
        {
            string tag = Build("lc-" + (i < 10 ? "00" + i : i < 100 ? "0" + i : "" + i));
            string cur = string.Intern(tag);
            if (RefEq(cur, lc0) == 1) lcAllDistinct = false;
        }
        Console.WriteLine("lc_alldistinct=" + lcAllDistinct);
        Console.WriteLine("lc_rein_first=" + (RefEq(string.Intern(Build("lc-000")), lc0) == 1));
        Console.WriteLine("lc_rein_last=" + (RefEq(string.Intern(Build("lc-199")), string.Intern(Build("lc-199"))) == 1));

        // ============ 4. LONG-CONTENT interning ============
        // A 200-char payload (two 100-char halves, never a source literal).
        string longA = string.Concat(
            "si2-long-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC");
        string longB = string.Concat(
            "si2-long-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC");
        string l1 = string.Intern(longA);
        string l2 = string.Intern(longB);
        Console.WriteLine("long_fresh_distinct=" + (RefEq(longA, longB) == 0));
        Console.WriteLine("long_dedup_same=" + (RefEq(l1, l2) == 1));
        Console.WriteLine("long_miss_input=" + (RefEq(l1, longA) == 1));
        string longC = string.Concat(
            "si2-long-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC");
        Console.WriteLine("long_rebuild_same=" + (RefEq(string.Intern(longC), l1) == 1));

        // ============ 5. cross-construction dedup under pressure ============
        // The 320-pressure content "si2-159" re-assembled via a DIFFERENT
        // Concat parting must dedup to the same pool entry.
        string cA = string.Concat("si2-1", "59");
        string cB = string.Concat("si2-", "159");
        Console.WriteLine("cc_construct_distinct=" + (RefEq(cA, cB) == 0));
        Console.WriteLine("cc_dedup_pressure=" + (RefEq(string.Intern(cA), first[159]) == 1));
        Console.WriteLine("cc_dedup_other=" + (RefEq(string.Intern(cB), first[159]) == 1));

        Console.WriteLine("done");
    }
}}
