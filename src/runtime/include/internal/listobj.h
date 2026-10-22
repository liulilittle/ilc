#pragma once
// listobj.h -- managed System.Collections.Generic.List<object> runtime
// Generic object List fallback for List<T> where T is reference type.
// Layout mirrors listi4 but element array is reference array (elemSize 8, isRef true)
// and element ops use object pointer semantics with write barriers.

#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "sortedlist.h"
#include "decimal.h"      // p0linq4 (D1): Il2CppDecimal (List<decimal> elements)
#include "il2cpp_time.h"  // p0linq4 (D1): Il2CppDateTime / Il2CppTimeSpan
#include <cstddef>
#include <cstdint>

namespace il2cpp {

struct Il2CppListObj
{
    Il2CppObject obj;
    Il2CppArray* items;
    int32_t count;
};

Il2CppClass* il2cpp_list_obj_class();
// ilistException: klass-identity predicate for the ienum dispatcher --
// g_listObjClass is file-local to listobj.cpp, so arraysort.cpp routes
// through this instead of comparing descriptors directly.
bool il2cpp_is_list_obj(Il2CppObject* obj);
Il2CppObject* il2cpp_list_obj_create();
Il2CppObject* il2cpp_list_obj_create_with_capacity(int32_t capacity);
void il2cpp_list_obj_add(Il2CppObject* self, Il2CppObject* value);
void il2cpp_list_obj_insert(Il2CppObject* self, int32_t index, Il2CppObject* value);
void il2cpp_list_obj_remove_at(Il2CppObject* self, int32_t index);
bool il2cpp_list_obj_remove(Il2CppObject* self, Il2CppObject* value);
Il2CppObject* il2cpp_list_obj_get_item(Il2CppObject* self, int32_t index);
void il2cpp_list_obj_set_item(Il2CppObject* self, int32_t index, Il2CppObject* value);
int32_t il2cpp_list_obj_get_count(Il2CppObject* self);
void il2cpp_list_obj_clear(Il2CppObject* self);
int32_t il2cpp_list_obj_index_of(Il2CppObject* self, Il2CppObject* value);
bool il2cpp_list_obj_contains(Il2CppObject* self, Il2CppObject* value);
Il2CppObject* il2cpp_list_obj_to_array(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_get_enumerator(Il2CppObject* self);
void il2cpp_list_obj_sort(Il2CppObject* self);
void il2cpp_list_obj_copy_to(Il2CppObject* self, Il2CppObject* array, int32_t arrayIndex);
int32_t il2cpp_list_obj_enumerator_move_next(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_enumerator_get_current(Il2CppObject* self);
void il2cpp_list_obj_enumerator_reset(Il2CppObject* self);
void il2cpp_list_obj_enumerator_dispose(Il2CppObject* self);
// G4: System.Int64 element expansion -- values box on entry and unbox on
// exit through the runtime's Int64 box identity (icall_box_long /
// icall_unbox_long), so IndexOf/Contains/Remove and Sort observe net48 value
// semantics without a separate storage layout.
void il2cpp_list_obj_add_long(Il2CppObject* self, int64_t value);
void il2cpp_list_obj_insert_long(Il2CppObject* self, int32_t index, int64_t value);
void il2cpp_list_obj_set_item_long(Il2CppObject* self, int32_t index, int64_t value);
bool il2cpp_list_obj_remove_long(Il2CppObject* self, int64_t value);
int32_t il2cpp_list_obj_index_of_long(Il2CppObject* self, int64_t value);
bool il2cpp_list_obj_contains_long(Il2CppObject* self, int64_t value);
int64_t il2cpp_list_obj_get_item_long(Il2CppObject* self, int32_t index);
int64_t il2cpp_list_obj_enumerator_get_current_long(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_long(Il2CppObject* self);

// G4: List<double> rides the same listobj storage; the value crosses the
// boundary through the runtime's Double box identity (icall_box_double /
// icall_unbox_double), so IndexOf/Contains/Remove and Sort observe net48
// value equality and ordering (ComparerCompareCore compares the Double box
// payload by value).
void il2cpp_list_obj_add_double(Il2CppObject* self, double value);
void il2cpp_list_obj_insert_double(Il2CppObject* self, int32_t index, double value);
void il2cpp_list_obj_set_item_double(Il2CppObject* self, int32_t index, double value);
bool il2cpp_list_obj_remove_double(Il2CppObject* self, double value);
int32_t il2cpp_list_obj_index_of_double(Il2CppObject* self, double value);
bool il2cpp_list_obj_contains_double(Il2CppObject* self, double value);
double il2cpp_list_obj_get_item_double(Il2CppObject* self, int32_t index);
double il2cpp_list_obj_enumerator_get_current_double(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_double(Il2CppObject* self);

// W5 (T2): small-value element expansions -- the same box-on-entry /
// unbox-on-exit discipline as the G4 long/double families above. float
// crosses as double (icall_box_float / icall_unbox_float);
// bool/char/sbyte/byte/short/ushort/uint cross as int32 (the boxing_ext /
// _ext2 pair). ToArray materialises the typed value array (sizeof(T)
// stride, narrowed on store).
void il2cpp_list_obj_add_float(Il2CppObject* self, double value);
void il2cpp_list_obj_insert_float(Il2CppObject* self, int32_t index, double value);
void il2cpp_list_obj_set_item_float(Il2CppObject* self, int32_t index, double value);
bool il2cpp_list_obj_remove_float(Il2CppObject* self, double value);
int32_t il2cpp_list_obj_index_of_float(Il2CppObject* self, double value);
bool il2cpp_list_obj_contains_float(Il2CppObject* self, double value);
double il2cpp_list_obj_get_item_float(Il2CppObject* self, int32_t index);
double il2cpp_list_obj_enumerator_get_current_float(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_float(Il2CppObject* self);
void il2cpp_list_obj_add_bool(Il2CppObject* self, int32_t value);
void il2cpp_list_obj_insert_bool(Il2CppObject* self, int32_t index, int32_t value);
void il2cpp_list_obj_set_item_bool(Il2CppObject* self, int32_t index, int32_t value);
bool il2cpp_list_obj_remove_bool(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_index_of_bool(Il2CppObject* self, int32_t value);
bool il2cpp_list_obj_contains_bool(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_get_item_bool(Il2CppObject* self, int32_t index);
int32_t il2cpp_list_obj_enumerator_get_current_bool(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_bool(Il2CppObject* self);
void il2cpp_list_obj_add_char(Il2CppObject* self, int32_t value);
void il2cpp_list_obj_insert_char(Il2CppObject* self, int32_t index, int32_t value);
void il2cpp_list_obj_set_item_char(Il2CppObject* self, int32_t index, int32_t value);
bool il2cpp_list_obj_remove_char(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_index_of_char(Il2CppObject* self, int32_t value);
bool il2cpp_list_obj_contains_char(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_get_item_char(Il2CppObject* self, int32_t index);
int32_t il2cpp_list_obj_enumerator_get_current_char(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_char(Il2CppObject* self);
void il2cpp_list_obj_add_sbyte(Il2CppObject* self, int32_t value);
void il2cpp_list_obj_insert_sbyte(Il2CppObject* self, int32_t index, int32_t value);
void il2cpp_list_obj_set_item_sbyte(Il2CppObject* self, int32_t index, int32_t value);
bool il2cpp_list_obj_remove_sbyte(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_index_of_sbyte(Il2CppObject* self, int32_t value);
bool il2cpp_list_obj_contains_sbyte(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_get_item_sbyte(Il2CppObject* self, int32_t index);
int32_t il2cpp_list_obj_enumerator_get_current_sbyte(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_sbyte(Il2CppObject* self);
void il2cpp_list_obj_add_byte(Il2CppObject* self, int32_t value);
void il2cpp_list_obj_insert_byte(Il2CppObject* self, int32_t index, int32_t value);
void il2cpp_list_obj_set_item_byte(Il2CppObject* self, int32_t index, int32_t value);
bool il2cpp_list_obj_remove_byte(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_index_of_byte(Il2CppObject* self, int32_t value);
bool il2cpp_list_obj_contains_byte(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_get_item_byte(Il2CppObject* self, int32_t index);
int32_t il2cpp_list_obj_enumerator_get_current_byte(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_byte(Il2CppObject* self);
void il2cpp_list_obj_add_short(Il2CppObject* self, int32_t value);
void il2cpp_list_obj_insert_short(Il2CppObject* self, int32_t index, int32_t value);
void il2cpp_list_obj_set_item_short(Il2CppObject* self, int32_t index, int32_t value);
bool il2cpp_list_obj_remove_short(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_index_of_short(Il2CppObject* self, int32_t value);
bool il2cpp_list_obj_contains_short(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_get_item_short(Il2CppObject* self, int32_t index);
int32_t il2cpp_list_obj_enumerator_get_current_short(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_short(Il2CppObject* self);
void il2cpp_list_obj_add_ushort(Il2CppObject* self, int32_t value);
void il2cpp_list_obj_insert_ushort(Il2CppObject* self, int32_t index, int32_t value);
void il2cpp_list_obj_set_item_ushort(Il2CppObject* self, int32_t index, int32_t value);
bool il2cpp_list_obj_remove_ushort(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_index_of_ushort(Il2CppObject* self, int32_t value);
bool il2cpp_list_obj_contains_ushort(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_get_item_ushort(Il2CppObject* self, int32_t index);
int32_t il2cpp_list_obj_enumerator_get_current_ushort(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_ushort(Il2CppObject* self);
void il2cpp_list_obj_add_uint(Il2CppObject* self, int32_t value);
void il2cpp_list_obj_insert_uint(Il2CppObject* self, int32_t index, int32_t value);
void il2cpp_list_obj_set_item_uint(Il2CppObject* self, int32_t index, int32_t value);
bool il2cpp_list_obj_remove_uint(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_index_of_uint(Il2CppObject* self, int32_t value);
bool il2cpp_list_obj_contains_uint(Il2CppObject* self, int32_t value);
int32_t il2cpp_list_obj_get_item_uint(Il2CppObject* self, int32_t index);
int32_t il2cpp_list_obj_enumerator_get_current_uint(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_uint(Il2CppObject* self);

//------------------------------------------------------------------------------
// p0linq4 (D1) -- List<decimal> / List<DateTime> / List<TimeSpan> /
// List<user struct>.
//
// The listobj STORAGE is a reference array: an element is always an
// Il2CppObject*. A 16-byte decimal therefore cannot ride the slot directly --
// every value element is BOXED on entry and UNBOXED on exit (the G4
// long/double precedent, extended to the remaining value families):
//
//   decimal    -> icall_box_decimal / icall_unbox_decimal  (System.Decimal box,
//                 16-byte payload at offset 8)
//   DateTime   -> the runtime Int64 box carrying the CLR ARRAY image
//                 (il2cpp_datetime_to_array_bits: 62-bit ticks + 2-bit kind),
//                 so the List element observes the same value the managed
//                 DateTime[] cell holds
//   TimeSpan   -> the Int64 box carrying the raw ticks
//   user struct-> icall_box_user_struct(klass, value, sizeof(T)) -- the box's
//                 klass is the struct's own generated descriptor
//
// Element reads use the same by-value / result-slot shapes as the rest of the
// runtime:
//   * decimal getters write through an explicit RESULT SLOT (the decimal T3
//     contract: a 16-byte value cannot ride the return register);
//   * DateTime/TimeSpan getters RETURN the 8/16-byte runtime struct by value;
//   * a user-struct getter returns the box PAYLOAD ADDRESS (the caller copies
//     it into its own value slot -- the il2cpp_unbox_user_struct contract).
//
// IndexOf/Contains/Remove for DateTime/TimeSpan/decimal reuse the boxed-value
// equality of the shared listobj scan (icall_object_equals -> BoxedValueEquals
// compares the decimal payload by value and the Int64 payload by bits, which
// is exactly the packed array image), so no separate scan entry is needed.
//------------------------------------------------------------------------------
void il2cpp_list_obj_add_dec(Il2CppObject* self, const runtime::Il2CppDecimal* value);
void il2cpp_list_obj_insert_dec(Il2CppObject* self, int32_t index, const runtime::Il2CppDecimal* value);
void il2cpp_list_obj_set_item_dec(Il2CppObject* self, int32_t index, const runtime::Il2CppDecimal* value);
bool il2cpp_list_obj_remove_dec(Il2CppObject* self, const runtime::Il2CppDecimal* value);
int32_t il2cpp_list_obj_index_of_dec(Il2CppObject* self, const runtime::Il2CppDecimal* value);
bool il2cpp_list_obj_contains_dec(Il2CppObject* self, const runtime::Il2CppDecimal* value);
// The decimal GETTERS write through an explicit RESULT SLOT (the decimal T3
// contract: a 16-byte value cannot ride the return register).
void il2cpp_list_obj_get_item_dec(runtime::Il2CppDecimal* result, Il2CppObject* self, int32_t index);
void il2cpp_list_obj_enumerator_get_current_dec(runtime::Il2CppDecimal* result, Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_dec(Il2CppObject* self);

void il2cpp_list_obj_add_dt(Il2CppObject* self, runtime::Il2CppDateTime value);
void il2cpp_list_obj_insert_dt(Il2CppObject* self, int32_t index, runtime::Il2CppDateTime value);
void il2cpp_list_obj_set_item_dt(Il2CppObject* self, int32_t index, runtime::Il2CppDateTime value);
bool il2cpp_list_obj_remove_dt(Il2CppObject* self, runtime::Il2CppDateTime value);
int32_t il2cpp_list_obj_index_of_dt(Il2CppObject* self, runtime::Il2CppDateTime value);
bool il2cpp_list_obj_contains_dt(Il2CppObject* self, runtime::Il2CppDateTime value);
runtime::Il2CppDateTime il2cpp_list_obj_get_item_dt(Il2CppObject* self, int32_t index);
runtime::Il2CppDateTime il2cpp_list_obj_enumerator_get_current_dt(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_dt(Il2CppObject* self);

void il2cpp_list_obj_add_ts(Il2CppObject* self, runtime::Il2CppTimeSpan value);
void il2cpp_list_obj_insert_ts(Il2CppObject* self, int32_t index, runtime::Il2CppTimeSpan value);
void il2cpp_list_obj_set_item_ts(Il2CppObject* self, int32_t index, runtime::Il2CppTimeSpan value);
bool il2cpp_list_obj_remove_ts(Il2CppObject* self, runtime::Il2CppTimeSpan value);
int32_t il2cpp_list_obj_index_of_ts(Il2CppObject* self, runtime::Il2CppTimeSpan value);
bool il2cpp_list_obj_contains_ts(Il2CppObject* self, runtime::Il2CppTimeSpan value);
runtime::Il2CppTimeSpan il2cpp_list_obj_get_item_ts(Il2CppObject* self, int32_t index);
runtime::Il2CppTimeSpan il2cpp_list_obj_enumerator_get_current_ts(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_ts(Il2CppObject* self);

void il2cpp_list_obj_add_struct(Il2CppObject* self, const void* value,
                                Il2CppClass* klass, int32_t payloadSize);
void il2cpp_list_obj_insert_struct(Il2CppObject* self, int32_t index, const void* value,
                                   Il2CppClass* klass, int32_t payloadSize);
void il2cpp_list_obj_set_item_struct(Il2CppObject* self, int32_t index, const void* value,
                                     Il2CppClass* klass, int32_t payloadSize);
bool il2cpp_list_obj_remove_struct(Il2CppObject* self, const void* value,
                                   Il2CppClass* klass, int32_t payloadSize);
int32_t il2cpp_list_obj_index_of_struct(Il2CppObject* self, const void* value,
                                        Il2CppClass* klass, int32_t payloadSize);
bool il2cpp_list_obj_contains_struct(Il2CppObject* self, const void* value,
                                     Il2CppClass* klass, int32_t payloadSize);
// The user-struct GETTER copies the boxed payload into the caller's value slot
// (the caller's C++ struct has no runtime-known type here).
void il2cpp_list_obj_get_item_struct(void* result, Il2CppObject* self, int32_t index,
                                     int32_t payloadSize);
void il2cpp_list_obj_enumerator_get_current_struct(void* result, Il2CppObject* self,
                                                   int32_t payloadSize);
Il2CppObject* il2cpp_list_obj_to_array_struct(Il2CppObject* self, int32_t elemSize);

//------------------------------------------------------------------------------
// p0linq4 (D1): typed USER-STRUCT conveniences. The generated call site knows
// the concrete C++ struct type, so these thin inline templates let it state the
// type once: the element is boxed with the struct's OWN descriptor (passed as
// the trailing `klass` argument) and the getter copies the payload into a
// value of the caller's type. The VALUE parameters are POINTERS so the
// emitter's uniform "addr:v" argument cast serves both shapes (a value
// expression takes its address, an ldloca address passes through).
//------------------------------------------------------------------------------
template <typename T>
inline void il2cpp_list_obj_add_struct_t(Il2CppObject* self, const T* value, Il2CppClass* klass)
{
    il2cpp_list_obj_add_struct(self, value, klass, static_cast<int32_t>(sizeof(T)));
}

template <typename T>
inline void il2cpp_list_obj_insert_struct_t(Il2CppObject* self, int32_t index, const T* value,
                                           Il2CppClass* klass)
{
    il2cpp_list_obj_insert_struct(self, index, value, klass, static_cast<int32_t>(sizeof(T)));
}

template <typename T>
inline void il2cpp_list_obj_set_item_struct_t(Il2CppObject* self, int32_t index, const T* value,
                                             Il2CppClass* klass)
{
    il2cpp_list_obj_set_item_struct(self, index, value, klass, static_cast<int32_t>(sizeof(T)));
}

template <typename T>
inline bool il2cpp_list_obj_remove_struct_t(Il2CppObject* self, const T* value, Il2CppClass* klass)
{
    return il2cpp_list_obj_remove_struct(self, value, klass, static_cast<int32_t>(sizeof(T)));
}

template <typename T>
inline int32_t il2cpp_list_obj_index_of_struct_t(Il2CppObject* self, const T* value,
                                                Il2CppClass* klass)
{
    return il2cpp_list_obj_index_of_struct(self, value, klass, static_cast<int32_t>(sizeof(T)));
}

template <typename T>
inline bool il2cpp_list_obj_contains_struct_t(Il2CppObject* self, const T* value, Il2CppClass* klass)
{
    return il2cpp_list_obj_contains_struct(self, value, klass, static_cast<int32_t>(sizeof(T)));
}

template <typename T>
inline T il2cpp_list_obj_get_item_struct_t(Il2CppObject* self, int32_t index)
{
    T out;
    il2cpp_list_obj_get_item_struct(&out, self, index, static_cast<int32_t>(sizeof(T)));
    return out;
}

template <typename T>
inline T il2cpp_list_obj_enumerator_get_current_struct_t(Il2CppObject* self)
{
    T out;
    il2cpp_list_obj_enumerator_get_current_struct(&out, self, static_cast<int32_t>(sizeof(T)));
    return out;
}

template <typename T>
inline Il2CppObject* il2cpp_list_obj_to_array_struct_t(Il2CppObject* self)
{
    return il2cpp_list_obj_to_array_struct(self, static_cast<int32_t>(sizeof(T)));
}

} // namespace il2cpp
