//------------------------------------------------------------------------------
// genericcomparer_obj.cpp -- the DEFAULT generic comparer singletons (G6
// caudit1; STAGED -- see genericcomparer_obj.h for the design).
//------------------------------------------------------------------------------

#include "genericcomparer_obj.h"

#include "arraysort.h" // il2cpp_icomparable_compare_to (Comparer<object>)
#include "il2cpp_runtime.h" // il2cpp_class_register
#include "string_compare.h" // ordinal string equality / comparison
#include "il2cpp_delegate.h" // Comparer<T>.Create wraps a delegate
#include "il2cpp_exception_ext.h"
#include "il2cpp_gcroot.h"   // Comparer<T>.Create rooting

#include <cstring>

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
    // nb9 fix P0-2: a Comparer<T>.Create wrapper runs its delegate.
    if (il2cpp_is_created_comparer(self))
        return il2cpp_created_comparer_compare_double(self, x, y);
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
    // nb9 fix P0-2: a Comparer<T>.Create wrapper runs its delegate.
    if (il2cpp_is_created_comparer(self))
        return il2cpp_created_comparer_compare_int(self, x, y);
    return (x < y) ? -1 : (x > y) ? 1 : 0;
}

int32_t icall_comparer_long_compare(Il2CppObject* self, int64_t x, int64_t y)
{
    if (il2cpp_is_created_comparer(self))
        return il2cpp_created_comparer_compare_long(self, x, y);
    return (x < y) ? -1 : (x > y) ? 1 : 0;
}

int32_t icall_comparer_string_compare(Il2CppObject* self, Il2CppObject* x, Il2CppObject* y)
{
    if (il2cpp_is_created_comparer(self))
        return il2cpp_created_comparer_compare_ref(self, x, y);
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

//------------------------------------------------------------------------------
// nb9 (M4/M18, P0-2): Comparer<T>.Create(Comparison<T>).
//
// net48 returns an object whose Compare(x, y) invokes the WRAPPED delegate.
// The wrapper is modelled as a two-slot managed object (header + the delegate
// reference). One class per element family so the tracked receiver type (and
// therefore the Compare ABI) matches the T of the Create call:
//   * `_created_int`    Comparison<int>    -> i(i,i)
//   * `_created_long`   Comparison<long>   -> i(l,l)
//   * `_created_double` Comparison<double> -> i(d,d)
//   * `_created_ref`    Comparison<TRef>   -> i(r,r)
// The previous single `_created_int` class made every Create result claim the
// int ABI, and icall_comparer_*_compare threw the delegate away.
//------------------------------------------------------------------------------
namespace {

Il2CppClass g_createdComparerIntClass = {
    "System.Collections.Generic.Comparer`1_created_int", // name
    static_cast<int32_t>(sizeof(void*) * 2),             // instanceSize: header + delegate
    0b10u,                                               // refFieldBitmap: bit 1 = the delegate
    1,                                                   // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

#define NB9_CREATED_CMP_CLASS(camel, kind) \
Il2CppClass g_createdComparer##camel##Class = { \
    "System.Collections.Generic.Comparer`1_created_" kind, \
    static_cast<int32_t>(sizeof(void*) * 2), 0b10u, 1, \
    nullptr, nullptr, 0, nullptr, 0, \
};

NB9_CREATED_CMP_CLASS(Long, "long")
NB9_CREATED_CMP_CLASS(Double, "double")
NB9_CREATED_CMP_CLASS(Ref, "ref")

#undef NB9_CREATED_CMP_CLASS

// Managed layout: header + the wrapped Comparison<T> delegate.
struct CreatedComparer
{
    Il2CppObject obj;
    Il2CppObject* comparison;
};

const char kCreatedComparerPrefix[] = "System.Collections.Generic.Comparer`1_created";

Il2CppObject* CreateComparerObject(Il2CppClass* klass, Il2CppObject* comparison,
                                   const char* name)
{
    if (comparison == nullptr)
    {
        il2cpp_throw_argument_null_exception("comparison");
    }
    il2cpp_class_register(klass);
    GcRoot<Il2CppObject*> cmpRoot(comparison, name);
    CreatedComparer* c = reinterpret_cast<CreatedComparer*>(
        il2cpp_object_new(klass));
    Il2CppObject* self = reinterpret_cast<Il2CppObject*>(c);
    GcRoot<Il2CppObject*> selfRoot(self, name);
    c->comparison = cmpRoot.Get();
    il2cpp_write_barrier(reinterpret_cast<void**>(&c->comparison), c->comparison);
    return selfRoot.Get();
}

} // namespace

Il2CppObject* il2cpp_comparer_create_int(Il2CppObject* comparison)
{
    return CreateComparerObject(&g_createdComparerIntClass, comparison,
                                "comparer_create_int");
}

Il2CppObject* il2cpp_comparer_create_long(Il2CppObject* comparison)
{
    return CreateComparerObject(&g_createdComparerLongClass, comparison,
                                "comparer_create_long");
}

Il2CppObject* il2cpp_comparer_create_double(Il2CppObject* comparison)
{
    return CreateComparerObject(&g_createdComparerDoubleClass, comparison,
                                "comparer_create_double");
}

Il2CppObject* il2cpp_comparer_create_ref(Il2CppObject* comparison)
{
    return CreateComparerObject(&g_createdComparerRefClass, comparison,
                                "comparer_create_ref");
}

bool il2cpp_is_created_comparer(Il2CppObject* obj)
{
    if (obj == nullptr || obj->klass == nullptr || obj->klass->name == nullptr)
    {
        return false;
    }
    return std::strncmp(obj->klass->name, kCreatedComparerPrefix,
                        sizeof(kCreatedComparerPrefix) - 1) == 0;
}

int32_t il2cpp_created_comparer_compare_int(Il2CppObject* self, int32_t a, int32_t b)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    CreatedComparer* c = reinterpret_cast<CreatedComparer*>(self);
    Il2CppDelegate* d = reinterpret_cast<Il2CppDelegate*>(c->comparison);
    if (d == nullptr)
    {
        return a < b ? -1 : a > b ? 1 : 0;
    }
    typedef int32_t (*ComparisonFn)(int32_t, int32_t);
    typedef int32_t (*ComparisonThisFn)(Il2CppObject*, int32_t, int32_t);
    return d->target != nullptr
        ? reinterpret_cast<ComparisonThisFn>(d->methodPtr)(d->target, a, b)
        : reinterpret_cast<ComparisonFn>(d->methodPtr)(a, b);
}

