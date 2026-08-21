// test_delegate_gcstress.cpp -- GC-stress suite for the delegate combine /
// remove / multicast-invoke surface (M4 icall domain).
//
// The collector is EXPLICIT-ONLY (gc.cpp: "directly (il2cpp_gc_collect) for
// explicit/forced collection"): allocation never auto-triggers a collection,
// so no collection can fire in the middle of a single icall. The GC-safety
// contract under test is therefore the CROSS-CALL one: a rooted multicast
// chain whose nodes (refFieldBitmap bits 1+2 = first/second), leaf targets
// and invocation-list array are reachable ONLY through the chain must survive
// repeated full collections (with Gen0 compaction moving every survivor), and
// combine/remove/invoke must still resolve correctly afterwards.
//
// NOTE on invoke semantics: icall_multicast_invoke_i4 returns the LAST leaf's
// result (.NET multicast semantics), so "every leaf fires" is verified with a
// GLOBAL SIDE-EFFECT counter (each leaf's method increments it) -- not by the
// return value.
//
// Sections:
//   1. Combine chain under GC pressure: 60 instance delegates combined into
//      one chain, a forced collection every 8 combines, then the chain length
//      is intact and every leaf still fires (side-effect count + sum).
//   2. Remove under GC pressure: leaves removed from a rooted chain with
//      collections interleaved; the remaining chain still invokes exactly the
//      surviving leaves.
//   3. Invoke under GC pressure: a rooted chain survives repeated collections
//      and every leaf still fires each round; leaf targets re-derived through
//      the invocation list are intact.
//   4. Mixed churn: LCG pseudo-random combine/remove with a forced collection
//      every few ops, verified against a model of live leaf values.
//
// GC discipline (the compacting collector MOVES Gen0 survivors and rewrites
// every reference): the chain is registered as a static root and every leaf
// is held in a FIXED array registered as a root range (the collector rewrites
// the pointers in place) -- never unrooted C++ locals across a collection.

#include "il2cpp_delegate_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstdio>
#include <vector>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Deterministic LCG (same sequence on every run -- no wall-clock dependence).
uint32_t g_lcg = 0xdeadbeefu;
uint32_t NextRand()
{
    g_lcg = g_lcg * 1664525u + 1013904223u;
    return g_lcg;
}

// --- Minimal instance class + method (mirrors test_delegateext) ---

struct TestInst
{
    il2cpp::Il2CppObject obj;
    int32_t value;
};

il2cpp::Il2CppClass g_instClass = {
    "InstTest.Counter",           // name
    static_cast<int32_t>(sizeof(TestInst)), // instanceSize
    0u,                           // refFieldBitmap: no reference fields
    0,                            // refFieldCount
    nullptr,                      // baseClass
    nullptr,                      // vtable
    0,                            // vtableSize
    nullptr,                      // interfaces
    0,                            // interfaceCount
};

bool g_instRegistered = false;

TestInst* NewInst(int32_t value)
{
    if (!g_instRegistered)
    {
        il2cpp::il2cpp_class_register(&g_instClass);
        g_instRegistered = true;
    }
    TestInst* t = reinterpret_cast<TestInst*>(il2cpp::il2cpp_gc_alloc(&g_instClass));
    t->value = value;
    return t;
}

// Side-effect counters: every leaf's method increments these, so "every leaf
// fires" is observable even though the invoke returns only the LAST result.
int32_t g_fireCount = 0;
int32_t g_fireSum = 0;

// Instance method with the generated-code instance ABI: arg0 = this.
int32_t InstAdd(il2cpp::Il2CppObject* this_, int32_t a0, int32_t a1, int32_t a2, int32_t a3)
{
    (void)a1; (void)a2; (void)a3;
    const int32_t r = reinterpret_cast<TestInst*>(this_)->value + a0;
    ++g_fireCount;
    g_fireSum += r;
    return r;
}

typedef int32_t (*I4ThisFn)(il2cpp::Il2CppObject*, int32_t, int32_t, int32_t, int32_t);

il2cpp::Il2CppDelegate* MakeInstDel(I4ThisFn fn, il2cpp::Il2CppObject* target)
{
    return il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(fn), target, 0);
}

il2cpp::Il2CppObject* Combine(il2cpp::Il2CppObject* a, il2cpp::Il2CppObject* b)
{
    return il2cpp::runtime::icall_delegate_combine(a, b);
}

