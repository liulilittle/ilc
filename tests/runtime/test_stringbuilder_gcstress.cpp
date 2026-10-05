//------------------------------------------------------------------------------
// test_stringbuilder_gcstress.cpp -- GC-stress tests for the StringBuilder
// icall domain (M5 BCL). Deep-testing companion to test_stringbuilder.cpp:
// where that suite verifies net48 SEMANTICS, this suite verifies GC SAFETY
// under collection pressure -- the exact latent-risk area flagged by the
// #114 analysis (docs/plans/issue-114-analysis.md): the ctor_string buffer
// store (stringbuilder.cpp:632), the AppendUnits/InsertUnits rooting paths,
// and the growth (buffer-replacement) path.
//
// The collector under test (gc.cpp) performs full-heap mark-sweep WITH
// young-generation compaction: every surviving Gen0 block is COPIED to a
// fresh Gen1 segment and every reference (static roots, root ranges, object
// fields) is rewritten through the forward map (Phase C). Two consequences
// drive this harness:
//
//   * Object addresses are NOT stable across collections. Every pointer is
//     re-read through a REGISTERED ROOT SLOT after a collection, never
//     through a cached C++ local.
//   * The StringBuilder's 'chars' buffer is a managed array (refFieldBitmap
//     slot 2). A collection can COPY the builder AND its buffer; the content
//     must survive intact.
//
// Coverage (normal + edge + error paths):
//   1. ctor_string under GC pressure: build a builder from a long string,
//      then force collections while the builder AND the source string are
//      rooted; verify length + content after each collection. Exercises the
//      flagged ctor_string buffer store (stringbuilder.cpp:632).
//   2. Growth under GC pressure: start with a small capacity and append
//      repeatedly so the buffer is REPLACED (GrowBuffer allocates a fresh
//      array) while collections fire between appends; verify the content
//      survives every replacement. Exercises the AppendUnits rooting path.
//   3. Insert under GC pressure: insert into a builder while collections
//      fire; verify the spliced content survives. Exercises the InsertUnits
//      rooting path (the batch-170 bug family).
//   4. Replace under GC pressure: replace occurrences while collections
//      fire; verify the replaced content survives.
//   5. Aliasing: insert a string that is a ToString() snapshot of the SAME
//      builder (the snapshot is a fresh managed string -- the units pointer
//      must stay valid across the growth allocation inside InsertUnits).
//   6. Mixed-operation churn: a deterministic pseudo-random sequence of
//      append/insert/remove/replace with a collection after every few
//      operations; verify the final content matches the expected model.
//   7. Rooted builder survives a collection with its buffer intact (the
//      refFieldBitmap slot-2 scan) -- the test_stringbuilder.cpp section-13
//      invariant, repeated under heavier pressure.
//
// Plain executable + minimal assertion harness (see test_string.cpp): any
// failed Expect() prints FAIL and bumps the counter; the process exits
// non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_stringbuilder.h"

#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

namespace {

static_assert(sizeof(void*) == 8, "stringbuilder gc-stress fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Expect 'obj' (a managed string) to hold exactly the ASCII literal 'ascii'.
void ExpectString(il2cpp::Il2CppObject* obj, const char* ascii, const char* what)
{
    const il2cpp::Il2CppString* s = reinterpret_cast<const il2cpp::Il2CppString*>(obj);
    const size_t len = std::strlen(ascii);
    if (s == nullptr)
    {
        Expect(false, what);
        return;
    }
    if (static_cast<size_t>(s->length) != len)
    {
        Expect(false, what);
        return;
    }
    bool match = true;
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            match = false;
            break;
        }
    }
    Expect(match, what);
}

// Allocate a fresh StringBuilder via the generated-code shape.
il2cpp::Il2CppStringBuilder* NewDefault()
{
    il2cpp::Il2CppObject* sb = il2cpp::il2cpp_object_new(il2cpp::il2cpp_stringbuilder_class());
    il2cpp::runtime::icall_stringbuilder_ctor_default(sb);
    return reinterpret_cast<il2cpp::Il2CppStringBuilder*>(sb);
}

il2cpp::Il2CppStringBuilder* NewCapacity(int32_t capacity)
{
    il2cpp::Il2CppObject* sb = il2cpp::il2cpp_object_new(il2cpp::il2cpp_stringbuilder_class());
    il2cpp::runtime::icall_stringbuilder_ctor_capacity(sb, capacity);
    return reinterpret_cast<il2cpp::Il2CppStringBuilder*>(sb);
}

