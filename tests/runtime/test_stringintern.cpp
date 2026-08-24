//------------------------------------------------------------------------------
// test_stringintern.cpp -- string literal interning runtime unit tests.
//
// Coverage (normal + edge + error paths):
//   1. Same literal twice -> SAME instance (the .NET intern contract).
//   2. Different content   -> different instances.
//   3. il2cpp_string_intern on a fresh non-pool string with existing content
//      returns the POOL instance, not the input (identity semantics).
//   4. Interning an already-interned string returns that same instance.
//   5. The empty literal is interned exactly once.
//   6. Defensive inputs: null -> null; non-string object -> unchanged.
//   7. The pool holds STRONG references: an interned string (reachable only
//      through the table) survives a collection -- and survives Gen0
//      COMPACTION with its identity intact (the static-root mechanism the
//      pool registers is rewritten by Phase C; a fresh pool lookup returns
//      the moved instance).
//   8. Table growth: 500 distinct literals force several doublings (the
//      70% load factor is crossed at 45/90/179/359 entries); every identity
//      survives rehash, and a fresh intern() of an old content still returns
//      the ORIGINAL instance.
//   9. GC after growth: identity + content survive another collection.
//  10. Concurrency: N threads interning DISTINCT literals + String.Intern on
//      fresh strings simultaneously must converge to a consistent pool --
//      every thread sees the SAME instance per content (net48 String.Intern
//      is thread-safe; the pool serializes find/insert/grow). Also a
//      concurrent HIT on the same content from every thread returns the one
//      pool instance.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_string_intern.h"

#include "il2cpp_array.h"
#include "il2cpp_gc.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <thread>
#include <vector>

namespace {

static_assert(sizeof(void*) == 8, "intern test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// A GC root holding the first instance of an interned literal (the pool's
// own static root keeps the TABLE alive; this root keeps a CONCRETE pointer
// comparable across collections -- Phase C rewrites root slots).
il2cpp::Il2CppObject* g_keep = nullptr;

bool IsIntact(const il2cpp::Il2CppString* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const size_t len = std::strlen(ascii);
    if (static_cast<size_t>(s->length) != len)
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

} // namespace

int main()
{
    // --- 1. Same literal twice -> same instance ---
    il2cpp::Il2CppObject* a1 = il2cpp::il2cpp_string_intern_literal("abc");
    il2cpp::Il2CppObject* a2 = il2cpp::il2cpp_string_intern_literal("abc");
    Expect(a1 != nullptr, "interned literal is non-null");
    Expect(a1 == a2, "the same literal twice returns the SAME instance");
    Expect(IsIntact(reinterpret_cast<il2cpp::Il2CppString*>(a1), "abc"),
           "interned literal content is intact");

    // --- 2. Different content -> different instances ---
    il2cpp::Il2CppObject* b1 = il2cpp::il2cpp_string_intern_literal("xyz");
    Expect(b1 != nullptr && b1 != a1, "different literals are different instances");
    Expect(IsIntact(reinterpret_cast<il2cpp::Il2CppString*>(b1), "xyz"),
           "second literal content is intact");

    // --- 3. intern() on a fresh string with existing content -> pool instance ---
    il2cpp::Il2CppString* fresh = il2cpp::il2cpp_string_new_from_cs("abc");
    il2cpp::Il2CppObject* r3 = il2cpp::il2cpp_string_intern(reinterpret_cast<il2cpp::Il2CppObject*>(fresh));
    Expect(r3 != reinterpret_cast<il2cpp::Il2CppObject*>(fresh),
           "intern() of an existing content returns the pool instance, not the input");
    Expect(r3 == a1, "intern() returns the very same instance the literal got");
    // Two fresh strings with the same new content intern to ONE instance.
    il2cpp::Il2CppString* f1 = il2cpp::il2cpp_string_new_from_cs("qwe");
    il2cpp::Il2CppString* f2 = il2cpp::il2cpp_string_new_from_cs("qwe");
    il2cpp::Il2CppObject* i1 = il2cpp::il2cpp_string_intern(reinterpret_cast<il2cpp::Il2CppObject*>(f1));
    il2cpp::Il2CppObject* i2 = il2cpp::il2cpp_string_intern(reinterpret_cast<il2cpp::Il2CppObject*>(f2));
    Expect(i1 == i2, "two fresh same-content strings intern to one instance");
    Expect(il2cpp::il2cpp_string_intern_literal("qwe") == i1,
           "intern_literal finds the instance intern() created");

    // --- 4. Interning an already-interned string returns that same instance ---
    Expect(il2cpp::il2cpp_string_intern(a1) == a1,
           "intern() of an interned string returns the same instance");

    // --- 5. The empty literal is interned exactly once ---
    il2cpp::Il2CppObject* e1 = il2cpp::il2cpp_string_intern_literal("");
    il2cpp::Il2CppObject* e2 = il2cpp::il2cpp_string_intern_literal("");
    Expect(e1 != nullptr && e1 == e2, "the empty literal is interned exactly once");
    il2cpp::Il2CppString* eFresh = il2cpp::il2cpp_string_new_from_cs("");
    Expect(il2cpp::il2cpp_string_intern(reinterpret_cast<il2cpp::Il2CppObject*>(eFresh)) == e1,
           "a fresh empty string interns to the pool empty instance");

    // --- 6. Defensive inputs ---
    Expect(il2cpp::il2cpp_string_intern(nullptr) == nullptr,
           "intern(null) returns null (defensive)");
    il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(nullptr, 4, false, 2);
    Expect(il2cpp::il2cpp_string_intern(reinterpret_cast<il2cpp::Il2CppObject*>(arr)) ==
               reinterpret_cast<il2cpp::Il2CppObject*>(arr),
           "intern(non-string) returns the input unchanged (defensive)");

    // --- 7. Strong reference: interned strings survive GC + compaction ---
    g_keep = il2cpp::il2cpp_string_intern_literal("gckeep");
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_keep));
    // Garbage pressure: unrooted strings that must be reclaimed, never the
    // pool (the table is rooted by the pool's own static root).
    for (int i = 0; i < 200; ++i)
    {
        il2cpp::Il2CppString* junk = il2cpp::il2cpp_string_new_from_cs("junkjunkjunkjunkjunk");
        (void)junk;
    }
    il2cpp::il2cpp_gc_collect();
    Expect(g_keep != nullptr && IsIntact(reinterpret_cast<il2cpp::Il2CppString*>(g_keep), "gckeep"),
           "interned string survives collection with content intact (strong reference)");
    Expect(il2cpp::il2cpp_string_intern_literal("gckeep") == g_keep,
           "pool lookup after collection returns the SAME moved instance (identity preserved)");

