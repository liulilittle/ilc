#pragma once
//------------------------------------------------------------------------------
// genericcomparer_obj.h -- the DEFAULT generic comparer singletons (G6
// caudit1; STAGED -- copy into src/runtime/include/internal + src/runtime/src
// when the full-pool rescan completes).
//
//   EqualityComparer<int>.Default   (net48: ObjectEqualityComparer<int>)
//     get_Default -> a static singleton object; Equals(x, y) -> x == y.
//   Comparer<double>.Default        (net48: GenericComparer<double>)
//     get_Default -> a static singleton object; Compare(x, y) -> the
//     Double.CompareTo verdict (NaN sorts first).
//
// The singletons follow the System.Collections.Comparer precedent
// (arraysort.cpp): a name-bearing static klass descriptor registered lazily;
// the object is static -- never heap-allocated, never moved by the collector.
// The transpiler tracks the get_Default result under the instance pseudo-type
// ("System.Collections.Generic.EqualityComparer`1_int" /
// "System.Collections.Generic.Comparer`1_double") so the INSTANCE member call
// resolves on the receiver hint; any other instantiation falls back to the
// generated-name path (loud at link).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {

Il2CppObject* il2cpp_equality_comparer_default_int();
// W6 (T2): the remaining EqualityComparer / Comparer singletons ride the
// same static-singleton discipline as the int/double precedent above.
Il2CppObject* il2cpp_equality_comparer_default_string();
Il2CppObject* il2cpp_equality_comparer_default_long();
Il2CppObject* il2cpp_equality_comparer_default_double();
Il2CppObject* il2cpp_equality_comparer_default_object();
Il2CppObject* il2cpp_equality_comparer_default_class();
Il2CppObject* il2cpp_comparer_default_int();
Il2CppObject* il2cpp_comparer_default_double();
Il2CppObject* il2cpp_comparer_default_string();
Il2CppObject* il2cpp_comparer_default_long();
Il2CppObject* il2cpp_comparer_default_object();

namespace runtime {

// EqualityComparer<int>.Default.Equals(int, int): value equality.
bool icall_equality_comparer_int_equals(Il2CppObject* self, int32_t x, int32_t y);

// W6: long/double compare by value (double uses .NET Double.Equals
// semantics -- NaN == NaN, +0.0 == -0.0); string compares ordinal
// (null-first, UTF-16 code-unit order, the arraysort ordinal contract);
// object/class compare by reference identity.
bool icall_equality_comparer_string_equals(Il2CppObject* self, Il2CppObject* x, Il2CppObject* y);
bool icall_equality_comparer_long_equals(Il2CppObject* self, int64_t x, int64_t y);
bool icall_equality_comparer_double_equals(Il2CppObject* self, double x, double y);
bool icall_equality_comparer_object_equals(Il2CppObject* self, Il2CppObject* x, Il2CppObject* y);
bool icall_equality_comparer_class_equals(Il2CppObject* self, Il2CppObject* x, Il2CppObject* y);

// Comparer<double>.Default.Compare(double, double): net48 Double.CompareTo
// (less -1 / equal 0 / greater 1; NaN sorts before any non-NaN; NaN vs NaN 0).
int32_t icall_comparer_double_compare(Il2CppObject* self, double x, double y);

// W6: int/long compare numerically; string compares ordinal
// (null-first, the CompareStringRefs contract in arraysort.cpp); object
// dispatches the first argument's IComparable.CompareTo
// (il2cpp_icomparable_compare_to in arraysort.h).
int32_t icall_comparer_int_compare(Il2CppObject* self, int32_t x, int32_t y);
int32_t icall_comparer_string_compare(Il2CppObject* self, Il2CppObject* x, Il2CppObject* y);
int32_t icall_comparer_long_compare(Il2CppObject* self, int64_t x, int64_t y);
int32_t icall_comparer_object_compare(Il2CppObject* self, Il2CppObject* x, Il2CppObject* y);

} // namespace runtime
} // namespace il2cpp
