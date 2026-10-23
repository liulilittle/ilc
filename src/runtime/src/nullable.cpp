//------------------------------------------------------------------------------
// nullable.cpp -- System.Nullable`1<T> runtime support (see il2cpp_nullable.h).
//
// The representation is the transpiler contract: a Nullable<T> VALUE is an
// Il2CppObject* -- nullptr = HasValue false, boxed T = HasValue true. Every
// helper here therefore works on the OBJECT (never on a nullable struct).
//
// The DateTime/TimeSpan/Guid box/unbox icalls live here as well: they are the
// payload side of the same boxed-object model and were the missing runtime
// surface (ScalarTable named icall_box_date_time etc. but no definition
// existed, so `(object)dt` / `DateTime?` boxes could never link).
//------------------------------------------------------------------------------

#include "il2cpp_nullable.h"

#include "decimal_format.h"          // Il2CppDecimal payload (Hash/Equals dispatch)
#include "il2cpp_boxing_ext.h"       // icall_boxed_equals (legacy primitive set)
#include "il2cpp_boxing_ext2.h"      // icall_boxed_equals_ext2 (the superset)
#include "il2cpp_exception.h"        // il2cpp_throw_new
#include "il2cpp_exception_ext.h"    // il2cpp_throw_invalid_operation_exception
#include "il2cpp_format.h"           // icall_int32_to_string / icall_int64_to_string
#include "il2cpp_gc.h"               // il2cpp_gc_alloc_size
#include "il2cpp_metadata.h"         // System_DateTime_class / System_Guid_class ...
#include "il2cpp_object.h"
#include "il2cpp_string_intern.h"    // il2cpp_string_intern_literal("")
#include "il2cpp_time.h"
#include "il2cpp_value_semantics.h"  // icall_object_to_string / get_hash_code
#include "typeinfo.h"                // il2cpp_typeinfo_lookup (user-struct ToString)

#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

// One aligned payload slot after the object header -- the SAME layout every
// box class in the runtime uses (boxing.cpp / boxingext.cpp / boxingext2.cpp).
constexpr int32_t kNullableBoxPayloadOffset = 8;

[[noreturn]] void ThrowNullableNoValue()
{
    il2cpp_throw_invalid_operation_exception("Nullable object must have a value.");
}

// The boxed payload as a DateTime / TimeSpan / Guid value.
Il2CppDateTime BoxedDateTime(Il2CppObject* obj)
{
    Il2CppDateTime value;
    std::memcpy(&value, reinterpret_cast<char*>(obj) + kNullableBoxPayloadOffset,
                sizeof(value));
    return value;
}

Il2CppTimeSpan BoxedTimeSpan(Il2CppObject* obj)
{
    Il2CppTimeSpan value;
    std::memcpy(&value, reinterpret_cast<char*>(obj) + kNullableBoxPayloadOffset,
                sizeof(value));
    return value;
}

Il2CppGuid BoxedGuid(Il2CppObject* obj)
{
    Il2CppGuid value;
    std::memcpy(&value, reinterpret_cast<char*>(obj) + kNullableBoxPayloadOffset,
                sizeof(value));
    return value;
}

[[noreturn]] void ThrowBoxNull()
{
    il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                     "Object reference not set to an instance of an object.");
}

[[noreturn]] void ThrowBoxInvalidCast(const char* typeName)
{
    const char* prefix = "Specified cast is not valid: object is not a boxed ";
    char message[160];
    size_t n = 0;
    for (const char* p = prefix; *p != 0 && n < sizeof(message) - 1; ++p) message[n++] = *p;
    for (const char* p = typeName; *p != 0 && n < sizeof(message) - 1; ++p) message[n++] = *p;
    if (n < sizeof(message) - 1) message[n++] = '.';
    message[n] = 0;
    il2cpp_throw_new("System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
                     message);
}

// A box of the expected BCL value type? (exact descriptor identity; the box
// icalls stamp the metadata.cpp descriptor, so a pointer compare is exact).
bool IsBoxOf(Il2CppObject* obj, Il2CppClass* klass)
{
    return obj->klass == klass;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_nullable_require_value -- the no-value guard of get_Value: net48
// throws InvalidOperationException("Nullable object must have a value.").
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_nullable_require_value(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowNullableNoValue();
    }
    return obj;
}

//------------------------------------------------------------------------------
// il2cpp_nullable_to_string -- net48 Nullable<T>.ToString(): the EMPTY string
// when there is no value (never null, never "null"), otherwise the underlying
// value's ToString (the boxed-value dispatch).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_nullable_to_string(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        return il2cpp_string_intern_literal("");
    }
    return reinterpret_cast<Il2CppObject*>(icall_object_to_string(obj));
}

