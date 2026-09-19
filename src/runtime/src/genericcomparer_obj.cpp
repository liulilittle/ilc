//------------------------------------------------------------------------------
// genericcomparer_obj.cpp -- the DEFAULT generic comparer singletons (G6
// caudit1; STAGED -- see genericcomparer_obj.h for the design).
//------------------------------------------------------------------------------

#include "genericcomparer_obj.h"

#include "arraysort.h" // il2cpp_icomparable_compare_to (Comparer<object>)
#include "il2cpp_runtime.h" // il2cpp_class_register
#include "string_compare.h" // ordinal string equality / comparison

#include <cmath> // std::isnan (double equality / comparison)

namespace il2cpp {

namespace {

// The name-bearing static klass descriptors + singletons (the
// System.Collections.Comparer precedent, arraysort.cpp:72-86).
Il2CppClass g_equalityComparerIntClass = {
    "System.Collections.Generic.EqualityComparer`1_int", // name
    static_cast<int32_t>(sizeof(Il2CppObject)),          // instanceSize: header only
    0u,                                                  // refFieldBitmap
    0,                                                   // refFieldCount
    nullptr,                                             // baseClass
    nullptr,                                             // vtable
    0,                                                   // vtableSize
    nullptr,                                             // interfaces
    0,                                                   // interfaceCount
};

Il2CppObject g_equalityComparerIntSingleton = {
    &g_equalityComparerIntClass,
};

Il2CppClass g_comparerDoubleClass = {
    "System.Collections.Generic.Comparer`1_double", // name
    static_cast<int32_t>(sizeof(Il2CppObject)),     // instanceSize: header only
    0u,                                             // refFieldBitmap
    0,                                              // refFieldCount
    nullptr,                                        // baseClass
    nullptr,                                        // vtable
    0,                                              // vtableSize
    nullptr,                                        // interfaces
    0,                                              // interfaceCount
};

Il2CppObject g_comparerDoubleSingleton = {
    &g_comparerDoubleClass,
};

// W6 (T2): one name-bearing static klass + singleton per new instantiation
// (same static-object discipline as the int/double precedent above).
#define W6_SINGLETON(lower, camel, fullname) \
Il2CppClass g_equalityComparer##camel##Class = { \
    fullname, static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0, \
    nullptr, nullptr, 0, nullptr, 0, \
}; \
Il2CppObject g_equalityComparer##camel##Singleton = { \
    &g_equalityComparer##camel##Class, \
};

W6_SINGLETON(string, String, "System.Collections.Generic.EqualityComparer`1_string")
W6_SINGLETON(long, Long, "System.Collections.Generic.EqualityComparer`1_long")
W6_SINGLETON(double, Double, "System.Collections.Generic.EqualityComparer`1_double")
W6_SINGLETON(object, Object, "System.Collections.Generic.EqualityComparer`1_object")
W6_SINGLETON(class, Class, "System.Collections.Generic.EqualityComparer`1_class")

#undef W6_SINGLETON

#define W6_CMP_SINGLETON(camel, fullname) \
Il2CppClass g_comparer##camel##Class = { \
    fullname, static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0, \
    nullptr, nullptr, 0, nullptr, 0, \
}; \
Il2CppObject g_comparer##camel##Singleton = { \
    &g_comparer##camel##Class, \
};

W6_CMP_SINGLETON(Int, "System.Collections.Generic.Comparer`1_int")
W6_CMP_SINGLETON(String, "System.Collections.Generic.Comparer`1_string")
W6_CMP_SINGLETON(Long, "System.Collections.Generic.Comparer`1_long")
W6_CMP_SINGLETON(Object, "System.Collections.Generic.Comparer`1_object")

#undef W6_CMP_SINGLETON

} // namespace

Il2CppObject* il2cpp_equality_comparer_default_int()
{
    il2cpp_class_register(&g_equalityComparerIntClass);
    return &g_equalityComparerIntSingleton;
}

// W6 getters (same register-then-return-static discipline).
Il2CppObject* il2cpp_equality_comparer_default_string()
{
    il2cpp_class_register(&g_equalityComparerStringClass);
    return &g_equalityComparerStringSingleton;
}

Il2CppObject* il2cpp_equality_comparer_default_long()
{
    il2cpp_class_register(&g_equalityComparerLongClass);
    return &g_equalityComparerLongSingleton;
}

Il2CppObject* il2cpp_equality_comparer_default_double()
{
    il2cpp_class_register(&g_equalityComparerDoubleClass);
    return &g_equalityComparerDoubleSingleton;
}

Il2CppObject* il2cpp_equality_comparer_default_object()
{
    il2cpp_class_register(&g_equalityComparerObjectClass);
    return &g_equalityComparerObjectSingleton;
}

