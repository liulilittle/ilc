// test_arraylist_gcstress.cpp -- GC-stress suite for System.Collections.
// ArrayList (M4 icall domain).
//
// The collector is EXPLICIT-ONLY (gc.cpp: "directly (il2cpp_gc_collect) for
// explicit/forced collection"): allocation never auto-triggers a collection,
// so no collection can fire in the middle of a single icall. The GC-safety
// contract under test is therefore the CROSS-CALL one: a rooted list whose
// items array, elements and boxed values are reachable ONLY through the list
// must survive repeated full collections (with Gen0 compaction moving every
// survivor), and every operation (add/insert/remove/set/get) must still
// resolve correctly afterwards -- including across multiple GROWTHS (the
// capacity-doubling path allocates a fresh items array and copies every
// element).
//
// Sections:
//   1. Add + growth under GC pressure: 200 string elements (crossing several
//      capacity-doubling thresholds), a forced collection every 10 adds, then
//      every element still retrievable by content.
//   2. Insert under GC pressure: inserts at various positions interleaved
//      with collections; the final order matches the model.
//   3. Remove under GC pressure: removals interleaved with collections;
//      removed elements stay gone, remaining stay intact.
//   4. Boxed values under GC pressure: boxed int32 elements survive repeated
//      collections (klass + value intact).
//   5. Mixed churn: LCG pseudo-random add/insert/remove/set with a forced
//      collection every few ops, verified against a std::vector model.
//
// GC discipline (the compacting collector MOVES Gen0 survivors and rewrites
// every reference): the list is registered as a static root for the whole
// test; every post-collection access re-reads the list through the root and
// uses FRESH lookups with move-safe CONTENT equality -- never cached C++
// pointers.

#include "arraylist.h"
#include "il2cpp_gc.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
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