il2cpp::Il2CppObject* Remove(il2cpp::Il2CppObject* s, il2cpp::Il2CppObject* v)
{
    return il2cpp::runtime::icall_delegate_remove(s, v);
}

int32_t Invoke4(il2cpp::Il2CppObject* d, int32_t x)
{
    return il2cpp::runtime::icall_multicast_invoke_i4(d, x, 0, 0, 0);
}

int32_t ListLen(il2cpp::Il2CppObject* d)
{
    il2cpp::Il2CppObject* arr = il2cpp::runtime::icall_delegate_get_invocation_list(d);
    if (arr == nullptr)
    {
        return -1;
    }
    return il2cpp::il2cpp_array_get_length(reinterpret_cast<il2cpp::Il2CppArray*>(arr));
}

// Read the leaf target's value through the invocation list (move-safe: the
// list is re-derived from the rooted chain each call).
int32_t LeafValue(il2cpp::Il2CppObject* d, int32_t i)
{
    il2cpp::Il2CppObject* arr = il2cpp::runtime::icall_delegate_get_invocation_list(d);
    il2cpp::Il2CppDelegate** slots =
        reinterpret_cast<il2cpp::Il2CppDelegate**>(reinterpret_cast<il2cpp::Il2CppArray*>(arr)->data);
    return reinterpret_cast<TestInst*>(slots[i]->target)->value;
}

} // namespace

