#pragma once
//------------------------------------------------------------------------------
// il2cpp_nullable.h -- System.Nullable`1<T> support (GN-2 / nullable domain).
//
// The runtime models a Nullable<T> VALUE as an Il2CppObject*:
//     nullptr      -> HasValue == false (the default/nullable "no value")
//     boxed T      -> HasValue == true, Value == the boxed payload
//
// There is no generated C++ struct for Nullable<T> and no nullable box class.
// The transpiler maps the BCL members to the helpers below (it also inlines
// the trivial ones: HasValue is a null test, the ctor boxes the underlying T
// with the ordinary box helper).
//
// net48 contracts reproduced here:
//   * get_Value on the no-value path throws InvalidOperationException with
//     the exact net48 message ("Nullable object must have a value.").
//   * ToString() on the no-value path returns the EMPTY STRING (never null,
//     never "null"); the has-value path is the underlying value's ToString.
//   * GetHashCode() on the no-value path returns 0.
//   * Equals(object): both no-value -> true; exactly one no-value -> false;
//     otherwise the underlying value's boxed equality.
//
// The DateTime / TimeSpan / Guid box helpers live here too: the 16-byte /
// 8-byte BCL value types need a real box class (System.DateTime /
// System.TimeSpan / System.Guid descriptors, metadata.cpp) so a boxed value
// carries its own type identity for ToString/GetHashCode/Equals dispatch --
// exactly like the primitive boxes. The unbox helpers are the inverse and
// throw NRE / InvalidCastException like every other unbox icall.
//------------------------------------------------------------------------------

#include "guid.h"
#include "il2cpp_dtoffset.h"
#include "il2cpp_object.h"
#include "il2cpp_time.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// Null-receiver guard for the value-returning members: returns obj unchanged
// when it holds a value, throws InvalidOperationException otherwise.
Il2CppObject* il2cpp_nullable_require_value(Il2CppObject* obj);

// Nullable<T>.ToString(): "" when no value, the boxed value's text otherwise.
Il2CppObject* il2cpp_nullable_to_string(Il2CppObject* obj);

// Nullable<T>.GetHashCode(): 0 when no value.
int32_t il2cpp_nullable_get_hash_code(Il2CppObject* obj);

// Nullable<T>.Equals(object): null-aware comparison of the boxed payloads.
bool il2cpp_nullable_equals(Il2CppObject* a, Il2CppObject* b);

// Box/unbox for the 8-byte and 16-byte BCL value types (the Nullable<T>
// ctor/value paths and the ordinary box/unbox IL).
Il2CppObject* icall_box_date_time(Il2CppDateTime value);
Il2CppDateTime icall_unbox_date_time(Il2CppObject* obj);
Il2CppObject* icall_box_time_span(Il2CppTimeSpan value);
Il2CppTimeSpan icall_unbox_time_span(Il2CppObject* obj);
Il2CppObject* icall_box_guid(const Il2CppGuid* value);
Il2CppGuid icall_unbox_guid(Il2CppObject* obj);
// dtoffset (delta 2): the 16-byte System.DateTimeOffset box (the ScalarTable
// box/unbox names finally get their runtime definitions, exactly like the
// DateTime/TimeSpan/Guid helpers above). The List<DateTimeOffset> element
// family boxes through these.
Il2CppObject* icall_box_date_time_offset(const Il2CppDateTimeOffset* value);
Il2CppDateTimeOffset icall_unbox_date_time_offset(Il2CppObject* obj);

// A translated USER value type's ToString (the constrained callvirt on a
// struct receiver): finds the type's reflected ToString override through the
// type's OWN descriptor and calls it with the value ADDRESS (the value-type
// `this` ABI). Falls back to the type name when the struct has no reflected
// override (net48's Object.ToString default).
Il2CppObject* il2cpp_user_struct_to_string(Il2CppClass* klass, void* valueAddr);

} // namespace runtime
} // namespace il2cpp
