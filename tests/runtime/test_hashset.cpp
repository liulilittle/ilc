//------------------------------------------------------------------------------
// test_hashset.cpp -- HashSet<string> runtime unit tests (hashset.h, the
// set-of-unique-members GC-object wrapper over the dictsi domain; D-HSET-1).
//
// Coverage (normal + edge + error paths):
//   1. ctor + count: a fresh set has Count 0; a ctor with a positive capacity
//      still starts empty.
//   2. Add returns TRUE for a brand-new member and FALSE for a duplicate
//      (net48 HashSet.Add semantics -- a duplicate is a no-op, NOT a throw).
//   3. Contains: true on a hit, false on a miss; case-distinct / prefix
//      strings are DIFFERENT members (ordinal content equality).
//   4. Content equality: two DISTINCT string instances with equal content are
//      the SAME member (dictsi ordinal content-equality contract).
//   5. Remove: true on a hit (count decrements, member gone), false when
//      absent (set unchanged).
//   6. Clear: count -> 0, every member gone, the set stays reusable.
//   7. Count tracks unique members across mixed add/remove sequences.
//   8. A null member raises ArgumentNullException from add/contains/remove
//      (the dictsi null-key contract).
//   9. Growth: adding 100 distinct members reads every membership back after
//      multiple dictsi bucket doublings (the 0.72 load-factor path).
//
// GC-rooting contract: the wrapper is a MANAGED object on the MOVING GC heap
// (the collector relocates live objects). The generated e2e code roots the
// receiver across calls (RootSlotPass); the test does the same -- every
// wrapper is registered as a static root for its scope (test_listi4
// precedent), so a string allocation inside an icall can never relocate the
// receiver underneath the test.
//
// Plain executable + minimal assertion harness (see test_dictsi.cpp).
//------------------------------------------------------------------------------

#include "hashset.h"

#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "hashset test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// The exception-factory chains (see il2cpp_exception_ext.h).
const char* kArgumentNullChain =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";

template <typename Fn>
bool ThrowsDotNet(Fn fn, const char* expectedChain)
{
    try
    {
        fn();
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedChain) == 0;
    }
}

il2cpp::Il2CppObject* O(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

// A GC-rooted wrapper: the collector moves live objects, so the test must
// keep the wrapper reachable across every icall (which may allocate strings).
struct RootedSet
{
    il2cpp::Il2CppObject* self;

    RootedSet(int32_t capacity)
    {
        self = il2cpp::il2cpp_object_new(&il2cpp::System_Collections_Generic_HashSet_1_string_class);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&self));
        il2cpp::runtime::icall_hashset_string_ctor(self, capacity);
    }

    ~RootedSet()
    {
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&self));
    }
};

} // namespace

