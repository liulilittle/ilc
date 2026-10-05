#pragma once
//------------------------------------------------------------------------------
// il2cpp_gchandle.h -- System.Runtime.InteropServices.GCHandle icall surface
// (gchandle.cpp). Implements the net48 GCHandle handle semantics on top of
// the runtime's existing handle-table model (see il2cpp_gc_ext.h -- the
// WeakReference W-1 work order already proved the pattern: a NON-MOVING
// static C++ table of Il2CppObject* entries registered with the collector,
// so the object moves under the Gen0 compaction while the int32 handle is
// copied verbatim and stays valid).
//
// GCHANDLE TYPE (netcore GCHandleType.cs -- same enum values as net48):
//   Weak = 0, WeakTrackResurrection = 1, Normal = 2, Pinned = 3
//
// HANDLE MODEL
//   * A GCHandle is an opaque int32 HANDLE value (the M2 value carrier -- an
//     IntPtr rides in an int32 slot, see intptr.h). The handle indexes a
//     static entry table in gchandle.cpp; each entry stores the referent
//     (Il2CppObject*) plus its GCHandleType.
//   * The collector is told whether the referent slot is STRONG or WEAK:
//       Normal/Pinned   -> il2cpp_gc_add_static_root(&entry.target) -- the
//                          referent stays alive AND the slot follows the
//                          Gen0 compaction (roots are rewritten).
//       Weak/WeakTrack  -> il2cpp_gc_add_weak_ref(&entry.target) -- cleared
//                          when the referent dies (gcext precedent).
//     The entry address is non-moving static memory, exactly like the gcext
//     weak-target table.
//   * HANDLE VALUE ENCODING (mirrors the netcore InternalGCHandle
//     convention): a handle is (index+1) << 1 -- a positive value with bit 0
//     ALWAYS CLEAR, so 0 is never a valid handle and the managed-side pin
//     flag (netcore GetHandleValue = handle & ~1) never collides. The
//     runtime itself tracks the pinned state in the table entry; the
//     transpiled GCHandle does not need the mscorlib wrapper's bit tricks.
//
// DEVIATIONS (documented, ILC scope)
//   * WeakTrackResurrection is treated as Weak (the slot clears when the
//     referent dies) -- the handle-table model does not track resurrection,
//     the same documented simplification as MethodMap.WeakReference.cs.
//   * PIN LIMITATION (mirrors il2cpp_marshal_ext.h): ILC has no real
//     pinning -- the compacting Gen0 collection moves young objects. The
//     address from icall_gchandle_addr_of_pinned is valid only until the
//     next collection; a long-lived address needs the object kept rooted and
//     no collection in between.
//   * Invalid-handle access (free/get/set on a freed or garbage handle)
//     throws InvalidOperationException -- a safe superset of net48's
//     undefined behavior (same stance as marshalext3.cpp bounds-checking).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// GCHandleType constants (netcore GCHandleType.cs; net48 enum values).
constexpr int32_t kGCHandleTypeWeak                  = 0;
constexpr int32_t kGCHandleTypeWeakTrackResurrection = 1;
constexpr int32_t kGCHandleTypeNormal                = 2;
constexpr int32_t kGCHandleTypePinned                = 3;

//------------------------------------------------------------------------------
// Alloc(handle from object) -- net48 GCHandle.InternalAlloc:
//   allocate a handle storing 'value' with handle type 'type'. Returns the
//   handle value (positive, bit 0 clear). A type outside [Weak, Pinned]
//   throws ArgumentOutOfRangeException. Null value is legal (net48
//   GCHandle.Alloc(null, ...) allocates a handle whose target is null).
//------------------------------------------------------------------------------
int32_t icall_gchandle_alloc(Il2CppObject* value, int32_t type);

// GCHandle.Alloc(object) -- the one-argument overload (the transpiler's
// MethodMap cannot inject the constant 2 = Normal, so this is a dedicated
// entry point). Equivalent to icall_gchandle_alloc(value, kGCHandleTypeNormal).
int32_t icall_gchandle_alloc_normal(Il2CppObject* value);

// GCHandle.Free -- release the handle: unregister the collector slot, clear
// the entry, return the table slot to the free list. An invalid / already-
// freed handle throws InvalidOperationException (net48 double-free).
void icall_gchandle_free(int32_t handle);

// GCHandle.get_Target -- the object held by the handle (null when the weak
// referent died or the handle's target is null). Invalid handle throws
// InvalidOperationException.
Il2CppObject* icall_gchandle_get_target(int32_t handle);

// GCHandle.set_Target -- re-target the handle to 'value'. The handle's type
// is fixed at allocation (net48 semantics: the setter never changes it).
// Invalid handle throws InvalidOperationException.
void icall_gchandle_set_target(int32_t handle, Il2CppObject* value);

// net48 GCHandle.InternalGetHandleType -- the handle's GCHandleType (drives
// the IsPinned semantics). Invalid handle throws InvalidOperationException.
int32_t icall_gchandle_get_handle_type(int32_t handle);

// GCHandle.get_IsAllocated -- true iff 'handle' refers to a live (not-freed)
// table slot. A garbage handle value is simply not allocated (false -- no
// throw; net48 IsAllocated never throws).
bool icall_gchandle_is_allocated(int32_t handle);

// GCHandle.AddrOfPinnedObject -- the address of the pinned object's data
// (string -> &chars[0]; array -> &data[0]; other object -> first payload
// byte after the header). A handle that is not Pinned throws
// InvalidOperationException; a null target returns 0 (netcore default). PIN
// LIMITATION: valid only until the next collection (see the header).
intptr_t icall_gchandle_addr_of_pinned(int32_t handle);

} // namespace runtime
} // namespace il2cpp
