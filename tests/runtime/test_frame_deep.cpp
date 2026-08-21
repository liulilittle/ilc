//------------------------------------------------------------------------------
// test_frame_deep.cpp -- managed frame-stack stress test (deep recursion).
//
// The frame protocol (il2cpp_frame.h) makes a method's reference locals GC
// roots while the method is on the call stack: each frame carries the
// ADDRESSES of the reference slots, the collector marks *slot, and after a
// compacting collection Phase C rewrites every frame slot through the forward
// map (gc.cpp:585-600, 682-692). This test drives the protocol to its limits:
//
//   1. Deep recursion: one frame + one heap object per level (kDepth levels),
//      a full collection with the ENTIRE chain live, then an unwind that
//      verifies every level's object survived, its payload is intact, and its
//      frame slot was rewritten to the promoted (moved) copy.
//   2. Two mid-stack collections: Gen0 -> Gen1 -> Gen2 promotion with the
//      chain live; every level's object ends at Gen2 with payload intact.
//   3. Multi-slot frames: 2 objects + 1 null slot; all three slots survive
//      collection intact (the null slot stays null -- ForwardLookup(null)
//      returns null).
//   4. Frames with refs == nullptr / refCount 0 (collector skips them):
//      objects rooted by a real frame still survive; unrooted objects are
//      still reclaimed; the heap stays usable.
//   5. RAII unwind: a throw at the bottom of kDepth nested frames -- every
//      frame pops through its destructor (LIFO), balance returns to zero,
//      the frame stack is empty, and the heap stays usable.
//   6. Allocation pressure: a single rooted frame survives many implicit
//      alloc-triggered collections (block table saturation) while thousands
//      of dead objects churn.
//
// Compaction-aware assertions: with the M3 moving collector, a surviving
// Gen0 object is COPIED to a fresh Gen1 segment and the frame slot is
// rewritten; correctness is proven by content (klass + payload), never by
// stale C++ local addresses (a C++ local is not a GC root).
//------------------------------------------------------------------------------

#include "il2cpp_frame.h"
#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"

#include <cstdint>
#include <cstdio>
#include <stdexcept>