int main()
{
    // --- 1. ctor + count: fresh set starts at 0; capacity ctor starts empty ---
    {
        RootedSet r(0);
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 0, "fresh set: count 0");
        il2cpp::runtime::icall_hashset_string_clear(r.self);
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 0, "clear on empty: count 0");
    }
    {
        RootedSet r(16);
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 0, "capacity ctor: count 0");
    }

    // --- 2. Add: true for new, false for duplicate ---
    {
        RootedSet r(0);
        Expect(il2cpp::runtime::icall_hashset_string_add(r.self, O("alpha")) == true, "Add(new member) returns true");
        Expect(il2cpp::runtime::icall_hashset_string_add(r.self, O("alpha")) == false, "Add(duplicate) returns false");
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 1, "duplicate Add: count unchanged at 1");
        Expect(il2cpp::runtime::icall_hashset_string_add(r.self, O("beta")) == true, "Add(second member) returns true");
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 2, "two members: count 2");
    }

    // --- 3. Contains: hit/miss + case/prefix distinctness ---
    {
        RootedSet r(0);
        il2cpp::runtime::icall_hashset_string_add(r.self, O("alpha"));
        il2cpp::runtime::icall_hashset_string_add(r.self, O("beta"));
        Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O("alpha")) == true, "Contains hit");
        Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O("beta")) == true, "Contains second hit");
        Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O("gamma")) == false, "Contains miss");
        Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O("ALPHA")) == false, "case-distinct member miss");
        Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O("alphabet")) == false, "prefix-distinct member miss");
    }

    // --- 4. Content equality: distinct instances, equal content = same member ---
    {
        RootedSet r(0);
        il2cpp::Il2CppString* keyA = il2cpp::il2cpp_string_new_from_cs("alpha");
        il2cpp::Il2CppString* keyB = il2cpp::il2cpp_string_new_from_cs("alpha");
        Expect(keyA != keyB, "two distinct string instances");
        Expect(il2cpp::runtime::icall_hashset_string_add(r.self, reinterpret_cast<il2cpp::Il2CppObject*>(keyB)) == true,
               "Add with a content-equal distinct instance: added");
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 1, "content-equal add: count 1");
        Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O("alpha")) == true, "content-equal membership");
    }

    // --- 5. Remove: true on hit (count drops, member gone), false on miss ---
    {
        RootedSet r(0);
        il2cpp::runtime::icall_hashset_string_add(r.self, O("alpha"));
        il2cpp::runtime::icall_hashset_string_add(r.self, O("beta"));
        Expect(il2cpp::runtime::icall_hashset_string_remove(r.self, O("beta")) == true, "Remove(hit) returns true");
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 1, "after remove: count 1");
        Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O("beta")) == false, "removed member gone");
        Expect(il2cpp::runtime::icall_hashset_string_remove(r.self, O("gamma")) == false, "Remove(miss) returns false");
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 1, "remove miss: count unchanged");
    }

    // --- 6. Clear: count -> 0, reusable ---
    {
        RootedSet r(0);
        il2cpp::runtime::icall_hashset_string_add(r.self, O("alpha"));
        il2cpp::runtime::icall_hashset_string_add(r.self, O("beta"));
        il2cpp::runtime::icall_hashset_string_clear(r.self);
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 0, "after clear: count 0");
        Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O("alpha")) == false, "cleared member gone");
        il2cpp::runtime::icall_hashset_string_add(r.self, O("new"));
        Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O("new")) == true, "set reusable after clear");
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 1, "reused set: count 1");
    }

    // --- 7. Count across mixed add/remove sequences ---
    {
        RootedSet r(0);
        const char* words[] = { "a", "b", "c", "d", "e" };
        for (int i = 0; i < 5; ++i)
            il2cpp::runtime::icall_hashset_string_add(r.self, O(words[i]));
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 5, "5 adds: count 5");
        il2cpp::runtime::icall_hashset_string_remove(r.self, O("b"));
        il2cpp::runtime::icall_hashset_string_remove(r.self, O("d"));
        il2cpp::runtime::icall_hashset_string_add(r.self, O("a"));
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 3, "mixed seq: count 3");
        Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O("c")) == true, "mixed seq: c present");
        Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O("b")) == false, "mixed seq: b gone");
    }

    // --- 8. Null member -> ArgumentNullException ---
    {
        RootedSet r(0);
        Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_hashset_string_add(r.self, nullptr); }, kArgumentNullChain),
               "Add(null) raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_hashset_string_contains(r.self, nullptr); }, kArgumentNullChain),
               "Contains(null) raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_hashset_string_remove(r.self, nullptr); }, kArgumentNullChain),
               "Remove(null) raises ArgumentNullException");
    }

    // --- 9. Growth: 100 distinct members, every membership back after
    //    multiple dictsi bucket doublings ---
    {
        RootedSet r(0);
        char buf[24];
        for (int i = 0; i < 100; ++i)
        {
            std::snprintf(buf, sizeof(buf), "key%03d", i);
            Expect(il2cpp::runtime::icall_hashset_string_add(r.self, O(buf)) == true, "growth add returns true");
        }
        Expect(il2cpp::runtime::icall_hashset_string_get_count(r.self) == 100, "growth: count 100");
        for (int i = 0; i < 100; i += 7)
        {
            std::snprintf(buf, sizeof(buf), "key%03d", i);
            Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O(buf)) == true, "growth membership back");
        }
        Expect(il2cpp::runtime::icall_hashset_string_contains(r.self, O("key999")) == false, "growth miss for absent member");
    }

    if (g_failures == 0)
        std::printf("PASS: test_hashset\n");
    else
        std::printf("FAIL: %d assertion(s) failed\n", g_failures);
    return g_failures == 0 ? 0 : 1;
}
