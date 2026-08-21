// test_hashtable_gcstress.cpp -- GC-stress suite for the managed non-generic
// hashtable (System.Collections.Hashtable semantics, M4 icall domain).
//
// The collector is EXPLICIT-ONLY (gc.cpp: "directly (il2cpp_gc_collect) for
// explicit/forced collection"): allocation never auto-triggers a collection,
// so no collection can fire in the middle of a single icall. The GC-safety
// contract under test is therefore the CROSS-CALL one: a rooted table whose
// bucket array, entry chains, keys and values are reachable ONLY through the
// table must survive repeated full collections (with Gen0 compaction moving
// every survivor), and every operation (add/get/remove/keys/values) must
// still resolve correctly afterwards -- including across multiple RESIZES
// (the load factor 0.72 doubling rehashes every entry from its live key).
//
// Sections:
//   1. Add + rehash under GC pressure: 200 string-key entries (crossing
//      several resize thresholds), a forced collection every 10 adds, then
//      every entry still retrievable by content.
//   2. Get under GC pressure: a rooted populated table survives repeated
//      collections; lookups resolve by content (move-safe).
//   3. Remove under GC pressure: removals interleaved with collections;
//      removed keys stay gone, remaining entries stay intact.
//   4. Keys/Values snapshots under GC pressure: a snapshot taken after a
//      collection still lists every entry (same order, content-equal).
//   5. Mixed churn: LCG pseudo-random add/get/remove with a forced collection
//      every few ops, verified against a std::map model.
//
// GC discipline (the compacting collector MOVES Gen0 survivors and rewrites
// every reference): the table is registered as a static root for the whole
// test; every post-collection access re-reads the table through the root and
// uses FRESH lookups with move-safe CONTENT equality -- never cached C++
// pointers.

#include "il2cpp_gc.h"
#include "il2cpp_hashtable.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <map>
#include <string>

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

// Create a hashtable through the icall path (managed allocation + capacity
// ctor), mirroring what generated newobj/ctor code does.
il2cpp::Il2CppHashtable* MakeHashtable(int32_t capacity)
{
    il2cpp::Il2CppHashtable* h = reinterpret_cast<il2cpp::Il2CppHashtable*>(
        il2cpp::il2cpp_object_new(il2cpp::il2cpp_hashtable_class()));
    il2cpp::runtime::icall_hashtable_ctor(reinterpret_cast<il2cpp::Il2CppObject*>(h), capacity);
    return h;
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
uint32_t g_lcg = 0x12345678u;
uint32_t NextRand()
{
    g_lcg = g_lcg * 1664525u + 1013904223u;
    return g_lcg;
}

// Build "k<decimal>" / "v<decimal>" ASCII keys/values.
std::string KeyName(int32_t i)
{
    return "k" + std::to_string(i);
}
std::string ValueName(int32_t i)
{
    return "v" + std::to_string(i);
}

} // namespace

