//------------------------------------------------------------------------------
// typedref.cpp -- the TypedReference carrier accessors (see
// il2cpp_typedref.h for the layout contract and the net48 measurements).
//
// All three opcodes funnel here so the carrier's fields are read/written in
// exactly one translation unit (no raw field access at any call site).
//------------------------------------------------------------------------------

#include "il2cpp_typedref.h"

#include "il2cpp_exception.h"

namespace il2cpp {

Il2CppTypedReference il2cpp_typedref_make(Il2CppClass* type, void* value)
{
    Il2CppTypedReference tr;
    tr.type = type;
    tr.value = value;
    return tr;
}

Il2CppClass* il2cpp_typedref_type(Il2CppTypedReference tr)
{
    return tr.type;
}

void* il2cpp_typedref_value(Il2CppTypedReference tr, Il2CppClass* expected)
{
    // net48: refanyval demands the EXACT declared type (measured:
    // string->object / derived->base / base->derived all throw
    // InvalidCastException), and a default(TypedReference) has no type.
    if (tr.type == nullptr || expected == nullptr || tr.type != expected)
    {
        // The thrown TYPE carries its base chain (comma-separated, the same
        // shape every other runtime throw site uses) so a generated
        // `catch (System.Exception)` / `catch (System.SystemException)`
        // matches, while GetType().FullName still reads the FIRST segment
        // (System.InvalidCastException -- net48 measured).
        il2cpp_throw_new("System.InvalidCastException,System.InvalidCastException,"
                         "System.SystemException,System.Exception",
                         "Object cannot be converted to the type of the TypedReference.");
    }
    return tr.value;
}

} // namespace il2cpp