Il2CppObject* il2cpp_equality_comparer_default_class()
{
    il2cpp_class_register(&g_equalityComparerClassClass);
    return &g_equalityComparerClassSingleton;
}

Il2CppObject* il2cpp_comparer_default_double()
{
    il2cpp_class_register(&g_comparerDoubleClass);
    return &g_comparerDoubleSingleton;
}

Il2CppObject* il2cpp_comparer_default_int()
{
    il2cpp_class_register(&g_comparerIntClass);
    return &g_comparerIntSingleton;
}

Il2CppObject* il2cpp_comparer_default_string()
{
    il2cpp_class_register(&g_comparerStringClass);
    return &g_comparerStringSingleton;
}

Il2CppObject* il2cpp_comparer_default_long()
{
    il2cpp_class_register(&g_comparerLongClass);
    return &g_comparerLongSingleton;
}

Il2CppObject* il2cpp_comparer_default_object()
{
    il2cpp_class_register(&g_comparerObjectClass);
    return &g_comparerObjectSingleton;
}

namespace runtime {

bool icall_equality_comparer_int_equals(Il2CppObject* self, int32_t x, int32_t y)
{
    (void)self; // the singleton receiver is not consulted
    return x == y;
}

// W6 EqualityComparer icalls. Reference fast-path first (identical
// reference -- including both null -- is equal; exactly-one null is not);
// then the per-kind value rule.
bool icall_equality_comparer_string_equals(Il2CppObject* self, Il2CppObject* x, Il2CppObject* y)
{
    (void)self;
    if (x == y) return true;
    if (x == nullptr || y == nullptr) return false;
    return il2cpp_string_equals_ordinal(reinterpret_cast<const Il2CppString*>(x),
                                        reinterpret_cast<const Il2CppString*>(y));
}

bool icall_equality_comparer_long_equals(Il2CppObject* self, int64_t x, int64_t y)
{
    (void)self;
    return x == y;
}

bool icall_equality_comparer_double_equals(Il2CppObject* self, double x, double y)
{
    (void)self;
    // .NET Double.Equals: NaN == NaN, +0.0 == -0.0 (== already folds zeros).
    return x == y || (std::isnan(x) && std::isnan(y));
}

bool icall_equality_comparer_object_equals(Il2CppObject* self, Il2CppObject* x, Il2CppObject* y)
{
    (void)self;
    return x == y;
}

bool icall_equality_comparer_class_equals(Il2CppObject* self, Il2CppObject* x, Il2CppObject* y)
{
    (void)self;
    return x == y;
}

int32_t icall_comparer_double_compare(Il2CppObject* self, double x, double y)
{
    (void)self; // the singleton receiver is not consulted
    // net48 Double.CompareTo: the ordered comparison first; the NaN cases
    // only when neither <, > nor == holds (NaN sorts first; NaN vs NaN 0).
    if (x < y) return -1;
    if (x > y) return 1;
    if (x == y) return 0;
    const bool xn = x != x;
    const bool yn = y != y;
    if (xn) return yn ? 0 : -1;
    return 1;
}

// W6 Comparer icalls (-1 / 0 / 1 verdicts).
int32_t icall_comparer_int_compare(Il2CppObject* self, int32_t x, int32_t y)
{
    (void)self;
    return (x < y) ? -1 : (x > y) ? 1 : 0;
}

int32_t icall_comparer_long_compare(Il2CppObject* self, int64_t x, int64_t y)
{
    (void)self;
    return (x < y) ? -1 : (x > y) ? 1 : 0;
}

int32_t icall_comparer_string_compare(Il2CppObject* self, Il2CppObject* x, Il2CppObject* y)
{
    (void)self;
    // net48 Comparer<string>.Default is ordinal, null-first; identical
    // references (including both null) compare equal.
    if (x == y) return 0;
    if (x == nullptr) return -1;
    if (y == nullptr) return 1;
    const int v = il2cpp_string_compare_ordinal(reinterpret_cast<const Il2CppString*>(x),
                                                reinterpret_cast<const Il2CppString*>(y));
    return (v < 0) ? -1 : (v > 0) ? 1 : 0;
}

int32_t icall_comparer_object_compare(Il2CppObject* self, Il2CppObject* x, Il2CppObject* y)
{
    (void)self;
    // net48 Comparer<object>.Default defers to IComparable on the first
    // argument (null-first ordering like the default comparer).
    if (x == y) return 0;
    if (x == nullptr) return -1;
    if (y == nullptr) return 1;
    return il2cpp_icomparable_compare_to(x, y);
}

} // namespace runtime
} // namespace il2cpp