int main()
{
    // --- 1. Combine chain under GC pressure: 60 delegates combined into one
    //    chain, a forced collection every 8 combines, then the chain length
    //    is intact and every leaf still fires ---
    {
        il2cpp::Il2CppObject* chain = nullptr;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&chain));
        int64_t expectedSum = 0;
        for (int32_t i = 0; i < 60; ++i)
        {
            TestInst* t = NewInst(i);
            il2cpp::Il2CppDelegate* d = MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(t));
            chain = Combine(chain, reinterpret_cast<il2cpp::Il2CppObject*>(d));
            expectedSum += i;
            if (i % 8 == 7)
            {
                il2cpp::il2cpp_gc_collect(); // may move the chain + nodes + targets
            }
        }
        il2cpp::il2cpp_gc_collect();
        Expect(ListLen(chain) == 60, "60-leaf chain survives collections (length)");
        const int32_t x = 5;
        g_fireCount = 0;
        g_fireSum = 0;
        const int32_t ret = Invoke4(chain, x);
        Expect(g_fireCount == 60, "every leaf fires after collections (count)");
        Expect(g_fireSum == static_cast<int32_t>(expectedSum) + 60 * x,
               "every leaf fires with the right value (sum)");
        Expect(ret == 59 + x, "invoke returns the LAST leaf result (multicast)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&chain));
    }

    // --- 2. Remove under GC pressure: leaves removed from a rooted chain
    //    with collections interleaved; the remaining chain still invokes
    //    exactly the surviving leaves ---
    {
        il2cpp::Il2CppObject* chain = nullptr;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&chain));
        il2cpp::Il2CppObject* leaves[40] = {};
        il2cpp::il2cpp_gc_add_root_range(reinterpret_cast<void**>(leaves), 40);
        for (int32_t i = 0; i < 40; ++i)
        {
            TestInst* t = NewInst(i);
            il2cpp::Il2CppDelegate* d = MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(t));
            leaves[i] = reinterpret_cast<il2cpp::Il2CppObject*>(d);
            chain = Combine(chain, leaves[i]);
        }
        // Remove every even-indexed leaf (values 0,2,4,...,38). The leaves
        // array is a ROOT RANGE: the collector rewrites its pointers in
        // place, so leaves[i] stays valid across the collections below.
        for (int32_t i = 0; i < 40; i += 2)
        {
            chain = Remove(chain, leaves[i]);
            if (i % 16 == 14)
            {
                il2cpp::il2cpp_gc_collect();
            }
        }
        il2cpp::il2cpp_gc_collect();
        Expect(ListLen(chain) == 20, "removed leaves stay gone (length)");
        int64_t expectedSum = 0;
        for (int32_t i = 1; i < 40; i += 2)
        {
            expectedSum += i;
        }
        const int32_t x = 3;
        g_fireCount = 0;
        g_fireSum = 0;
        Invoke4(chain, x);
        Expect(g_fireCount == 20, "exactly the surviving leaves fire (count)");
        Expect(g_fireSum == static_cast<int32_t>(expectedSum) + 20 * x,
               "surviving leaves fire with the right value (sum)");
        il2cpp::il2cpp_gc_remove_root_range(reinterpret_cast<void**>(leaves));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&chain));
    }

    // --- 3. Invoke under GC pressure: a rooted chain survives repeated
    //    collections and every leaf still fires each round ---
    {
        il2cpp::Il2CppObject* chain = nullptr;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&chain));
        int64_t expectedSum = 0;
        for (int32_t i = 0; i < 30; ++i)
        {
            TestInst* t = NewInst(i * 2);
            il2cpp::Il2CppDelegate* d = MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(t));
            chain = Combine(chain, reinterpret_cast<il2cpp::Il2CppObject*>(d));
            expectedSum += i * 2;
        }
        for (int32_t round = 0; round < 5; ++round)
        {
            il2cpp::il2cpp_gc_collect();
            const int32_t x = round + 1;
            g_fireCount = 0;
            g_fireSum = 0;
            Invoke4(chain, x);
            Expect(g_fireCount == 30, "every leaf fires after collection (count)");
            Expect(g_fireSum == static_cast<int32_t>(expectedSum) + 30 * x,
                   "every leaf fires with the right value (sum)");
        }
        // Leaf targets re-derived through the invocation list are intact.
        bool targetsIntact = true;
        for (int32_t i = 0; i < 30; ++i)
        {
            if (LeafValue(chain, i) != i * 2)
            {
                targetsIntact = false;
                break;
            }
        }
        Expect(targetsIntact, "leaf targets intact after collections");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&chain));
    }

    // --- 4. Mixed churn: LCG pseudo-random combine/remove with a forced
    //    collection every few ops, verified against a model of live values ---
    {
        il2cpp::Il2CppObject* chain = nullptr;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&chain));
        const int32_t kSlots = 200;
        il2cpp::Il2CppObject* leafSlots[200] = {};
        il2cpp::il2cpp_gc_add_root_range(reinterpret_cast<void**>(leafSlots), kSlots);
        std::vector<int32_t> liveSlots; // slot indices currently in the chain
        std::vector<int32_t> liveValues; // their values (model)
        int32_t nextSlot = 0;
        for (int32_t op = 0; op < 200; ++op)
        {
            const uint32_t r = NextRand();
            if (r % 3 == 0 && !liveSlots.empty())
            {
                // remove a random live leaf
                const size_t idx = static_cast<size_t>(NextRand() % liveSlots.size());
                chain = Remove(chain, leafSlots[liveSlots[idx]]);
                liveSlots.erase(liveSlots.begin() + static_cast<ptrdiff_t>(idx));
                liveValues.erase(liveValues.begin() + static_cast<ptrdiff_t>(idx));
            }
            else
            {
                // combine a fresh leaf into a fresh slot
                const int32_t v = static_cast<int32_t>(NextRand() % 100);
                TestInst* t = NewInst(v);
                il2cpp::Il2CppDelegate* d = MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(t));
                leafSlots[nextSlot] = reinterpret_cast<il2cpp::Il2CppObject*>(d);
                liveSlots.push_back(nextSlot);
                liveValues.push_back(v);
                chain = Combine(chain, leafSlots[nextSlot]);
                ++nextSlot;
            }
            if (op % 7 == 6)
            {
                il2cpp::il2cpp_gc_collect();
            }
        }
        il2cpp::il2cpp_gc_collect();
        Expect(ListLen(chain) == static_cast<int32_t>(liveValues.size()),
               "churn: chain length matches model after collections");
        int64_t expectedSum = 0;
        for (int32_t v : liveValues)
        {
            expectedSum += v;
        }
        const int32_t x = 2;
        g_fireCount = 0;
        g_fireSum = 0;
        Invoke4(chain, x);
        Expect(g_fireCount == static_cast<int32_t>(liveValues.size()),
               "churn: every model leaf fires (count)");
        Expect(g_fireSum == static_cast<int32_t>(expectedSum) +
                                static_cast<int32_t>(liveValues.size()) * x,
               "churn: fired sum matches model after collections");
        il2cpp::il2cpp_gc_remove_root_range(reinterpret_cast<void**>(leafSlots));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&chain));
    }

    if (g_failures == 0)
    {
        std::printf("all delegate gc-stress tests passed\n");
        return 0;
    }
    std::printf("%d delegate gc-stress test(s) FAILED\n", g_failures);
    return 1;
}
