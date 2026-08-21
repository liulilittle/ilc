#pragma once
//------------------------------------------------------------------------------
// il2cpp_gc_api.h -- System.GC icall module (M5 BCL runtime domain).
//
// The transpiler maps the static methods of System.GC to these runtime
// helpers (they are [MethodImplOptions.InternalCall]-style intrinsics: the
// collector itself lives in gc.cpp, whose public contract is il2cpp_gc.h,
// and this module is the thin icall layer in between).
//
// Naming follows the existing icall convention (icall_<owner>_<name>).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48), the project's reference
// baseline for behaviour:
//   * GC.Collect()                -- full collection of ALL generations
//                                    (0/1/2 + LOH), delegating to
//                                    il2cpp::il2cpp_gc_collect.
//   * GC.GetTotalMemory(false)    -- total bytes currently allocated on the
//                                    managed heap (a lower bound, not the
//                                    reserved address space).
//   * GC.GetGeneration(obj)       -- the object's generation: 0 (fresh),
//                                    1/2 (promoted survivors), 3 (LOH);
//                                    null -> ArgumentNullException.
//   * GC.KeepAlive(obj)           -- a no-op by design: the argument is a GC
//                                    root for the duration of the call,
//                                    which is exactly the liveness .NET
//                                    guarantees (the generated frame-root
//                                    protocol roots reference arguments).
//   * GC.SuppressFinalize(obj) / GC.ReRegisterForFinalize(obj)
//                                -- net48 null contract implemented (null ->
//                                ArgumentNullException). True finalizer
//                                suppression for non-null objects requires a
//                                collector-side suppress/unregister primitive
//                                (il2cpp_gc.h exposes only register + run) --
//                                reported to the orchestrator; the non-null
//                                path is currently a no-op.
//   * GC.GetAllocatedBytesForCurrentThread()
//                                -- cumulative bytes allocated by the
//                                current thread since process start
//                                (delegates to
//                                il2cpp_gc_get_allocated_bytes_for_current_thread,
//                                the counter gc.cpp's AllocCore maintains).
//
// RESOLVED (M-batch audit): the earlier KNOWN GAP note claimed the managed-
// heap statistics were unreachable and the two statistics icalls returned 0.
// That is obsolete: gc.cpp maintains the live-block total and the cumulative
// allocation counter behind the public il2cpp_gc.h accessors
// il2cpp_gc_get_total_memory / il2cpp_gc_get_allocated_bytes /
// il2cpp_gc_get_allocated_bytes_for_current_thread, and BOTH icalls now
// delegate to them. No gc.cpp extension is required.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// GC.Collect(): full collection of all generations (0, 1, 2 and the LOH).
// Delegates to il2cpp::il2cpp_gc_collect; safe to call at any time.
void icall_gc_collect();

// GC.GetTotalMemory(false): managed-heap bytes in use (delegates to
// il2cpp_gc_get_total_memory -- the live-block sum in gc.cpp's block table).
// Note: the net48 `forceFullCollection` flag is dropped at the call site by
// the transpiler (MethodMap.GC.cs emits the parameterless icall), so
// GetTotalMemory(true) does not trigger a collection first -- a documented
// transpiler-side simplification.
int64_t icall_gc_get_total_memory();

// GC.GetGeneration(obj): the object's current generation (0/1/2, 3 = LOH).
// null -> ArgumentNullException (net48 contract). A non-null object that is
// no longer a live heap block (a dangling pointer -- a caller bug) reports
// -1 via the underlying gc.cpp diagnostic helper.
int32_t icall_gc_get_generation(Il2CppObject* obj);

// GC.KeepAlive(obj): no-op (the argument is rooted by the caller's frame
// protocol for the duration of the call).
void icall_gc_keep_alive(Il2CppObject* obj);

// GC.SuppressFinalize(obj): placeholder no-op (no finalizer mechanism yet).
void icall_gc_suppress_finalize(Il2CppObject* obj);

// GC.ReRegisterForFinalize(obj): placeholder no-op (no finalizer mechanism
// yet).
void icall_gc_re_register_for_finalize(Il2CppObject* obj);

// GC.GetAllocatedBytesForCurrentThread(): cumulative allocation counter
// (delegates to il2cpp_gc_get_allocated_bytes_for_current_thread -- the
// counter gc.cpp's AllocCore maintains; the single-threaded collector's
// per-thread and process-wide counters coincide).
int64_t icall_gc_get_allocated_bytes();

}} // namespace il2cpp::runtime
