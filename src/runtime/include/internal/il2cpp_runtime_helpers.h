#pragma once
//------------------------------------------------------------------------------
// il2cpp_runtime_helpers.h -- System.Runtime.CompilerServices.RuntimeHelpers
// icall contract (see runtimehelpers.cpp for the implementations).
//
// The net48 audit §3 P0 batch A: the six RuntimeHelpers members that the
// transpiler used to reject with a LOUD NotSupportedException. The runtime
// side is wired here; the transpiler emits ordinary calls (MethodMap.
// RuntimeHelpers.cs). net48 semantics were probe-measured on the reference
// machine (csc 4.0.30319 x64; the raw lines live in the seat NOTES):
//
//   * PrepareDelegate / PrepareContractedDelegate -- JIT-prepare hints.
//     net48 accepts null and a valid delegate without throwing and without
//     any observable state change; an AOT image has no JIT work to do, so the
//     helpers are no-ops (probe: [prep.valid]/[prep.null]/[prepc.*] =
//     no-throw).
//   * EnsureSufficientExecutionStack -- net48 throws
//     System.InsufficientExecutionStackException when LESS THAN HALF of the
//     thread's stack reservation remains (probe: [ens.normal]=no-throw,
//     [ens.deep]=InsufficientExecutionStackException; measured remaining at
//     the throw = 2086540 of 4194304 bytes = 49.7%). This helper performs a
//     REAL per-thread check against the reservation bounds
//     (GetCurrentThreadStackLimits on Windows, pthread_getattr_np on Linux)
//     -- never a no-op.
//   * ProbeForSufficientStack -- a stack-touch hint, not a managed-limit
//     check: the same deep recursion that makes EnsureSufficientExecutionStack
//     throw terminates the net48 process with a raw StackOverflowException
//     (probe evidence). Generated code already materializes real stack
//     frames, so normal-depth behavior (no-throw) is preserved by a no-op.
//   * ExecuteCodeWithGuaranteedCleanup -- net48 runs the cleanup delegate on
//     BOTH paths (bool argument false on normal completion, true when the try
//     delegate threw) and lets the original exception keep propagating;
//     null code/backoutCode -> ArgumentNullException (probe evidence).
//   * InitializeArray -- the compiler-generated `new T[] { ... }` shape is
//     folded by StackFold (the ldtoken<RVA field> + newarr pattern). This
//     helper is the runtime arm for the shapes the fold cannot see (a handle
//     obtained at runtime): the AOT field model carries no RVA blob, so the
//     helper follows net48's invalid-handle contract -- default handle ->
//     ArgumentNullException, any other un-folded handle -> ArgumentException.
//
// Naming: icall_runtime_helpers_* (the runtime-call prefix used by the other
// icall_* domains); every function is void and static-call shaped, matching
// the managed signatures.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// RuntimeHelpers.PrepareDelegate(Delegate) -> void (JIT-prepare no-op).
void icall_runtime_helpers_prepare_delegate(Il2CppObject* d);

// RuntimeHelpers.PrepareContractedDelegate(Delegate) -> void (no-op).
void icall_runtime_helpers_prepare_contracted_delegate(Il2CppObject* d);

// RuntimeHelpers.EnsureSufficientExecutionStack() -> void. Throws
// System.InsufficientExecutionStackException when the current thread has less
// than the guard margin left on its stack.
void icall_runtime_helpers_ensure_sufficient_execution_stack();

// RuntimeHelpers.ProbeForSufficientStack() -> void (stack-touch hint no-op;
// see the header comment for the measured net48 contract).
void icall_runtime_helpers_probe_for_sufficient_stack();

// RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup(TryCode, CleanupCode,
// object) -> void. Runs code(userData); the cleanup always runs with
// (userData, exceptionThrown) and the original exception keeps propagating.
void icall_runtime_helpers_execute_code_with_guaranteed_cleanup(
    Il2CppObject* code, Il2CppObject* backoutCode, Il2CppObject* userData);

// RuntimeHelpers.InitializeArray(Array, RuntimeFieldHandle) -> void. The
// RuntimeFieldHandle value rides as an intptr_t (reflecthandle.h model).
void icall_runtime_helpers_initialize_array(Il2CppObject* array, intptr_t fieldHandle);

//------------------------------------------------------------------------------
// corelib-p0: the remaining CoreLib-INTERNAL RuntimeHelpers members (measured
// present in BOTH shipped System.Private.CoreLib 8.0.31 and 9.0.20).
//------------------------------------------------------------------------------

// RuntimeHelpers.TryEnsureSufficientExecutionStack() -> bool. The
// non-throwing twin of EnsureSufficientExecutionStack: it runs the SAME
// per-thread stack-limit check (one implementation, no drift) and reports
// "not enough stack" as false instead of throwing
// InsufficientExecutionStackException. Implemented in stubs.cpp by catching
// the DotNetException the shared check raises.
bool icall_runtime_helpers_try_ensure_sufficient_execution_stack();

// RuntimeHelpers.CompileMethod(RuntimeMethodHandleInternal) -> void. A JIT
// request; an AOT image is already compiled, so the call is a no-op. The
// handle rides as an intptr_t (reflecthandle.h value model).
void icall_runtime_helpers_compile_method(intptr_t methodHandle);

// RuntimeHelpers.PrepareMethod(RuntimeMethodHandleInternal, IntPtr*
// instantiation, int count) -> void (the net48 public shape is
// PrepareMethod(RuntimeMethodHandle, RuntimeTypeHandle[])). A JIT-prepare
// hint: everything is prepared in an AOT image, so the call is a no-op.
// 'instantiation' / 'instantiationCount' select a generic instantiation that
// is already compiled.
void icall_runtime_helpers_prepare_method(intptr_t methodHandle, void* instantiation,
                                          int32_t instantiationCount);

// RuntimeHelpers.AllocateTypeAssociatedMemory(Type, int) -> IntPtr (the QCall
// twin takes a type handle + uint size). Associated memory is released when
// its AssemblyLoadContext unloads; an AOT image never unloads, so a plain
// process-lifetime allocation is the exact lifetime contract. 'type' is the
// type identity (an Il2CppClass* / Type object) and is not needed to keep the
// block alive. Returns 0 for a zero/negative size (the managed wrapper
// validates first). The intptr_t return IS the managed IntPtr carrier (the
// "ptr" ResultKind), so the emitted call needs no pointer cast.
intptr_t icall_runtime_helpers_allocate_type_associated_memory(void* type, int32_t size);

}} // namespace il2cpp::runtime
