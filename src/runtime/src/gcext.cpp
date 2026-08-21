//------------------------------------------------------------------------------
// gcext.cpp -- GC extension icall wrappers: weak references + finalization
// (see il2cpp_gc_ext.h). Deliberately thin: every real behaviour delegates
// to the collector's public contract (il2cpp_gc.h); the collection-time
// semantics (weak-slot clearing after the sweep, two-collection finalization
// with reentrancy protection) live in gc.cpp.
//
// W-1 weakrefcase work order: System.WeakReference OBJECT MODEL lives here --
// the System_WeakReference_class descriptor (Il2CppWeakRefObject: header +
// int32 handle) and the weak-target HANDLE TABLE (a static C++ array of
// Il2CppObject*, non-moving memory). The collector's weak slots are the
// ADDRESSES of the table entries (&g_weakTargets[handle]) -- never addresses
// inside the managed object, because the compacting collector copies every
// surviving Gen0 object (gc.cpp Phase A), which would leave an object-embedded
// slot address stale. The int32 handle is copied verbatim by the compaction
// and stays valid.
//------------------------------------------------------------------------------

#include "il2cpp_gc_ext.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_null_reference_exception
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"       // il2cpp_class_register

#include <cstdint>
#include <cstdio>
#include <cstdlib>

//------------------------------------------------------------------------------
// System.WeakReference runtime descriptor (GLOBAL scope -- the transpiler
// emits &System_WeakReference_class for newobj/isinst/castclass, the same
// contract as System_Version_class in versionuri.cpp). instanceSize covers
// the header + int32 handle; there are NO reference fields, so the collector
// never scans past the header (refFieldBitmap 0 -- the target must stay
// WEAK, so it cannot sit in the bitmap: a bitmap bit is a strong reference).
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_WeakReference_class = {
    "System.WeakReference",                              // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppWeakRefObject)), // instanceSize: header + handle
    0u,                                                  // refFieldBitmap: no reference fields
    0,                                                   // refFieldCount
    &System_Object_class,                                // baseClass (a WeakReference IS-A object)
    nullptr,                                             // vtable
    0,                                                   // vtableSize
    nullptr,                                             // interfaces
    0,                                                   // interfaceCount
};

namespace il2cpp {

namespace {

// Layout pin: the handle sits at slot 1 (offset 8), mirroring the
// refFieldBitmap slot convention (slot 0 = klass header).
static_assert(offsetof(Il2CppWeakRefObject, handle) == 1 * sizeof(void*),
              "Il2CppWeakRefObject: handle must sit at slot 1 (offset 8)");

bool g_weakRefClassRegistered = false;

//------------------------------------------------------------------------------
// Weak-target handle table (non-moving memory -- see the OBJECT MODEL block
// in il2cpp_gc_ext.h). The collector clears/rewrites the registered entry
// (&g_weakTargets[handle]) at every collection; the entry is the single
// authoritative target location the WeakReference icalls read and write.
//------------------------------------------------------------------------------
constexpr int32_t kMaxWeakRefSlots = 256; // mirrors the collector's weak table bound

Il2CppObject* g_weakTargets[kMaxWeakRefSlots];
int32_t       g_weakFree[kMaxWeakRefSlots];
int32_t       g_weakFreeCount = 0;
bool          g_weakTableArmed = false;

void WeakRefFailLoud(const char* what)
{
    std::fprintf(stderr, "il2cpp_weakref: %s\n", what);
    std::abort();
}

void WeakRefEnsureReady()
{
    if (g_weakTableArmed)
    {
        return;
    }
    g_weakTableArmed = true;
    // First use: push every index onto the free stack (descending, so the
    // first allocation pops index 0).
    for (int32_t i = kMaxWeakRefSlots - 1; i >= 0; --i)
    {
        g_weakFree[g_weakFreeCount++] = i;
    }
}

int32_t WeakRefAllocHandle()
{
    WeakRefEnsureReady();
    if (g_weakFreeCount == 0)
    {
        WeakRefFailLoud("weak-target handle table overflow");
    }
    const int32_t idx = g_weakFree[--g_weakFreeCount];
    g_weakTargets[idx] = nullptr;
    return idx;
}

void WeakRefReleaseHandle(int32_t idx)
{
    if (idx < 0 || idx >= kMaxWeakRefSlots)
    {
        return; // idempotent teardown: no live handle
    }
    il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&g_weakTargets[idx]));
    g_weakTargets[idx] = nullptr;
    g_weakFree[g_weakFreeCount++] = idx;
}

