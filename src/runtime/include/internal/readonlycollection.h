//------------------------------------------------------------------------------
// readonlycollection.h -- System.Collections.ObjectModel.ReadOnlyCollection<T>.
//
// The type is a thin VIEW over an existing array: it stores the array and
// answers every member by delegating to it. net48's ReadOnlyCollection<T>
// wraps an IList<T>; the wired surface here is the one Array.AsReadOnly
// produces (an array), so the carrier is the array itself.
//
//   struct Il2CppReadOnlyCollection
//   {
//       Il2CppObject obj;      // object header: klass pointer
//       Il2CppArray* list;     // the wrapped array (GC ref, bitmap bit 1)
//   };
//
// GC SAFETY: instanceSize 16, refFieldBitmap bit 1 (slot 1 = the wrapped
// array). The collector keeps the array reachable for as long as the wrapper
// is, which is what makes the view semantics correct: a write through the
// ORIGINAL array is visible through the wrapper (net48 contract).
//
// ELEMENT FAMILIES: the wrapper is element-agnostic -- it stores the array and
// reads elements through the shared boxed accessor, so one implementation
// covers int32, the other primitives and reference elements. The typed
// accessors below exist only where the transpiler needs an unboxed value
// (the int32 indexer, which is the common case).
//------------------------------------------------------------------------------

#pragma once

#include "il2cpp_object.h"
#include "il2cpp_metadata.h"

#include <cstdint>

namespace il2cpp {

struct Il2CppArray;

struct Il2CppReadOnlyCollection
{
    Il2CppObject obj;      // object header (klass pointer)
    Il2CppArray* list;     // the wrapped array (GC ref)
};

// The runtime descriptor (registered lazily on first use).
Il2CppClass* il2cpp_readonlycollection_class();

// Array.AsReadOnly<T>(T[] array) -- allocate the wrapper over 'array'.
// A null array raises ArgumentNullException("array") (net48).
Il2CppObject* il2cpp_readonlycollection_create(Il2CppObject* array);

// int Count: the wrapped array's length.
int32_t il2cpp_readonlycollection_get_count(Il2CppObject* self);

// T this[int index]: the element at 'index'. The int32 form returns the value
// unboxed; the object form returns the boxed element (reference elements come
// back as the reference itself).
int32_t il2cpp_readonlycollection_get_item_i4(Il2CppObject* self, int32_t index);
Il2CppObject* il2cpp_readonlycollection_get_item_obj(Il2CppObject* self, int32_t index);

// bool Contains(T value) / int IndexOf(T value).
bool il2cpp_readonlycollection_contains_i4(Il2CppObject* self, int32_t value);
bool il2cpp_readonlycollection_contains_obj(Il2CppObject* self, Il2CppObject* value);
int32_t il2cpp_readonlycollection_index_of_i4(Il2CppObject* self, int32_t value);
int32_t il2cpp_readonlycollection_index_of_obj(Il2CppObject* self, Il2CppObject* value);

// void CopyTo(T[] array, int index): copy the elements into 'array'.
void il2cpp_readonlycollection_copy_to(Il2CppObject* self, Il2CppObject* array, int32_t index);

// IEnumerator<T> GetEnumerator(): the array enumerator over the wrapped array.
Il2CppObject* il2cpp_readonlycollection_get_enumerator(Il2CppObject* self);

} // namespace il2cpp
