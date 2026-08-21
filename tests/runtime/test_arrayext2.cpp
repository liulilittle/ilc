//------------------------------------------------------------------------------
// test_arrayext2.cpp -- System.Array extension-2 icalls tests.
//
// Coverage (normal + edge + error paths):
//   1. icall_array_sort_strings -- unsorted, already-sorted, reversed,
//      duplicates, null elements (null sorts FIRST), empty, single,
//      culture ordering (case-insensitive primary, lowercase<uppercase
//      secondary -- net48 Comparer<string>.Default under zh-CN Current
//      Culture), null array (ArgumentNullException), int32 array
//      (NotSupportedException).
//   2. icall_array_binary_search_int32 -- hits (odd/even length, first/last
//      element, negatives, duplicates -> first match), misses (complement of
//      the insertion point: before-first, middle, after-last), empty array
//      -> -1, null array, reference array.
//   3. icall_array_binary_search_string -- ordinal hits/misses, null value
//      (matches null elements), non-string value (ArgumentException), empty
//      array, int32 array.
//   4. icall_array_index_of_int32 / last_index_of_int32 -- first/last
//      occurrence semantics, absent -> -1, empty array, null array.
//   5. icall_array_clear_int32 -- full/partial/head/tail clears, length 0
//      no-op (one-past-end index legal), negative index/length
//      (ArgumentOutOfRangeException), range overflow (ArgumentException),
//      int32-overflow defense (INT32_MAX + INT32_MAX must fail, not wrap),
//      null array, reference array.
//   6. icall_array_find_index_int32 -- hit/miss, startIndex/count windowing,
//      legal empty window at Length, the four ArgumentOutOfRange
//      validations, null array.
//   6b. icall_array_find_index (the DELEGATE form) -- the predicate's
//      methodPtr runs per element with per-binding dispatch: hit/miss,
//      startIndex/count windowing, empty window, empty array, all-true
//      predicate, an INSTANCE-BOUND (captured-lambda) predicate whose target
//      arrives in the `this` slot, a mid-loop GC probe (rooted array +
//      delegate + target survive a compaction), the four ArgumentOutOfRange
//      validations, null array / null predicate (ArgumentNullException),
//      multicast delegate (InvalidOperationException), non-delegate object
//      (ArgumentException), reference array (NotSupportedException).
//   6c. icall_array_find_i4 / exists_i4 / true_for_all_i4 /
//      convert_all_i4_i4 / resize_i4 (the Find/Exists/TrueForAll/ConvertAll/
//      Resize int32 surface) -- Find returns the matched VALUE (0 default),
//      Exists/TrueForAll bool 0/1 (empty TrueForAll vacuously true), ConvertAll
//      builds a fresh converted array (source untouched, GC probe), Resize
//      grows/shrinks/null/same-length/zero, plus the null-argument, reference-
//      array and multicast-delegate rejections.
//   6d. icall_array_find_all_i4 (the FindAll int32 surface) -- a NEW int32[]
//      of every matching element in source order (empty -> zero-length; the
//      source untouched; the predicate runs exactly once per element), the
//      all-true/never-true/empty extremes, an INSTANCE-BOUND predicate, a
//      mid-loop GC probe, and the null-argument, reference-array and
//      multicast-delegate rejections.
//   7. icall_array_reverse_range -- full/partial ranges, length 0/1 no-ops,
//      negative args, range overflow, int32-overflow defense, null array.
//
// The value-comparison icalls (sort/binary-search/index-of/clear/find-index-
// int32/reverse) allocate nothing (no GC safe point), so their string
// elements need no rooting (see the note in test_arrayext.cpp about array.cpp's
// under-allocation bug making reference arrays unsafe across collections --
// avoided here). The FindIndex DELEGATE form is different: the predicate may
// allocate, so a collection CAN fire mid-loop -- the icall roots the array
// and the predicate (RootScopeExt2), and the GC probe test (FiGcIsTwenty)
// proves the rooted pair survives a forced compaction.
//
// Plain executable + minimal assertion harness (see test_array.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_array_ext2.h"
#include "il2cpp_delegate.h"
#include "il2cpp_delegate_ext.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace {

int g_failures = 0;

void ExpectI32(int32_t actual, int32_t expected, const char* what)
{
    if (actual != expected)
    {
        std::printf("FAIL: %s (expected %d, got %d)\n", what, expected, actual);
        ++g_failures;
    }
}

// Runs 'fn' expecting a managed DotNetException whose type chain HEAD exactly
// equals 'expectedType' (only the head is asserted, see test_arrayext.cpp).
template <typename F>
void ExpectThrows(const char* expectedType, const char* what, F&& fn)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        const char* thrown = e.type_name();
        const size_t len = std::strlen(expectedType);
        const bool headMatches = std::strncmp(thrown, expectedType, len) == 0 &&
                                 (thrown[len] == ',' || thrown[len] == '\0');
        if (headMatches)
        {
            return;
        }
        std::printf("FAIL: %s (threw %s, expected %s)\n", what, thrown, expectedType);
        ++g_failures;
        return;
    }
    std::printf("FAIL: %s (no exception thrown, expected %s)\n", what, expectedType);
    ++g_failures;
}

// A non-string managed object (for the "value is not a string" paths).
il2cpp::Il2CppClass g_mockElemClass = {
    "Test.ArrayExt2Elem", // name
    24,                   // instanceSize
    0b00000010u,          // refFieldBitmap: slot 1 is a GC reference
    1,                    // refFieldCount
    nullptr,              // baseClass
    nullptr,              // vtable
    0,                    // vtableSize
    nullptr,              // interfaces
    0,                    // interfaceCount
};

// Managed exception type names (exact chain head, as thrown by arrayext2.cpp).
const char kArgNullType[]        = "System.ArgumentNullException";
const char kArgOutOfRangeType[]  = "System.ArgumentOutOfRangeException";
const char kArgumentType[]       = "System.ArgumentException";
const char kNotSupportedType[]   = "System.NotSupportedException";
const char kInvalidOpType[]      = "System.InvalidOperationException";

il2cpp::Il2CppObject* AsObject(il2cpp::Il2CppArray* arr)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(arr);
}

//------------------------------------------------------------------------------
// FindIndex predicate callbacks (single-cast static-method delegates, the
// generated-code ABI -- same shape as the linq_map tests).
//------------------------------------------------------------------------------
bool FiIsTwenty(int32_t v) { return v == 20; }
bool FiNeverTrue(int32_t) { return false; }
bool FiAlwaysTrue(int32_t) { return true; }

// GC probe: force a compaction in the middle of the FindIndex element loop.
bool FiGcIsTwenty(int32_t v)
{
    il2cpp::il2cpp_gc_collect();
    return v == 20;
}

//------------------------------------------------------------------------------
// Find / Exists / TrueForAll predicate callbacks (same single-cast static
// delegate ABI as the FindIndex predicates above).
//------------------------------------------------------------------------------
bool FdIsPositive(int32_t v) { return v > 0; }
bool FdIsTwenty(int32_t v) { return v == 20; }
bool FdNeverTrue(int32_t) { return false; }
bool FdAlwaysTrue(int32_t) { return true; }

// GC probe predicate: force a compaction on every element (the FindAll buffer
// and the source array must survive -- the rooted pair is rewritten).
bool FdGcCollect(int32_t v)
{
    il2cpp::il2cpp_gc_collect();
    return v > 0;
}