//------------------------------------------------------------------------------
// il2cpp_nullable_get_hash_code -- net48 Nullable<T>.GetHashCode(): 0 when
// there is no value, the underlying value's hash otherwise.
//------------------------------------------------------------------------------
int32_t il2cpp_nullable_get_hash_code(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        return 0;
    }
    return icall_object_get_hash_code(obj);
}

//------------------------------------------------------------------------------
// il2cpp_nullable_equals -- net48 Nullable<T>.Equals(object other):
//   both no-value            -> true
//   exactly one no-value     -> false
//   both have a value        -> the boxed payloads compare by value
//------------------------------------------------------------------------------
bool il2cpp_nullable_equals(Il2CppObject* a, Il2CppObject* b)
{
    if (a == nullptr || b == nullptr)
    {
        return a == b;
    }
    return icall_boxed_equals_ext2(a, b);
}

//------------------------------------------------------------------------------
// icall_box_date_time / icall_unbox_date_time
//------------------------------------------------------------------------------
Il2CppObject* icall_box_date_time(Il2CppDateTime value)
{
    Il2CppObject* boxed = il2cpp_gc_alloc_size(&System_DateTime_class,
                                              kNullableBoxPayloadOffset + sizeof(Il2CppDateTime));
    std::memcpy(reinterpret_cast<char*>(boxed) + kNullableBoxPayloadOffset, &value,
                sizeof(value));
    return boxed;
}

Il2CppDateTime icall_unbox_date_time(Il2CppObject* obj)
{
    if (obj == nullptr) ThrowBoxNull();
    if (!IsBoxOf(obj, &System_DateTime_class)) ThrowBoxInvalidCast("DateTime");
    return BoxedDateTime(obj);
}

//------------------------------------------------------------------------------
// icall_box_time_span / icall_unbox_time_span
//------------------------------------------------------------------------------
Il2CppObject* icall_box_time_span(Il2CppTimeSpan value)
{
    Il2CppObject* boxed = il2cpp_gc_alloc_size(&System_TimeSpan_class,
                                              kNullableBoxPayloadOffset + sizeof(Il2CppTimeSpan));
    std::memcpy(reinterpret_cast<char*>(boxed) + kNullableBoxPayloadOffset, &value,
                sizeof(value));
    return boxed;
}

Il2CppTimeSpan icall_unbox_time_span(Il2CppObject* obj)
{
    if (obj == nullptr) ThrowBoxNull();
    if (!IsBoxOf(obj, &System_TimeSpan_class)) ThrowBoxInvalidCast("TimeSpan");
    return BoxedTimeSpan(obj);
}

//------------------------------------------------------------------------------
// icall_box_guid / icall_unbox_guid -- the 16-byte payload copies verbatim
// (the guid.h byte layout).
//------------------------------------------------------------------------------
Il2CppObject* icall_box_guid(const Il2CppGuid* value)
{
    Il2CppObject* boxed = il2cpp_gc_alloc_size(&System_Guid_class,
                                              kNullableBoxPayloadOffset + sizeof(Il2CppGuid));
    std::memcpy(reinterpret_cast<char*>(boxed) + kNullableBoxPayloadOffset, value,
                sizeof(*value));
    return boxed;
}

Il2CppGuid icall_unbox_guid(Il2CppObject* obj)
{
    if (obj == nullptr) ThrowBoxNull();
    if (!IsBoxOf(obj, &System_Guid_class)) ThrowBoxInvalidCast("Guid");
    return BoxedGuid(obj);
}

//------------------------------------------------------------------------------
// il2cpp_user_struct_to_string -- a translated USER value type's ToString
// (the constrained callvirt shape). The type's reflected method table carries
// the override and its vtable slot; the receiver is the value ADDRESS (the
// value-type `this` ABI, the il2cpp_unbox_user_struct payload contract).
// A struct with no reflected override answers its type name (net48
// Object.ToString).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_user_struct_to_string(Il2CppClass* klass, void* valueAddr)
{
    if (klass != nullptr && valueAddr != nullptr)
    {
        if (const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(klass->name))
        {
            for (int32_t i = 0; i < info->methodCount; ++i)
            {
                const Il2CppMethodInfo& m = info->methods[i];
                if (m.name != nullptr && std::strcmp(m.name, "ToString") == 0 &&
                    m.paramCount == 0 && m.virtualSlot >= 0 &&
                    klass->vtable != nullptr && m.virtualSlot < klass->vtableSize &&
                    klass->vtable[m.virtualSlot] != nullptr)
                {
                    using ToStringFn = Il2CppObject* (*)(void*);
                    return reinterpret_cast<ToStringFn>(
                        klass->vtable[m.virtualSlot])(valueAddr);
                }
            }
        }
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new_from_utf8(klass != nullptr ? klass->name : "System.ValueType"));
}

} // namespace runtime
} // namespace il2cpp
