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

// The Delegate class descriptor (GLOBAL scope -- batch 162, eventcase: the
// transpiler's isinst/castclass on BCL delegates (System.EventHandler etc.)
// resolves to this symbol; every delegate object carries this class or the
// multicast class whose baseClass points here).
extern Il2CppClass System_Delegate_class;

// Managed delegate (see header comment).
struct Il2CppDelegate
{
    Il2CppObject  obj;       // object header (klass pointer)
    void*         methodPtr; // target method function pointer
    Il2CppObject* target;    // instance target (null for static methods)
    // BUG-2 (batch 177b): the delegate's TYPE ID -- FNV-1a of its managed
    // full type name (stamped by the transpiler at creation; 0 = untracked).
    // The runtime cannot recover a delegate's type from the raw methodPtr
    // (every leaf shares the System.Delegate descriptor), so the id rides on
    // the object and icall_delegate_combine compares it to reject a
    // heterogeneous combine (net48 ArgumentException).
    uint64_t      typeId;
};

// Create a delegate wrapping a method pointer (ldftn + newobj DelegateCtor).
// 'target' is the instance for instance-method delegates (the object the
// method's `this` binds to at invoke time) and must be null for static
// methods. The target is a traced GC reference (the delegate's class
// descriptor marks slot 2), so a rooted delegate keeps its instance alive.
// 'typeId' (BUG-2, batch 177b) is the delegate-type ID (FNV-1a of the full
// type name) the transpiler stamps at creation; 0 = untracked (the runtime
// Combine check is skipped for such delegates -- conservative).
//
// methodPtr comes FIRST so the transpiler's single-argument emission
// `il2cpp_delegate_new(&<method>)` keeps compiling: target defaults to null
// (static-method delegate, the M3 behavior) and typeId defaults to 0.
Il2CppDelegate* il2cpp_delegate_new(void* methodPtr, Il2CppObject* target = nullptr,
                                    uint64_t typeId = 0);

} // namespace il2cpp
