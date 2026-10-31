#pragma once
//------------------------------------------------------------------------------
// il2cpp_typedref.h -- the TypedReference carrier (ECMA-335 III.4.22/4.23/4.26:
// mkrefany 0xC6 / refanytype 0xFE1D / refanyval 0xC2).
//
// net48 layout (MEASURED on Framework64 v4.0.30319 -- not from memory):
//   typeof(TypedReference) is a sealed SequentialLayout value type whose
//   instance fields are `IntPtr Value` + `IntPtr Type` only (two pointer-sized
//   slots; Marshal.SizeOf == 16 on x64). The Reference Source
//   mscorlib/system/typedreference.cs carries the same two members (m_value /
//   m_type); the historical third `m_klass` slot is NOT part of the shipped
//   net48 struct, so it is not modelled here.
//
// Runtime mapping (single authority -- no call site may touch the fields
// directly; use the accessors below):
//   type  <- the declared type of the referenced storage, as this runtime's
//            ONE type identity: the Il2CppClass* descriptor (the same
//            descriptor typeof/GetTypeFromHandle/box/isinst use).
//   value <- the ADDRESS of the referenced storage (mkrefany records the
//            storage, it does not copy the value -- net48 alias semantics:
//            writing the original variable after __makeref is visible through
//            __refvalue).
//
// Semantics implemented here (net48 measured):
//   * __reftype(tr)   -> the declared type: the accessor returns the
//     descriptor; the generated code feeds it to the existing
//     System.Type::GetTypeFromHandle icall (the full-name-string Type model).
//   * __refvalue(tr,T)-> the storage address, but ONLY when T is the EXACT
//     declared type (net48 throws System.InvalidCastException for any other
//     type -- no assignability: string->object, derived->base and base->
//     derived all throw). A default(TypedReference) (all-zero carrier) has no
//     type at all -> every __refvalue throws InvalidCastException and
//     __reftype answers a null handle (Type.GetTypeFromHandle -> null).
//------------------------------------------------------------------------------

#include <cstddef>
#include <cstdint>

namespace il2cpp {

struct Il2CppClass;

// The managed TypedReference value. Field order/meaning is the authoritative
// layout: every access MUST go through il2cpp_typedref_make /
// il2cpp_typedref_type / il2cpp_typedref_value.
struct Il2CppTypedReference
{
    Il2CppClass* type;  // declared type descriptor (null for default(TR))
    void*        value; // address of the referenced storage (null for default)
};

// Construct a carrier: 'type' = the declared type descriptor, 'value' = the
// storage address (mkrefany). Plain value construction -- no allocation, no
// GC interaction (the collector never moves objects in this runtime).
Il2CppTypedReference il2cpp_typedref_make(Il2CppClass* type, void* value);

// The declared type descriptor (refanytype). Null for a default carrier; the
// consumer (Type.GetTypeFromHandle) turns null into a null System.Type.
Il2CppClass* il2cpp_typedref_type(Il2CppTypedReference tr);

// The storage address presented as 'expected' (refanyval). Requires the EXACT
// declared type: tr.type != expected (or a default carrier) throws
// System.InvalidCastException (net48); otherwise returns tr.value.
void* il2cpp_typedref_value(Il2CppTypedReference tr, Il2CppClass* expected);

} // namespace il2cpp