int32_t il2cpp_created_comparer_compare_long(Il2CppObject* self, int64_t a, int64_t b)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    CreatedComparer* c = reinterpret_cast<CreatedComparer*>(self);
    Il2CppDelegate* d = reinterpret_cast<Il2CppDelegate*>(c->comparison);
    if (d == nullptr)
    {
        return a < b ? -1 : a > b ? 1 : 0;
    }
    typedef int32_t (*ComparisonFn)(int64_t, int64_t);
    typedef int32_t (*ComparisonThisFn)(Il2CppObject*, int64_t, int64_t);
    return d->target != nullptr
        ? reinterpret_cast<ComparisonThisFn>(d->methodPtr)(d->target, a, b)
        : reinterpret_cast<ComparisonFn>(d->methodPtr)(a, b);
}

int32_t il2cpp_created_comparer_compare_double(Il2CppObject* self, double a, double b)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    CreatedComparer* c = reinterpret_cast<CreatedComparer*>(self);
    Il2CppDelegate* d = reinterpret_cast<Il2CppDelegate*>(c->comparison);
    if (d == nullptr)
    {
        return a < b ? -1 : a > b ? 1 : 0;
    }
    typedef int32_t (*ComparisonFn)(double, double);
    typedef int32_t (*ComparisonThisFn)(Il2CppObject*, double, double);
    return d->target != nullptr
        ? reinterpret_cast<ComparisonThisFn>(d->methodPtr)(d->target, a, b)
        : reinterpret_cast<ComparisonFn>(d->methodPtr)(a, b);
}

int32_t il2cpp_created_comparer_compare_ref(Il2CppObject* self, Il2CppObject* a, Il2CppObject* b)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    CreatedComparer* c = reinterpret_cast<CreatedComparer*>(self);
    Il2CppDelegate* d = reinterpret_cast<Il2CppDelegate*>(c->comparison);
    if (d == nullptr)
    {
        return 0;
    }
    typedef int32_t (*ComparisonFn)(Il2CppObject*, Il2CppObject*);
    typedef int32_t (*ComparisonThisFn)(Il2CppObject*, Il2CppObject*, Il2CppObject*);
    return d->target != nullptr
        ? reinterpret_cast<ComparisonThisFn>(d->methodPtr)(d->target, a, b)
        : reinterpret_cast<ComparisonFn>(d->methodPtr)(a, b);
}

} // namespace runtime
} // namespace il2cpp