namespace {

static_assert(sizeof(void*) == 8, "frame test fixture assumes 8-byte pointers");

// Number of failed assertions accumulated by Expect(); main() returns
// non-zero when this counter is not zero (CI observes the exit code).
int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Fixture class: a 24-byte leaf object (no reference fields). Payload slot 1
// (byte offset 8) carries an int32 id.
il2cpp::Il2CppClass g_nodeClass = {
    "Test.DeepNode", // name
    24,              // instanceSize: header + 2 payload slots (24B)
    0u,              // refFieldBitmap: no reference fields (leaf)
    0,               // refFieldCount
    nullptr,         // baseClass
    nullptr,         // vtable
    0,               // vtableSize
    nullptr,         // interfaces
    0,               // interfaceCount
};

int32_t* IdSlot(il2cpp::Il2CppObject* obj)
{
    return reinterpret_cast<int32_t*>(reinterpret_cast<char*>(obj) + 8);
}

// 5000 overflows the default 1MB C++ stack at -O0 (guard-page AV); 4900
// passes. 4000 keeps ~20% margin and matches the C# recurse.cs alloc depth.
const int32_t kDepth = 4000; // recursion depth (frame chain length)
// Sum of 0..kDepth-1 -- computed from kDepth so the checksum scales with it.
const int64_t kExpectedSum = 1LL * kDepth * (kDepth - 1) / 2;

//------------------------------------------------------------------------------
// 1. Deep recursion with one frame + one object per level and a single
//    mid-stack collection at the bottom (all kDepth frames live).
//------------------------------------------------------------------------------
int64_t Descend(int32_t n)
{
    il2cpp::Il2CppObject* obj = il2cpp::il2cpp_gc_alloc(&g_nodeClass);
    *IdSlot(obj) = n;
    // Capture the pre-collection address for the "moved" assertion below.
    // This is a plain C++ local: NEVER dereferenced after the collection.
    il2cpp::Il2CppObject* const oldAddr = obj;
    void* refs[1] = { &obj };
    il2cpp::Il2CppFrame frame = { nullptr, "test_frame_deep", refs, 1 };
    il2cpp::il2cpp_frame_push(&frame);

    int64_t rest = 0;
    if (n == 0)
    {
        // Bottom of the recursion: the whole chain is live.
        Expect(il2cpp::il2cpp_frame_top() == &frame,
               "innermost frame is the stack top");
        Expect(il2cpp::il2cpp_frame_balance() == kDepth,
               "all nested frames are live at the bottom of the recursion");
        int32_t chainLen = 0;
        for (il2cpp::Il2CppFrame* f = il2cpp::il2cpp_frame_top(); f != nullptr; f = f->prev)
        {
            ++chainLen;
        }
        Expect(chainLen == kDepth,
               "frame chain length matches the recursion depth");
        il2cpp::il2cpp_gc_collect(); // mid-stack collection
    }
    else
    {
        rest = Descend(n - 1);
    }

    // After the collection the frame slot was rewritten by Phase C: obj now
    // holds the promoted copy's address. Every level's object was Gen0 at the
    // collection, so every level's slot must have been rewritten.
    Expect(obj != nullptr && obj->klass == &g_nodeClass,
           "level object survives the deep frame collection");
    Expect(*IdSlot(obj) == n,
           "level payload intact after the deep frame collection");
    Expect(obj != oldAddr,
           "frame slot rewritten to the promoted (moved) copy");
    il2cpp::il2cpp_frame_pop(&frame);
    return rest + n;
}

//------------------------------------------------------------------------------
// 2. Deep recursion with TWO mid-stack collections: Gen0 -> Gen1 -> Gen2.
//    (Gen1/Gen2 survivors are NOT moved; the second collection is a
//    no-move cycle -- Phase C must be idempotent.)
//------------------------------------------------------------------------------
int64_t DescendTwice(int32_t n)
{
    il2cpp::Il2CppObject* obj = il2cpp::il2cpp_gc_alloc(&g_nodeClass);
    *IdSlot(obj) = n + 100000; // distinct payload range
    void* refs[1] = { &obj };
    il2cpp::Il2CppFrame frame = { nullptr, "test_frame_deep_twice", refs, 1 };
    il2cpp::il2cpp_frame_push(&frame);

    int64_t rest = 0;
    if (n == 0)
    {
        il2cpp::il2cpp_gc_collect();
        il2cpp::il2cpp_gc_collect();
    }
    else
    {
        rest = DescendTwice(n - 1);
    }

    Expect(obj != nullptr && obj->klass == &g_nodeClass,
           "level object survives two mid-stack collections");
    Expect(*IdSlot(obj) == n + 100000,
           "level payload intact after two mid-stack collections");
    Expect(il2cpp::il2cpp_gc_get_generation(obj) == il2cpp::kGcGeneration2,
           "objects promoted to Gen2 by two mid-stack collections");
    il2cpp::il2cpp_frame_pop(&frame);
    return rest + n;
}

//------------------------------------------------------------------------------
// 3. Multi-slot frame: two objects + one null slot.
//------------------------------------------------------------------------------
void MultiSlotFrame()
{
    il2cpp::Il2CppObject* a = nullptr;
    il2cpp::Il2CppObject* b = nullptr;
    il2cpp::Il2CppObject* c = nullptr;
    void* refs[3] = { &a, &b, &c };
    il2cpp::Il2CppFrame frame = { nullptr, "test_frame_deep_multi", refs, 3 };
    il2cpp::il2cpp_frame_push(&frame);
    a = il2cpp::il2cpp_gc_alloc(&g_nodeClass);
    b = il2cpp::il2cpp_gc_alloc(&g_nodeClass);
    *IdSlot(a) = 111;
    *IdSlot(b) = 222;
    il2cpp::il2cpp_gc_collect();
    Expect(a != nullptr && a->klass == &g_nodeClass && *IdSlot(a) == 111,
           "multi-slot frame slot 0 survives collection");
    Expect(b != nullptr && b->klass == &g_nodeClass && *IdSlot(b) == 222,
           "multi-slot frame slot 1 survives collection");
    Expect(c == nullptr,
           "null slot in a multi-slot frame stays null through collection");
    il2cpp::il2cpp_frame_pop(&frame);
}

//------------------------------------------------------------------------------
// 4. Null-refs frame (refs == nullptr, refCount 0): skipped by the collector.
//------------------------------------------------------------------------------
void NullRefsFrame()
{
    // A frame that carries no reference slots -- the collector must skip it
    // without dereferencing refs.
    il2cpp::Il2CppFrame nullFrame = { nullptr, "test_frame_deep_nullrefs", nullptr, 0 };
    il2cpp::il2cpp_frame_push(&nullFrame);

    // A real frame on top of it roots one object...
    il2cpp::Il2CppObject* kept = nullptr;
    void* keptRefs[1] = { &kept };
    il2cpp::Il2CppFrame realFrame = { nullptr, "test_frame_deep_kept", keptRefs, 1 };
    il2cpp::il2cpp_frame_push(&realFrame);
    kept = il2cpp::il2cpp_gc_alloc(&g_nodeClass);
    *IdSlot(kept) = 42;

    // ...while an unrooted object must still be reclaimed.
    il2cpp::Il2CppObject* dead = il2cpp::il2cpp_gc_alloc(&g_nodeClass);
    il2cpp::il2cpp_gc_collect();
    Expect(kept != nullptr && kept->klass == &g_nodeClass && *IdSlot(kept) == 42,
           "object rooted by a real frame survives with a null-refs frame on the stack");
    il2cpp::Il2CppObject* probe = il2cpp::il2cpp_gc_alloc(&g_nodeClass);
    Expect(probe == dead,
           "unrooted object is reclaimed while a null-refs frame is on the stack");

    // Unwind LIFO: real frame first, then the null-refs frame.
    il2cpp::il2cpp_frame_pop(&realFrame);
    il2cpp::il2cpp_frame_pop(&nullFrame);
}

//------------------------------------------------------------------------------
// 5. RAII unwind: throw at the bottom of the recursion -- every frame pops
//    through its destructor during unwinding; balance returns to zero.
//------------------------------------------------------------------------------
void ThrowingDescend(int32_t n)
{
    il2cpp::Il2CppObject* obj = il2cpp::il2cpp_gc_alloc(&g_nodeClass);
    *IdSlot(obj) = n;
    void* refs[1] = { &obj };
    il2cpp::Il2CppFrame frame = { nullptr, "test_frame_deep_throw", refs, 1 };
    il2cpp::il2cpp_frame_push(&frame);
    if (n == 0)
    {
        il2cpp::il2cpp_gc_collect(); // collection, then unwind
        throw std::runtime_error("frame unwind");
    }
    ThrowingDescend(n - 1);
    // Never reached while unwinding: the destructor pops the frame. On the
    // normal path n==0 always throws, so this line is never executed either.
    il2cpp::il2cpp_frame_pop(&frame);
}

void ExceptionUnwind()
{
    bool caught = false;
    try
    {
        ThrowingDescend(kDepth - 1);
    }
    catch (const std::runtime_error&)
    {
        caught = true;
    }
    Expect(caught,
           "exception propagates through kDepth nested frames");
    Expect(il2cpp::il2cpp_frame_balance() == 0,
           "RAII unwind pops every frame (balance back to zero)");
    Expect(il2cpp::il2cpp_frame_top() == nullptr,
           "frame stack empty after exception unwind");
    // The unwound objects are garbage now; the heap must stay usable.
    il2cpp::il2cpp_gc_collect();
    il2cpp::Il2CppObject* post = il2cpp::il2cpp_gc_alloc(&g_nodeClass);
    Expect(post != nullptr && post->klass == &g_nodeClass,
           "heap accepts allocation after exception unwind");
}

//------------------------------------------------------------------------------
// 6. Allocation pressure: one rooted frame survives many implicit
//    alloc-triggered collections while thousands of dead objects churn.
//    (kMaxBlocks = 16384 live blocks; 30000 churn allocations force several
//    collection cycles.)
//------------------------------------------------------------------------------
void AllocationPressure()
{
    il2cpp::Il2CppObject* a = nullptr;
    il2cpp::Il2CppObject* b = nullptr;
    void* refs[2] = { &a, &b };
    il2cpp::Il2CppFrame frame = { nullptr, "test_frame_deep_pressure", refs, 2 };
    il2cpp::il2cpp_frame_push(&frame);
    a = il2cpp::il2cpp_gc_alloc(&g_nodeClass);
    b = il2cpp::il2cpp_gc_alloc(&g_nodeClass);
    *IdSlot(a) = 1;
    *IdSlot(b) = 2;
    for (int i = 0; i < 30000; ++i)
    {
        il2cpp::Il2CppObject* dead = il2cpp::il2cpp_gc_alloc(&g_nodeClass);
        *IdSlot(dead) = i;
    }
    Expect(a != nullptr && a->klass == &g_nodeClass && *IdSlot(a) == 1,
           "rooted slot A survives allocation-pressure collections");
    Expect(b != nullptr && b->klass == &g_nodeClass && *IdSlot(b) == 2,
           "rooted slot B survives allocation-pressure collections");
    Expect(il2cpp::il2cpp_gc_get_generation(a) >= il2cpp::kGcGeneration1,
           "rooted object was promoted by an implicit collection (pressure was real)");
    il2cpp::il2cpp_frame_pop(&frame);
}

} // namespace

