#pragma once
//------------------------------------------------------------------------------
// com_factory.h -- System.Runtime.InteropServices.ExtensibleClassFactory
// (p0c D11; implementation + measured net48 behavior in com_factory.cpp).
//
// RegisterObjectCreationCallback(ObjectCreationDelegate) reproduces net48's
// two reachable outcomes: ArgumentNullException for a null delegate, and
// InvalidOperationException for every caller (there is no COM-creatable class
// constructor in this runtime).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

// The ObjectCreationDelegate descriptor (see com_factory.cpp): the transpiler's
// BCL-delegate rewrite table does not cover this delegate type, so `new
// ObjectCreationDelegate(m)` allocates through this descriptor and initializes
// it with icall_object_creation_delegate_ctor.
extern il2cpp::Il2CppClass System_Runtime_InteropServices_ObjectCreationDelegate_class;

namespace il2cpp { namespace runtime {

void icall_extensible_class_factory_register(Il2CppObject* creationCallback);

// The (object target, IntPtr method) delegate ctor shape, applied to the
// already-allocated delegate object.
void icall_object_creation_delegate_ctor(Il2CppObject* self, Il2CppObject* target, void* methodPtr);

}} // namespace il2cpp::runtime
