//------------------------------------------------------------------------------
// safehandle.cpp -- System.Runtime.InteropServices.SafeHandle minimal
// reference counting (see safehandle.h for the full contract).
//
// Storage is a flat bounded table (constraint #12 -- no global-constructor
// allocation), exactly like the pinvokeext callback table: overflow aborts
// loudly.
//------------------------------------------------------------------------------

#include "safehandle.h"
#include "il2cpp_exception_ext.h"

#include <cstdio>
#include <cstdlib>

namespace il2cpp { namespace runtime {

namespace {

// Flat side table: bounded static storage. Overflow aborts loudly (a
// programming error -- the transpiler emits a bounded set of handles).
constexpr int32_t kMaxHandles = 256;

struct HandleEntry
{
    Il2CppObject* obj;   // the SafeHandle receiver (opaque object address)
    int32_t       refCount;
    bool          closed;
};

HandleEntry g_handles[kMaxHandles];
int32_t     g_handleCount = 0;

HandleEntry* Find(Il2CppObject* obj)
{
    for (int32_t i = 0; i < g_handleCount; ++i)
    {
        if (g_handles[i].obj == obj)
            return &g_handles[i];
    }
    return nullptr;
}

// First touch of an object creates the entry with the implicit construction
// reference (refCount = 1, net48's RefCountOne initial state), closed = false.
HandleEntry& FindOrCreate(Il2CppObject* obj)
{
    if (HandleEntry* e = Find(obj))
        return *e;
    if (g_handleCount >= kMaxHandles)
    {
        std::fprintf(stderr, "icall_safehandle_*: handle table overflow\n");
        std::abort();
    }
    g_handles[g_handleCount].obj = obj;
    g_handles[g_handleCount].refCount = 1;
    g_handles[g_handleCount].closed = false;
    ++g_handleCount;
    return g_handles[g_handleCount - 1];
}

// ObjectDisposedException: the thrown type-name chain starts with the exact
// managed full name and lists the .NET base classes so catch matching follows
// .NET inheritance (the il2cpp_exception_ext.h chain contract).
[[noreturn]] void ThrowObjectDisposed()
{
    il2cpp_throw_new(
        "System.ObjectDisposedException,System.ObjectDisposedException,System.InvalidOperationException,System.SystemException,System.Exception",
        "Safe handle has been closed");
    // Defensive fallback: il2cpp_throw_new throws a C++ exception and never
    // returns; the abort also silences -Winvalid-noreturn (array.cpp precedent).
#ifdef _MSC_VER
    __assume(0);
#else
    std::abort();
#endif
}

} // namespace

void icall_safehandle_dangerous_add_ref(Il2CppObject* self, int32_t* success)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    if (success == nullptr)
    {
        std::fprintf(stderr, "icall_safehandle_dangerous_add_ref: null success slot\n");
        std::abort();
    }
    HandleEntry& e = FindOrCreate(self);
    if (e.closed)
    {
        ThrowObjectDisposed(); // success is never written -- the caller's slot stays false
    }
    ++e.refCount;
    *success = 1;
}

void icall_safehandle_dangerous_release(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    HandleEntry& e = FindOrCreate(self);
    if (e.refCount == 0)
    {
        ThrowObjectDisposed(); // unbalanced release
    }
    --e.refCount;
    if (e.refCount == 0)
    {
        // Release: net48 calls the subclass's ReleaseHandle() here; ILC has no
        // SafeHandle subclass model, so the release only marks the handle
        // closed (the entry stays as a tombstone -- a further release throws).
        e.closed = true;
    }
}

void icall_safehandle_set_handle_as_invalid(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    FindOrCreate(self).closed = true;
}

}} // namespace il2cpp::runtime
