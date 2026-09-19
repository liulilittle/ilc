//------------------------------------------------------------------------------
// boxing.cpp -- value-type boxing/unboxing and type-instance checks.
//
// M2 boxed-value model:
//   A boxed int32 is a MANAGED OBJECT whose payload is the 4-byte value at
//   byte offset 8 (slot 1). The object header is a normal Il2CppObject with a
//   dedicated box class descriptor -- the runtime's builtin Int32 box class
//   System_Int32_class (metadata.cpp): instanceSize 16 -- header + aligned
//   value slot --, the .NET interface table (IConvertible/IComparable/
//   IFormattable) and no reference fields. This mirrors .NET: a boxed int IS
//   an object; unboxing checks the runtime type and returns the payload.
//
//   Layout (16 bytes, 8-byte grid):
//     offset 0: klass      (Il2CppObject header)
//     offset 8: int32_t    (the boxed value)
//
//   The builtin descriptors (System_Int32 & co., metadata.cpp) are shared
//   with generated isinst/castclass code (they reference &System_Int32_class
//   by name), so pointer identity holds: boxed objects carry exactly the
//   descriptor the generated type checks compare against.
//
// il2cpp_is_instance -- .NET isinst/castclass semantics:
//   obj == null        -> false (isinst) / true (castclass of null -> null)
//   obj->klass == klass->... along the base chain (baseClass walk) or an
//   interface map hit (the class implements the interface). M2 walks
//   baseClass (user inheritance) and scans the interfaces table (linear).
//------------------------------------------------------------------------------

#include "il2cpp_exception.h" // il2cpp_throw_new (managed exception carrier)
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>

namespace il2cpp {

namespace {

// The Int32 box class descriptor is the BUILTIN System_Int32_class
// (metadata.cpp, extern in il2cpp_metadata.h) -- the SAME descriptor the
// transpiler's generated isinst/castclass code compares against
// (&System_Int32_class) and the registered descriptor that
// il2cpp_class_by_name("System.Int32") resolves. It carries the .NET
// interface table (IConvertible/IComparable/IFormattable) and the
// System.Object baseClass. Registered (with all builtin System.* descriptors)
// through il2cpp_ensure_builtin_system_classes(), lazily on the first
// box/unbox call.

void EnsureInt32BoxRegistered()
{
    // Idempotent: registers System.Object/String/Int32/Boolean/Char/Double/
    // IConvertible/IComparable/IFormattable exactly once.
    il2cpp_ensure_builtin_system_classes();
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_box_int32 -- allocate a boxed int32 on the managed heap.
//
//   Returns a GC object whose klass is the builtin Int32 box class
//   (System_Int32_class: instanceSize 16 -- header + aligned value slot,
//   interface table attached, empty ref bitmap) and whose payload slot
//   (offset 8) holds 'value'. The object is a normal managed reference (the
//   collector scans it with the box class's empty bitmap -- no fields).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_box_int32(Il2CppClass* klass, int32_t value)
{
    EnsureInt32BoxRegistered();
    Il2CppObject* boxed = il2cpp_gc_alloc(klass != nullptr ? klass : &System_Int32_class);
    // Payload at offset 8: write the value (zero-init already cleared slot 0).
    *reinterpret_cast<int32_t*>(reinterpret_cast<char*>(boxed) + 8) = value;
    return boxed;
}

//------------------------------------------------------------------------------
// il2cpp_unbox_int32 -- extract the int32 payload of a boxed int.
//
//   Type check: the object's klass MUST be the builtin Int32 box class
//   (System_Int32_class) or a subtype of it. A mismatch is an
//   InvalidCastException in .NET; this helper throws through the managed
//   carrier (il2cpp_throw_new, exception.cpp) so generated try/catch and the
//   top-level guard handle it like any other managed throw -- a wrong-type
//   unbox NEVER silently returns garbage and NEVER aborts the process.
//------------------------------------------------------------------------------
int32_t il2cpp_unbox_int32(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    EnsureInt32BoxRegistered();
    if (obj->klass != &System_Int32_class && !il2cpp_is_instance(obj, &System_Int32_class))
    {
        il2cpp_throw_new("System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
                         "Specified cast is not valid: object is not a boxed Int32.");
    }
    return *reinterpret_cast<int32_t*>(reinterpret_cast<char*>(obj) + 8);
}

//------------------------------------------------------------------------------
// il2cpp_is_instance -- .NET isinst/castclass type-conformance test.
//
//   obj  : the managed object (may be null -- isinst(null) is false)
//   klass: the target type (class or interface descriptor)
//
//   True when obj's runtime type is klass or derives from it (baseClass walk),
//   or when klass is an interface the runtime type implements (interfaces
//   table scan). False otherwise.
//------------------------------------------------------------------------------
bool il2cpp_is_instance(Il2CppObject* obj, Il2CppClass* klass)
{
    if (obj == nullptr || klass == nullptr) return false;
    Il2CppClass* cur = obj->klass;
    // Class conformance: walk the base chain.
    for (; cur != nullptr; cur = cur->baseClass)
    {
        if (cur == klass) return true;
    }
    // Interface conformance: scan the interfaces table of the runtime type
    // (M2 linear; a hash map is M3). Also scan base-class interface tables
    // (a derived type inherits its base's interfaces).
    for (Il2CppClass* scan = obj->klass; scan != nullptr; scan = scan->baseClass)
    {
        for (int32_t i = 0; i < scan->interfaceCount; ++i)
        {
            if (scan->interfaces[i].iface == klass) return true;
        }
    }
    return false;
}

} // namespace il2cpp