il2cpp::Il2CppStringBuilder* NewString(il2cpp::Il2CppObject* value)
{
    il2cpp::Il2CppObject* sb = il2cpp::il2cpp_object_new(il2cpp::il2cpp_stringbuilder_class());
    il2cpp::runtime::icall_stringbuilder_ctor_string(sb, value);
    return reinterpret_cast<il2cpp::Il2CppStringBuilder*>(sb);
}

// Allocate a managed string from an ASCII literal, returned as Il2CppObject*
// (Il2CppString embeds Il2CppObject as its first member -- explicit cast).
il2cpp::Il2CppObject* NewManagedString(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

// Deterministic pseudo-random source (LCG; identical sequence on every
// platform -- the stress pattern must be reproducible).
uint64_t g_rng = 0x243F6A8885A308D3ull;

uint32_t NextRand()
{
    g_rng = g_rng * 6364136223846793005ull + 1442695040888963407ull;
    return static_cast<uint32_t>(g_rng >> 32);
}

} // namespace

int main()
{
    using il2cpp::Il2CppObject;
    using il2cpp::Il2CppStringBuilder;
    using il2cpp::runtime::icall_stringbuilder_ctor_string;
    using il2cpp::runtime::icall_stringbuilder_append_s;
    using il2cpp::runtime::icall_stringbuilder_append_c;
    using il2cpp::runtime::icall_stringbuilder_append_i;
    using il2cpp::runtime::icall_stringbuilder_insert;
    using il2cpp::runtime::icall_stringbuilder_remove;
    using il2cpp::runtime::icall_stringbuilder_replace_s_s;
    using il2cpp::runtime::icall_stringbuilder_to_string;

    //--- 1. ctor_string under GC pressure (the flagged stringbuilder.cpp:632
    //        buffer store) ---
    // Build a builder from a long string, root BOTH the builder and the
    // source string, then force collections. The builder's buffer must
    // survive every collection with the full content intact.
    {
        const char* src = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        Il2CppObject* srcStr = NewManagedString(src);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&srcStr));
        Il2CppStringBuilder* sb = NewString(srcStr);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sb));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(sb)), src,
                         "ctor_string builder survives collections with full content");
            Expect(sb->chars != nullptr && sb->chars->length == sb->capacity,
                   "ctor_string buffer array survives collections (slot-2 scan)");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sb));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&srcStr));
    }

    //--- 2. Growth under GC pressure (the AppendUnits rooting path) ---
    // Start with a tiny capacity and append repeatedly so the buffer is
    // REPLACED (GrowBuffer allocates a fresh array) while collections fire
    // between appends. The content must survive every replacement.
    {
        Il2CppStringBuilder* sb = NewCapacity(2);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sb));
        std::string model;
        for (int32_t i = 0; i < 40; ++i)
        {
            char piece[2] = { static_cast<char>('a' + (i % 26)), '\0' };
            (void)icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(sb),
                                               reinterpret_cast<Il2CppObject*>(NewManagedString(piece)));
            model += piece;
            if (i % 3 == 0)
            {
                il2cpp::il2cpp_gc_collect();
            }
            char buf[64];
            std::snprintf(buf, sizeof(buf), "growth append %d survives collections", i);
            ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(sb)), model.c_str(), buf);
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sb));
    }

    //--- 3. Insert under GC pressure (the InsertUnits rooting path, the
    //        batch-170 bug family) ---
    {
        Il2CppStringBuilder* sb = NewString(reinterpret_cast<Il2CppObject*>(NewManagedString("Hlo")));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sb));
        std::string model = "Hlo";
        for (int32_t i = 0; i < 20; ++i)
        {
            (void)icall_stringbuilder_insert(reinterpret_cast<Il2CppObject*>(sb), 1,
                                             reinterpret_cast<Il2CppObject*>(NewManagedString("el")));
            model.insert(1, "el");
            if (i % 2 == 0)
            {
                il2cpp::il2cpp_gc_collect();
            }
        }
        ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(sb)),
                     model.c_str(),
                     "repeated insert survives collections (content intact)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sb));
    }

    //--- 4. Replace under GC pressure ---
    {
        Il2CppStringBuilder* sb = NewString(reinterpret_cast<Il2CppObject*>(NewManagedString("abababab")));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sb));
        for (int32_t i = 0; i < 20; ++i)
        {
            (void)icall_stringbuilder_replace_s_s(reinterpret_cast<Il2CppObject*>(sb),
                                                  reinterpret_cast<Il2CppObject*>(NewManagedString("ab")),
                                                  reinterpret_cast<Il2CppObject*>(NewManagedString("cd")));
            if (i % 2 == 0)
            {
                il2cpp::il2cpp_gc_collect();
            }
        }
        ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(sb)),
                     "cdcdcdcd",
                     "replace survives collections (content intact)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sb));
    }

    //--- 5. Aliasing: insert a ToString() snapshot of the SAME builder ---
    // The snapshot is a fresh managed string; the units pointer must stay
    // valid across the growth allocation inside InsertUnits.
    {
        Il2CppStringBuilder* sb = NewString(reinterpret_cast<Il2CppObject*>(NewManagedString("abc")));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sb));
        Il2CppObject* snap = icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(sb));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&snap));
        il2cpp::il2cpp_gc_collect();
        (void)icall_stringbuilder_insert(reinterpret_cast<Il2CppObject*>(sb), 0, snap);
        ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(sb)),
                     "abcabc",
                     "inserting a ToString snapshot of the same builder works");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&snap));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sb));
    }

    //--- 6. Mixed-operation churn with collections ---
    // A deterministic pseudo-random sequence of append/insert/remove/replace
    // with a collection after every few operations; verify the final content
    // matches the expected model.
    {
        Il2CppStringBuilder* sb = NewDefault();
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sb));
        // Model the content as a C++ std::string.
        std::string model;
        for (int32_t step = 0; step < 200; ++step)
        {
            const uint32_t op = NextRand() % 4u;
            if (op == 0)
            {
                char piece[2] = { static_cast<char>('a' + (NextRand() % 26u)), '\0' };
                (void)icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(sb),
                                                   reinterpret_cast<Il2CppObject*>(NewManagedString(piece)));
                model += piece;
            }
            else if (op == 1)
            {
                char piece[2] = { static_cast<char>('A' + (NextRand() % 26u)), '\0' };
                int32_t idx = static_cast<int32_t>(NextRand() % (model.size() + 1));
                (void)icall_stringbuilder_insert(reinterpret_cast<Il2CppObject*>(sb), idx,
                                                 reinterpret_cast<Il2CppObject*>(NewManagedString(piece)));
                model.insert(static_cast<size_t>(idx), piece);
            }
            else if (op == 2)
            {
                if (model.size() > 0)
                {
                    int32_t idx = static_cast<int32_t>(NextRand() % model.size());
                    (void)icall_stringbuilder_remove(reinterpret_cast<Il2CppObject*>(sb), idx, 1);
                    model.erase(static_cast<size_t>(idx), 1);
                }
            }
            else
            {
                char oldc[2] = { static_cast<char>('a' + (NextRand() % 26u)), '\0' };
                char newc[2] = { static_cast<char>('a' + (NextRand() % 26u)), '\0' };
                (void)icall_stringbuilder_replace_s_s(reinterpret_cast<Il2CppObject*>(sb),
                                                      reinterpret_cast<Il2CppObject*>(NewManagedString(oldc)),
                                                      reinterpret_cast<Il2CppObject*>(NewManagedString(newc)));
                for (size_t i = 0; i < model.size(); ++i)
                {
                    if (model[i] == oldc[0])
                    {
                        model[i] = newc[0];
                    }
                }
            }
            if (step % 5 == 0)
            {
                il2cpp::il2cpp_gc_collect();
            }
        }
        ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(sb)),
                     model.c_str(),
                     "mixed-operation churn matches the expected model after collections");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sb));
    }

    //--- 7. Rooted builder survives a collection with its buffer intact ---
    // The test_stringbuilder.cpp section-13 invariant, repeated under heavier
    // pressure (multiple collections + a growth in between).
    {
        Il2CppStringBuilder* sb = NewCapacity(4);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sb));
        (void)icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(sb),
                                           reinterpret_cast<Il2CppObject*>(NewManagedString("0123456789")));
        il2cpp::il2cpp_gc_collect();
        ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(sb)), "0123456789",
                     "rooted builder survives a collection with its content intact");
        Expect(sb->chars != nullptr && sb->chars->length == sb->capacity,
               "the char buffer array survived the collection (refFieldBitmap slot 2 scan)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sb));
    }

    //--- Report ---
    if (g_failures != 0)
    {
        std::printf("%d stringbuilder gc-stress assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all stringbuilder gc-stress tests passed\n");
    return 0;
}
