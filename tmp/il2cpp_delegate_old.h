#pragma once
//------------------------------------------------------------------------------
// il2cpp_delegate.h -- managed delegate type (M3 Task 8).
//
// .NET delegates are objects that hold a method pointer + optional target:
//
//   struct Il2CppDelegate
//   {
//       Il2CppObject obj;     // object header
//       void*        methodPtr; // the target method's function pointer
//       Il2CppObject* target;  // the instance for instance methods (null for static)
//   };
//
//   Created by: ldftn (method pointer) + newobj <DelegateCtor> (wrap into a
//   delegate object). Invoked by: callvirt <Delegate::Invoke> -- the
//   generated call reads the method pointer and calls through it:
//     ((RetType(*)(...))d->methodPtr)(args...)
//
//   Instance-method delegates (target binding) are supported: pass the
//   instance to il2cpp_delegate_new and the invoke side passes it back as
//   the method's `this` (see delegateext.cpp icall_multicast_invoke_i4).
//   Multicast (+=/Combine, -=/Remove) lives in il2cpp_delegate_ext.h;
//   the full System.Delegate/System.MulticastDelegate API surface is
//   M3.5+.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {

// Managed delegate (see header comment).
struct Il2CppDelegate
{
    Il2CppObject  obj;       // object header (klass pointer)
    void*         methodPtr; // target method function pointer
    Il2CppObject* target;    // instance target (null for static methods)
};

// Create a delegate wrapping a method pointer (ldftn + newobj DelegateCtor).
// 'target' is the instance for instance-method delegates (the object the
// method's `this` binds to at invoke time) and must be null for static
// methods. The target is a traced GC reference (the delegate's class
// descriptor marks slot 2), so a rooted delegate keeps its instance alive.
//
// methodPtr comes FIRST so the transpiler's single-argument emission
// `il2cpp_delegate_new(&<method>)` keeps compiling: target defaults to null
// (static-method delegate, the M3 behavior).
Il2CppDelegate* il2cpp_delegate_new(void* methodPtr, Il2CppObject* target = nullptr);

} // namespace il2cpp
