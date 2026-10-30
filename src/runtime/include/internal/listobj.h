#pragma once
// listobj.h -- managed System.Collections.Generic.List<object> runtime
// Generic object List fallback for List<T> where T is reference type.
// Layout mirrors listi4 but element array is reference array (elemSize 8, isRef true)
// and element ops use object pointer semantics with write barriers.

#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "sortedlist.h"
#include "decimal.h"      // p0linq4 (D1): Il2CppDecimal (List<decimal> elements)
#include "guid.h"         // p0linq4 (D1): Il2CppGuid (List<Guid> elements)
#include "il2cpp_dtoffset.h" // delta 2: Il2CppDateTimeOffset (List<DateTimeOffset>)
#include "il2cpp_time.h"  // p0linq4 (D1): Il2CppDateTime / Il2CppTimeSpan
#include "il2cpp_delegate.h" // impl-listrange: by-value struct/tuple predicate leaves
#include <cstddef>
#include <cstdint>
#include <cstring>        // impl-listrange: by-value struct/tuple payload copies

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

//------------------------------------------------------------------------------
// List<T>(int capacity) / List<T>(IEnumerable<T>) -- the SELF-MUTATING ctor
// ABI (the il2cpp_list_i4_create_with_capacity/_with_collection convention):
// the transpiler's newobj already allocated the shell, so these initialize it
// in place and return void. They are the listobj-domain counterparts of the
// two single-argument net48 ctors for EVERY non-int32 element family:
//
//   il2cpp_list_obj_ctor_capacity(self, capacity)
//     Count 0; the items array length IS the Capacity, exactly 'capacity'
//     (capacity 0 keeps a zero-length array -- the first Add grows to the
//     4-slot minimum, net48 DefaultCapacity). capacity < 0 raises
//     ArgumentOutOfRangeException("capacity") (net48
//     ArgumentOutOfRange_MustBeNonNegNum).
//
//   il2cpp_list_obj_ctor_collection(self, source, kind)
//     a COPY of the source elements; for the wired ICollection<T> sources
//     (an array or a List<T>) the initial Capacity equals the source count
//     (net48), so an empty source yields Capacity 0. A null source raises
//     ArgumentNullException("collection"). 'kind' names the TARGET element
//     family (ListObjCtorKind below) and only selects how a VALUE-CELL array
//     source is read/re-boxed; a List source is already in the boxed listobj
//     storage and is copied by reference slot.
//
// The kind codes are LISTOBJ-LOCAL on purpose: this domain must not depend on
// the LINQ layer (il2cpp_linq_* owns its own, differently numbered
// IL2CPP_LIST_KIND_* contract in linq_list.h).
//------------------------------------------------------------------------------
enum ListObjCtorKind : int32_t
{
    kListObjCtorRef = 0,
    kListObjCtorLong = 1,
    kListObjCtorDouble = 2,
    kListObjCtorFloat = 3,
    kListObjCtorBool = 4,
    kListObjCtorChar = 5,
    kListObjCtorSbyte = 6,
    kListObjCtorByte = 7,
    kListObjCtorShort = 8,
    kListObjCtorUshort = 9,
    kListObjCtorUint = 10,
    kListObjCtorDecimal = 11,
    kListObjCtorDateTime = 12,
    kListObjCtorTimeSpan = 13,
    kListObjCtorGuid = 14,
    kListObjCtorDateTimeOffset = 15,
};
void il2cpp_list_obj_ctor_capacity(Il2CppObject* self, int32_t capacity);
void il2cpp_list_obj_ctor_collection(Il2CppObject* self, Il2CppObject* source, int32_t kind);
// The user-STRUCT / tuple element form: the value cell has no runtime-known
// width or box identity from the kind alone, so the call site passes the
// struct's OWN descriptor and the template fixes the payload size.
void il2cpp_list_obj_ctor_collection_struct(Il2CppObject* self, Il2CppObject* source,
                                            Il2CppClass* klass, int32_t payloadSize);