//------------------------------------------------------------------------------
// main -- run every frame-protocol invariant and report the outcome.
//------------------------------------------------------------------------------
int main()
{
    // Unbuffered stdio: when the process is run with redirected output (the
    // test harness), MSVCRT buffers stdout/stderr and a crash discards the
    // buffered diagnostics -- setvbuf(_IONBF) keeps every checkpoint visible.
    std::setvbuf(stdout, nullptr, _IONBF, 0);
    std::setvbuf(stderr, nullptr, _IONBF, 0);

    Expect(il2cpp::il2cpp_frame_balance() == 0,
           "frame stack is balanced before any test section");

    // --- 1. Deep recursion, single mid-stack collection ---
    int64_t total = Descend(kDepth - 1);
    Expect(total == kExpectedSum,
           "sum of all level payloads after the deep frame collection");
    Expect(il2cpp::il2cpp_frame_balance() == 0,
           "deep recursion unwinds to a balanced frame stack");

    // --- 2. Two mid-stack collections (Gen0 -> Gen1 -> Gen2) ---
    int64_t total2 = DescendTwice(kDepth - 1);
    Expect(total2 == kExpectedSum,
           "double-collection payload checksum matches");
    Expect(il2cpp::il2cpp_frame_balance() == 0,
           "double-collection recursion unwinds balanced");

    // --- 3. Multi-slot frame ---
    MultiSlotFrame();
    Expect(il2cpp::il2cpp_frame_balance() == 0,
           "multi-slot frame test unwinds balanced");

    // --- 4. Null-refs frame ---
    NullRefsFrame();
    Expect(il2cpp::il2cpp_frame_balance() == 0,
           "null-refs frame test unwinds balanced");

    // --- 5. RAII unwind ---
    ExceptionUnwind();

    // --- 6. Allocation pressure ---
    AllocationPressure();
    Expect(il2cpp::il2cpp_frame_balance() == 0,
           "allocation-pressure section unwinds balanced");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d frame-deep assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime frame-deep tests passed\n");
    return 0;
}