// ConvertAll<int,int> converter callback: multiply by ten.
int32_t CaTimesTen(int32_t v) { return v * 10; }
// GC probe converter: force a compaction before converting (the converter may
// allocate in net48 semantics; the rooted source/result must survive).
int32_t CaGcTimesTen(int32_t v)
{
    il2cpp::il2cpp_gc_collect();
    return v * 10;
}

// Wrap a method pointer into a single-cast delegate (generated ldftn +
// newobj DelegateCtor shape).
il2cpp::Il2CppDelegate* FiDel(void* fn)
{
    return il2cpp::il2cpp_delegate_new(fn);
}

//------------------------------------------------------------------------------
// Instance-bound predicate (the arrayext2 delegate shape: a captured-lambda
// delegate whose target is the compiler-generated display class instance --
// e.g. <>c.<>9 bound to <>c::<Main>b__0_0). The target is a GC object whose
// int payload the instance method reads through its `this` (arg0) -- exactly
// what transpiler-generated instance methods do (CodeGenerator: instance
// arg0 = this).
//------------------------------------------------------------------------------
struct FiInst
{
    il2cpp::Il2CppObject obj; // object header
    int32_t match;            // instance payload the bound method reads
};

il2cpp::Il2CppClass g_fiInstClass = {
    "ArrayExt2Test.FiInst",   // name
    static_cast<int32_t>(sizeof(FiInst)), // instanceSize
    0u,                       // refFieldBitmap: no reference fields
    0,                        // refFieldCount
    nullptr,                  // baseClass
    nullptr,                  // vtable
    0,                        // vtableSize
    nullptr,                  // interfaces
    0,                        // interfaceCount
};

bool g_fiInstRegistered = false;

FiInst* NewFiInst(int32_t match)
{
    if (!g_fiInstRegistered)
    {
        il2cpp::il2cpp_class_register(&g_fiInstClass);
        g_fiInstRegistered = true;
    }
    FiInst* t = reinterpret_cast<FiInst*>(il2cpp::il2cpp_gc_alloc(&g_fiInstClass));
    t->match = match;
    return t;
}

// Instance method with the generated-code instance ABI: arg0 = this.
bool FiInstIsMatch(il2cpp::Il2CppObject* this_, int32_t v)
{
    return v == reinterpret_cast<FiInst*>(this_)->match;
}

// GC probe: reads the bound payload FIRST (a managed pointer must not be held
// across a collection inside the callback -- the int32 match is a by-value
// copy), then forces a compaction mid-loop.
bool FiInstIsMatchCollect(il2cpp::Il2CppObject* this_, int32_t v)
{
    const int32_t match = reinterpret_cast<FiInst*>(this_)->match;
    il2cpp::il2cpp_gc_collect();
    return v == match;
}

// Wrap a method pointer + target into an instance-bound delegate (mirrors the
// transpiler's 2-arg emission: il2cpp_delegate_new(&M, obj)).
il2cpp::Il2CppDelegate* FiInstDel(void* fn, il2cpp::Il2CppObject* target)
{
    return il2cpp::il2cpp_delegate_new(fn, target);
}

//------------------------------------------------------------------------------
// Instance-bound converter (ConvertAll<int,int> with a captured lambda): the
// bound payload is the multiplier.
//------------------------------------------------------------------------------
struct FiConvInst
{
    il2cpp::Il2CppObject obj;
    int32_t factor;
};

il2cpp::Il2CppClass g_fiConvClass = {
    "ArrayExt2Test.FiConvInst", // name
    static_cast<int32_t>(sizeof(FiConvInst)), // instanceSize
    0u,                        // refFieldBitmap
    0,                         // refFieldCount
    nullptr,                   // baseClass
    nullptr,                   // vtable
    0,                         // vtableSize
    nullptr,                   // interfaces
    0,                         // interfaceCount
};

bool g_fiConvRegistered = false;

FiConvInst* NewFiConv(int32_t factor)
{
    if (!g_fiConvRegistered)
    {
        il2cpp::il2cpp_class_register(&g_fiConvClass);
        g_fiConvRegistered = true;
    }
    FiConvInst* t = reinterpret_cast<FiConvInst*>(il2cpp::il2cpp_gc_alloc(&g_fiConvClass));
    t->factor = factor;
    return t;
}

int32_t FiConvInstTimes(il2cpp::Il2CppObject* this_, int32_t v)
{
    return v * reinterpret_cast<FiConvInst*>(this_)->factor;
}

il2cpp::Il2CppArray* NewInt32Array(int32_t length)
{
    return il2cpp::il2cpp_array_new(nullptr, 4, false, length);
}

void FillInt32(il2cpp::Il2CppArray* arr, const int32_t* values, int32_t count)
{
    int32_t* data = reinterpret_cast<int32_t*>(arr->data);
    for (int32_t i = 0; i < count; ++i)
    {
        data[i] = values[i];
    }
}

void ExpectInt32Content(il2cpp::Il2CppArray* arr, const int32_t* expected,
                        int32_t count, const char* what)
{
    const int32_t* data = reinterpret_cast<const int32_t*>(arr->data);
    bool ok = true;
    for (int32_t i = 0; i < count; ++i)
    {
        if (data[i] != expected[i])
        {
            ok = false;
            break;
        }
    }
    if (!ok)
    {
        std::printf("FAIL: %s (content: ", what);
        for (int32_t i = 0; i < count; ++i)
        {
            std::printf("%s%d", i > 0 ? " " : "", data[i]);
        }
        std::printf(")\n");
        ++g_failures;
    }
}

// A string[] (reference array) holding the given ASCII literals; a nullptr
// entry in 'values' becomes a null element.
il2cpp::Il2CppArray* NewStringArray(const char* const* values, int32_t count)
{
    il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(nullptr, 8, true, count);
    il2cpp::Il2CppObject** slots = reinterpret_cast<il2cpp::Il2CppObject**>(arr->data);
    for (int32_t i = 0; i < count; ++i)
    {
        slots[i] = values[i] != nullptr
                       ? reinterpret_cast<il2cpp::Il2CppObject*>(
                             il2cpp::il2cpp_string_new_from_cs(values[i]))
                       : nullptr;
    }
    return arr;
}

// Asserts that the reference slots hold exactly the expected string content
// (element-wise, in order). 'expected' is the same literal convention as
// NewStringArray (nullptr -> null slot).
void ExpectStringContent(il2cpp::Il2CppArray* arr, const char* const* expected,
                         int32_t count, const char* what)
{
    const il2cpp::Il2CppObject* const* slots =
        reinterpret_cast<const il2cpp::Il2CppObject* const*>(arr->data);
    for (int32_t i = 0; i < count; ++i)
    {
        if (expected[i] == nullptr)
        {
            if (slots[i] != nullptr)
            {
                std::printf("FAIL: %s (element %d: expected null)\n", what, i);
                ++g_failures;
            }
            continue;
        }
        const il2cpp::Il2CppString* s =
            reinterpret_cast<const il2cpp::Il2CppString*>(slots[i]);
        if (s == nullptr || s->length != static_cast<int32_t>(std::strlen(expected[i])))
        {
            std::printf("FAIL: %s (element %d: length mismatch)\n", what, i);
            ++g_failures;
            continue;
        }
        bool same = true;
        for (int32_t u = 0; u < s->length; ++u)
        {
            if (static_cast<char>(s->chars[u]) != expected[i][u])
            {
                same = false;
                break;
            }
        }
        if (!same)
        {
            std::printf("FAIL: %s (element %d: content mismatch)\n", what, i);
            ++g_failures;
        }
    }
}

} // namespace