void il2cpp_list_obj_add(Il2CppObject* self, Il2CppObject* value);
void il2cpp_list_obj_insert(Il2CppObject* self, int32_t index, Il2CppObject* value);
void il2cpp_list_obj_remove_at(Il2CppObject* self, int32_t index);
bool il2cpp_list_obj_remove(Il2CppObject* self, Il2CppObject* value);
Il2CppObject* il2cpp_list_obj_get_item(Il2CppObject* self, int32_t index);
void il2cpp_list_obj_set_item(Il2CppObject* self, int32_t index, Il2CppObject* value);
int32_t il2cpp_list_obj_get_count(Il2CppObject* self);
// Capacity: the allocated backing-array length (>= Count).
int32_t il2cpp_list_obj_get_capacity(Il2CppObject* self);
// set_Capacity(value): a value below Count raises ArgumentOutOfRangeException
// ("value"); an equal capacity is a no-op; otherwise the items array is rebuilt
// at exactly 'value' slots (the element references are preserved).
void il2cpp_list_obj_set_capacity(Il2CppObject* self, int32_t value);
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
// Guid is a 16-byte value element; the boxed carrier is icall_box_guid.
// The VALUE parameters cross as `const uint8_t*` because the transpiler's
// Guid "g" cast renders exactly that (GuidValueBytePtr), and the getter
// writes through a `uint8_t*` result slot (the "guid" result kind appends
// reinterpret_cast<uint8_t*>(&target)).
void il2cpp_list_obj_add_guid(Il2CppObject* self, const uint8_t* value);
void il2cpp_list_obj_insert_guid(Il2CppObject* self, int32_t index, const uint8_t* value);
void il2cpp_list_obj_set_item_guid(Il2CppObject* self, int32_t index, const uint8_t* value);
bool il2cpp_list_obj_remove_guid(Il2CppObject* self, const uint8_t* value);
int32_t il2cpp_list_obj_index_of_guid(Il2CppObject* self, const uint8_t* value);
bool il2cpp_list_obj_contains_guid(Il2CppObject* self, const uint8_t* value);
void il2cpp_list_obj_get_item_guid(Il2CppObject* self, int32_t index, uint8_t* result);
void il2cpp_list_obj_enumerator_get_current_guid(Il2CppObject* self, uint8_t* result);
Il2CppObject* il2cpp_list_obj_to_array_guid(Il2CppObject* self);
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

