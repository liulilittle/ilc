#pragma once
//------------------------------------------------------------------------------
// il2cpp_gc_ext.h -- GC extension icall surface: weak references and
// finalization (runtime-level wrappers over the collector's public contract
// in il2cpp_gc.h). This is the transpiler wiring target for
// System.WeakReference and GC finalization support.
//
// All wrappers delegate to the collector; the semantics (collection-time
// weak-slot clearing, two-collection finalization, reentrancy protection)
// are documented in il2cpp_gc.h and implemented in gc.cpp.
//------------------------------------------------------------------------------

#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)
#include "il2cpp_object.h"

//------------------------------------------------------------------------------
// WEAKREFERENCE OBJECT MODEL (W-1 weakrefcase work order) -- System.WeakReference
// is a managed object allocated through its GLOBAL-scope class descriptor
// (System_WeakReference_class, defined in gcext.cpp -- the same contract as
// System_Version_class in versionuri.cpp, because generated newobj emits
// il2cpp_object_new(&System_WeakReference_class) directly).
//
//   * Il2CppWeakRefObject: header + an int32 HANDLE -- NO GC reference fields
//     (refFieldBitmap 0), so the object moves freely under Gen0 compaction.
//   * The handle indexes the gcext WEAK-TARGET HANDLE TABLE (a static C++
//     array of Il2CppObject* -- non-moving memory owned by gcext.cpp). The
//     collector registers &table[handle] as a weak slot, so it clears the
//     entry when the target dies and rewrites it when a live target moves.
//     The slot address MUST NOT live inside the managed object: the collector
//     copies every surviving Gen0 object at each collection (gc.cpp Phase A),
//     which would leave an object-embedded slot address stale (dangling write
//     target). The int32 handle is copied verbatim and stays valid.
//   * Handle lifetime: allocated by icall_weakref_ctor, released by a
//     registered finalizer (icall_gc_register_finalizer -- two-collection
//     semantics) once the WeakReference itself dies.
//------------------------------------------------------------------------------

namespace il2cpp {

// Managed System.WeakReference layout: object header + weak-target handle.
// handle is -1 when the object has no live handle (never observable from
// managed code -- the ctor always arms one).
struct Il2CppWeakRefObject
{
    Il2CppObject obj;  // object header (klass pointer, offset 0)
    int32_t handle;    // index into the gcext weak-target handle table (offset 8)
    bool track;        // trackResurrection flag (offset 12; value slot, bitmap 0)
};

// The System.WeakReference descriptor (lazily registered on first use;
// generated code allocates through &System_WeakReference_class directly).
Il2CppClass* il2cpp_weakref_class();

} // namespace il2cpp

// System.WeakReference runtime descriptor (GLOBAL scope -- the transpiler
// emits &System_WeakReference_class for newobj/isinst/castclass; defined in
// gcext.cpp, registered lazily through il2cpp_weakref_class()).
extern il2cpp::Il2CppClass System_WeakReference_class;

namespace il2cpp { namespace runtime {

// Register/remove a weak-reference slot: the ADDRESS of a client-owned
// Il2CppObject* location (see il2cpp_gc.h -- the collector clears the slot
// when its target dies and rewrites it when a live target is moved by the
// Gen0 compaction).
void icall_gc_weak_ref_add(void** slot);
void icall_gc_weak_ref_remove(void** slot);

// Register a finalizer callback for 'obj': the callback runs exactly once,
// before the object's memory is reclaimed (two-collection semantics; see
// il2cpp_gc.h). Re-registering the same object replaces the callback.
void icall_gc_register_finalizer(Il2CppObject* obj, void (*fn)(Il2CppObject*));

// Run any due (pending) finalizers now. Normally they run automatically at
// the start of the next collection; this direct trigger exists for tests and
// future finalizer-thread emulation.
void icall_gc_finalize_trigger();

//------------------------------------------------------------------------------
// WeakReference object icalls (see the OBJECT MODEL block above -- the
// receiver is an Il2CppWeakRefObject allocated through
// System_WeakReference_class; the object owns only an int32 handle, the
// target lives in the non-moving handle table).
//------------------------------------------------------------------------------

// WeakReference(object target): arms the weak-target handle and registers it
// with the collector (the entry is cleared when the target dies, rewritten
// when a live target is moved by the Gen0 compaction). Also registers the
// handle-release finalizer (released after two collections, once this
// WeakReference itself dies). Null target is legal (net48: IsAlive false).
// Null receiver -> NullReferenceException (defensive; generated code
// null-checks first).
void icall_weakref_ctor(Il2CppObject* self, Il2CppObject* target);

// IsAlive: true iff the handle entry currently holds a non-null target (the
// collector clears it when the target dies -- checked at every collection).
bool icall_weakref_get_is_alive(Il2CppObject* self);

// Target: the current target, or null once it died. The returned reference
// is the collector-maintained entry (already rewritten through the forward
// map when the target moved).
Il2CppObject* icall_weakref_get_target(Il2CppObject* self);

// Target setter: re-arms the weak handle with a new target (the collector
// manages the new entry from the next collection on). Null value clears it.
void icall_weakref_set_target(Il2CppObject* self, Il2CppObject* target);

// Tracked-resurrection ctor + flag reader (net48 trackResurrection surface;
// the handle-table model stores the flag, long/short distinction simplified).
void icall_weakref_ctor_track(Il2CppObject* self, Il2CppObject* target, bool track);
bool icall_weakref_get_track_resurrection(Il2CppObject* self);

// WeakReference<T> generics (T erased to object): same handle-table model.
void icall_weakref_generic_ctor(Il2CppObject* self, Il2CppObject* target);
bool icall_weakref_generic_try_get_target(Il2CppObject* self, Il2CppObject** outTarget);
void icall_weakref_generic_set_target(Il2CppObject* self, Il2CppObject* target);

}} // namespace il2cpp::runtime
