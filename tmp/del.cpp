//------------------------------------------------------------------------------
// delegate.cpp -- managed delegate creation (see il2cpp_delegate.h).
//
// M3 scope: static-method delegates. The delegate object is a GC object with
// the method pointer + target fields; the collector scans it (target is a GC
// reference when instance delegates arrive in M3.5 -- the bitmap already
// marks slot 1 for the target).
//------------------------------------------------------------------------------

#include "il2cpp_delegate.h"

#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_runtime.h"

namespace il2cpp {

namespace {

// The Delegate class descriptor: one reference field (target, slot 1).
Il2CppClass g_delegateClass = {
    "System.Delegate",            // name
    static_cast<int32_t>(sizeof(Il2CppDelegate)), // instanceSize
    0b00000100u,                  // refFieldBitmap: slot 2 (target) is a GC ref (slot 1 is methodPtr, a code pointer -- never a managed reference)
    1,                            // refFieldCount
    nullptr,                      // baseClass
    nullptr,                      // vtable
    0,                            // vtableSize
    nullptr,                      // interfaces
    0,                            // interfaceCount
};

bool g_delegateRegistered = false;

void EnsureDelegateRegistered()
{
    if (g_delegateRegistered) return;
    il2cpp_class_register(&g_delegateClass);
    g_delegateRegistered = true;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_delegate_new -- wrap a method pointer (+ optional instance target)
// into a delegate object.
//
// The generated instance-method ABI is `{ret} M(Il2CppObject* this, args...)`
// (CodeGenerator: instance arg0 = this), so the invoke side calls an
// instance-bound leaf as (target, args...) -- see
// icall_multicast_invoke_i4 in delegateext.cpp.
//------------------------------------------------------------------------------
Il2CppDelegate* il2cpp_delegate_new(void* methodPtr, Il2CppObject* target)
{
    EnsureDelegateRegistered();
    Il2CppDelegate* d = reinterpret_cast<Il2CppDelegate*>(
        il2cpp_gc_alloc(&g_delegateClass));
    d->methodPtr = methodPtr;
    d->target = target; // null for static-method delegates
    return d;
}

} // namespace il2cpp