il2cpp::Il2CppObject* Str(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

// Move-safe content check: does 'obj' hold a managed string whose UTF-16
// units equal the ASCII text? Used after collections, where local pointers
// are STALE (the compacting collector moves Gen0 survivors) -- content
// equality is the only comparison that stays valid across a move.
bool IsContent(il2cpp::Il2CppObject* obj, const char* ascii)
{
    if (obj == nullptr || obj->klass == nullptr ||
        std::strcmp(obj->klass->name, "System.String") != 0)
    {
        return false;
    }
    const il2cpp::Il2CppString* s = reinterpret_cast<const il2cpp::Il2CppString*>(obj);
    const size_t len = std::strlen(ascii);
    if (s->length != static_cast<int32_t>(len))
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

// Deterministic LCG (same sequence on every run -- no wall-clock dependence).
uint32_t g_lcg = 0x9e3779b9u;
uint32_t NextRand()
{
    g_lcg = g_lcg * 1664525u + 1013904223u;
    return g_lcg;
}

std::string ElemName(int32_t i)
{
    return "e" + std::to_string(i);
}

} // namespace

int main()
{
    // --- 1. Add + growth under GC pressure: 200 elements, a forced
    //    collection every 10 adds, then every element still retrievable ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&list));
        for (int32_t i = 0; i < 200; ++i)
        {
            const std::string e = ElemName(i);
            il2cpp::il2cpp_arraylist_add(list, Str(e.c_str()));
            if (i % 10 == 9)
            {
                il2cpp::il2cpp_gc_collect(); // may move the list + items array
            }
        }
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 200,
               "200 elements survive repeated collections + growths (count)");
        for (int32_t i = 0; i < 200; ++i)
        {
            const std::string e = ElemName(i);
            Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(list, i), e.c_str()),
                   "element survives growth + collection (content)");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&list));
    }

    // --- 2. Insert under GC pressure: inserts at various positions
    //    interleaved with collections; the final order matches the model ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&list));
        std::vector<std::string> model;
        for (int32_t i = 0; i < 60; ++i)
        {
            const int32_t pos = static_cast<int32_t>(NextRand() % (model.size() + 1));
            const std::string e = ElemName(i);
            il2cpp::il2cpp_arraylist_insert(list, pos, Str(e.c_str()));
            model.insert(model.begin() + pos, e);
            if (i % 8 == 7)
            {
                il2cpp::il2cpp_gc_collect();
            }
        }
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_arraylist_get_count(list) ==
                   static_cast<int32_t>(model.size()),
               "insert order count matches model after collections");
        bool orderMatches = true;
        for (size_t i = 0; i < model.size(); ++i)
        {
            if (!IsContent(il2cpp::il2cpp_arraylist_get_item(list, static_cast<int32_t>(i)),
                           model[i].c_str()))
            {
                orderMatches = false;
                break;
            }
        }
        Expect(orderMatches, "insert order matches model after collections");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&list));
    }

    // --- 3. Remove under GC pressure: removals interleaved with
    //    collections; removed elements stay gone, remaining stay intact ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&list));
        for (int32_t i = 0; i < 100; ++i)
        {
            const std::string e = ElemName(i);
            il2cpp::il2cpp_arraylist_add(list, Str(e.c_str()));
        }
        for (int32_t i = 0; i < 100; i += 2)
        {
            il2cpp::il2cpp_arraylist_remove_at(list, 0); // remove even-indexed
            if (i % 20 == 18)
            {
                il2cpp::il2cpp_gc_collect();
            }
        }
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_arraylist_get_count(list) == 50,
               "removed elements stay gone across collections (count)");
        for (int32_t i = 0; i < 50; ++i)
        {
            const std::string e = ElemName(50 + i); // e50..e99 remain
            Expect(IsContent(il2cpp::il2cpp_arraylist_get_item(list, i), e.c_str()),
                   "remaining element stays intact (content)");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&list));
    }

    // --- 4. Boxed values under GC pressure: boxed int32 elements survive
    //    repeated collections (klass + value intact) ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&list));
        for (int32_t i = 0; i < 40; ++i)
        {
            il2cpp::il2cpp_arraylist_add(list, il2cpp::il2cpp_box_int32(nullptr, i * 7));
        }
        for (int32_t round = 0; round < 4; ++round)
        {
            il2cpp::il2cpp_gc_collect();
            for (int32_t i = 0; i < 40; ++i)
            {
                il2cpp::Il2CppObject* boxed = il2cpp::il2cpp_arraylist_get_item(list, i);
                if (boxed == nullptr || boxed->klass == nullptr ||
                    std::strcmp(boxed->klass->name, "System.Int32") != 0)
                {
                    Expect(false, "boxed element klass intact after collection");
                    continue;
                }
                const int32_t* payload = reinterpret_cast<const int32_t*>(
                    reinterpret_cast<const char*>(boxed) + sizeof(il2cpp::Il2CppObject));
                Expect(*payload == i * 7, "boxed element value intact after collection");
            }
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&list));
    }

    // --- 5. Mixed churn: LCG pseudo-random add/insert/remove/set with a
    //    forced collection every few ops, verified against a std::vector
    //    model ---
    {
        il2cpp::Il2CppObject* list = il2cpp::il2cpp_arraylist_create();
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&list));
        std::vector<std::string> model;
        for (int32_t op = 0; op < 300; ++op)
        {
            const uint32_t r = NextRand();
            const std::string e = ElemName(static_cast<int32_t>(r % 50));
            switch (r % 4)
            {
            case 0: // add
                il2cpp::il2cpp_arraylist_add(list, Str(e.c_str()));
                model.push_back(e);
                break;
            case 1: // insert at random position
            {
                const int32_t pos = static_cast<int32_t>(NextRand() % (model.size() + 1));
                il2cpp::il2cpp_arraylist_insert(list, pos, Str(e.c_str()));
                model.insert(model.begin() + pos, e);
                break;
            }
            case 2: // remove_at random position (if any)
                if (!model.empty())
                {
                    const int32_t pos = static_cast<int32_t>(NextRand() % model.size());
                    il2cpp::il2cpp_arraylist_remove_at(list, pos);
                    model.erase(model.begin() + pos);
                }
                break;
            default: // set at random position (if any)
                if (!model.empty())
                {
                    const int32_t pos = static_cast<int32_t>(NextRand() % model.size());
                    il2cpp::il2cpp_arraylist_set_item(list, pos, Str(e.c_str()));
                    model[pos] = e;
                }
                break;
            }
            if (op % 7 == 6)
            {
                il2cpp::il2cpp_gc_collect();
            }
        }
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_arraylist_get_count(list) ==
                   static_cast<int32_t>(model.size()),
               "churn: count matches model after collections");
        bool churnMatches = true;
        for (size_t i = 0; i < model.size(); ++i)
        {
            if (!IsContent(il2cpp::il2cpp_arraylist_get_item(list, static_cast<int32_t>(i)),
                           model[i].c_str()))
            {
                churnMatches = false;
                break;
            }
        }
        Expect(churnMatches, "churn: every model element resolves by content");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&list));
    }

    if (g_failures == 0)
    {
        std::printf("all arraylist gc-stress tests passed\n");
        return 0;
    }
    std::printf("%d arraylist gc-stress test(s) FAILED\n", g_failures);
    return 1;
}