int main()
{
    // --- 1. Sort(string[]) ---
    {
        const char* const unsorted[] = { "pear", "apple", "cherry" };
        il2cpp::Il2CppArray* a = NewStringArray(unsorted, 3);
        il2cpp::runtime::icall_array_sort_strings(AsObject(a));
        const char* const sorted[] = { "apple", "cherry", "pear" };
        ExpectStringContent(a, sorted, 3, "sort_strings orders an unsorted array ascending");

        const char* const already[] = { "apple", "cherry", "pear" };
        il2cpp::Il2CppArray* b = NewStringArray(already, 3);
        il2cpp::runtime::icall_array_sort_strings(AsObject(b));
        ExpectStringContent(b, already, 3, "sort_strings keeps already-sorted input");

        const char* const reversed[] = { "pear", "cherry", "apple" };
        il2cpp::Il2CppArray* c = NewStringArray(reversed, 3);
        il2cpp::runtime::icall_array_sort_strings(AsObject(c));
        ExpectStringContent(c, sorted, 3, "sort_strings reverses a reversed array");

        const char* const dup[] = { "b", "a", "b", "a" };
        il2cpp::Il2CppArray* d = NewStringArray(dup, 4);
        il2cpp::runtime::icall_array_sort_strings(AsObject(d));
        const char* const dupSorted[] = { "a", "a", "b", "b" };
        ExpectStringContent(d, dupSorted, 4, "sort_strings handles duplicates");

        // null elements sort FIRST (Comparer<string>.Default policy).
        const char* const withNull[] = { "b", nullptr, "a", nullptr };
        il2cpp::Il2CppArray* e = NewStringArray(withNull, 4);
        il2cpp::runtime::icall_array_sort_strings(AsObject(e));
        const char* const nullSorted[] = { nullptr, nullptr, "a", "b" };
        ExpectStringContent(e, nullSorted, 4, "sort_strings puts null elements first");

        // Culture order (net48 Comparer<string>.Default under zh-CN Current
        // Culture): case-INSENSITIVE primary -- 'a' group before 'b' group;
        // within a group the SECONDARY pass orders LOWERCASE before UPPERCASE
        // at the first case-differing position (csc-probe-verified 2026-08-11).
        const char* const caseMix[] = { "apple", "Apple", "Banana", "banana" };
        il2cpp::Il2CppArray* f = NewStringArray(caseMix, 4);
        il2cpp::runtime::icall_array_sort_strings(AsObject(f));
        const char* const caseSorted[] = { "apple", "Apple", "banana", "Banana" };
        ExpectStringContent(f, caseSorted, 4, "sort_strings orders by the culture comparison (case-insensitive primary, lowercase<uppercase secondary)");

        il2cpp::Il2CppArray* empty = NewStringArray(nullptr, 0);
        il2cpp::runtime::icall_array_sort_strings(AsObject(empty));
        ExpectI32(il2cpp::il2cpp_array_get_length(empty), 0,
                  "sort_strings of an empty array is a no-op");

        const char* const singleArr[] = { "solo" };
        il2cpp::Il2CppArray* single = NewStringArray(singleArr, 1);
        il2cpp::runtime::icall_array_sort_strings(AsObject(single));
        ExpectStringContent(single, singleArr, 1, "sort_strings of a single element is a no-op");

        ExpectThrows(kArgNullType, "sort_strings of null throws ArgumentNullException",
                     [] { il2cpp::runtime::icall_array_sort_strings(nullptr); });
        il2cpp::Il2CppArray* intArr = NewInt32Array(2);
        ExpectThrows(kNotSupportedType, "sort_strings of an int32 array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_sort_strings(AsObject(intArr)); });
    }

    // --- 2. BinarySearch(int32) ---
    {
        il2cpp::Il2CppArray* odd = NewInt32Array(5);
        const int32_t oddVals[5] = { 1, 3, 5, 7, 9 };
        FillInt32(odd, oddVals, 5);
        ExpectI32(il2cpp::runtime::icall_array_binary_search_int32(AsObject(odd), 5), 2,
                  "binary_search_int32 finds the middle element");
        ExpectI32(il2cpp::runtime::icall_array_binary_search_int32(AsObject(odd), 1), 0,
                  "binary_search_int32 finds the first element");
        ExpectI32(il2cpp::runtime::icall_array_binary_search_int32(AsObject(odd), 9), 4,
                  "binary_search_int32 finds the last element");
        // Misses: complement of the insertion point.
        ExpectI32(il2cpp::runtime::icall_array_binary_search_int32(AsObject(odd), 4), ~2,
                  "binary_search_int32 miss in the middle returns ~insertion point");
        ExpectI32(il2cpp::runtime::icall_array_binary_search_int32(AsObject(odd), 0), ~0,
                  "binary_search_int32 miss before the first element returns ~0");
        ExpectI32(il2cpp::runtime::icall_array_binary_search_int32(AsObject(odd), 10), ~5,
                  "binary_search_int32 miss after the last element returns ~length");

        il2cpp::Il2CppArray* even = NewInt32Array(4);
        const int32_t evenVals[4] = { 1, 3, 5, 7 };
        FillInt32(even, evenVals, 4);
        ExpectI32(il2cpp::runtime::icall_array_binary_search_int32(AsObject(even), 3), 1,
                  "binary_search_int32 finds in an even-length array");
        ExpectI32(il2cpp::runtime::icall_array_binary_search_int32(AsObject(even), 2), ~1,
                  "binary_search_int32 even-length miss returns ~insertion point");

        // Duplicates: any match is a valid index; this implementation returns
        // the FIRST occurrence (lower-bound).
        il2cpp::Il2CppArray* dup = NewInt32Array(5);
        const int32_t dupVals[5] = { 1, 2, 2, 2, 3 };
        FillInt32(dup, dupVals, 5);
        ExpectI32(il2cpp::runtime::icall_array_binary_search_int32(AsObject(dup), 2), 1,
                  "binary_search_int32 with duplicates returns the first match");

        il2cpp::Il2CppArray* neg = NewInt32Array(3);
        const int32_t negVals[3] = { -5, -3, -1 };
        FillInt32(neg, negVals, 3);
        ExpectI32(il2cpp::runtime::icall_array_binary_search_int32(AsObject(neg), -3), 1,
                  "binary_search_int32 finds negatives");
        ExpectI32(il2cpp::runtime::icall_array_binary_search_int32(AsObject(neg), -4), ~1,
                  "binary_search_int32 negative miss returns ~insertion point");

        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        ExpectI32(il2cpp::runtime::icall_array_binary_search_int32(AsObject(empty), 42), -1,
                  "binary_search_int32 of an empty array returns -1");

        ExpectThrows(kArgNullType, "binary_search_int32 of null throws ArgumentNullException",
                     [] { il2cpp::runtime::icall_array_binary_search_int32(nullptr, 1); });
        il2cpp::Il2CppArray* refArr = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, 2);
        ExpectThrows(kNotSupportedType, "binary_search_int32 of a reference array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_binary_search_int32(AsObject(refArr), 1); });
    }

    // --- 3. BinarySearch(string) ---
    {
        const char* const vals[] = { "apple", "cherry", "pear" };
        il2cpp::Il2CppArray* a = NewStringArray(vals, 3);
        il2cpp::Il2CppObject* cherry = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("cherry"));
        ExpectI32(il2cpp::runtime::icall_array_binary_search_string(AsObject(a), cherry), 1,
                  "binary_search_string finds a present string");
        il2cpp::Il2CppObject* banana = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("banana"));
        ExpectI32(il2cpp::runtime::icall_array_binary_search_string(AsObject(a), banana), ~1,
                  "binary_search_string miss returns ~insertion point");
        il2cpp::Il2CppObject* zoo = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("zoo"));
        ExpectI32(il2cpp::runtime::icall_array_binary_search_string(AsObject(a), zoo), ~3,
                  "binary_search_string miss after the last returns ~length");
        il2cpp::Il2CppObject* apple = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("apple"));
        ExpectI32(il2cpp::runtime::icall_array_binary_search_string(AsObject(a), apple), 0,
                  "binary_search_string finds the first element");

        // null value: matches only null elements; insertion point 0 otherwise.
        const char* const withNull[] = { nullptr, "a", "b" };
        il2cpp::Il2CppArray* b = NewStringArray(withNull, 3);
        ExpectI32(il2cpp::runtime::icall_array_binary_search_string(AsObject(b), nullptr), 0,
                  "binary_search_string null value matches a null element");
        ExpectI32(il2cpp::runtime::icall_array_binary_search_string(AsObject(a), nullptr), ~0,
                  "binary_search_string null value without null elements returns ~0");

        il2cpp::Il2CppObject* nonString = il2cpp::il2cpp_gc_alloc(&g_mockElemClass);
        ExpectThrows(kArgumentType, "binary_search_string with a non-string value throws ArgumentException",
                     [&] { il2cpp::runtime::icall_array_binary_search_string(AsObject(a), nonString); });

        il2cpp::Il2CppArray* empty = NewStringArray(nullptr, 0);
        ExpectI32(il2cpp::runtime::icall_array_binary_search_string(AsObject(empty), apple), -1,
                  "binary_search_string of an empty array returns -1");

        ExpectThrows(kArgNullType, "binary_search_string of null throws ArgumentNullException",
                     [] { il2cpp::runtime::icall_array_binary_search_string(nullptr, nullptr); });
        il2cpp::Il2CppArray* intArr = NewInt32Array(2);
        ExpectThrows(kNotSupportedType, "binary_search_string of an int32 array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_binary_search_string(AsObject(intArr), apple); });
    }

    // --- 4. IndexOf / LastIndexOf (int32) ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(5);
        const int32_t vals[5] = { 10, 20, 30, 20, 40 };
        FillInt32(a, vals, 5);

        ExpectI32(il2cpp::runtime::icall_array_index_of_int32(AsObject(a), 20), 1,
                  "index_of_int32 returns the first occurrence");
        ExpectI32(il2cpp::runtime::icall_array_index_of_int32(AsObject(a), 40), 4,
                  "index_of_int32 finds the last element");
        ExpectI32(il2cpp::runtime::icall_array_index_of_int32(AsObject(a), 99), -1,
                  "index_of_int32 of an absent value returns -1");
        ExpectI32(il2cpp::runtime::icall_array_last_index_of_int32(AsObject(a), 20), 3,
                  "last_index_of_int32 returns the LAST occurrence");
        ExpectI32(il2cpp::runtime::icall_array_last_index_of_int32(AsObject(a), 10), 0,
                  "last_index_of_int32 finds the first element");
        ExpectI32(il2cpp::runtime::icall_array_last_index_of_int32(AsObject(a), 99), -1,
                  "last_index_of_int32 of an absent value returns -1");

        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        ExpectI32(il2cpp::runtime::icall_array_index_of_int32(AsObject(empty), 1), -1,
                  "index_of_int32 of an empty array returns -1");
        ExpectI32(il2cpp::runtime::icall_array_last_index_of_int32(AsObject(empty), 1), -1,
                  "last_index_of_int32 of an empty array returns -1");

        ExpectThrows(kArgNullType, "index_of_int32 of null throws ArgumentNullException",
                     [] { il2cpp::runtime::icall_array_index_of_int32(nullptr, 1); });
        ExpectThrows(kArgNullType, "last_index_of_int32 of null throws ArgumentNullException",
                     [] { il2cpp::runtime::icall_array_last_index_of_int32(nullptr, 1); });
        il2cpp::Il2CppArray* refArr = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, 2);
        ExpectThrows(kNotSupportedType, "index_of_int32 of a reference array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_index_of_int32(AsObject(refArr), 1); });
        ExpectThrows(kNotSupportedType, "last_index_of_int32 of a reference array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_last_index_of_int32(AsObject(refArr), 1); });
    }

    // --- 5. Clear(int32) ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(5);
        const int32_t vals[5] = { 1, 2, 3, 4, 5 };
        FillInt32(a, vals, 5);
        il2cpp::runtime::icall_array_clear_int32(AsObject(a), 0, 5);
        const int32_t allZero[5] = { 0, 0, 0, 0, 0 };
        ExpectInt32Content(a, allZero, 5, "clear_int32 full-range clears every element");

        il2cpp::Il2CppArray* b = NewInt32Array(5);
        FillInt32(b, vals, 5);
        il2cpp::runtime::icall_array_clear_int32(AsObject(b), 1, 3);
        const int32_t partial[5] = { 1, 0, 0, 0, 5 };
        ExpectInt32Content(b, partial, 5, "clear_int32 clears the requested range only");

        il2cpp::Il2CppArray* c = NewInt32Array(5);
        FillInt32(c, vals, 5);
        il2cpp::runtime::icall_array_clear_int32(AsObject(c), 3, 2);
        const int32_t tail[5] = { 1, 2, 3, 0, 0 };
        ExpectInt32Content(c, tail, 5, "clear_int32 clears the tail range");

        // length 0 is a validated no-op; index == length is legal.
        il2cpp::Il2CppArray* d = NewInt32Array(5);
        FillInt32(d, vals, 5);
        il2cpp::runtime::icall_array_clear_int32(AsObject(d), 5, 0);
        ExpectInt32Content(d, vals, 5, "clear_int32 zero-length clear at one-past-end is a no-op");
        il2cpp::runtime::icall_array_clear_int32(AsObject(d), 2, 0);
        ExpectInt32Content(d, vals, 5, "clear_int32 zero-length clear anywhere is a no-op");

        ExpectThrows(kArgOutOfRangeType, "clear_int32 negative index throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_clear_int32(AsObject(a), -1, 1); });
        ExpectThrows(kArgOutOfRangeType, "clear_int32 negative length throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_clear_int32(AsObject(a), 0, -1); });
        ExpectThrows(kArgumentType, "clear_int32 range overflow throws ArgumentException",
                     [&] { il2cpp::runtime::icall_array_clear_int32(AsObject(a), 4, 2); });
        // int32 overflow defense: INT32_MAX + INT32_MAX must not wrap the check.
        ExpectThrows(kArgumentType, "clear_int32 INT32_MAX index+length throws ArgumentException (no wrap)",
                     [&] { il2cpp::runtime::icall_array_clear_int32(AsObject(a), INT32_MAX, INT32_MAX); });
        ExpectThrows(kArgNullType, "clear_int32 of null throws ArgumentNullException",
                     [] { il2cpp::runtime::icall_array_clear_int32(nullptr, 0, 1); });
        il2cpp::Il2CppArray* refArr = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, 2);
        ExpectThrows(kNotSupportedType, "clear_int32 of a reference array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_clear_int32(AsObject(refArr), 0, 1); });
    }

    // --- 6. FindIndex(int32) ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(6);
        const int32_t vals[6] = { 10, 20, 30, 20, 40, 20 };
        FillInt32(a, vals, 6);

        ExpectI32(il2cpp::runtime::icall_array_find_index_int32(AsObject(a), 0, 6, 20), 1,
                  "find_index_int32 finds the first match over the full range");
        ExpectI32(il2cpp::runtime::icall_array_find_index_int32(AsObject(a), 2, 4, 20), 3,
                  "find_index_int32 honors startIndex");
        ExpectI32(il2cpp::runtime::icall_array_find_index_int32(AsObject(a), 2, 1, 20), -1,
                  "find_index_int32 honors count (match outside the window)");
        ExpectI32(il2cpp::runtime::icall_array_find_index_int32(AsObject(a), 0, 1, 20), -1,
                  "find_index_int32 count 1 window misses a later match");
        ExpectI32(il2cpp::runtime::icall_array_find_index_int32(AsObject(a), 0, 0, 10), -1,
                  "find_index_int32 zero-length window returns -1");
        ExpectI32(il2cpp::runtime::icall_array_find_index_int32(AsObject(a), 6, 0, 10), -1,
                  "find_index_int32 legal empty window at Length returns -1");
        ExpectI32(il2cpp::runtime::icall_array_find_index_int32(AsObject(a), 4, 2, 40), 4,
                  "find_index_int32 finds the match at the window end");
        ExpectI32(il2cpp::runtime::icall_array_find_index_int32(AsObject(a), 0, 6, 99), -1,
                  "find_index_int32 absent value returns -1");

        ExpectThrows(kArgOutOfRangeType, "find_index_int32 negative startIndex throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_find_index_int32(AsObject(a), -1, 1, 10); });
        ExpectThrows(kArgOutOfRangeType, "find_index_int32 startIndex beyond Length throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_find_index_int32(AsObject(a), 7, 0, 10); });
        ExpectThrows(kArgOutOfRangeType, "find_index_int32 negative count throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_find_index_int32(AsObject(a), 0, -1, 10); });
        ExpectThrows(kArgOutOfRangeType, "find_index_int32 count beyond Length - startIndex throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_find_index_int32(AsObject(a), 4, 3, 10); });
        ExpectThrows(kArgNullType, "find_index_int32 of null throws ArgumentNullException",
                     [] { il2cpp::runtime::icall_array_find_index_int32(nullptr, 0, 1, 1); });
    }

    // --- 6b. FindIndex(Predicate<int>) -- the DELEGATE form ---
    // icall_array_find_index(arr, predicate, start, count): the predicate's
    // methodPtr runs per element with per-binding dispatch (the linq callback
    // contract -- instance-bound leaves accepted, multicast loudly rejected).
    {
        il2cpp::Il2CppArray* a = NewInt32Array(6);
        const int32_t vals[6] = { 10, 20, 30, 20, 40, 20 };
        FillInt32(a, vals, 6);

        il2cpp::Il2CppObject* pred20 = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FiIsTwenty)));

        ExpectI32(il2cpp::runtime::icall_array_find_index(a, pred20, 0, 6), 1,
                  "find_index predicate finds the first matching element");
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, pred20, 2, 4), 3,
                  "find_index predicate honors startIndex");
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, pred20, 2, 1), -1,
                  "find_index predicate honors count (match outside the window)");
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, pred20, 0, 1), -1,
                  "find_index predicate count 1 window misses a later match");
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, pred20, 0, 0), -1,
                  "find_index predicate zero-length window returns -1");
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, pred20, 6, 0), -1,
                  "find_index predicate legal empty window at Length returns -1");
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, pred20, 4, 2), 5,
                  "find_index predicate finds the match at the window end");

        il2cpp::Il2CppObject* predNever = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FiNeverTrue)));
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, predNever, 0, 6), -1,
                  "find_index predicate absent match returns -1");

        il2cpp::Il2CppObject* predAlways = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FiAlwaysTrue)));
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, predAlways, 0, 6), 0,
                  "find_index predicate all-true returns the first element");

        // Empty array: any predicate misses.
        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        ExpectI32(il2cpp::runtime::icall_array_find_index(empty, pred20, 0, 0), -1,
                  "find_index predicate on an empty array returns -1");

        // GC: the callback forces a compaction in the middle of the loop; the
        // rooted array + predicate stay intact.
        il2cpp::Il2CppObject* predGc = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FiGcIsTwenty)));
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, predGc, 0, 6), 1,
                  "find_index predicate survives a mid-loop collection");

        // Range validation (net48 order: array, startIndex, count, match).
        ExpectThrows(kArgOutOfRangeType, "find_index predicate negative startIndex throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_find_index(a, pred20, -1, 1); });
        ExpectThrows(kArgOutOfRangeType, "find_index predicate startIndex beyond Length throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_find_index(a, pred20, 7, 0); });
        ExpectThrows(kArgOutOfRangeType, "find_index predicate negative count throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_find_index(a, pred20, 0, -1); });
        ExpectThrows(kArgOutOfRangeType, "find_index predicate count beyond Length - startIndex throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_find_index(a, pred20, 4, 3); });

        // Null array / null predicate -> ArgumentNullException.
        ExpectThrows(kArgNullType, "find_index predicate null array throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_find_index(nullptr, pred20, 0, 1); });
        ExpectThrows(kArgNullType, "find_index predicate null predicate throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_find_index(a, nullptr, 0, 1); });

        // The linq callback contract: an INSTANCE-BOUND (captured-lambda)
        // delegate is a first-class callback -- the target arrives in the
        // `this` slot (the arrayext2 delegate shape: <>c.<>9 bound to
        // <>c::<Main>b__0_0). The predicate filters with the bound payload.
        FiInst* inst20 = NewFiInst(20);
        il2cpp::Il2CppObject* predInst20 = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiInstDel(reinterpret_cast<void*>(&FiInstIsMatch), reinterpret_cast<il2cpp::Il2CppObject*>(inst20)));
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, predInst20, 0, 6), 1,
                  "find_index instance-bound predicate finds the first element equal to the bound payload");
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, predInst20, 2, 4), 3,
                  "find_index instance-bound predicate honors startIndex");
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, predInst20, 4, 2), 5,
                  "find_index instance-bound predicate finds a match at the window end");

        // The SAME method bound to a DIFFERENT payload matches that payload.
        FiInst* inst40 = NewFiInst(40);
        il2cpp::Il2CppObject* predInst40 = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiInstDel(reinterpret_cast<void*>(&FiInstIsMatch), reinterpret_cast<il2cpp::Il2CppObject*>(inst40)));
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, predInst40, 0, 6), 4,
                  "a different bound instance filters with its own payload (40 at index 4)");

        // GC: an instance-bound predicate that collects mid-loop. The bound
        // payload is read into a by-value copy BEFORE the collect; the rooted
        // delegate + array survive and the result stays correct.
        FiInst* instGc = NewFiInst(20);
        il2cpp::Il2CppObject* predInstGc = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiInstDel(reinterpret_cast<void*>(&FiInstIsMatchCollect), reinterpret_cast<il2cpp::Il2CppObject*>(instGc)));
        ExpectI32(il2cpp::runtime::icall_array_find_index(a, predInstGc, 0, 6), 1,
                  "find_index instance-bound predicate survives a mid-loop collection");

        // A multicast container is still loudly rejected.
        il2cpp::Il2CppObject* mc = il2cpp::runtime::icall_delegate_combine(
            reinterpret_cast<il2cpp::Il2CppObject*>(FiDel(reinterpret_cast<void*>(&FiIsTwenty))),
            reinterpret_cast<il2cpp::Il2CppObject*>(FiDel(reinterpret_cast<void*>(&FiAlwaysTrue))));
        ExpectThrows(kInvalidOpType, "find_index predicate multicast delegate throws InvalidOperationException",
                     [&] { il2cpp::runtime::icall_array_find_index(a, mc, 0, 1); });

        // A non-delegate object is a caller bug -> ArgumentException.
        ExpectThrows(kArgumentType, "find_index predicate non-delegate object throws ArgumentException",
                     [&] { il2cpp::runtime::icall_array_find_index(a, AsObject(NewInt32Array(1)), 0, 1); });

        // Wrong element family -> NotSupportedException.
        const char* const refVals[] = { "x" };
        il2cpp::Il2CppArray* refArr = NewStringArray(refVals, 1);
        ExpectThrows(kNotSupportedType, "find_index predicate on a reference array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_find_index(refArr, pred20, 0, 1); });
    }

    // --- 6c. Find / Exists / TrueForAll / ConvertAll / Resize (int32) ---
    // net48 oracle (rtaud5-net48.cs): Find hit -> element value, miss/empty ->
    // 0 (default), null array/match -> ArgumentNullException; Exists hit ->
    // 1, miss/empty -> 0; TrueForAll all -> 1, some -> 0, empty -> 1
    // (vacuous); ConvertAll int->int {1,2,3} *10 -> {10,20,30}, empty -> len 0,
    // null array/converter -> ArgumentNullException; Resize grow
    // {1,2,3}->5 -> {1,2,3,0,0}, shrink -> {1,2}, null -> new, same len no-op,
    // negative -> ArgumentOutOfRangeException.
    {
        il2cpp::Il2CppArray* a = NewInt32Array(3);
        const int32_t vals[3] = { -5, 20, 30 };
        FillInt32(a, vals, 3);

        il2cpp::Il2CppObject* pred20 = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FdIsTwenty)));
        il2cpp::Il2CppObject* predPos = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FdIsPositive)));
        il2cpp::Il2CppObject* predNever = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FdNeverTrue)));
        il2cpp::Il2CppObject* predAlways = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FdAlwaysTrue)));

        // Find: value of the first matching element, or 0.
        ExpectI32(il2cpp::runtime::icall_array_find_i4(a, pred20), 20,
                  "find_i4 returns the matched element value");
        ExpectI32(il2cpp::runtime::icall_array_find_i4(a, predPos), 20,
                  "find_i4 returns the FIRST matching element value");
        ExpectI32(il2cpp::runtime::icall_array_find_i4(a, predNever), 0,
                  "find_i4 of no match returns default(int)");
        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        ExpectI32(il2cpp::runtime::icall_array_find_i4(empty, pred20), 0,
                  "find_i4 of an empty array returns default(int)");
        ExpectThrows(kArgNullType, "find_i4 null array throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_find_i4(nullptr, pred20); });
        ExpectThrows(kArgNullType, "find_i4 null match throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_find_i4(a, nullptr); });
        const char* const refValsFd[] = { "x" };
        il2cpp::Il2CppArray* refArrFd = NewStringArray(refValsFd, 1);
        ExpectThrows(kNotSupportedType, "find_i4 on a reference array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_find_i4(refArrFd, pred20); });
        // The shared callback contract (same as the FindIndex predicate
        // form): an INSTANCE-BOUND delegate is accepted -- the target arrives
        // in the `this` slot -- while a multicast container is loudly
        // rejected.
        FiInst* inst20f = NewFiInst(20);
        il2cpp::Il2CppObject* predInst20f = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiInstDel(reinterpret_cast<void*>(&FiInstIsMatch), reinterpret_cast<il2cpp::Il2CppObject*>(inst20f)));
        ExpectI32(il2cpp::runtime::icall_array_find_i4(a, predInst20f), 20,
                  "find_i4 instance-bound predicate returns the matched element value");
        ExpectI32(il2cpp::runtime::icall_array_exists_i4(a, predInst20f), 1,
                  "exists_i4 instance-bound predicate hit returns 1");
        ExpectI32(il2cpp::runtime::icall_array_true_for_all_i4(a, predInst20f), 0,
                  "true_for_all_i4 instance-bound predicate with a partial match returns 0");
        FiInst* instNeverf = NewFiInst(999);
        il2cpp::Il2CppObject* predInstNeverf = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiInstDel(reinterpret_cast<void*>(&FiInstIsMatch), reinterpret_cast<il2cpp::Il2CppObject*>(instNeverf)));
        ExpectI32(il2cpp::runtime::icall_array_exists_i4(a, predInstNeverf), 0,
                  "exists_i4 instance-bound predicate miss returns 0");

        il2cpp::Il2CppObject* mcFd = il2cpp::runtime::icall_delegate_combine(
            reinterpret_cast<il2cpp::Il2CppObject*>(FiDel(reinterpret_cast<void*>(&FdIsTwenty))),
            reinterpret_cast<il2cpp::Il2CppObject*>(FiDel(reinterpret_cast<void*>(&FdAlwaysTrue))));
        ExpectThrows(kInvalidOpType, "find_i4 multicast delegate throws InvalidOperationException",
                     [&] { il2cpp::runtime::icall_array_find_i4(a, mcFd); });

        // Exists: 1 when any element matches.
        ExpectI32(il2cpp::runtime::icall_array_exists_i4(a, pred20), 1,
                  "exists_i4 hit returns 1");
        ExpectI32(il2cpp::runtime::icall_array_exists_i4(a, predNever), 0,
                  "exists_i4 miss returns 0");
        ExpectI32(il2cpp::runtime::icall_array_exists_i4(empty, predAlways), 0,
                  "exists_i4 of an empty array returns 0");
        ExpectThrows(kArgNullType, "exists_i4 null array throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_exists_i4(nullptr, pred20); });
        ExpectThrows(kArgNullType, "exists_i4 null match throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_exists_i4(a, nullptr); });

        // TrueForAll: 1 when every element matches; empty is vacuously true.
        il2cpp::Il2CppArray* posAll = NewInt32Array(2);
        const int32_t posVals[2] = { 5, 30 };
        FillInt32(posAll, posVals, 2);
        ExpectI32(il2cpp::runtime::icall_array_true_for_all_i4(posAll, predPos), 1,
                  "true_for_all_i4 all-true returns 1");
        ExpectI32(il2cpp::runtime::icall_array_true_for_all_i4(a, pred20), 0,
                  "true_for_all_i4 some-false returns 0");
        ExpectI32(il2cpp::runtime::icall_array_true_for_all_i4(empty, predNever), 1,
                  "true_for_all_i4 of an empty array is vacuously true");
        ExpectThrows(kArgNullType, "true_for_all_i4 null array throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_true_for_all_i4(nullptr, pred20); });
        ExpectThrows(kArgNullType, "true_for_all_i4 null match throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_true_for_all_i4(a, nullptr); });

        // ConvertAll: a new int32[] of the same length with each element
        // converted; the source is untouched.
        il2cpp::Il2CppObject* convX10 = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&CaTimesTen)));
        il2cpp::Il2CppArray* conv = il2cpp::runtime::icall_array_convert_all_i4_i4(a, convX10);
        if (conv == nullptr)
        {
            std::printf("FAIL: convert_all returns a non-null result\n");
            ++g_failures;
        }
        else
        {
            const int32_t expected[3] = { -50, 200, 300 };
            ExpectInt32Content(conv, expected, 3, "convert_all maps every element through the converter");
            ExpectInt32Content(a, vals, 3, "convert_all leaves the source untouched");
        }
        il2cpp::Il2CppArray* convEmpty = il2cpp::runtime::icall_array_convert_all_i4_i4(empty, convX10);
        ExpectI32(convEmpty != nullptr ? convEmpty->length : -1, 0,
                  "convert_all of an empty array returns an empty result");
        // GC: the converter forces a compaction; the rooted source/result
        // survive and the result is still correct.
        il2cpp::Il2CppObject* convGc = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&CaGcTimesTen)));
        il2cpp::Il2CppArray* convGcArr = il2cpp::runtime::icall_array_convert_all_i4_i4(a, convGc);
        const int32_t gcExpected[3] = { -50, 200, 300 };
        ExpectInt32Content(convGcArr, gcExpected, 3, "convert_all survives a mid-loop collection");
        // An INSTANCE-BOUND converter: the bound payload is the multiplier
        // (the target arrives in the `this` slot).
        FiConvInst* convInst = NewFiConv(100);
        il2cpp::Il2CppObject* convInstObj = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiInstDel(reinterpret_cast<void*>(&FiConvInstTimes), reinterpret_cast<il2cpp::Il2CppObject*>(convInst)));
        il2cpp::Il2CppArray* convInstArr = il2cpp::runtime::icall_array_convert_all_i4_i4(a, convInstObj);
        const int32_t instExpected[3] = { -500, 2000, 3000 };
        ExpectInt32Content(convInstArr, instExpected, 3, "convert_all instance-bound converter reads the bound multiplier");
        ExpectThrows(kArgNullType, "convert_all null array throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_convert_all_i4_i4(nullptr, convX10); });
        ExpectThrows(kArgNullType, "convert_all null converter throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_convert_all_i4_i4(a, nullptr); });

        // Resize (ref): grow / shrink / null / same-length / negative.
        il2cpp::Il2CppArray* ra = NewInt32Array(3);
        const int32_t rVals[3] = { 1, 2, 3 };
        FillInt32(ra, rVals, 3);
        il2cpp::Il2CppArray* slot = ra;
        il2cpp::runtime::icall_array_resize_i4(&slot, 5);
        const int32_t grown[5] = { 1, 2, 3, 0, 0 };
        ExpectInt32Content(slot, grown, 5, "resize grow keeps the old elements and zero-fills the tail");
        il2cpp::runtime::icall_array_resize_i4(&slot, 2);
        const int32_t shrunk[2] = { 1, 2 };
        ExpectInt32Content(slot, shrunk, 2, "resize shrink keeps the first newSize elements");

        il2cpp::Il2CppArray* nullSlot = nullptr;
        il2cpp::runtime::icall_array_resize_i4(&nullSlot, 3);
        if (nullSlot == nullptr)
        {
            std::printf("FAIL: resize of a null array produces a non-null array\n");
            ++g_failures;
        }
        else
        {
            ExpectI32(nullSlot->length, 3, "resize of a null array allocates new int32[newSize]");
        }

        il2cpp::Il2CppArray* sameSlot = NewInt32Array(1);
        const int32_t one[1] = { 7 };
        FillInt32(sameSlot, one, 1);
        il2cpp::Il2CppArray* before = sameSlot;
        il2cpp::runtime::icall_array_resize_i4(&sameSlot, 1);
        ExpectI32(sameSlot == before ? 1 : 0, 1, "resize with an equal length is a no-op (same reference)");
        ExpectInt32Content(sameSlot, one, 1, "resize with an equal length keeps the content");

        il2cpp::Il2CppArray* toZero = NewInt32Array(2);
        il2cpp::runtime::icall_array_resize_i4(&toZero, 0);
        ExpectI32(toZero != nullptr && toZero->length == 0 ? 1 : 0, 1,
                  "resize to 0 produces a zero-length array");

        ExpectThrows(kArgOutOfRangeType, "resize negative newSize throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_resize_i4(&ra, -1); });
        ExpectThrows(kNotSupportedType, "resize of a reference array throws NotSupportedException",
                     [&] {
                         const char* const refValsR[] = { "x" };
                         il2cpp::Il2CppArray* refArrR = NewStringArray(refValsR, 1);
                         il2cpp::runtime::icall_array_resize_i4(&refArrR, 2);
                     });
    }

    // --- 6d. FindAll (int32) ---
    // icall_array_find_all_i4(arr, match): a NEW int32[] of EVERY matching
    // element in source order (empty -> zero-length); the source is untouched.
    // net48 contract: the predicate runs exactly once per element; null
    // array/match -> ArgumentNullException; the same per-binding callback and
    // rooting contract as find_i4 (instance-bound leaves accepted, multicast
    // loudly rejected).
    {
        il2cpp::Il2CppArray* a = NewInt32Array(5);
        const int32_t vals[5] = { -5, 20, 30, 20, 40 };
        FillInt32(a, vals, 5);

        il2cpp::Il2CppObject* pred20 = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FdIsTwenty)));
        il2cpp::Il2CppObject* predPos = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FdIsPositive)));
        il2cpp::Il2CppObject* predNever = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FdNeverTrue)));
        il2cpp::Il2CppObject* predAlways = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FdAlwaysTrue)));

        // Some-matches subset: exact length, source order, source untouched.
        il2cpp::Il2CppArray* fa20 = il2cpp::runtime::icall_array_find_all_i4(a, pred20);
        const int32_t fa20Exp[2] = { 20, 20 };
        ExpectI32(fa20 != nullptr ? fa20->length : -1, 2,
                  "find_all returns exactly the matching elements");
        ExpectInt32Content(fa20, fa20Exp, 2, "find_all preserves source order");
        ExpectInt32Content(a, vals, 5, "find_all leaves the source untouched");

        // A subset predicate that matches non-contiguous elements.
        il2cpp::Il2CppArray* faPos = il2cpp::runtime::icall_array_find_all_i4(a, predPos);
        const int32_t faPosExp[4] = { 20, 30, 20, 40 };
        ExpectI32(faPos != nullptr ? faPos->length : -1, 4,
                  "find_all collects every matching element");
        ExpectInt32Content(faPos, faPosExp, 4, "find_all keeps the matching elements in order");

        // All-true -> the full array; never-true -> a zero-length array.
        il2cpp::Il2CppArray* faAll = il2cpp::runtime::icall_array_find_all_i4(a, predAlways);
        ExpectI32(faAll != nullptr ? faAll->length : -1, 5,
                  "find_all all-true returns every element");
        ExpectInt32Content(faAll, vals, 5, "find_all all-true preserves the full order");
        il2cpp::Il2CppArray* faNever = il2cpp::runtime::icall_array_find_all_i4(a, predNever);
        ExpectI32(faNever != nullptr ? faNever->length : -1, 0,
                  "find_all never-true returns a zero-length array");

        // Empty source -> a zero-length result.
        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        il2cpp::Il2CppArray* faEmpty = il2cpp::runtime::icall_array_find_all_i4(empty, predPos);
        ExpectI32(faEmpty != nullptr ? faEmpty->length : -1, 0,
                  "find_all of an empty array returns a zero-length array");

        // Instance-bound predicate: the target arrives in the `this` slot.
        FiInst* inst20fa = NewFiInst(20);
        il2cpp::Il2CppObject* predInst20fa = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiInstDel(reinterpret_cast<void*>(&FiInstIsMatch), reinterpret_cast<il2cpp::Il2CppObject*>(inst20fa)));
        il2cpp::Il2CppArray* faInst = il2cpp::runtime::icall_array_find_all_i4(a, predInst20fa);
        ExpectI32(faInst != nullptr ? faInst->length : -1, 2,
                  "find_all instance-bound predicate matches the bound payload");
        const int32_t faInstExp[2] = { 20, 20 };
        ExpectInt32Content(faInst, faInstExp, 2, "find_all instance-bound predicate order");

        // GC: the predicate collects mid-loop; the rooted source + buffer
        // survive and the result is still correct.
        il2cpp::Il2CppObject* predGcFa = reinterpret_cast<il2cpp::Il2CppObject*>(
            FiDel(reinterpret_cast<void*>(&FdGcCollect)));
        il2cpp::Il2CppArray* faGc = il2cpp::runtime::icall_array_find_all_i4(a, predGcFa);
        ExpectI32(faGc != nullptr ? faGc->length : -1, 4,
                  "find_all survives a mid-loop collection");
        ExpectInt32Content(faGc, faPosExp, 4, "find_all content survives a mid-loop collection");

        // Exception contracts.
        ExpectThrows(kArgNullType, "find_all null array throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_find_all_i4(nullptr, predPos); });
        ExpectThrows(kArgNullType, "find_all null match throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_find_all_i4(a, nullptr); });
        // A multicast container is loudly rejected.
        il2cpp::Il2CppObject* mcFa = il2cpp::runtime::icall_delegate_combine(
            reinterpret_cast<il2cpp::Il2CppObject*>(FiDel(reinterpret_cast<void*>(&FdIsTwenty))),
            reinterpret_cast<il2cpp::Il2CppObject*>(FiDel(reinterpret_cast<void*>(&FdAlwaysTrue))));
        ExpectThrows(kInvalidOpType, "find_all multicast delegate throws InvalidOperationException",
                     [&] { il2cpp::runtime::icall_array_find_all_i4(a, mcFa); });
        // A non-delegate object is a caller bug -> ArgumentException.
        ExpectThrows(kArgumentType, "find_all non-delegate object throws ArgumentException",
                     [&] { il2cpp::runtime::icall_array_find_all_i4(a, AsObject(NewInt32Array(1))); });
        // Wrong element family -> NotSupportedException.
        const char* const refValsFa[] = { "x" };
        il2cpp::Il2CppArray* refArrFa = NewStringArray(refValsFa, 1);
        ExpectThrows(kNotSupportedType, "find_all on a reference array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_find_all_i4(refArrFa, pred20); });
    }

    // --- 7. Reverse(int32) range ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(5);
        const int32_t vals[5] = { 1, 2, 3, 4, 5 };
        FillInt32(a, vals, 5);
        il2cpp::runtime::icall_array_reverse_range(AsObject(a), 0, 5);
        const int32_t fullRev[5] = { 5, 4, 3, 2, 1 };
        ExpectInt32Content(a, fullRev, 5, "reverse_range over the full range flips the array");

        il2cpp::Il2CppArray* b = NewInt32Array(5);
        FillInt32(b, vals, 5);
        il2cpp::runtime::icall_array_reverse_range(AsObject(b), 1, 3);
        const int32_t partialRev[5] = { 1, 4, 3, 2, 5 };
        ExpectInt32Content(b, partialRev, 5, "reverse_range reverses the requested range only");

        il2cpp::Il2CppArray* c = NewInt32Array(5);
        FillInt32(c, vals, 5);
        il2cpp::runtime::icall_array_reverse_range(AsObject(c), 0, 0);
        il2cpp::runtime::icall_array_reverse_range(AsObject(c), 2, 1);
        ExpectInt32Content(c, vals, 5, "reverse_range length 0 and length 1 are no-ops");

        il2cpp::Il2CppArray* d = NewInt32Array(1);
        const int32_t one[1] = { 9 };
        FillInt32(d, one, 1);
        il2cpp::runtime::icall_array_reverse_range(AsObject(d), 0, 1);
        ExpectInt32Content(d, one, 1, "reverse_range of a single element is a no-op");

        ExpectThrows(kArgOutOfRangeType, "reverse_range negative index throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_reverse_range(AsObject(a), -1, 1); });
        ExpectThrows(kArgOutOfRangeType, "reverse_range negative length throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_reverse_range(AsObject(a), 0, -1); });
        ExpectThrows(kArgumentType, "reverse_range range overflow throws ArgumentException",
                     [&] { il2cpp::runtime::icall_array_reverse_range(AsObject(a), 4, 2); });
        // int32 overflow defense: INT32_MAX + INT32_MAX must not wrap the check.
        ExpectThrows(kArgumentType, "reverse_range INT32_MAX index+length throws ArgumentException (no wrap)",
                     [&] { il2cpp::runtime::icall_array_reverse_range(AsObject(a), INT32_MAX, INT32_MAX); });
        ExpectThrows(kArgNullType, "reverse_range of null throws ArgumentNullException",
                     [] { il2cpp::runtime::icall_array_reverse_range(nullptr, 0, 1); });
        il2cpp::Il2CppArray* refArr = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, 2);
        ExpectThrows(kNotSupportedType, "reverse_range of a reference array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_reverse_range(AsObject(refArr), 0, 1); });
    }

    // --- 8. Array.Empty<T> (zero-length arrays) ---
    {
        const char kIndexOutOfRangeType[] = "System.IndexOutOfRangeException";

        il2cpp::Il2CppObject* ec = il2cpp::runtime::icall_array_empty_char();
        il2cpp::Il2CppArray* ecArr = reinterpret_cast<il2cpp::Il2CppArray*>(ec);
        if (ec == nullptr)
        {
            std::printf("FAIL: array_empty_char returns a non-null array\n");
            ++g_failures;
        }
        ExpectI32(ecArr->length, 0, "array_empty_char has length 0");
        ExpectI32(ecArr->elemSize, static_cast<int32_t>(sizeof(char16_t)),
                  "array_empty_char has elemSize 2");
        if (ecArr->isRefElem)
        {
            std::printf("FAIL: array_empty_char is a non-reference array\n");
            ++g_failures;
        }

        il2cpp::Il2CppArray* ei = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::icall_array_empty_int32());
        ExpectI32(ei->length, 0, "array_empty_int32 has length 0");
        ExpectI32(ei->elemSize, static_cast<int32_t>(sizeof(int32_t)),
                  "array_empty_int32 has elemSize 4");

        il2cpp::Il2CppArray* es = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::icall_array_empty_string());
        ExpectI32(es->length, 0, "array_empty_string has length 0");
        ExpectI32(es->elemSize, static_cast<int32_t>(sizeof(void*)),
                  "array_empty_string has elemSize 8");
        if (!es->isRefElem)
        {
            std::printf("FAIL: array_empty_string is a reference array\n");
            ++g_failures;
        }

        // Zero-length arrays are still real arrays: any element access is an
        // out-of-range read and must throw the managed bounds exception.
        ExpectThrows(kIndexOutOfRangeType, "array_empty_char element access throws IndexOutOfRangeException",
                     [&] { (void)il2cpp::il2cpp_array_get_elem_ptr(ecArr, 0); });
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d arrayext2 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime arrayext2 tests passed\n");
    return 0;
}