    // --- 8. Growth: 500 distinct literals cross several table doublings ---
    // The first/mid/last instances are ROOTED before the growth so their
    // pointers stay valid across any collection the grows may trigger
    // (Phase C rewrites root slots; C++ locals would go stale).
    g_keep = il2cpp::il2cpp_string_intern_literal("s0000");
    il2cpp::Il2CppObject* g_mid = il2cpp::il2cpp_string_intern_literal("s0250");
    il2cpp::Il2CppObject* g_last = il2cpp::il2cpp_string_intern_literal("s0499");
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_keep));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_mid));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_last));
    for (int i = 0; i < 500; ++i)
    {
        char buf[32];
        std::snprintf(buf, sizeof(buf), "s%04d", i);
        Expect(il2cpp::il2cpp_string_intern_literal(buf) != nullptr,
               "interned literal during growth is non-null");
    }
    // Verification phase: every lookup below is a HIT, and a hit performs no
    // allocation -- no collection can run inside this window, so the captured
    // pointers stay valid for the whole loop (rehash correctness + identity).
    il2cpp::Il2CppObject* slot[500];
    for (int i = 0; i < 500; ++i)
    {
        char buf[32];
        std::snprintf(buf, sizeof(buf), "s%04d", i);
        slot[i] = il2cpp::il2cpp_string_intern_literal(buf);
    }
    for (int i = 0; i < 500; ++i)
    {
        const il2cpp::Il2CppString* s = reinterpret_cast<const il2cpp::Il2CppString*>(slot[i]);
        Expect(s != nullptr && s->length == 5 && s->chars[0] == u's',
               "rehashed instance has the expected shape (len 5, 's' prefix)");
        for (int j = i + 1; j < 500; ++j)
        {
            Expect(slot[i] != slot[j], "distinct literals remain distinct instances");
        }
    }
    Expect(slot[0] == g_keep && slot[250] == g_mid && slot[499] == g_last,
           "identities rooted before growth survived every doubling (same instances)");
    // A fresh string with pre-growth content still interns to the original.
    il2cpp::Il2CppString* oldContent = il2cpp::il2cpp_string_new_from_cs("s0000");
    Expect(il2cpp::il2cpp_string_intern(reinterpret_cast<il2cpp::Il2CppObject*>(oldContent)) == g_keep,
           "intern() after growth returns the original instance");

    // --- 9. GC after growth ---
    il2cpp::il2cpp_gc_collect();
    Expect(il2cpp::il2cpp_string_intern_literal("s0250") == g_mid,
           "interned instance identity survives a collection after growth");

    // --- 10. Concurrency: interning from N threads ---
    // net48 String.Intern is thread-safe (live oracle: intern_concurrent=OK
    // / intern_identity=same). The pool serializes find/insert/grow under an
    // internal recursive mutex, so N threads interning DISTINCT pre-created
    // strings concurrently must converge to a consistent pool -- every
    // content resolves to the SAME instance, no entry lost, no corruption.
    //
    // Allocator discipline: the M-stage managed allocator is SINGLE-THREADED
    // (thread callbacks must not allocate), so the worker threads only
    // perform pool operations that do NOT allocate: interning a pre-created
    // string is a pointer store on a miss / a hit on an existing entry, and
    // the pool is pre-sized so the concurrent inserts stay under the 70% load
    // factor (no grow, no allocation). Pre-created inputs are rooted via a
    // root range so a GC during their creation can never reclaim them.
    {
        constexpr int kThreads = 4;
        constexpr int kPerThread = 30; // 120 distinct strings total
        constexpr int kTotal = kThreads * kPerThread;
        // Pre-created distinct strings (rooted: the pool roots them only on
        // insertion, so the range keeps them alive between creation and the
        // concurrent phase).
        il2cpp::Il2CppObject* pre[kTotal];
        for (int i = 0; i < kTotal; ++i)
        {
            char buf[32];
            std::snprintf(buf, sizeof(buf), "w%04d", 6000 + i);
            pre[i] = reinterpret_cast<il2cpp::Il2CppObject*>(
                il2cpp::il2cpp_string_new_from_cs(buf));
        }
        il2cpp::il2cpp_gc_add_root_range(reinterpret_cast<void**>(pre), kTotal);
        // A shared content interned once on the main thread: every thread's
        // lookup of it must return THIS instance (hit, no allocation).
        il2cpp::Il2CppObject* expectedShared = il2cpp::il2cpp_string_intern_literal("shared-content");

        std::vector<std::thread> workers;
        std::atomic<bool> sharedFailed{false};
        for (int t = 0; t < kThreads; ++t)
        {
            workers.emplace_back([t, kPerThread, &pre, &sharedFailed, expectedShared] {
                const int base = t * kPerThread;
                for (int i = 0; i < kPerThread; ++i)
                {
                    // Miss: inserts the pre-created string (pointer store).
                    // Hit (another thread already inserted): returns the pool
                    // entry. Both paths must return the SAME instance.
                    il2cpp::Il2CppObject* a = il2cpp::il2cpp_string_intern(pre[base + i]);
                    il2cpp::Il2CppObject* b = il2cpp::il2cpp_string_intern(pre[base + i]);
                    if (a == nullptr || a != b)
                    {
                        sharedFailed = true;
                    }
                }
                // Concurrent HIT on the shared content: every thread must see
                // the one pool instance (no duplicate entries).
                if (il2cpp::il2cpp_string_intern_literal("shared-content") != expectedShared)
                {
                    sharedFailed = true;
                }
            });
        }
        for (auto& w : workers)
        {
            w.join();
        }
        Expect(!sharedFailed.load(), "concurrent intern: no thread observed an inconsistency");
        // Every distinct content is present and identity-consistent.
        for (int i = 0; i < kTotal; ++i)
        {
            char buf[32];
            std::snprintf(buf, sizeof(buf), "w%04d", 6000 + i);
            il2cpp::Il2CppObject* a = il2cpp::il2cpp_string_intern_literal(buf);
            il2cpp::Il2CppObject* b = il2cpp::il2cpp_string_intern_literal(buf);
            Expect(a != nullptr && a == b, "concurrent intern: distinct content identity holds");
            Expect(a == pre[i], "concurrent intern: content resolves to the pre-created instance");
        }
        // The shared content resolves to the SAME instance every thread saw.
        Expect(il2cpp::il2cpp_string_intern_literal("shared-content") == expectedShared,
               "concurrent intern: shared content still resolves to the first instance");
        il2cpp::il2cpp_gc_remove_root_range(reinterpret_cast<void**>(pre));
    }

    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&g_keep));
    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&g_mid));
    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&g_last));

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d string intern assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime string intern tests passed\n");
    return 0;
}