int main()
{
    // --- 1. Add + rehash under GC pressure: 200 entries, a forced
    //    collection every 10 adds, then every entry still retrievable ---
    {
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(MakeHashtable(0));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&hobj));
        for (int32_t i = 0; i < 200; ++i)
        {
            const std::string k = KeyName(i);
            const std::string v = ValueName(i);
            il2cpp::runtime::icall_hashtable_add(hobj, Str(k.c_str()), Str(v.c_str()));
            if (i % 10 == 9)
            {
                il2cpp::il2cpp_gc_collect(); // may move the table + entries
            }
        }
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 200,
               "200 entries survive repeated collections + resizes (count)");
        for (int32_t i = 0; i < 200; ++i)
        {
            const std::string k = KeyName(i);
            const std::string v = ValueName(i);
            Expect(IsContent(il2cpp::runtime::icall_hashtable_get(hobj, Str(k.c_str())), v.c_str()),
                   "entry survives rehash + collection (content)");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&hobj));
    }

    // --- 2. Get under GC pressure: a rooted populated table survives
    //    repeated collections; lookups resolve by content ---
    {
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(MakeHashtable(0));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&hobj));
        for (int32_t i = 0; i < 50; ++i)
        {
            const std::string k = KeyName(i);
            const std::string v = ValueName(i);
            il2cpp::runtime::icall_hashtable_add(hobj, Str(k.c_str()), Str(v.c_str()));
        }
        for (int32_t round = 0; round < 5; ++round)
        {
            il2cpp::il2cpp_gc_collect();
            for (int32_t i = 0; i < 50; ++i)
            {
                const std::string k = KeyName(i);
                const std::string v = ValueName(i);
                Expect(IsContent(il2cpp::runtime::icall_hashtable_get(hobj, Str(k.c_str())), v.c_str()),
                       "lookup resolves after collection (content)");
            }
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&hobj));
    }

    // --- 3. Remove under GC pressure: removals interleaved with
    //    collections; removed keys stay gone, remaining stay intact ---
    {
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(MakeHashtable(0));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&hobj));
        for (int32_t i = 0; i < 100; ++i)
        {
            const std::string k = KeyName(i);
            const std::string v = ValueName(i);
            il2cpp::runtime::icall_hashtable_add(hobj, Str(k.c_str()), Str(v.c_str()));
        }
        for (int32_t i = 0; i < 100; i += 2)
        {
            const std::string k = KeyName(i);
            il2cpp::runtime::icall_hashtable_remove(hobj, Str(k.c_str()));
            if (i % 20 == 18)
            {
                il2cpp::il2cpp_gc_collect();
            }
        }
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 50,
               "removed entries stay removed across collections (count)");
        for (int32_t i = 0; i < 100; ++i)
        {
            const std::string k = KeyName(i);
            const std::string v = ValueName(i);
            if (i % 2 == 0)
            {
                Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str(k.c_str())) == nullptr,
                       "removed key stays gone (null lookup)");
            }
            else
            {
                Expect(IsContent(il2cpp::runtime::icall_hashtable_get(hobj, Str(k.c_str())), v.c_str()),
                       "remaining entry stays intact (content)");
            }
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&hobj));
    }

    // --- 4. Keys/Values snapshots under GC pressure: a snapshot taken
    //    after a collection still lists every entry (same order) ---
    {
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(MakeHashtable(0));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&hobj));
        for (int32_t i = 0; i < 40; ++i)
        {
            const std::string k = KeyName(i);
            const std::string v = ValueName(i);
            il2cpp::runtime::icall_hashtable_add(hobj, Str(k.c_str()), Str(v.c_str()));
        }
        il2cpp::il2cpp_gc_collect();
        il2cpp::Il2CppArray* keys = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::icall_hashtable_get_keys(hobj));
        il2cpp::Il2CppArray* vals = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::icall_hashtable_get_values(hobj));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keys));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&vals));
        il2cpp::il2cpp_gc_collect(); // snapshots must survive a collection too
        Expect(keys->length == 40 && vals->length == 40,
               "snapshots list every entry after a collection (length)");
        bool orderMatches = true;
        for (int32_t i = 0; i < 40; ++i)
        {
            il2cpp::Il2CppObject** kslot = reinterpret_cast<il2cpp::Il2CppObject**>(keys->data) + i;
            il2cpp::Il2CppObject** vslot = reinterpret_cast<il2cpp::Il2CppObject**>(vals->data) + i;
            // Keys[i] and Values[i] describe the same entry: the value must
            // be the "v<key-number>" of the key's number.
            const il2cpp::Il2CppString* ks = reinterpret_cast<const il2cpp::Il2CppString*>(*kslot);
            if (ks == nullptr || ks->length < 1 || ks->chars[0] != u'k')
            {
                orderMatches = false;
                break;
            }
            int32_t num = 0;
            for (int32_t c = 1; c < ks->length; ++c)
            {
                num = num * 10 + (ks->chars[c] - u'0');
            }
            const std::string expectV = ValueName(num);
            if (!IsContent(*vslot, expectV.c_str()))
            {
                orderMatches = false;
                break;
            }
        }
        Expect(orderMatches, "Keys[i]/Values[i] describe the same entry after a collection");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&vals));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&keys));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&hobj));
    }

    // --- 5. Mixed churn: LCG pseudo-random add/get/remove with a forced
    //    collection every few ops, verified against a std::map model ---
    {
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(MakeHashtable(0));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&hobj));
        std::map<std::string, std::string> model;
        for (int32_t op = 0; op < 300; ++op)
        {
            const uint32_t r = NextRand();
            const int32_t key = static_cast<int32_t>(r % 60);
            const std::string k = KeyName(key);
            const std::string v = ValueName(key);
            switch (r % 3)
            {
            case 0: // add (replace semantics via set)
                il2cpp::runtime::icall_hashtable_set(hobj, Str(k.c_str()), Str(v.c_str()));
                model[k] = v;
                break;
            case 1: // get
            {
                il2cpp::Il2CppObject* got = il2cpp::runtime::icall_hashtable_get(hobj, Str(k.c_str()));
                const auto it = model.find(k);
                if (it == model.end())
                {
                    Expect(got == nullptr, "churn: missing key stays null");
                }
                else
                {
                    Expect(IsContent(got, it->second.c_str()), "churn: get matches model");
                }
                break;
            }
            default: // remove
                il2cpp::runtime::icall_hashtable_remove(hobj, Str(k.c_str()));
                model.erase(k);
                break;
            }
            if (op % 7 == 6)
            {
                il2cpp::il2cpp_gc_collect();
            }
        }
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) ==
                   static_cast<int32_t>(model.size()),
               "churn: count matches model after collections");
        for (const auto& kv : model)
        {
            Expect(IsContent(il2cpp::runtime::icall_hashtable_get(hobj, Str(kv.first.c_str())),
                             kv.second.c_str()),
                   "churn: every model entry resolves by content");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&hobj));
    }

    if (g_failures == 0)
    {
        std::printf("all hashtable gc-stress tests passed\n");
        return 0;
    }
    std::printf("%d hashtable gc-stress test(s) FAILED\n", g_failures);
    return 1;
}
