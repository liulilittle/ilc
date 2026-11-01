//------------------------------------------------------------------------------
// runtimehelpers.cpp -- System.Runtime.CompilerServices.RuntimeHelpers icall
// implementations (see il2cpp_runtime_helpers.h for the measured net48
// contract of every entry).
//------------------------------------------------------------------------------

#if defined(__linux__) && !defined(_GNU_SOURCE)
// pthread_getattr_np (the per-thread stack bounds query) is a GNU extension.
#define _GNU_SOURCE
#endif

#include "il2cpp_runtime_helpers.h"

#include "il2cpp_delegate.h"
#include "il2cpp_delegate_ext.h"
#include "il2cpp_exception.h"

#include <cstddef>
#include <cstdint>
#include <cstdlib>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <windows.h> // NtCurrentTeb / NT_TIB (StackLimit)
#elif defined(__linux__)
    #include <pthread.h>
#endif

#if defined(_MSC_VER)
    #include <intrin.h> // _AddressOfReturnAddress
#endif

namespace il2cpp { namespace runtime {

namespace {

// net48's EnsureSufficientExecutionStack throws while at least HALF of the
// thread's stack is still available. MEASURED on the reference machine
// (.NET Framework 4.8 x64, csc 4.0.30319, 4 MiB thread stack):
//
//   [total]=4194304  [depth]=26159
//   [rem.first]=4189916  [rem.1000]=4109916  [rem.atthrow]=2086540
//
// i.e. the throw fires at 2086540 / 4194304 = 49.7% remaining -- the CLR's
// "IsStackSpaceAvailable(0.5)" contract, NOT a fixed byte guard. (The old
// 64 KiB guess was wrong; TEB->StackLimit is the guard-page boundary only
// ~1 page below SP, so it cannot measure the remaining stack either.)
constexpr double kSufficientStackFraction = 0.5;

// Bounds of the CURRENT thread's stack reservation. Returns false when the
// platform cannot answer (the check then degrades to no-throw -- never a
// false positive).
bool CurrentThreadStackBounds(uintptr_t* outLow, uintptr_t* outHigh)
{
#if defined(_WIN32)
    // GetCurrentThreadStackLimits (Windows 8 / Server 2012+) reports the
    // RESERVATION bounds: measured on the reference machine as
    // low=..400000 high=..600000 (4 MiB) with SP ~1 KiB below high, whereas
    // TEB->StackLimit sat only ~15 KiB below SP (the guard-page boundary).
    // Resolved dynamically so the runtime keeps loading on older Windows.
    typedef void(WINAPI* GetCurrentThreadStackLimitsFn)(PULONG_PTR, PULONG_PTR);
    static GetCurrentThreadStackLimitsFn fn = []() -> GetCurrentThreadStackLimitsFn {
        HMODULE kernel32 = ::GetModuleHandleW(L"kernel32.dll");
        if (kernel32 == nullptr)
        {
            return nullptr;
        }
        return reinterpret_cast<GetCurrentThreadStackLimitsFn>(
            reinterpret_cast<void*>(::GetProcAddress(kernel32, "GetCurrentThreadStackLimits")));
    }();
    if (fn == nullptr)
    {
        return false;
    }
    ULONG_PTR low = 0;
    ULONG_PTR high = 0;
    fn(&low, &high);
    if (low == 0 || high == 0 || high <= low)
    {
        return false;
    }
    *outLow = static_cast<uintptr_t>(low);
    *outHigh = static_cast<uintptr_t>(high);
    return true;
#elif defined(__linux__)
    pthread_attr_t attr;
    if (pthread_getattr_np(pthread_self(), &attr) != 0)
    {
        return false;
    }
    void* base = nullptr;
    size_t size = 0;
    const bool ok = pthread_attr_getstack(&attr, &base, &size) == 0
        && base != nullptr && size != 0;
    pthread_attr_destroy(&attr);
    if (!ok)
    {
        return false;
    }
    *outLow = reinterpret_cast<uintptr_t>(base);
    *outHigh = *outLow + static_cast<uintptr_t>(size);
    return true;
#else
    (void)outLow;
    (void)outHigh;
    return false;
#endif
}

// Current frame address (the stack pointer proxy). MSVC has no
// __builtin_frame_address; _AddressOfReturnAddress is the equivalent
// intrinsic.
uintptr_t CurrentStackPointer()
{
#if defined(_MSC_VER)
    return reinterpret_cast<uintptr_t>(_AddressOfReturnAddress());
#else
    return reinterpret_cast<uintptr_t>(__builtin_frame_address(0));
#endif
}

// The ONE stack-availability check shared by EnsureSufficientExecutionStack
// (throws when it answers false) and TryEnsureSufficientExecutionStack (which
// reports that same answer). Keeping a single implementation is the point: the
// throwing and non-throwing members must never disagree about how much stack
// counts as "sufficient". Returns true when the platform cannot answer (never
// a false alarm).
bool HasSufficientExecutionStack()
{
    uintptr_t lowLimit = 0;
    uintptr_t highLimit = 0;
    if (!CurrentThreadStackBounds(&lowLimit, &highLimit))
    {
        return true; // no platform query: cannot prove a low stack
    }
    const uintptr_t sp = CurrentStackPointer();
    if (sp <= lowLimit || sp > highLimit)
    {
        return true; // not on the queried stack (unexpected): never a false alarm
    }
    const double total = static_cast<double>(highLimit - lowLimit);
    const double remaining = static_cast<double>(sp - lowLimit);
    return remaining >= total * kSufficientStackFraction;
}

// CleanupCode(object userData, bool exceptionThrown) invocation (single-cast
// delegate; the net48 API is a plain two-argument delegate, and the AOT
// delegate object carries methodPtr + optional target exactly like every
// other generated delegate). Static targets use void(object, bool); an
// instance-bound target receives `this` first (the generated instance ABI).
void InvokeCleanupCode(Il2CppObject* delegateObject, Il2CppObject* userData, bool exceptionThrown)
{
    Il2CppDelegate* leaf = reinterpret_cast<Il2CppDelegate*>(delegateObject);
    if (leaf->target != nullptr)
    {
        typedef void (*CleanupThisFn)(Il2CppObject*, Il2CppObject*, bool);
        reinterpret_cast<CleanupThisFn>(leaf->methodPtr)(leaf->target, userData, exceptionThrown);
        return;
    }
    typedef void (*CleanupFn)(Il2CppObject*, bool);
    reinterpret_cast<CleanupFn>(leaf->methodPtr)(userData, exceptionThrown);
}

} // namespace

void icall_runtime_helpers_prepare_delegate(Il2CppObject* d)
{
    // net48 probe: PrepareDelegate(null) and PrepareDelegate(valid) both
    // return without throwing and without observable side effects -- it is a
    // pure JIT-prepare hint. There is no JIT in an AOT image, so nothing is
    // left to do.
    (void)d;
}

void icall_runtime_helpers_prepare_contracted_delegate(Il2CppObject* d)
{
    // Same measured contract as PrepareDelegate (probe: [prepc.valid] /
    // [prepc.null] = no-throw).
    (void)d;
}

void icall_runtime_helpers_ensure_sufficient_execution_stack()
{
    if (HasSufficientExecutionStack())
    {
        return;
    }
    il2cpp_throw_new(
        "System.InsufficientExecutionStackException,System.Exception",
        "Insufficient stack to continue executing the program safely. "
        "This can happen from having too many functions on the call stack "
        "or function on the stack using too much stack space.");
}

bool icall_runtime_helpers_try_ensure_sufficient_execution_stack()
{
    // The non-throwing twin: SAME check, reported as a bool. Deliberately not
    // implemented by catching the exception above -- a throw/catch pair would
    // unwind the caller's frames and cost an exception per probe, and (worse)
    // the two members could drift if either is edited. One predicate, used by
    // both, is the contract.
    return HasSufficientExecutionStack();
}

void icall_runtime_helpers_probe_for_sufficient_stack()
{
    // net48 probe: normal depth -> no-throw; the SAME deep recursion that
    // makes EnsureSufficientExecutionStack throw instead terminates the
    // net48 process with a raw StackOverflowException. This is a stack-touch
    // hint with no managed exception contract; generated code already
    // materializes real frames, so the no-op preserves the observable
    // normal-depth behavior.
}

void icall_runtime_helpers_execute_code_with_guaranteed_cleanup(
    Il2CppObject* code, Il2CppObject* backoutCode, Il2CppObject* userData)
{
    if (code == nullptr)
    {
        il2cpp_throw_new_with_param("System.ArgumentNullException,System.Exception",
                                    "Value cannot be null.", "code");
    }
    if (backoutCode == nullptr)
    {
        il2cpp_throw_new_with_param("System.ArgumentNullException,System.Exception",
                                    "Value cannot be null.", "backoutCode");
    }
    try
    {
        // TryCode(object userData): the shared 1-ref void multicast invoke
        // (delegateext.cpp) -- it dispatches static/instance leaves and
        // raises NRE for a null delegate, matching the managed contract.
        icall_multicast_invoke_r_void(code, userData);
        InvokeCleanupCode(backoutCode, userData, false);
    }
    catch (...)
    {
        InvokeCleanupCode(backoutCode, userData, true);
        throw; // net48: the original exception keeps propagating
    }
}

void icall_runtime_helpers_initialize_array(Il2CppObject* array, intptr_t fieldHandle)
{
    if (array == nullptr)
    {
        il2cpp_throw_new_with_param("System.ArgumentNullException,System.Exception",
                                    "Value cannot be null.", "array");
    }
    if (fieldHandle == 0)
    {
        // net48 probe: default(RuntimeFieldHandle) -> ArgumentNullException.
        il2cpp_throw_new_with_param("System.ArgumentNullException,System.Exception",
                                    "Value cannot be null.", "fldHandle");
    }
    // net48 probe: a non-RVA field handle (and a blob/element-type mismatch)
    // -> ArgumentException. The AOT field model (Il2CppFieldInfo = name +
    // typeName, reflecthandle.h) carries no RVA blob, so an un-folded call can
    // only reach the invalid-handle contract. The compiler-generated
    // `new T[] { ... }` shape never gets here: StackFold copies the RVA blob
    // at the call site.
    il2cpp_throw_new("System.ArgumentException,System.Exception",
                     "Value does not fall within the expected range.");
}

//------------------------------------------------------------------------------
// corelib-p0: the remaining CoreLib-INTERNAL RuntimeHelpers entries (see
// il2cpp_runtime_helpers.h).
//------------------------------------------------------------------------------

void icall_runtime_helpers_compile_method(intptr_t methodHandle)
{
    // A JIT request. An AOT image is already compiled, so the request is
    // already satisfied: the handle is not dereferenced and nothing is
    // emitted. (The managed caller only needs the method to BE compiled --
    // there is no "was it compiled" reply.)
    (void)methodHandle;
}

void icall_runtime_helpers_prepare_method(intptr_t methodHandle, void* instantiation,
                                          int32_t instantiationCount)
{
    // A JIT-prepare hint for a method (and, for the generic shape, one
    // instantiation of it). Everything is prepared in an AOT image. The
    // instantiation array selects which MethodSpec is prepared -- it does not
    // affect any observable state here, so it is accepted and ignored.
    (void)methodHandle;
    (void)instantiation;
    (void)instantiationCount;
}

intptr_t icall_runtime_helpers_allocate_type_associated_memory(void* type, int32_t size)
{
    // Type-associated memory is freed when its AssemblyLoadContext unloads.
    // An AOT image never unloads, so process lifetime IS the contract -- a
    // plain allocation is exact, and the type identity plays no part in
    // keeping the block alive.
    //
    // The managed wrapper validates the size (ArgumentOutOfRangeException for
    // <= 0) before calling, so a non-positive size here is a caller bug;
    // answering 0 keeps the contract "returns a usable block or the null
    // IntPtr" without allocating a zero-length region.
    (void)type;
    if (size <= 0)
    {
        return 0;
    }
    return reinterpret_cast<intptr_t>(std::malloc(static_cast<size_t>(size)));
}

}} // namespace il2cpp::runtime
