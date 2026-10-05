//------------------------------------------------------------------------------
// test_gchandle.cpp -- runtime GCHandle domain unit tests (il2cpp_gchandle.h /
// gchandle.cpp): the net48 GCHandle handle semantics on the handle-table model.
//
// Coverage (normal + edge + error paths):
//   1. Alloc (handle from object): a Normal handle returns a positive handle
//      value with bit 0 clear (the netcore pin-flag convention); the target
//      round-trips through get_Target; get_HandleType reports Normal; the
//      one-argument Alloc(object) shorthand behaves identically.
//   2. Alloc type validation: an out-of-range GCHandleType throws
//      ArgumentOutOfRangeException; null target is legal.
//   3. Strong semantics (Normal): the handle KEEPS the referent alive across
//      collections -- the referent survives ONLY through the handle's static
//      root, and get_Target returns the CURRENT address after the Gen0
//      compaction rewrites the root slot.
//   4. Weak semantics: the handle is cleared when its referent dies;
//      WeakTrackResurrection is the documented Weak simplification.
//   5. Pinned + AddrOfPinnedObject: the address of the pinned object's data
//      (string -> &chars[0], array -> &data[0], plain object -> payload
//      after the header); a non-Pinned handle throws InvalidOperationException;
//      a null-target pinned handle returns 0.
//   6. Free: releasing a handle clears IsAllocated and makes every accessor
//      throw InvalidOperationException; double-free and garbage-handle frees
//      throw InvalidOperationException (net48 double-free semantics).
//   7. IsAllocated: true for a live handle, false after Free and for every
//      garbage value (0 / negative / odd).
//   8. SetTarget: re-targeting a strong handle keeps the NEW referent alive
//      (the old one dies) and is honored by a weak handle as well.
//   9. Handle table reuse: freeing and re-allocating yields a valid new
//      handle (the slot returns to the free list).
//
// GC-safety: objects that must survive a collection are registered as static
// roots FIRST and unregistered after the last use; objects under test for
// weak death are deliberately NOT rooted -- the handle is the only root (a
// Normal handle) or there is no root at all (a Weak handle), which is the
// whole point.
//------------------------------------------------------------------------------

#include "il2cpp_gchandle.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

// The fixtures use pointer-sized slots; verify the platform assumption once.
static_assert(sizeof(void*) == 8, "gchandle test fixture assumes 8-byte pointers");

// Number of failed assertions accumulated by Expect(). main() returns
// non-zero when this counter is not zero, so CI sees a red signal.
int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Fixture class: a 32-byte leaf object (no reference fields).
il2cpp::Il2CppClass g_leaf = {
    "Test.GcHandle.Leaf",
    32,
    0u, // refFieldBitmap: no reference fields
    0,
    nullptr,
    nullptr,
    0,
    nullptr,
    0,
};

// Did the DotNetException thrown by 'fn' carry the expected managed type?
// DotNetException::type_name() is the FULL base-chain string (exceptionext.h
// chain convention "{Type},{Type},{Base1},..."), so conformance uses the
// runtime matcher (il2cpp_exception_matches) -- exact OR base-chain walk.
template <typename Fn>
bool ThrowsAs(Fn fn, const char* typeName)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return il2cpp::il2cpp_exception_matches(typeName, e.type_name());
    }
    return false;
}

} // namespace

