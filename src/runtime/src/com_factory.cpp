//------------------------------------------------------------------------------
// com_factory.cpp -- System.Runtime.InteropServices.ExtensibleClassFactory
// (p0c D11).
//
// net48's RegisterObjectCreationCallback is an InternalCall that registers a
// per-thread object-creation callback for the COM class whose CONSTRUCTOR is
// running. MEASURED (probe-p0c, .NET Framework 4.8 x64):
//   * called from ordinary code -> InvalidOperationException, message
//     "Object creation callback can only be registered from within the class
//     constructor of a COM creatable class."
//   * called with null         -> ArgumentNullException (checked FIRST).
// There is no COM-creatable class context in this runtime (com.cpp models the
// COM SURFACE -- interface casting, HRESULTs, apartment state -- but no
// managed class factory, and no way for a managed ctor to become one), so the
// two measured outcomes ARE the complete reachable behavior. Implementing them
// keeps a program that probes the API in agreement with net48 instead of
// crashing on an unwired phantom symbol.
//------------------------------------------------------------------------------

#include "com_factory.h"

#include "il2cpp_delegate.h"      // Il2CppDelegate / System_Delegate_class
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_null_exception / invalid_operation
#include "il2cpp_metadata.h"      // System_Object_class
#include "il2cpp_runtime.h"       // il2cpp_class_register

//------------------------------------------------------------------------------
// System.Runtime.InteropServices.ObjectCreationDelegate
// (`public delegate IntPtr ObjectCreationDelegate(IntPtr aggregator)`).
//
// The transpiler's BCL-delegate rewrite table (CodeGenerator BclAllocExpr /
// the isBclDelegateCtor list) covers Action / Predicate / Converter /
// Comparison / UnhandledExceptionEventHandler only, so `new
// ObjectCreationDelegate(m)` falls to the generic path: il2cpp_object_new on
// this descriptor + a generated-name ctor. The descriptor therefore has to
// exist, and icall_object_creation_delegate_ctor below plays the role the
// rewritten ctor would: it initializes the ALREADY-ALLOCATED delegate object
// (the standard (object target, IntPtr method) shape), exactly like
// il2cpp_delegate_new does for the table-covered types.
//------------------------------------------------------------------------------
::il2cpp::Il2CppClass System_Runtime_InteropServices_ObjectCreationDelegate_class = {
    "System.Runtime.InteropServices.ObjectCreationDelegate",
    static_cast<int32_t>(sizeof(::il2cpp::Il2CppDelegate)),
    0b00000100u, // refFieldBitmap: slot 2 (target) is a GC reference
    1,
    &il2cpp::System_Delegate_class, // baseClass (isinst walks up to System.Delegate)
    nullptr, 0, nullptr, 0,
};

namespace il2cpp { namespace runtime {

namespace {

bool g_objectCreationDelegateRegistered = false;

void EnsureObjectCreationDelegateRegistered()
{
    if (g_objectCreationDelegateRegistered)
    {
        return;
    }
    il2cpp_class_register(&System_Runtime_InteropServices_ObjectCreationDelegate_class);
    g_objectCreationDelegateRegistered = true;
}

} // namespace

void icall_object_creation_delegate_ctor(Il2CppObject* self, Il2CppObject* target, void* methodPtr)
{
    EnsureObjectCreationDelegateRegistered();
    auto* d = reinterpret_cast<Il2CppDelegate*>(self);
    d->methodPtr = methodPtr;
    d->target = target;
    d->typeId = 0; // untracked (same conservative default as the BCL rewrite path)
}

void icall_extensible_class_factory_register(Il2CppObject* creationCallback)
{
    // net48 checks the delegate argument before the context check.
    if (creationCallback == nullptr)
    {
        il2cpp_throw_argument_null_exception("creationCallback");
    }
    il2cpp_throw_invalid_operation_exception(
        "Object creation callback can only be registered from within the class "
        "constructor of a COM creatable class.");
}

}} // namespace il2cpp::runtime
