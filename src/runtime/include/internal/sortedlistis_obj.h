#pragma once
//------------------------------------------------------------------------------
// sortedlistis_obj.h -- the SortedList<int,string> AOT expansion (int key,
// string value) over the non-generic sorted-array storage (sortedlist.h).
//
// The int KEY is boxed on entry / unboxed on exit at the C++ boundary (the
// boxed-int key order is the core's ascending sort); the string VALUE is
// already a managed reference. The wrapper is the int->string mirror of
// sortedlistint_obj.h (string->int): the core owns the sort order, the
// duplicate-key -> ArgumentException / null-key -> ArgumentNullException
// contract and the index range checks.
//
// net48 deltas: get_Item raises KeyNotFoundException on a miss (the core
// returns null); Keys is an int32[] snapshot (the core's keys are boxed ints).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// .ctor(capacity): the parameterless form arrives as capacity 0
// (AppendZeroArg) and reuses il2cpp_sortedlist_create directly in the mapping.
// Add(int32_t key, string value) (insert-or-throw).
void icall_sortedlist_is_add(Il2CppObject* self, int32_t key, Il2CppObject* value);

// get_Item(int32_t key): the value, or KeyNotFoundException on a miss.
Il2CppObject* icall_sortedlist_is_get_by_key(Il2CppObject* self, int32_t key);

// IndexOfKey(int32_t key): the sorted index of the key, or -1 when absent.
int32_t icall_sortedlist_is_index_of_key(Il2CppObject* self, int32_t key);

// Keys: an int32[] snapshot of the keys in ascending (sorted) order.
Il2CppObject* icall_sortedlist_is_get_keys(Il2CppObject* self);

} // namespace runtime
} // namespace il2cpp