int main()
{
    //--- 1. Alloc (handle from object): encoding + get_Target round-trip ---
    {
        il2cpp::Il2CppObject* obj = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(obj, il2cpp::runtime::kGCHandleTypeNormal);
        Expect(h > 0, "alloc returns a positive handle");
        Expect((h & 1) == 0, "handle bit 0 is clear (netcore pin-flag convention)");
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) == obj,
               "get_Target round-trips the allocated referent");
        Expect(il2cpp::runtime::icall_gchandle_get_handle_type(h) == il2cpp::runtime::kGCHandleTypeNormal,
               "get_HandleType reports Normal for a Normal handle");
        Expect(il2cpp::runtime::icall_gchandle_is_allocated(h),
               "IsAllocated is true for a live handle");
        il2cpp::runtime::icall_gchandle_free(h);
    }

    //--- 1b. Alloc(object) one-argument shorthand is a Normal handle ---
    {
        il2cpp::Il2CppObject* obj = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc_normal(obj);
        Expect(il2cpp::runtime::icall_gchandle_get_handle_type(h) == il2cpp::runtime::kGCHandleTypeNormal,
               "Alloc(object) allocates a Normal handle");
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) == obj,
               "Alloc(object) stores the referent");
        il2cpp::runtime::icall_gchandle_free(h);
    }

    //--- 2. Alloc type validation + null target ---
    {
        Expect(ThrowsAs([] { il2cpp::runtime::icall_gchandle_alloc(nullptr, 4); },
                        "System.ArgumentOutOfRangeException"),
               "alloc with type above Pinned throws ArgumentOutOfRangeException");
        Expect(ThrowsAs([] { il2cpp::runtime::icall_gchandle_alloc(nullptr, -1); },
                        "System.ArgumentOutOfRangeException"),
               "alloc with a negative type throws ArgumentOutOfRangeException");
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(nullptr, il2cpp::runtime::kGCHandleTypeNormal);
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) == nullptr,
               "alloc(null) yields a handle whose target is null");
        il2cpp::runtime::icall_gchandle_free(h);
    }

    //--- 3. Strong semantics: the handle keeps the referent alive ---
    {
        il2cpp::Il2CppObject* obj = il2cpp::il2cpp_gc_alloc(&g_leaf);
        // Root the local so the test can compare against the collector's
        // CURRENT address after compaction (the root slot is rewritten).
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&obj));
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(obj, il2cpp::runtime::kGCHandleTypeNormal);
        // The handle is now the ONLY other reference; collect (compaction
        // MOVES the surviving Gen0 object and rewrites both root slots).
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) == obj,
               "Normal handle keeps its referent alive and follows compaction");
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) != nullptr &&
                   il2cpp::runtime::icall_gchandle_get_target(h)->klass == &g_leaf,
               "surviving referent is the intact test object");
        il2cpp::runtime::icall_gchandle_free(h);
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&obj));
    }

    //--- 4. Weak semantics: cleared when the referent dies ---
    {
        il2cpp::Il2CppObject* dead = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(dead, il2cpp::runtime::kGCHandleTypeWeak);
        // 'dead' is NOT rooted: the weak handle does not keep it alive.
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) == nullptr,
               "Weak handle is cleared when its referent dies");
        Expect(il2cpp::runtime::icall_gchandle_is_allocated(h),
               "cleared weak handle is still allocated (the handle outlives the target)");
        il2cpp::runtime::icall_gchandle_free(h);
    }

    //--- 4b. WeakTrackResurrection is the documented Weak simplification ---
    {
        il2cpp::Il2CppObject* dead = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(dead, il2cpp::runtime::kGCHandleTypeWeakTrackResurrection);
        Expect(il2cpp::runtime::icall_gchandle_get_handle_type(h) == il2cpp::runtime::kGCHandleTypeWeakTrackResurrection,
               "get_HandleType reports WeakTrackResurrection");
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) == nullptr,
               "WeakTrackResurrection follows the Weak simplification (cleared at death)");
        il2cpp::runtime::icall_gchandle_free(h);
    }

    //--- 5. Pinned + AddrOfPinnedObject ---
    {
        // Plain object: the data address is the first payload byte.
        il2cpp::Il2CppObject* obj = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(obj, il2cpp::runtime::kGCHandleTypePinned);
        const intptr_t addr = il2cpp::runtime::icall_gchandle_addr_of_pinned(h);
        Expect(addr == static_cast<intptr_t>(
                   reinterpret_cast<uintptr_t>(obj) + sizeof(il2cpp::Il2CppObject)),
               "AddrOfPinnedObject on a plain object returns the payload start");
        il2cpp::runtime::icall_gchandle_free(h);
    }
    {
        // String: the data address is &chars[0].
        il2cpp::Il2CppString* str = il2cpp::il2cpp_string_new_from_cs("abc");
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(reinterpret_cast<il2cpp::Il2CppObject*>(str),
                                                               il2cpp::runtime::kGCHandleTypePinned);
        Expect(il2cpp::runtime::icall_gchandle_addr_of_pinned(h) ==
                   static_cast<intptr_t>(reinterpret_cast<uintptr_t>(str->chars)),
               "AddrOfPinnedObject on a string returns &chars[0]");
        il2cpp::runtime::icall_gchandle_free(h);
    }
    {
        // Array: the data address is &data[0].
        il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(nullptr, 4, false, 5);
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(reinterpret_cast<il2cpp::Il2CppObject*>(arr),
                                                               il2cpp::runtime::kGCHandleTypePinned);
        Expect(il2cpp::runtime::icall_gchandle_addr_of_pinned(h) ==
                   static_cast<intptr_t>(reinterpret_cast<uintptr_t>(arr->data)),
               "AddrOfPinnedObject on an array returns &data[0]");
        il2cpp::runtime::icall_gchandle_free(h);
    }
    {
        // Non-pinned handle: AddrOfPinnedObject throws InvalidOperationException.
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc_normal(nullptr);
        Expect(ThrowsAs([h] { il2cpp::runtime::icall_gchandle_addr_of_pinned(h); },
                        "System.InvalidOperationException"),
               "AddrOfPinnedObject on a non-pinned handle throws InvalidOperationException");
        il2cpp::runtime::icall_gchandle_free(h);
    }
    {
        // Null-target pinned handle: address is 0 (netcore default).
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(nullptr, il2cpp::runtime::kGCHandleTypePinned);
        Expect(il2cpp::runtime::icall_gchandle_addr_of_pinned(h) == 0,
               "AddrOfPinnedObject on a null-target pinned handle returns 0");
        il2cpp::runtime::icall_gchandle_free(h);
    }
    {
        // 5b. Pinned address stability ACROSS a compacting collection: the
        // GCHandle.Pinned contract -- AddrOfPinnedObject returns an address
        // that stays valid until the pin is released, no matter how many
        // collections run in between (a COM/Win32 call marshaling a pinned
        // buffer while the callee allocates would otherwise write through a
        // stale address after the Gen0 compaction moved the array). The pin
        // is registered with the collector as IMMOVABLE, so the compaction
        // must promote it non-moving.
        il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(nullptr, 4, false, 5);
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(reinterpret_cast<il2cpp::Il2CppObject*>(arr),
                                                               il2cpp::runtime::kGCHandleTypePinned);
        const intptr_t pinnedAddr = il2cpp::runtime::icall_gchandle_addr_of_pinned(h);
        Expect(pinnedAddr == static_cast<intptr_t>(reinterpret_cast<uintptr_t>(arr->data)),
               "pinned array address is &data[0] before any collection");
        // Force the Gen0 compaction (allocating churn guarantees a real
        // collection). The pinned array must NOT move.
        for (int32_t i = 0; i < 8; ++i)
        {
            il2cpp::il2cpp_gc_collect();
        }
        Expect(il2cpp::runtime::icall_gchandle_addr_of_pinned(h) == pinnedAddr,
               "pinned address is stable across compacting collections (immovable pin)");
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) != nullptr,
               "pinned referent survives collections intact");
        // The pin still works after the handle is released (the slot is
        // unregistered, so the next collection may move the object again).
        il2cpp::runtime::icall_gchandle_free(h);
        il2cpp::il2cpp_gc_collect();
        Expect(!il2cpp::runtime::icall_gchandle_is_allocated(h),
               "freed pinned handle is not allocated");
    }

    //--- 6. Free: release + double-free + garbage-handle frees ---
    {
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc_normal(nullptr);
        il2cpp::runtime::icall_gchandle_free(h);
        Expect(!il2cpp::runtime::icall_gchandle_is_allocated(h),
               "IsAllocated is false after Free");
        Expect(ThrowsAs([h] { il2cpp::runtime::icall_gchandle_get_target(h); },
                        "System.InvalidOperationException"),
               "get_Target on a freed handle throws InvalidOperationException");
        Expect(ThrowsAs([h] { il2cpp::runtime::icall_gchandle_get_handle_type(h); },
                        "System.InvalidOperationException"),
               "get_HandleType on a freed handle throws InvalidOperationException");
        Expect(ThrowsAs([h] { il2cpp::runtime::icall_gchandle_addr_of_pinned(h); },
                        "System.InvalidOperationException"),
               "AddrOfPinnedObject on a freed handle throws InvalidOperationException");
        Expect(ThrowsAs([h] { il2cpp::runtime::icall_gchandle_free(h); },
                        "System.InvalidOperationException"),
               "double-Free throws InvalidOperationException (net48 semantics)");
        Expect(ThrowsAs([] { il2cpp::runtime::icall_gchandle_free(0); },
                        "System.InvalidOperationException"),
               "Free(0) throws InvalidOperationException");
        Expect(ThrowsAs([] { il2cpp::runtime::icall_gchandle_free(7); },
                        "System.InvalidOperationException"),
               "Free on an odd (garbage) handle throws InvalidOperationException");
    }

    //--- 7. IsAllocated on garbage values never throws, reports false ---
    {
        Expect(!il2cpp::runtime::icall_gchandle_is_allocated(0),
               "IsAllocated(0) is false");
        Expect(!il2cpp::runtime::icall_gchandle_is_allocated(-1),
               "IsAllocated(-1) is false");
        Expect(!il2cpp::runtime::icall_gchandle_is_allocated(3),
               "IsAllocated on an odd handle is false");
        Expect(!il2cpp::runtime::icall_gchandle_is_allocated(99999),
               "IsAllocated on an out-of-range handle is false");
    }

    //--- 8. SetTarget: strong handle keeps the NEW referent alive ---
    {
        il2cpp::Il2CppObject* oldObj = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* newObj = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* newRoot = newObj; // root the NEW target locally
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&newRoot));
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(oldObj, il2cpp::runtime::kGCHandleTypeNormal);
        il2cpp::runtime::icall_gchandle_set_target(h, newObj);
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) == newObj,
               "set_Target re-targets the handle");
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) == newRoot,
               "re-targeted strong handle keeps the NEW referent alive through compaction");
        il2cpp::runtime::icall_gchandle_free(h);
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&newRoot));
    }
    {
        // set_Target on a weak handle is honored (the new referent is the one
        // tracked -- the handle stays weak).
        il2cpp::Il2CppObject* keep = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* keepRoot = keep;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keepRoot));
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(nullptr, il2cpp::runtime::kGCHandleTypeWeak);
        il2cpp::runtime::icall_gchandle_set_target(h, keep);
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) == keepRoot,
               "set_Target on a weak handle tracks the rooted new referent");
        il2cpp::runtime::icall_gchandle_free(h);
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&keepRoot));
    }
    {
        // set_Target on a freed handle throws InvalidOperationException.
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc_normal(nullptr);
        il2cpp::runtime::icall_gchandle_free(h);
        Expect(ThrowsAs([h] { il2cpp::runtime::icall_gchandle_set_target(h, nullptr); },
                        "System.InvalidOperationException"),
               "set_Target on a freed handle throws InvalidOperationException");
    }
    {
        // set_Target on a PINNED handle: the handle stays Pinned (net48: the
        // setter never changes the handle type) and AddrOfPinnedObject reports
        // the NEW target's data address.
        il2cpp::Il2CppObject* first = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* second = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(first, il2cpp::runtime::kGCHandleTypePinned);
        il2cpp::runtime::icall_gchandle_set_target(h, second);
        Expect(il2cpp::runtime::icall_gchandle_get_handle_type(h) == il2cpp::runtime::kGCHandleTypePinned,
               "set_Target leaves a Pinned handle pinned (net48 type is fixed at allocation)");
        Expect(il2cpp::runtime::icall_gchandle_addr_of_pinned(h) ==
                   static_cast<intptr_t>(reinterpret_cast<uintptr_t>(second) + sizeof(il2cpp::Il2CppObject)),
               "AddrOfPinnedObject reports the NEW target's payload start after set_Target");
        il2cpp::runtime::icall_gchandle_free(h);
    }
    {
        // set_Target on a Weak handle whose referent DIED re-arms the weak
        // slot: the new target is the tracked referent (net48 setter on a
        // cleared weak handle is legal and tracks the new object). The new
        // referent is re-read through its static root -- the first compaction
        // moves every surviving Gen0 object, so the local address goes stale.
        il2cpp::Il2CppObject* dead = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* keep = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* keepRoot = keep;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keepRoot));
        const int32_t h = il2cpp::runtime::icall_gchandle_alloc(dead, il2cpp::runtime::kGCHandleTypeWeak);
        il2cpp::il2cpp_gc_collect(); // 'dead' has no root: the weak slot clears
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) == nullptr,
               "weak handle target died: the slot is cleared");
        il2cpp::runtime::icall_gchandle_set_target(h, keepRoot); // current address after compaction
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) == keepRoot,
               "set_Target re-arms a cleared weak handle with the new referent");
        il2cpp::il2cpp_gc_collect(); // the new referent is rooted -> survives
        Expect(il2cpp::runtime::icall_gchandle_get_target(h) == keepRoot,
               "re-armed weak handle tracks the rooted new referent through compaction");
        il2cpp::runtime::icall_gchandle_free(h);
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&keepRoot));
    }


    //--- 9. Handle table reuse: free then re-alloc yields a valid handle ---
    {
        il2cpp::Il2CppObject* obj = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const int32_t first = il2cpp::runtime::icall_gchandle_alloc_normal(obj);
        il2cpp::runtime::icall_gchandle_free(first);
        const int32_t second = il2cpp::runtime::icall_gchandle_alloc_normal(obj);
        Expect(second == first,
               "freed slot returns to the free stack and is reused (same handle value)");
        Expect(il2cpp::runtime::icall_gchandle_get_target(second) == obj,
               "re-allocated handle stores the referent after the slot was reused");
        Expect(il2cpp::runtime::icall_gchandle_is_allocated(second),
               "re-allocated handle is live");
        il2cpp::runtime::icall_gchandle_free(second);
    }

    //--- Report ---
    if (g_failures != 0)
    {
        std::printf("%d gchandle assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime gchandle tests passed\n");
    return 0;
}
 