// Handle-release finalizer: runs exactly once (two-collection semantics), as
// soon as the WeakReference itself is dead. Reads/writes only the retained
// block (safe -- finalization holds the memory) and the static tables (no
// allocation, so no nested collection).
void WeakRefHandleFinalize(Il2CppObject* obj)
{
    Il2CppWeakRefObject* w = reinterpret_cast<Il2CppWeakRefObject*>(obj);
    WeakRefReleaseHandle(w->handle);
    w->handle = -1;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_weakref_class -- the System.WeakReference descriptor (lazily
// registered on first use; idempotent -- il2cpp_class_register dedupes by
// pointer).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_weakref_class()
{
    if (!g_weakRefClassRegistered)
    {
        il2cpp_class_register(&System_WeakReference_class);
        g_weakRefClassRegistered = true;
    }
    return &System_WeakReference_class;
}

} // namespace il2cpp

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// icall_gc_weak_ref_add / icall_gc_weak_ref_remove -- register/remove a
// weak-reference slot (the ADDRESS of a client-owned Il2CppObject* location).
// The collector clears the slot when its target dies and rewrites it when a
// live target is moved by the Gen0 compaction (see il2cpp_gc.h).
//------------------------------------------------------------------------------
void icall_gc_weak_ref_add(void** slot)
{
    il2cpp::il2cpp_gc_add_weak_ref(slot);
}

void icall_gc_weak_ref_remove(void** slot)
{
    il2cpp::il2cpp_gc_remove_weak_ref(slot);
}

//------------------------------------------------------------------------------
// icall_gc_register_finalizer -- register a finalizer callback for 'obj'.
// The callback runs exactly once, before the object's memory is reclaimed
// (two-collection semantics; see il2cpp_gc.h). Re-registering the same
// object replaces the callback.
//------------------------------------------------------------------------------
void icall_gc_register_finalizer(Il2CppObject* obj, void (*fn)(Il2CppObject*))
{
    il2cpp::il2cpp_gc_register_finalizer(obj, fn);
}

//------------------------------------------------------------------------------
// icall_gc_finalize_trigger -- run any due (pending) finalizers now.
// Normally they run automatically at the start of the next collection; this
// direct trigger exists for tests and future finalizer-thread emulation.
//------------------------------------------------------------------------------
void icall_gc_finalize_trigger()
{
    il2cpp::il2cpp_gc_run_finalizers();
}

//------------------------------------------------------------------------------
// WeakReference object icalls (see the OBJECT MODEL block in il2cpp_gc_ext.h:
// the receiver is an Il2CppWeakRefObject; the target lives in the non-moving
// handle table, registered with the collector as a weak slot).
//------------------------------------------------------------------------------

namespace {

Il2CppWeakRefObject* AsWeakRef(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppWeakRefObject*>(self);
}

// Defensive null-receiver guard: generated code null-checks first; this is
// the icall's contract (mirrors versionuri.cpp).
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

Il2CppObject* WeakRefReadTarget(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppWeakRefObject* w = AsWeakRef(self);
    if (w->handle < 0 || w->handle >= kMaxWeakRefSlots)
    {
        return nullptr; // no live handle: no target
    }
    return g_weakTargets[w->handle];
}

} // namespace

void icall_weakref_ctor(Il2CppObject* self, Il2CppObject* target)
{
    ThrowIfNullReceiver(self);
    Il2CppWeakRefObject* w = AsWeakRef(self);
    // NO release here: a fresh object is zero-initialized (il2cpp_object_new
    // contract), so handle == 0 -- which is ALSO the first valid handle
    // index. Releasing it pushes index 0 onto the already-full free stack:
    // g_weakFree[g_weakFreeCount++] lands 4 bytes past the array and
    // clobbers the adjacent g_weakFreeCount (first alloc then aborts
    // "handle table overflow"). Re-ctor of a live object is illegal IL
    // (newobj always allocates fresh), so the ctor never owns a stale
    // handle; the handle-release finalizer (WeakRefHandleFinalize, set
    // below) is the single release point -- 1:1 with this alloc.
    const int32_t idx = WeakRefAllocHandle();
    w->handle = idx;
    // The target slot is the ADDRESS of the table entry -- non-moving memory,
    // so the registration stays valid no matter how often the WeakReference
    // object itself is copied by the Gen0 compaction.
    g_weakTargets[idx] = target;
    il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&g_weakTargets[idx]));
    // Two-collection lifecycle: once THIS object is unreachable, the
    // collector runs WeakRefHandleFinalize (at the start of the following
    // collection) and the handle is released.
    il2cpp::il2cpp_gc_register_finalizer(self, &WeakRefHandleFinalize);
}

bool icall_weakref_get_is_alive(Il2CppObject* self)
{
    return WeakRefReadTarget(self) != nullptr;
}

Il2CppObject* icall_weakref_get_target(Il2CppObject* self)
{
    return WeakRefReadTarget(self);
}

void icall_weakref_set_target(Il2CppObject* self, Il2CppObject* target)
{
    ThrowIfNullReceiver(self);
    Il2CppWeakRefObject* w = AsWeakRef(self);
    if (w->handle < 0 || w->handle >= kMaxWeakRefSlots)
    {
        return; // no live handle: nothing to re-arm
    }
    g_weakTargets[w->handle] = target;
}

}} // namespace il2cpp::runtime