// dtoffset (delta 2): the System.DateTimeOffset element family. The 16-byte
// value is boxed through icall_box_date_time_offset (il2cpp_nullable.h) into
// the listobj reference storage, exactly like the Guid family; the getters
// return the runtime value struct BY VALUE ("dto" result kind). Equality
// scans reuse the boxed-value compare (boxingext2.cpp, UTC-instant semantics).
void il2cpp_list_obj_add_dtoffset(Il2CppObject* self, runtime::Il2CppDateTimeOffset value);
void il2cpp_list_obj_insert_dtoffset(Il2CppObject* self, int32_t index, runtime::Il2CppDateTimeOffset value);
void il2cpp_list_obj_set_item_dtoffset(Il2CppObject* self, int32_t index, runtime::Il2CppDateTimeOffset value);
bool il2cpp_list_obj_remove_dtoffset(Il2CppObject* self, runtime::Il2CppDateTimeOffset value);
int32_t il2cpp_list_obj_index_of_dtoffset(Il2CppObject* self, runtime::Il2CppDateTimeOffset value);
bool il2cpp_list_obj_contains_dtoffset(Il2CppObject* self, runtime::Il2CppDateTimeOffset value);
runtime::Il2CppDateTimeOffset il2cpp_list_obj_get_item_dtoffset(Il2CppObject* self, int32_t index);
runtime::Il2CppDateTimeOffset il2cpp_list_obj_enumerator_get_current_dtoffset(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_to_array_dtoffset(Il2CppObject* self);

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

// List<T>(IEnumerable<T>) for a user-STRUCT / ValueTuple element: the value
// cells are boxed with the element's OWN descriptor (the same klass the Add
// forwarder uses), so the ctor and the element accessors agree on the box
// identity.
template <typename T>
inline void il2cpp_list_obj_ctor_collection_struct_t(Il2CppObject* self, Il2CppObject* source,
                                                     Il2CppClass* klass)
{
    il2cpp_list_obj_ctor_collection_struct(self, source, klass, static_cast<int32_t>(sizeof(T)));
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

//------------------------------------------------------------------------------
// impl-listrange: the RANGE / SEARCH / PREDICATE operator surface of the
// listobj reference storage -- EVERY non-int32 List<T> family.
//
// The storage is a reference array of BOXED elements, so the operations split
// into three classes:
//   * SLOT MOVES (Reverse / RemoveRange / GetRange / FindAll / the AddRange and
//     InsertRange slot copy): family-agnostic -- the element Il2CppObject* is
//     moved verbatim, never re-boxed;
//   * PREDICATE / ELEMENT-RETURN: the delegate is invoked through the ELEMENT
//     FAMILY'S OWN ABI (narrow cells widen to int32, float widens to double,
//     decimal and the BCL value structs cross BY VALUE, a user struct / tuple
//     crosses as its own C++ type);
//   * COMPARISON (IndexOf with startIndex/count, LastIndexOf, BinarySearch):
//     the boxed scan compares through the family's own equality / ordering
//     authority. System.DateTime compares TICKS ONLY (net48 DateTime.Equals /
//     CompareTo ignore DateTimeKind; the list stores the 62-bit ticks + 2-bit
//     kind ARRAY image boxed in an Int64, so a raw payload compare would let
//     the Kind bits decide -- a silent wrong value).
//
// The element-kind literal is the linq_list.h IL2CPP_LIST_KIND_* code:
//   0 int32   1 reference  2 int64   3 float   4 double   5 sbyte   6 byte
//   7 bool    8 short      9 ushort  10 char   11 uint    12 decimal
//   13 DateTime  14 TimeSpan  15 Guid  16 DateTimeOffset
//------------------------------------------------------------------------------
void il2cpp_list_obj_reverse(Il2CppObject* self);
void il2cpp_list_obj_reverse_range(Il2CppObject* self, int32_t index, int32_t count);
void il2cpp_list_obj_remove_range(Il2CppObject* self, int32_t index, int32_t count);
Il2CppObject* il2cpp_list_obj_get_range(Il2CppObject* self, int32_t index, int32_t count);

// AddRange(IEnumerable<T>) / InsertRange(int, IEnumerable<T>): the source is
// enumerated at the element family's own cell width and each element is boxed
// into the destination storage (a listobj source copies its slots directly; an
// array source reads the cell of the family; anything else walks the shared
// IEnumerator<T> dispatcher).
void il2cpp_list_obj_add_range(Il2CppObject* self, Il2CppObject* collection, int32_t kind);
void il2cpp_list_obj_insert_range(Il2CppObject* self, int32_t index, Il2CppObject* collection,
                                  int32_t kind);

// Predicate operators (the callback's element argument crosses per `kind`).
int32_t il2cpp_list_obj_remove_all(Il2CppObject* self, Il2CppObject* match, int32_t kind);
int32_t il2cpp_list_obj_find_index(Il2CppObject* self, Il2CppObject* match, int32_t kind);
int32_t il2cpp_list_obj_find_last_index(Il2CppObject* self, Il2CppObject* match, int32_t kind);
// bool results ride the int32 slot.
int32_t il2cpp_list_obj_exists(Il2CppObject* self, Il2CppObject* match, int32_t kind);
int32_t il2cpp_list_obj_true_for_all(Il2CppObject* self, Il2CppObject* match, int32_t kind);
void il2cpp_list_obj_for_each(Il2CppObject* self, Il2CppObject* action, int32_t kind);
Il2CppObject* il2cpp_list_obj_find_all(Il2CppObject* self, Il2CppObject* match, int32_t kind);

// Find / FindLast: the element RESULT follows the carrier suffix.
//   _i4   int32-slot families (int32 + the seven narrow kinds)
//   _i8   int64   _d double (float widens)   _ref reference element
//   _dec  decimal (result slot FIRST)        _dt DateTime (BY VALUE, ticks)
//   _ts   TimeSpan (BY VALUE)                _guid Guid (result slot LAST)
//   _dto  DateTimeOffset (BY VALUE)
// A miss answers default(T).
int32_t il2cpp_list_obj_find_i4(Il2CppObject* self, Il2CppObject* match, int32_t kind);
int64_t il2cpp_list_obj_find_i8(Il2CppObject* self, Il2CppObject* match, int32_t kind);
double il2cpp_list_obj_find_d(Il2CppObject* self, Il2CppObject* match, int32_t kind);
Il2CppObject* il2cpp_list_obj_find_ref(Il2CppObject* self, Il2CppObject* match, int32_t kind);
void il2cpp_list_obj_find_dec(runtime::Il2CppDecimal* result, Il2CppObject* self,
                              Il2CppObject* match, int32_t kind);
runtime::Il2CppDateTime il2cpp_list_obj_find_dt(Il2CppObject* self, Il2CppObject* match,
                                                int32_t kind);
runtime::Il2CppTimeSpan il2cpp_list_obj_find_ts(Il2CppObject* self, Il2CppObject* match,
                                                int32_t kind);
void il2cpp_list_obj_find_guid(Il2CppObject* self, Il2CppObject* match, int32_t kind,
                               uint8_t* result);
runtime::Il2CppDateTimeOffset il2cpp_list_obj_find_dto(Il2CppObject* self, Il2CppObject* match,
                                                       int32_t kind);
int32_t il2cpp_list_obj_find_last_i4(Il2CppObject* self, Il2CppObject* match, int32_t kind);
int64_t il2cpp_list_obj_find_last_i8(Il2CppObject* self, Il2CppObject* match, int32_t kind);
double il2cpp_list_obj_find_last_d(Il2CppObject* self, Il2CppObject* match, int32_t kind);
Il2CppObject* il2cpp_list_obj_find_last_ref(Il2CppObject* self, Il2CppObject* match, int32_t kind);
void il2cpp_list_obj_find_last_dec(runtime::Il2CppDecimal* result, Il2CppObject* self,
                                   Il2CppObject* match, int32_t kind);
runtime::Il2CppDateTime il2cpp_list_obj_find_last_dt(Il2CppObject* self, Il2CppObject* match,
                                                     int32_t kind);
runtime::Il2CppTimeSpan il2cpp_list_obj_find_last_ts(Il2CppObject* self, Il2CppObject* match,
                                                     int32_t kind);
void il2cpp_list_obj_find_last_guid(Il2CppObject* self, Il2CppObject* match, int32_t kind,
                                    uint8_t* result);
runtime::Il2CppDateTimeOffset il2cpp_list_obj_find_last_dto(Il2CppObject* self,
                                                            Il2CppObject* match, int32_t kind);
// The boxed core shared by the carriers and the struct templates (fromEnd != 0
// scans backwards). Null when no element matches.
Il2CppObject* il2cpp_list_obj_find_box(Il2CppObject* self, Il2CppObject* match, int32_t kind,
                                       int32_t fromEnd);

// IndexOf(T, [startIndex, [count]]) -- the value crosses in the family's own
// parameter ABI. `count < 0` means "through the end" (the 1/2-arg overloads).
int32_t il2cpp_list_obj_index_of_range_i4(Il2CppObject* self, int32_t value, int32_t start,
                                          int32_t count, int32_t kind);
int32_t il2cpp_list_obj_index_of_range_i8(Il2CppObject* self, int64_t value, int32_t start,
                                          int32_t count, int32_t kind);
int32_t il2cpp_list_obj_index_of_range_d(Il2CppObject* self, double value, int32_t start,
                                         int32_t count, int32_t kind);
int32_t il2cpp_list_obj_index_of_range_ref(Il2CppObject* self, Il2CppObject* value,
                                           int32_t start, int32_t count, int32_t kind);
int32_t il2cpp_list_obj_index_of_range_dec(Il2CppObject* self,
                                           const runtime::Il2CppDecimal* value, int32_t start,
                                           int32_t count, int32_t kind);
int32_t il2cpp_list_obj_index_of_range_dt(Il2CppObject* self, runtime::Il2CppDateTime value,
                                          int32_t start, int32_t count, int32_t kind);
int32_t il2cpp_list_obj_index_of_range_ts(Il2CppObject* self, runtime::Il2CppTimeSpan value,
                                          int32_t start, int32_t count, int32_t kind);
int32_t il2cpp_list_obj_index_of_range_guid(Il2CppObject* self, const uint8_t* value,
                                            int32_t start, int32_t count, int32_t kind);
int32_t il2cpp_list_obj_index_of_range_dto(Il2CppObject* self,
                                           runtime::Il2CppDateTimeOffset value, int32_t start,
                                           int32_t count, int32_t kind);

// LastIndexOf(T, [index, [count]]): `count < 0` means "through the beginning"
// (the 1/2-arg overloads); `index` is the INCLUSIVE start position.
int32_t il2cpp_list_obj_last_index_of_range_i4(Il2CppObject* self, int32_t value,
                                               int32_t index, int32_t count, int32_t kind);
int32_t il2cpp_list_obj_last_index_of_range_i8(Il2CppObject* self, int64_t value,
                                               int32_t index, int32_t count, int32_t kind);
int32_t il2cpp_list_obj_last_index_of_range_d(Il2CppObject* self, double value, int32_t index,
                                              int32_t count, int32_t kind);
int32_t il2cpp_list_obj_last_index_of_range_ref(Il2CppObject* self, Il2CppObject* value,
                                                int32_t index, int32_t count, int32_t kind);
int32_t il2cpp_list_obj_last_index_of_range_dec(Il2CppObject* self,
                                                const runtime::Il2CppDecimal* value,
                                                int32_t index, int32_t count, int32_t kind);
int32_t il2cpp_list_obj_last_index_of_range_dt(Il2CppObject* self,
                                               runtime::Il2CppDateTime value, int32_t index,
                                               int32_t count, int32_t kind);
int32_t il2cpp_list_obj_last_index_of_range_ts(Il2CppObject* self,
                                               runtime::Il2CppTimeSpan value, int32_t index,
                                               int32_t count, int32_t kind);
int32_t il2cpp_list_obj_last_index_of_range_guid(Il2CppObject* self, const uint8_t* value,
                                                 int32_t index, int32_t count, int32_t kind);
int32_t il2cpp_list_obj_last_index_of_range_dto(Il2CppObject* self,
                                                runtime::Il2CppDateTimeOffset value,
                                                int32_t index, int32_t count, int32_t kind);

// BinarySearch(T, [comparer]) / BinarySearch(index, count, T, comparer). A null
// comparer means Comparer<T>.Default (per-family ordering; DateTime orders by
// ticks). A miss answers the complement of the insertion point.
int32_t il2cpp_list_obj_binary_search_i4(Il2CppObject* self, int32_t value,
                                         Il2CppObject* comparer, int32_t start, int32_t count,
                                         int32_t kind);
int32_t il2cpp_list_obj_binary_search_i8(Il2CppObject* self, int64_t value,
                                         Il2CppObject* comparer, int32_t start, int32_t count,
                                         int32_t kind);
int32_t il2cpp_list_obj_binary_search_d(Il2CppObject* self, double value,
                                        Il2CppObject* comparer, int32_t start, int32_t count,
                                        int32_t kind);
int32_t il2cpp_list_obj_binary_search_ref(Il2CppObject* self, Il2CppObject* value,
                                          Il2CppObject* comparer, int32_t start, int32_t count,
                                          int32_t kind);
int32_t il2cpp_list_obj_binary_search_dec(Il2CppObject* self,
                                          const runtime::Il2CppDecimal* value,
                                          Il2CppObject* comparer, int32_t start, int32_t count,
                                          int32_t kind);
int32_t il2cpp_list_obj_binary_search_dt(Il2CppObject* self, runtime::Il2CppDateTime value,
                                         Il2CppObject* comparer, int32_t start, int32_t count,
                                         int32_t kind);
int32_t il2cpp_list_obj_binary_search_ts(Il2CppObject* self, runtime::Il2CppTimeSpan value,
                                         Il2CppObject* comparer, int32_t start, int32_t count,
                                         int32_t kind);
int32_t il2cpp_list_obj_binary_search_guid(Il2CppObject* self, const uint8_t* value,
                                           Il2CppObject* comparer, int32_t start, int32_t count,
                                           int32_t kind);
int32_t il2cpp_list_obj_binary_search_dto(Il2CppObject* self,
                                          runtime::Il2CppDateTimeOffset value,
                                          Il2CppObject* comparer, int32_t start, int32_t count,
                                          int32_t kind);

// The boxed comparison cores (used by the carriers above and by the struct
// templates below).
int32_t il2cpp_list_obj_index_of_box_range(Il2CppObject* self, Il2CppObject* valueBox,
                                           int32_t start, int32_t count, int32_t kind);
int32_t il2cpp_list_obj_last_index_of_box_range(Il2CppObject* self, Il2CppObject* valueBox,
                                                int32_t index, int32_t count, int32_t kind);
int32_t il2cpp_list_obj_binary_search_box(Il2CppObject* self, Il2CppObject* valueBox,
                                          Il2CppObject* comparer, int32_t start, int32_t count,
                                          int32_t kind);

// CopyTo(T[] array, int arrayIndex): the destination array carries the
// family's own cell width, so the copy writes the cell of `kind` (a reference
// array store for kind 1). Validation: null array, negative index, and
// index + Count over the destination length.
void il2cpp_list_obj_copy_to_kind(Il2CppObject* self, Il2CppObject* array, int32_t arrayIndex,
                                  int32_t kind);

// Sort(Comparison<T>): the comparison delegate receives the elements in the
// family's own ABI (`kind`); a null comparison throws ArgumentNullException.
void il2cpp_list_obj_sort_comparison(Il2CppObject* self, Il2CppObject* comparison, int32_t kind);

// Sort(int index, int count, IComparer<T> comparer): only the range is
// ordered. A null comparer means Comparer<T>.Default (per-family ordering).
void il2cpp_list_obj_sort_range_comparer(Il2CppObject* self, int32_t index, int32_t count,
                                         Il2CppObject* comparer, int32_t kind);

// USER-STRUCT / TUPLE element forms (the box payload is a T; the generated
// delegate leaf takes T BY VALUE, so these templates state T once).
typedef bool (*Il2CppListObjStructPredFn)(Il2CppObject* delegateObj, const void* payload,
                                          int32_t index);
typedef void (*Il2CppListObjStructActionFn)(Il2CppObject* delegateObj, const void* payload,
                                            int32_t index);
typedef int32_t (*Il2CppListObjStructCmpFn)(Il2CppObject* delegateObj, const void* a,
                                            const void* b);
constexpr int32_t IL2CPP_LIST_OBJ_SCAN_FIND_INDEX = 0;
constexpr int32_t IL2CPP_LIST_OBJ_SCAN_FIND_LAST_INDEX = 1;
constexpr int32_t IL2CPP_LIST_OBJ_SCAN_EXISTS = 2;
constexpr int32_t IL2CPP_LIST_OBJ_SCAN_TRUE_FOR_ALL = 3;
constexpr int32_t IL2CPP_LIST_OBJ_SCAN_REMOVE_ALL = 4;

Il2CppObject* il2cpp_list_obj_find_struct_core(Il2CppObject* self, Il2CppObject* match,
                                               Il2CppListObjStructPredFn pred, int32_t fromEnd);
int32_t il2cpp_list_obj_scan_struct_core(Il2CppObject* self, Il2CppObject* match,
                                         Il2CppListObjStructPredFn pred, int32_t mode);
Il2CppObject* il2cpp_list_obj_find_all_struct_core(Il2CppObject* self, Il2CppObject* match,
                                                   Il2CppListObjStructPredFn pred);
void il2cpp_list_obj_for_each_struct_core(Il2CppObject* self, Il2CppObject* action,
                                          Il2CppListObjStructActionFn fn);
void il2cpp_list_obj_add_range_struct(Il2CppObject* self, Il2CppObject* collection,
                                      Il2CppClass* klass, int32_t payloadSize);
void il2cpp_list_obj_insert_range_struct(Il2CppObject* self, int32_t index,
                                         Il2CppObject* collection, Il2CppClass* klass,
                                         int32_t payloadSize);
int32_t il2cpp_list_obj_index_of_struct_ex(Il2CppObject* self, const void* value,
                                           Il2CppClass* klass, int32_t payloadSize,
                                           int32_t start, int32_t count);
int32_t il2cpp_list_obj_last_index_of_struct_ex(Il2CppObject* self, const void* value,
                                                Il2CppClass* klass, int32_t payloadSize,
                                                int32_t index, int32_t count);
int32_t il2cpp_list_obj_binary_search_struct_ex(Il2CppObject* self, const void* value,
                                                Il2CppClass* klass, int32_t payloadSize,
                                                Il2CppObject* comparer, int32_t start,
                                                int32_t count);

template <typename T>
inline bool il2cpp_list_obj_struct_pred_t(Il2CppObject* d, const void* payload, int32_t)
{
    const Il2CppDelegate* del = reinterpret_cast<const Il2CppDelegate*>(d);
    const T* v = reinterpret_cast<const T*>(payload);
    typedef bool (*Fn)(T);
    typedef bool (*ThisFn)(Il2CppObject*, T);
    return del->target != nullptr
        ? reinterpret_cast<ThisFn>(del->methodPtr)(del->target, *v)
        : reinterpret_cast<Fn>(del->methodPtr)(*v);
}

template <typename T>
inline void il2cpp_list_obj_struct_action_t(Il2CppObject* d, const void* payload, int32_t)
{
    const Il2CppDelegate* del = reinterpret_cast<const Il2CppDelegate*>(d);
    const T* v = reinterpret_cast<const T*>(payload);
    typedef void (*Fn)(T);
    typedef void (*ThisFn)(Il2CppObject*, T);
    if (del->target != nullptr)
        reinterpret_cast<ThisFn>(del->methodPtr)(del->target, *v);
    else
        reinterpret_cast<Fn>(del->methodPtr)(*v);
}

template <typename T>
inline T il2cpp_list_obj_find_struct_t(Il2CppObject* self, Il2CppObject* match, int32_t fromEnd)
{
    Il2CppObject* box = il2cpp_list_obj_find_struct_core(
        self, match, &il2cpp_list_obj_struct_pred_t<T>, fromEnd);
    T out;
    if (box != nullptr)
        std::memcpy(&out, reinterpret_cast<const char*>(box) + 8, sizeof(T));
    else
        std::memset(&out, 0, sizeof(T));
    return out;
}

template <typename T>
inline int32_t il2cpp_list_obj_scan_struct_t(Il2CppObject* self, Il2CppObject* match,
                                             int32_t mode)
{
    return il2cpp_list_obj_scan_struct_core(self, match, &il2cpp_list_obj_struct_pred_t<T>, mode);
}

template <typename T>
inline Il2CppObject* il2cpp_list_obj_find_all_struct_t(Il2CppObject* self, Il2CppObject* match)
{
    return il2cpp_list_obj_find_all_struct_core(self, match, &il2cpp_list_obj_struct_pred_t<T>);
}

template <typename T>
inline void il2cpp_list_obj_for_each_struct_t(Il2CppObject* self, Il2CppObject* action)
{
    il2cpp_list_obj_for_each_struct_core(self, action, &il2cpp_list_obj_struct_action_t<T>);
}

template <typename T>
inline void il2cpp_list_obj_add_range_struct_t(Il2CppObject* self, Il2CppObject* collection,
                                               Il2CppClass* klass)
{
    il2cpp_list_obj_add_range_struct(self, collection, klass,
                                     static_cast<int32_t>(sizeof(T)));
}

template <typename T>
inline void il2cpp_list_obj_insert_range_struct_t(Il2CppObject* self, int32_t index,
                                                  Il2CppObject* collection, Il2CppClass* klass)
{
    il2cpp_list_obj_insert_range_struct(self, index, collection, klass,
                                        static_cast<int32_t>(sizeof(T)));
}

template <typename T>
inline int32_t il2cpp_list_obj_index_of_struct_range_t(Il2CppObject* self, const T* value,
                                                       Il2CppClass* klass, int32_t start,
                                                       int32_t count)
{
    return il2cpp_list_obj_index_of_struct_ex(self, value, klass,
                                              static_cast<int32_t>(sizeof(T)), start, count);
}

template <typename T>
inline int32_t il2cpp_list_obj_last_index_of_struct_range_t(Il2CppObject* self, const T* value,
                                                            Il2CppClass* klass, int32_t index,
                                                            int32_t count)
{
    return il2cpp_list_obj_last_index_of_struct_ex(self, value, klass,
                                                   static_cast<int32_t>(sizeof(T)), index, count);
}

template <typename T>
inline int32_t il2cpp_list_obj_binary_search_struct_t(Il2CppObject* self, const T* value,
                                                      Il2CppClass* klass,
                                                      Il2CppObject* comparer, int32_t start,
                                                      int32_t count)
{
    return il2cpp_list_obj_binary_search_struct_ex(self, value, klass,
                                                   static_cast<int32_t>(sizeof(T)), comparer,
                                                   start, count);
}

// CopyTo for a user-struct / tuple element: the destination array holds the
// raw T cells, so a boxed element is copied out as its payload.
void il2cpp_list_obj_copy_to_struct(Il2CppObject* self, Il2CppObject* array, int32_t arrayIndex,
                                    int32_t payloadSize);

template <typename T>
inline void il2cpp_list_obj_copy_to_struct_t(Il2CppObject* self, Il2CppObject* array,
                                             int32_t arrayIndex)
{
    il2cpp_list_obj_copy_to_struct(self, array, arrayIndex,
                                   static_cast<int32_t>(sizeof(T)));
}

// Sort(Comparison<T>) for a user-struct / tuple element: the comparison
// delegate leaf takes T BY VALUE.
template <typename T>
inline int32_t il2cpp_list_obj_struct_cmp_t(Il2CppObject* d, const void* a, const void* b)
{
    const Il2CppDelegate* del = reinterpret_cast<const Il2CppDelegate*>(d);
    const T* x = reinterpret_cast<const T*>(a);
    const T* y = reinterpret_cast<const T*>(b);
    typedef int32_t (*Fn)(T, T);
    typedef int32_t (*ThisFn)(Il2CppObject*, T, T);
    return del->target != nullptr
        ? reinterpret_cast<ThisFn>(del->methodPtr)(del->target, *x, *y)
        : reinterpret_cast<Fn>(del->methodPtr)(*x, *y);
}

void il2cpp_list_obj_sort_comparison_struct(Il2CppObject* self, Il2CppObject* comparison,
                                            Il2CppListObjStructCmpFn cmp);

template <typename T>
inline void il2cpp_list_obj_sort_comparison_struct_t(Il2CppObject* self,
                                                     Il2CppObject* comparison)
{
    il2cpp_list_obj_sort_comparison_struct(self, comparison, &il2cpp_list_obj_struct_cmp_t<T>);
}

} // namespace il2cpp
