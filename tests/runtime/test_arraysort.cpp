//------------------------------------------------------------------------------
// test_arraysort.cpp -- System.Array direct-call API (arraysort domain) tests.
//
// Coverage (normal + edge + error paths):
//   1. il2cpp_array_sort_int32 -- unsorted ascending, already-sorted,
//      duplicates, negatives (incl. INT32_MIN), empty, single, null array,
//      reference array (NotSupported), wrong-stride value array
//      (NotSupported).
//   2. il2cpp_array_sort_uint32 -- UINT32_MAX boundary values, duplicates.
//   3. il2cpp_array_sort_int64 -- INT64_MIN/MAX boundaries, negatives.
//   4. il2cpp_array_sort_string -- ordinal ascending, null elements sort
//      first, duplicates, empty.
//   5. il2cpp_array_sort_object -- comparison callback drives the order
//      (descending ordinal proves the callback is used), null callback
//      (ArgumentNullException), empty.
//   6. il2cpp_array_copy_to -- basic, into the middle, exact fit, empty src
//      (incl. legal dstIndex == dst length), same-array overlap (memmove
//      semantics), error paths: null src/dst (ArgumentNullException),
//      negative dstIndex (ArgumentOutOfRangeException), dst too small
//      (ArgumentException), int32-vs-int64 kind mismatch
//      (ArrayTypeMismatchException).
//   7. il2cpp_array_index_of_int32/int64/string -- first-match semantics,
//      absent -> -1, empty -> -1, null value matches a null slot (string),
//      non-string value (ArgumentException), cross-kind array
//      (NotSupported).
//   8. il2cpp_array_binary_search_int32/int64/string -- found index, missing
//      -> complement of the insertion point (before/after the range),
//      duplicates -> first match, empty -> -1, null value (string), null
//      array (ArgumentNullException).
//   9. il2cpp_array_sort_*(index, length) -- the net48 PARTIAL-sort range
//      overloads (Array.Sort<T>(T[], int, int)): only [index, index+length)
//      is ordered, head/tail untouched, full range == full sort, length 0/1
//      no-ops (incl. index == Length), error paths: negative index/length
//      (ArgumentOutOfRangeException), index + length > Length
//      (ArgumentException), null (ArgumentNullException), wrong element kind
//      (NotSupportedException).
//  10. il2cpp_array_sort_object(index, length, compare) -- the net48 partial
//      callback sort (Array.Sort<T>(T[], int, int, Comparison<T>)): the
//      callback drives the range order, null callback (ArgumentNullException),
//      range validation (same order as .NET: index/length/range BEFORE the
//      callback null check).
//  11. il2cpp_array_reverse_*(index, length) -- the net48 PARTIAL-reverse
//      range overloads (Array.Reverse<T>(T[], int, int)): only [index,
//      index+length) is reversed, full range == full reverse, reversing a
//      range twice restores the original, length 0/1 no-ops (incl. index ==
//      Length), error paths: negative index/length (ArgumentOutOfRangeException),
//      index + length > Length (ArgumentException), null (ArgumentNullException),
//      wrong element kind (NotSupportedException).
//
// Stability contract (net48): Array.Sort is an UNSTABLE sort -- equal
// elements may permute freely. Every duplicate-element assertion below
// therefore uses ExpectRefsPair / a set-equality check, never an exact
// duplicate order.
//
// Plain executable + minimal assertion harness (see test_array.cpp).
//------------------------------------------------------------------------------

#include "arraysort.h"
#include "il2cpp_array.h"
#include "il2cpp_boxing_ext.h"
#include "il2cpp_boxing_ext2.h"
#include "il2cpp_delegate.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"
#include "typeinfo.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

void ExpectI32(int32_t actual, int32_t expected, const char* what)
{
    if (actual != expected)
    {
        std::printf("FAIL: %s (expected %d, got %d)\n", what, expected, actual);
        ++g_failures;
    }
}

// Runs 'fn' expecting a managed DotNetException whose type chain HEAD exactly
// equals 'expectedType'. Any other outcome is a recorded failure.
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

// Element fixture class: a 24-byte object with one reference field at slot 1.
il2cpp::Il2CppClass g_mockElemClass = {
    "Test.ArraySortElem", // name
    24,                   // instanceSize
    0b00000010u,          // refFieldBitmap: slot 1 is a GC reference
    1,                    // refFieldCount
    nullptr,              // baseClass
    nullptr,              // vtable
    0,                    // vtableSize
    nullptr,              // interfaces
    0,                    // interfaceCount
};

// Managed exception type names (exact chain head, as thrown by arraysort.cpp).
const char kArgNullType[]       = "System.ArgumentNullException";
const char kArgOutOfRangeType[] = "System.ArgumentOutOfRangeException";
const char kArgumentType[]      = "System.ArgumentException";
const char kTypeMismatchType[]  = "System.ArrayTypeMismatchException";
const char kNotSupportedType[]  = "System.NotSupportedException";
const char kNullRefType[]       = "System.NullReferenceException";

il2cpp::Il2CppArray* NewInt32Array(int32_t length)
{
    return il2cpp::il2cpp_array_new(nullptr, 4, false, length);
}

il2cpp::Il2CppArray* NewUInt32Array(int32_t length)
{
    return il2cpp::il2cpp_array_new(nullptr, 4, false, length);
}

il2cpp::Il2CppArray* NewInt64Array(int32_t length)
{
    return il2cpp::il2cpp_array_new(nullptr, 8, false, length);
}

il2cpp::Il2CppArray* NewRefArray(int32_t length)
{
    return il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, length);
}

void FillInt32(il2cpp::Il2CppArray* arr, const int32_t* values, int32_t count)
{
    int32_t* data = reinterpret_cast<int32_t*>(arr->data);
    for (int32_t i = 0; i < count; ++i)
    {
        data[i] = values[i];
    }
}

void FillUInt32(il2cpp::Il2CppArray* arr, const uint32_t* values, int32_t count)
{
    uint32_t* data = reinterpret_cast<uint32_t*>(arr->data);
    for (int32_t i = 0; i < count; ++i)
    {
        data[i] = values[i];
    }
}

void FillInt64(il2cpp::Il2CppArray* arr, const int64_t* values, int32_t count)
{
    int64_t* data = reinterpret_cast<int64_t*>(arr->data);
    for (int32_t i = 0; i < count; ++i)
    {
        data[i] = values[i];
    }
}

void FillRefs(il2cpp::Il2CppArray* arr, il2cpp::Il2CppObject* const* values, int32_t count)
{
    il2cpp::Il2CppObject** slots = reinterpret_cast<il2cpp::Il2CppObject**>(arr->data);
    for (int32_t i = 0; i < count; ++i)
    {
        slots[i] = values[i];
    }
}

void ExpectInt32Content(il2cpp::Il2CppArray* arr, const int32_t* expected, int32_t count, const char* what)
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

void ExpectInt64Content(il2cpp::Il2CppArray* arr, const int64_t* expected, int32_t count, const char* what)
{
    const int64_t* data = reinterpret_cast<const int64_t*>(arr->data);
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
            std::printf("%s%lld", i > 0 ? " " : "",
                        static_cast<long long>(data[i]));
        }
        std::printf(")\n");
        ++g_failures;
    }
}

void ExpectRefs(il2cpp::Il2CppArray* arr, il2cpp::Il2CppObject* const* expected, int32_t count, const char* what)
{
    il2cpp::Il2CppObject** slots = reinterpret_cast<il2cpp::Il2CppObject**>(arr->data);
    int32_t badIndex = -1;
    for (int32_t i = 0; i < count; ++i)
    {
        if (slots[i] != expected[i])
        {
            badIndex = i;
            break;
        }
    }
    if (badIndex >= 0)
    {
        std::printf("FAIL: %s (slot %d: %p vs %p)\n", what, badIndex,
                    (void*)slots[badIndex], (void*)expected[badIndex]);
        ++g_failures;
    }
}

// An UNSTABLE sort (quicksort) may permute EQUAL elements freely; this
// checks that two specific slots hold exactly {a, b} in either order.
void ExpectRefsPair(il2cpp::Il2CppArray* arr, il2cpp::Il2CppObject* a,
                    il2cpp::Il2CppObject* b, int32_t indexA, int32_t indexB,
                    const char* what)
{
    il2cpp::Il2CppObject** slots = reinterpret_cast<il2cpp::Il2CppObject**>(arr->data);
    const bool ok = (slots[indexA] == a && slots[indexB] == b) ||
                    (slots[indexA] == b && slots[indexB] == a);
    if (!ok)
    {
        std::printf("FAIL: %s (slots %d/%d: %p/%p vs %p/%p)\n", what,
                    indexA, indexB, (void*)slots[indexA], (void*)slots[indexB],
                    (void*)a, (void*)b);
        ++g_failures;
    }
}

// Managed string literal -> Il2CppObject* slot (Il2CppString* and
// Il2CppObject* are unrelated structs; the object arrays need the base type).
il2cpp::Il2CppObject* S(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs(ascii));
}

// Ordinal comparison of two string slots (mirror of the runtime comparator,
// reading only the public Il2CppString layout).
int32_t OrdinalCompare(il2cpp::Il2CppObject* left, il2cpp::Il2CppObject* right)
{
    il2cpp::Il2CppString* a = reinterpret_cast<il2cpp::Il2CppString*>(left);
    il2cpp::Il2CppString* b = reinterpret_cast<il2cpp::Il2CppString*>(right);
    if (a == b)
    {
        return 0;
    }
    if (a == nullptr)
    {
        return -1;
    }
    if (b == nullptr)
    {
        return 1;
    }
    const int32_t common = (a->length < b->length) ? a->length : b->length;
    for (int32_t i = 0; i < common; ++i)
    {
        const int32_t diff = static_cast<int32_t>(a->chars[i]) -
                             static_cast<int32_t>(b->chars[i]);
        if (diff != 0)
        {
            return diff;
        }
    }
    return a->length - b->length;
}

// Comparison<T> callback for il2cpp_array_sort_object: REVERSED ordinal
// order -- proves the callback (not a built-in comparer) drives the sort.
int32_t DescendingStringCompare(il2cpp::Il2CppObject* left, il2cpp::Il2CppObject* right)
{
    return -OrdinalCompare(left, right);
}

//------------------------------------------------------------------------------
// D-ICMP (icomparedeep) fixtures -- mock receivers with a vtable slot + a
// registered typeinfo so the runtime interface-dispatch entries resolve.
//------------------------------------------------------------------------------

// Mock IComparable receiver: CompareTo = sign of the pointer order (a
// deterministic anti-symmetric order over distinct static objects).
int32_t MockCompareTo(il2cpp::Il2CppObject* self, il2cpp::Il2CppObject* other)
{
    return self < other ? -1 : (self > other ? 1 : 0);
}

// Mock IComparer: Compare = sign of the pointer order of the two elements.
int32_t MockPointerComparerCompare(il2cpp::Il2CppObject* self,
                                   il2cpp::Il2CppObject* a,
                                   il2cpp::Il2CppObject* b)
{
    return a < b ? -1 : (a > b ? 1 : 0);
}

// DESCENDING int32 comparer (unboxes the boxed elements): proves the user
// comparer (not the built-in ascending order) drives the int32 comparer sort.
int32_t MockDescendingIntComparer(il2cpp::Il2CppObject* self,
                                  il2cpp::Il2CppObject* a,
                                  il2cpp::Il2CppObject* b)
{
    const int32_t x = il2cpp::runtime::icall_unbox_int32(a);
    const int32_t y = il2cpp::runtime::icall_unbox_int32(b);
    return x < y ? 1 : (x > y ? -1 : 0);
}

// DESCENDING string comparer (null sorts last): proves the user comparer
// drives the reference-array comparer sort.
int32_t MockDescendingStringComparer(il2cpp::Il2CppObject* self,
                                     il2cpp::Il2CppObject* a,
                                     il2cpp::Il2CppObject* b)
{
    return -OrdinalCompare(a, b);
}

// Mock IComparable implementation ("Test.IfaceImpl").
void* g_mockIfaceVtable[] = { reinterpret_cast<void*>(&MockCompareTo) };
il2cpp::Il2CppClass g_mockIfaceClass = {
    "Test.IfaceImpl",                 // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize
    0u, 0, nullptr,                   // ref bitmap, count, baseClass
    reinterpret_cast<void**>(g_mockIfaceVtable), // vtable
    1,                                // vtableSize
    nullptr, 0,                       // interfaces
};
static const il2cpp::Il2CppMethodInfo g_mockIfaceMethods[] = {
    { "CompareTo", "i(r:System.Object)", 1, 0x8, 0, "Test.IfaceImpl",
      reinterpret_cast<void*>(&MockCompareTo) },
};
il2cpp::Il2CppTypeInfo g_mockIfaceInfo = {
    "Test.IfaceImpl", &g_mockIfaceClass, nullptr, nullptr, 0,
    nullptr, 0, g_mockIfaceMethods, 1, nullptr, 0,
};

// Mock pointer-order IComparer implementation ("Test.PtrComparer") -- used
// by the il2cpp_icomparer_compare interface-dispatch tests (deterministic
// anti-symmetric pointer order, no unboxing).
void* g_ptrCmpVtable[] = { reinterpret_cast<void*>(&MockPointerComparerCompare) };
il2cpp::Il2CppClass g_ptrCmpClass = {
    "Test.PtrComparer",
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)),
    0u, 0, nullptr,
    reinterpret_cast<void**>(g_ptrCmpVtable),
    1, nullptr, 0,
};
static const il2cpp::Il2CppMethodInfo g_ptrCmpMethods[] = {
    { "Compare", "i(r:System.Object,r:System.Object)", 2, 0x8, 0,
      "Test.PtrComparer", reinterpret_cast<void*>(&MockPointerComparerCompare) },
};
il2cpp::Il2CppTypeInfo g_ptrCmpInfo = {
    "Test.PtrComparer", &g_ptrCmpClass, nullptr, nullptr, 0,
    nullptr, 0, g_ptrCmpMethods, 1, nullptr, 0,
};
il2cpp::Il2CppObject g_ptrCmpReceiver = { &g_ptrCmpClass };

// Mock descending int32 IComparer ("Test.DescIntComparer").
void* g_descIntVtable[] = { reinterpret_cast<void*>(&MockDescendingIntComparer) };
il2cpp::Il2CppClass g_descIntClass = {
    "Test.DescIntComparer",
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)),
    0u, 0, nullptr,
    reinterpret_cast<void**>(g_descIntVtable),
    1, nullptr, 0,
};
static const il2cpp::Il2CppMethodInfo g_descIntMethods[] = {
    { "Compare", "i(r:System.Object,r:System.Object)", 2, 0x8, 0,
      "Test.DescIntComparer", reinterpret_cast<void*>(&MockDescendingIntComparer) },
};
il2cpp::Il2CppTypeInfo g_descIntInfo = {
    "Test.DescIntComparer", &g_descIntClass, nullptr, nullptr, 0,
    nullptr, 0, g_descIntMethods, 1, nullptr, 0,
};

// Mock descending string IComparer ("Test.DescStrComparer").
void* g_descStrVtable[] = { reinterpret_cast<void*>(&MockDescendingStringComparer) };
il2cpp::Il2CppClass g_descStrClass = {
    "Test.DescStrComparer",
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)),
    0u, 0, nullptr,
    reinterpret_cast<void**>(g_descStrVtable),
    1, nullptr, 0,
};
static const il2cpp::Il2CppMethodInfo g_descStrMethods[] = {
    { "Compare", "i(r:System.Object,r:System.Object)", 2, 0x8, 0,
      "Test.DescStrComparer", reinterpret_cast<void*>(&MockDescendingStringComparer) },
};
il2cpp::Il2CppTypeInfo g_descStrInfo = {
    "Test.DescStrComparer", &g_descStrClass, nullptr, nullptr, 0,
    nullptr, 0, g_descStrMethods, 1, nullptr, 0,
};

// The comparer receiver objects (their klass carries the vtable + typeinfo).
il2cpp::Il2CppObject g_descIntReceiver = { &g_descIntClass };
il2cpp::Il2CppObject g_descStrReceiver = { &g_descStrClass };

// Mock Comparison<T> delegates (the single-cast static methodPtr ABI).
il2cpp::Il2CppClass g_mockDelegateClass = {
    "Test.ComparisonMock",             // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppDelegate)), // instanceSize
    0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

// Comparison<int> static leaf: descending.
int32_t MockDescIntComparison(int32_t a, int32_t b)
{
    return a < b ? 1 : (a > b ? -1 : 0);
}

// Comparison<int> instance-bound leaf: descending (the target is ignored).
int32_t MockDescIntComparisonThis(il2cpp::Il2CppObject* target, int32_t a, int32_t b)
{
    return a < b ? 1 : (a > b ? -1 : 0);
}

// Comparison<string> static leaf: descending ordinal.
int32_t MockDescStrComparison(il2cpp::Il2CppObject* a, il2cpp::Il2CppObject* b)
{
    return -OrdinalCompare(a, b);
}

il2cpp::Il2CppDelegate g_staticIntCmp = {
    { &g_mockDelegateClass }, reinterpret_cast<void*>(&MockDescIntComparison), nullptr,
};
il2cpp::Il2CppDelegate g_thisIntCmp = {
    { &g_mockDelegateClass }, reinterpret_cast<void*>(&MockDescIntComparisonThis),
    reinterpret_cast<il2cpp::Il2CppObject*>(0x1),
};
il2cpp::Il2CppDelegate g_staticStrCmp = {
    { &g_mockDelegateClass }, reinterpret_cast<void*>(&MockDescStrComparison), nullptr,
};

} // namespace

int main()
{
    // --- 1. Sort int32 ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(8);
        const int32_t unsorted[8] = { 3, -1, 7, 0, -5, 2, 3, 9 };
        FillInt32(a, unsorted, 8);
        il2cpp::il2cpp_array_sort_int32(a);
        const int32_t sorted[8] = { -5, -1, 0, 2, 3, 3, 7, 9 };
        ExpectInt32Content(a, sorted, 8, "sort orders an unsorted int32 array ascending (with duplicates)");

        il2cpp::Il2CppArray* pre = NewInt32Array(5);
        const int32_t already[5] = { 1, 2, 3, 4, 5 };
        FillInt32(pre, already, 5);
        il2cpp::il2cpp_array_sort_int32(pre);
        ExpectInt32Content(pre, already, 5, "sort of an already-sorted int32 array is a no-op");

        il2cpp::Il2CppArray* neg = NewInt32Array(4);
        const int32_t negs[4] = { -1, INT32_MIN, -3, -2 };
        FillInt32(neg, negs, 4);
        il2cpp::il2cpp_array_sort_int32(neg);
        const int32_t negSorted[4] = { INT32_MIN, -3, -2, -1 };
        ExpectInt32Content(neg, negSorted, 4, "sort handles INT32_MIN");

        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        il2cpp::il2cpp_array_sort_int32(empty);
        ExpectI32(il2cpp::il2cpp_array_get_length(empty), 0, "sort of an empty int32 array is a no-op");

        il2cpp::Il2CppArray* single = NewInt32Array(1);
        const int32_t one[1] = { 9 };
        FillInt32(single, one, 1);
        il2cpp::il2cpp_array_sort_int32(single);
        ExpectInt32Content(single, one, 1, "sort of a single-element int32 array is a no-op");

        ExpectThrows(kArgNullType, "sort of null throws ArgumentNullException",
                     [] { il2cpp::il2cpp_array_sort_int32(nullptr); });
        il2cpp::Il2CppArray* refArr = NewRefArray(2);
        ExpectThrows(kNotSupportedType, "sort of a reference array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_sort_int32(refArr); });
        il2cpp::Il2CppArray* i64Arr = NewInt64Array(2);
        ExpectThrows(kNotSupportedType, "sort of an int64 array via the int32 entry throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_sort_int32(i64Arr); });
    }

    // --- 2. Sort uint32 ---
    {
        il2cpp::Il2CppArray* a = NewUInt32Array(5);
        const uint32_t unsorted[5] = { 5, UINT32_MAX, 0, 3, UINT32_MAX - 1 };
        FillUInt32(a, unsorted, 5);
        il2cpp::il2cpp_array_sort_uint32(a);
        const uint32_t* data = reinterpret_cast<const uint32_t*>(a->data);
        const uint32_t sorted[5] = { 0, 3, 5, UINT32_MAX - 1, UINT32_MAX };
        bool ok = true;
        for (int32_t i = 0; i < 5; ++i)
        {
            if (data[i] != sorted[i])
            {
                ok = false;
            }
        }
        Expect(ok, "sort orders a uint32 array ascending (UINT32_MAX boundary)");
    }

    // --- 3. Sort int64 ---
    {
        il2cpp::Il2CppArray* a = NewInt64Array(6);
        const int64_t unsorted[6] = { 3LL, -1000000000000LL, INT64_MIN, 7LL, INT64_MAX, 0LL };
        FillInt64(a, unsorted, 6);
        il2cpp::il2cpp_array_sort_int64(a);
        const int64_t sorted[6] = { INT64_MIN, -1000000000000LL, 0LL, 3LL, 7LL, INT64_MAX };
        ExpectInt64Content(a, sorted, 6, "sort orders an int64 array ascending (INT64_MIN/MAX boundary)");
    }

    // --- 4. Sort string (ordinal, nulls first) ---
    {
        il2cpp::Il2CppObject* pear = S("pear");
        il2cpp::Il2CppObject* apple = S("apple");
        il2cpp::Il2CppObject* banana = S("banana");
        il2cpp::Il2CppObject* apple2 = S("apple");
        il2cpp::Il2CppArray* a = NewRefArray(4);
        il2cpp::Il2CppObject* unsorted[4] = { pear, apple, banana, apple2 };
        FillRefs(a, unsorted, 4);
        il2cpp::il2cpp_array_sort_string(a);
        Expect(reinterpret_cast<il2cpp::Il2CppObject**>(a->data)[2] == banana &&
               reinterpret_cast<il2cpp::Il2CppObject**>(a->data)[3] == pear,
               "sort places banana and pear after the equal apples");
        ExpectRefsPair(a, apple, apple2, 0, 1,
                       "sort keeps the equal apple strings (either order)");

        il2cpp::Il2CppObject* zebra = S("zebra");
        il2cpp::Il2CppObject* alpha = S("alpha");
        il2cpp::Il2CppObject* mango = S("mango");
        il2cpp::Il2CppArray* n = NewRefArray(4);
        il2cpp::Il2CppObject* withNull[4] = { zebra, nullptr, alpha, mango };
        FillRefs(n, withNull, 4);
        il2cpp::il2cpp_array_sort_string(n);
        il2cpp::Il2CppObject* nullFirst[4] = { nullptr, alpha, mango, zebra };
        ExpectRefs(n, nullFirst, 4, "null string elements sort first");

        il2cpp::Il2CppArray* empty = NewRefArray(0);
        il2cpp::il2cpp_array_sort_string(empty);
        ExpectI32(il2cpp::il2cpp_array_get_length(empty), 0, "sort of an empty string array is a no-op");
        ExpectThrows(kNotSupportedType, "sort_string of an int32 array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_sort_string(NewInt32Array(2)); });
    }

    // --- 5. Sort object (comparison callback) ---
    {
        il2cpp::Il2CppObject* pear = S("pear");
        il2cpp::Il2CppObject* apple = S("apple");
        il2cpp::Il2CppObject* banana = S("banana");
        il2cpp::Il2CppObject* apple2 = S("apple");
        il2cpp::Il2CppArray* a = NewRefArray(4);
        il2cpp::Il2CppObject* unsorted[4] = { apple, pear, apple2, banana };
        FillRefs(a, unsorted, 4);
        il2cpp::il2cpp_array_sort_object(a, DescendingStringCompare);
        Expect(reinterpret_cast<il2cpp::Il2CppObject**>(a->data)[0] == pear &&
               reinterpret_cast<il2cpp::Il2CppObject**>(a->data)[1] == banana,
               "the comparison callback drives the object sort (descending ordinal)");
        ExpectRefsPair(a, apple, apple2, 2, 3,
                       "equal apples end up after pear and banana (either order)");

        il2cpp::Il2CppArray* empty = NewRefArray(0);
        il2cpp::il2cpp_array_sort_object(empty, DescendingStringCompare);
        ExpectI32(il2cpp::il2cpp_array_get_length(empty), 0, "object sort of an empty array is a no-op");

        ExpectThrows(kArgNullType, "object sort with a null callback throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_array_sort_object(NewRefArray(1), nullptr); });
        ExpectThrows(kNotSupportedType, "object sort of an int32 array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_sort_object(NewInt32Array(1), DescendingStringCompare); });
    }

    // --- 6. CopyTo ---
    {
        il2cpp::Il2CppArray* src = NewInt32Array(3);
        const int32_t sv[3] = { 10, 20, 30 };
        FillInt32(src, sv, 3);
        il2cpp::Il2CppArray* dst = NewInt32Array(5);
        il2cpp::il2cpp_array_copy_to(src, dst, 1);
        const int32_t intoMiddle[5] = { 0, 10, 20, 30, 0 };
        ExpectInt32Content(dst, intoMiddle, 5, "copy_to copies every element into the middle of dst");

        il2cpp::Il2CppArray* exact = NewInt32Array(3);
        il2cpp::il2cpp_array_copy_to(src, exact, 0);
        ExpectInt32Content(exact, sv, 3, "copy_to into an exact-fit dst");

        il2cpp::Il2CppArray* emptySrc = NewInt32Array(0);
        il2cpp::Il2CppArray* dst5 = NewInt32Array(5);
        const int32_t zeros[5] = { 0, 0, 0, 0, 0 };
        FillInt32(dst5, zeros, 5);
        il2cpp::il2cpp_array_copy_to(emptySrc, dst5, 5);
        ExpectInt32Content(dst5, zeros, 5, "copy_to of an empty src is a no-op (dstIndex == dst length legal)");

        il2cpp::Il2CppArray* ov = NewInt32Array(5);
        const int32_t five[5] = { 1, 2, 3, 4, 5 };
        FillInt32(ov, five, 5);
        il2cpp::il2cpp_array_copy_to(ov, ov, 0);
        ExpectInt32Content(ov, five, 5, "identity copy_to (dstIndex 0) leaves the array unchanged");
        ExpectThrows(kArgumentType, "an offset self-copy_to throws ArgumentException (CopyTo cannot overlap)",
                     [&] { il2cpp::il2cpp_array_copy_to(ov, ov, 1); });

        ExpectThrows(kArgNullType, "copy_to with null src throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_array_copy_to(nullptr, dst, 0); });
        ExpectThrows(kArgNullType, "copy_to with null dst throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_array_copy_to(src, nullptr, 0); });
        ExpectThrows(kArgOutOfRangeType, "copy_to with a negative dstIndex throws ArgumentOutOfRangeException",
                     [&] { il2cpp::il2cpp_array_copy_to(src, dst, -1); });
        ExpectThrows(kArgumentType, "copy_to into a too-small dst throws ArgumentException",
                     [&] { il2cpp::il2cpp_array_copy_to(src, dst, 4); });
        il2cpp::Il2CppArray* i64Dst = NewInt64Array(3);
        ExpectThrows(kTypeMismatchType, "copy_to with mismatched element kinds throws ArrayTypeMismatchException",
                     [&] { il2cpp::il2cpp_array_copy_to(src, i64Dst, 0); });
    }

    // --- 7. IndexOf family ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(5);
        const int32_t vals[5] = { 10, 20, 30, 20, 40 };
        FillInt32(a, vals, 5);
        ExpectI32(il2cpp::il2cpp_array_index_of_int32(a, 20), 1,
                  "index_of_int32 returns the first occurrence");
        ExpectI32(il2cpp::il2cpp_array_index_of_int32(a, 40), 4,
                  "index_of_int32 finds the last element");
        ExpectI32(il2cpp::il2cpp_array_index_of_int32(a, 99), -1,
                  "index_of_int32 of an absent value is -1");
        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        ExpectI32(il2cpp::il2cpp_array_index_of_int32(empty, 1), -1,
                  "index_of_int32 in an empty array is -1");
        ExpectThrows(kArgNullType, "index_of_int32 of null throws ArgumentNullException",
                     [] { il2cpp::il2cpp_array_index_of_int32(nullptr, 1); });

        il2cpp::Il2CppArray* l = NewInt64Array(4);
        const int64_t lvals[4] = { 1LL, 2LL, 3LL, 2LL };
        FillInt64(l, lvals, 4);
        ExpectI32(il2cpp::il2cpp_array_index_of_int64(l, 2LL), 1,
                  "index_of_int64 returns the first occurrence");
        ExpectI32(il2cpp::il2cpp_array_index_of_int64(l, 5LL), -1,
                  "index_of_int64 of an absent value is -1");

        il2cpp::Il2CppObject* apple = S("apple");
        il2cpp::Il2CppObject* banana = S("banana");
        il2cpp::Il2CppObject* apple2 = S("apple");
        il2cpp::Il2CppArray* s = NewRefArray(3);
        il2cpp::Il2CppObject* svals[3] = { apple, banana, apple2 };
        FillRefs(s, svals, 3);
        ExpectI32(il2cpp::il2cpp_array_index_of_string(s, apple), 0,
                  "index_of_string returns the first ordinal match");
        ExpectI32(il2cpp::il2cpp_array_index_of_string(s, banana), 1,
                  "index_of_string finds a later element");
        il2cpp::Il2CppObject* cherry = S("cherry");
        ExpectI32(il2cpp::il2cpp_array_index_of_string(s, cherry), -1,
                  "index_of_string of an absent value is -1");

        il2cpp::Il2CppArray* withNull = NewRefArray(2);
        il2cpp::Il2CppObject* nvals[2] = { nullptr, apple };
        FillRefs(withNull, nvals, 2);
        ExpectI32(il2cpp::il2cpp_array_index_of_string(withNull, nullptr), 0,
                  "index_of_string with a null value matches the first null slot");

        il2cpp::Il2CppObject* nonString = il2cpp::il2cpp_gc_alloc(&g_mockElemClass);
        ExpectThrows(kArgumentType, "index_of_string with a non-string value throws ArgumentException",
                     [&] { il2cpp::il2cpp_array_index_of_string(s, nonString); });
        ExpectThrows(kNotSupportedType, "index_of_int32 of a string array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_index_of_int32(s, 1); });
        ExpectThrows(kNotSupportedType, "index_of_string of an int32 array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_index_of_string(a, nullptr); });
    }

    // --- 8. BinarySearch family ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(5);
        const int32_t vals[5] = { 1, 3, 5, 7, 9 };
        FillInt32(a, vals, 5);
        ExpectI32(il2cpp::il2cpp_array_binary_search_int32(a, 5), 2,
                  "binary_search_int32 finds a middle value");
        ExpectI32(il2cpp::il2cpp_array_binary_search_int32(a, 1), 0,
                  "binary_search_int32 finds the first element");
        ExpectI32(il2cpp::il2cpp_array_binary_search_int32(a, 9), 4,
                  "binary_search_int32 finds the last element");
        ExpectI32(il2cpp::il2cpp_array_binary_search_int32(a, 4), ~2,
                  "binary_search_int32 of a missing value is ~insertion point");
        ExpectI32(il2cpp::il2cpp_array_binary_search_int32(a, 0), ~0,
                  "binary_search_int32 before the range is ~0");
        ExpectI32(il2cpp::il2cpp_array_binary_search_int32(a, 10), ~5,
                  "binary_search_int32 after the range is ~length");

        il2cpp::Il2CppArray* dup = NewInt32Array(5);
        const int32_t dvals[5] = { 1, 3, 3, 3, 7 };
        FillInt32(dup, dvals, 5);
        ExpectI32(il2cpp::il2cpp_array_binary_search_int32(dup, 3), 1,
                  "binary_search_int32 with duplicates returns the first match");
        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        ExpectI32(il2cpp::il2cpp_array_binary_search_int32(empty, 1), -1,
                  "binary_search_int32 of an empty array is -1");
        ExpectThrows(kArgNullType, "binary_search_int32 of null throws ArgumentNullException",
                     [] { il2cpp::il2cpp_array_binary_search_int32(nullptr, 1); });

        il2cpp::Il2CppArray* l = NewInt64Array(4);
        const int64_t lvals[4] = { 10LL, 20LL, 30LL, 40LL };
        FillInt64(l, lvals, 4);
        ExpectI32(il2cpp::il2cpp_array_binary_search_int64(l, 30LL), 2,
                  "binary_search_int64 finds a middle value");
        ExpectI32(il2cpp::il2cpp_array_binary_search_int64(l, 25LL), ~2,
                  "binary_search_int64 of a missing value is ~insertion point");

        il2cpp::Il2CppObject* apple = S("apple");
        il2cpp::Il2CppObject* banana = S("banana");
        il2cpp::Il2CppObject* mango = S("mango");
        il2cpp::Il2CppArray* s = NewRefArray(3);
        il2cpp::Il2CppObject* svals[3] = { apple, banana, mango };
        FillRefs(s, svals, 3);
        ExpectI32(il2cpp::il2cpp_array_binary_search_string(s, banana), 1,
                  "binary_search_string finds a present value");
        il2cpp::Il2CppObject* cherry = S("cherry");
        ExpectI32(il2cpp::il2cpp_array_binary_search_string(s, cherry), ~2,
                  "binary_search_string of a missing value is ~insertion point");
        ExpectI32(il2cpp::il2cpp_array_binary_search_string(s, nullptr), ~0,
                  "binary_search_string of null in a null-free array is ~0");

        il2cpp::Il2CppArray* withNull = NewRefArray(2);
        il2cpp::Il2CppObject* nvals[2] = { nullptr, apple };
        FillRefs(withNull, nvals, 2);
        ExpectI32(il2cpp::il2cpp_array_binary_search_string(withNull, nullptr), 0,
                  "binary_search_string of null matches a null slot");

        il2cpp::Il2CppObject* nonString = il2cpp::il2cpp_gc_alloc(&g_mockElemClass);
        ExpectThrows(kArgumentType, "binary_search_string with a non-string value throws ArgumentException",
                     [&] { il2cpp::il2cpp_array_binary_search_string(s, nonString); });
    }

    // --- 9. Partial sort (index, length) ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(8);
        const int32_t unsorted[8] = { 100, 9, 3, 7, 1, 5, 8, 200 };
        FillInt32(a, unsorted, 8);
        il2cpp::il2cpp_array_sort_int32(a, 2, 4); // sort [2, 6)
        const int32_t partial[8] = { 100, 9, 1, 3, 5, 7, 8, 200 };
        ExpectInt32Content(a, partial, 8,
                           "partial int32 sort orders only [index, index+length)");

        il2cpp::Il2CppArray* full = NewInt32Array(4);
        const int32_t fullIn[4] = { 4, 2, 3, 1 };
        FillInt32(full, fullIn, 4);
        il2cpp::il2cpp_array_sort_int32(full, 0, 4);
        const int32_t fullSorted[4] = { 1, 2, 3, 4 };
        ExpectInt32Content(full, fullSorted, 4,
                           "partial int32 sort with the full range sorts the whole array");

        il2cpp::Il2CppArray* shortArr = NewInt32Array(3);
        const int32_t shortIn[3] = { 3, 1, 2 };
        FillInt32(shortArr, shortIn, 3);
        il2cpp::il2cpp_array_sort_int32(shortArr, 1, 1);
        ExpectInt32Content(shortArr, shortIn, 3,
                           "partial int32 sort of a length-1 range is a no-op");
        il2cpp::il2cpp_array_sort_int32(shortArr, 3, 0);
        ExpectInt32Content(shortArr, shortIn, 3,
                           "partial int32 sort of a length-0 range at the end is a no-op");

        ExpectThrows(kArgOutOfRangeType, "partial int32 sort with a negative index throws ArgumentOutOfRangeException",
                     [&] { il2cpp::il2cpp_array_sort_int32(a, -1, 2); });
        ExpectThrows(kArgOutOfRangeType, "partial int32 sort with a negative length throws ArgumentOutOfRangeException",
                     [&] { il2cpp::il2cpp_array_sort_int32(a, 1, -1); });
        ExpectThrows(kArgumentType, "partial int32 sort with an out-of-range range throws ArgumentException",
                     [&] { il2cpp::il2cpp_array_sort_int32(a, 3, 6); });
        ExpectThrows(kArgNullType, "partial int32 sort of null throws ArgumentNullException",
                     [] { il2cpp::il2cpp_array_sort_int32(nullptr, 0, 1); });
        ExpectThrows(kNotSupportedType, "partial int32 sort of a reference array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_sort_int32(NewRefArray(2), 0, 2); });

        il2cpp::Il2CppArray* u = NewUInt32Array(5);
        const uint32_t uv[5] = { UINT32_MAX, 1, UINT32_MAX - 1, 2, 3 };
        FillUInt32(u, uv, 5);
        il2cpp::il2cpp_array_sort_uint32(u, 1, 3);
        const uint32_t* ud = reinterpret_cast<const uint32_t*>(u->data);
        // [1, 4) ascending: {1, MAX-1, 2} -> {1, 2, MAX-1}; head/tail untouched.
        const bool uok = ud[0] == UINT32_MAX && ud[1] == 1 && ud[2] == 2 &&
                         ud[3] == UINT32_MAX - 1 && ud[4] == 3;
        Expect(uok, "partial uint32 sort orders the range (UINT32_MAX boundary)");

        il2cpp::Il2CppArray* l = NewInt64Array(6);
        const int64_t lv[6] = { 10LL, INT64_MAX, -5LL, INT64_MIN, 0LL, 20LL };
        FillInt64(l, lv, 6);
        il2cpp::il2cpp_array_sort_int64(l, 1, 4);
        const int64_t lSorted[6] = { 10LL, INT64_MIN, -5LL, 0LL, INT64_MAX, 20LL };
        ExpectInt64Content(l, lSorted, 6,
                           "partial int64 sort orders the range (INT64 boundaries)");

        il2cpp::Il2CppObject* pear = S("pear");
        il2cpp::Il2CppObject* apple = S("apple");
        il2cpp::Il2CppObject* banana = S("banana");
        il2cpp::Il2CppArray* s = NewRefArray(4);
        il2cpp::Il2CppObject* sv[4] = { pear, banana, apple, nullptr };
        FillRefs(s, sv, 4);
        il2cpp::il2cpp_array_sort_string(s, 1, 2);
        il2cpp::Il2CppObject* sSorted[4] = { pear, apple, banana, nullptr };
        ExpectRefs(s, sSorted, 4,
                   "partial string sort orders only the range (ordinal, null tail kept)");

        il2cpp::Il2CppArray* dup = NewRefArray(4);
        il2cpp::Il2CppObject* dupIn[4] = { banana, apple, S("apple"), pear };
        FillRefs(dup, dupIn, 4);
        il2cpp::il2cpp_array_sort_string(dup, 0, 3); // sort [0,3): equal apples may permute
        Expect(reinterpret_cast<il2cpp::Il2CppObject**>(dup->data)[3] == pear,
               "partial string sort keeps a non-range element in place (pear at the tail)");
        ExpectRefsPair(dup, dupIn[1], dupIn[2], 0, 1,
                       "partial string sort permutes the equal apple strings freely (UNSTABLE net48)");
    }

    // --- 10. Partial object sort (comparison callback) ---
    {
        il2cpp::Il2CppObject* pear = S("pear");
        il2cpp::Il2CppObject* apple = S("apple");
        il2cpp::Il2CppObject* banana = S("banana");
        il2cpp::Il2CppObject* zebra = S("zebra");
        il2cpp::Il2CppArray* o = NewRefArray(4);
        il2cpp::Il2CppObject* ov[4] = { pear, apple, banana, zebra };
        FillRefs(o, ov, 4);
        il2cpp::il2cpp_array_sort_object(o, 1, 2, DescendingStringCompare);
        il2cpp::Il2CppObject* oSorted[4] = { pear, banana, apple, zebra };
        ExpectRefs(o, oSorted, 4,
                   "partial object sort drives the range with the comparison callback (descending)");

        ExpectThrows(kArgNullType, "partial object sort with a null callback throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_array_sort_object(o, 0, 1, nullptr); });
        ExpectThrows(kArgumentType, "partial object sort with an out-of-range range throws ArgumentException",
                     [&] { il2cpp::il2cpp_array_sort_object(o, 2, 3, DescendingStringCompare); });
        ExpectThrows(kArgOutOfRangeType, "partial object sort with a negative length throws ArgumentOutOfRangeException",
                     [&] { il2cpp::il2cpp_array_sort_object(o, 0, -1, DescendingStringCompare); });
        ExpectThrows(kArgNullType, "partial object sort of null throws ArgumentNullException",
                     [] { il2cpp::il2cpp_array_sort_object(nullptr, 0, 1, DescendingStringCompare); });
    }

    // --- 11. Partial reverse (index, length) ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(6);
        const int32_t v[6] = { 0, 1, 2, 3, 4, 5 };
        FillInt32(a, v, 6);
        il2cpp::il2cpp_array_reverse_int32(a, 1, 4); // reverse [1, 5)
        const int32_t rev[6] = { 0, 4, 3, 2, 1, 5 };
        ExpectInt32Content(a, rev, 6,
                           "partial int32 reverse reverses only [index, index+length)");

        il2cpp::Il2CppArray* full = NewInt32Array(4);
        const int32_t fullIn[4] = { 1, 2, 3, 4 };
        FillInt32(full, fullIn, 4);
        il2cpp::il2cpp_array_reverse_int32(full, 0, 4);
        const int32_t fullRev[4] = { 4, 3, 2, 1 };
        ExpectInt32Content(full, fullRev, 4,
                           "partial int32 reverse with the full range reverses the whole array");

        il2cpp::il2cpp_array_reverse_int32(a, 1, 4);
        ExpectInt32Content(a, v, 6,
                           "reversing a range twice restores the original order");

        il2cpp::Il2CppArray* shortArr = NewInt32Array(3);
        const int32_t shortIn[3] = { 7, 8, 9 };
        FillInt32(shortArr, shortIn, 3);
        il2cpp::il2cpp_array_reverse_int32(shortArr, 1, 1);
        il2cpp::il2cpp_array_reverse_int32(shortArr, 3, 0);
        ExpectInt32Content(shortArr, shortIn, 3,
                           "partial int32 reverse of length 0/1 ranges is a no-op");

        ExpectThrows(kArgOutOfRangeType, "partial reverse with a negative index throws ArgumentOutOfRangeException",
                     [&] { il2cpp::il2cpp_array_reverse_int32(a, -1, 2); });
        ExpectThrows(kArgOutOfRangeType, "partial reverse with a negative length throws ArgumentOutOfRangeException",
                     [&] { il2cpp::il2cpp_array_reverse_int32(a, 1, -1); });
        ExpectThrows(kArgumentType, "partial reverse with an out-of-range range throws ArgumentException",
                     [&] { il2cpp::il2cpp_array_reverse_int32(a, 3, 4); });
        ExpectThrows(kArgNullType, "partial reverse of null throws ArgumentNullException",
                     [] { il2cpp::il2cpp_array_reverse_int32(nullptr, 0, 1); });
        ExpectThrows(kNotSupportedType, "partial int32 reverse of a reference array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_reverse_int32(NewRefArray(2), 0, 2); });

        il2cpp::Il2CppArray* u = NewUInt32Array(4);
        const uint32_t uv[4] = { 1, 2, 3, 4 };
        FillUInt32(u, uv, 4);
        il2cpp::il2cpp_array_reverse_uint32(u, 1, 2); // reverse [1,3): 2,3 -> 3,2
        const uint32_t* ud = reinterpret_cast<const uint32_t*>(u->data);
        const bool uok = ud[0] == 1 && ud[1] == 3 && ud[2] == 2 && ud[3] == 4;
        Expect(uok, "partial uint32 reverse reverses only the range");

        il2cpp::Il2CppArray* l = NewInt64Array(4);
        const int64_t lv[4] = { 1LL, 2LL, 3LL, 4LL };
        FillInt64(l, lv, 4);
        il2cpp::il2cpp_array_reverse_int64(l, 0, 3); // reverse [0,3): 1,2,3 -> 3,2,1
        const int64_t lRev[4] = { 3LL, 2LL, 1LL, 4LL };
        ExpectInt64Content(l, lRev, 4, "partial int64 reverse reverses only the range");

        il2cpp::Il2CppObject* pear = S("pear");
        il2cpp::Il2CppObject* apple = S("apple");
        il2cpp::Il2CppArray* s = NewRefArray(4);
        il2cpp::Il2CppObject* sv[4] = { pear, nullptr, apple, pear };
        FillRefs(s, sv, 4);
        il2cpp::il2cpp_array_reverse_string(s, 1, 3); // reverse [1,4): null,apple,pear -> pear,apple,null
        il2cpp::Il2CppObject* sRev[4] = { pear, pear, apple, nullptr };
        ExpectRefs(s, sRev, 4,
                   "partial string reverse reverses only the range (identity, null slots legal)");

        il2cpp::Il2CppArray* o = NewRefArray(4);
        il2cpp::Il2CppObject* ov[4] = { apple, pear, nullptr, apple };
        FillRefs(o, ov, 4);
        il2cpp::il2cpp_array_reverse_object(o, 1, 2); // reverse [1,3): pear,null -> null,pear
        il2cpp::Il2CppObject* oRev[4] = { apple, nullptr, pear, apple };
        ExpectRefs(o, oRev, 4,
                   "partial object reverse reverses only the range (write barriers run)");

        ExpectThrows(kNotSupportedType, "partial int32 reverse of a string array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_reverse_int32(s, 0, 2); });
        ExpectThrows(kNotSupportedType, "partial string reverse of an int32 array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_reverse_string(a, 0, 2); });
    }

    // --- 12. D-ICMP comparer/comparison domain (arraysort.h D-ICMP section) ---
    {
        il2cpp::il2cpp_typeinfo_register(&g_mockIfaceInfo);
        il2cpp::il2cpp_typeinfo_register(&g_ptrCmpInfo);
        il2cpp::il2cpp_typeinfo_register(&g_descIntInfo);
        il2cpp::il2cpp_typeinfo_register(&g_descStrInfo);

        // IComparable.CompareTo interface dispatch (resolves the vtable slot
        // by name through the registered typeinfo).
        il2cpp::Il2CppObject mockA = { &g_mockIfaceClass };
        il2cpp::Il2CppObject mockB = { &g_mockIfaceClass };
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(&mockA, &mockB),
                  -il2cpp::il2cpp_icomparable_compare_to(&mockB, &mockA),
                  "icomparable dispatch is sign-symmetric");
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(&mockA, &mockA), 0,
                  "icomparable dispatch self-compare is 0");
        ExpectThrows(kNullRefType,
                     "icomparable dispatch of a null receiver throws NullReferenceException",
                     [] { il2cpp::il2cpp_icomparable_compare_to(nullptr, nullptr); });

        // IComparer.Compare interface dispatch.
        ExpectI32(il2cpp::il2cpp_icomparer_compare(&g_ptrCmpReceiver, &mockA, &mockB),
                  -il2cpp::il2cpp_icomparer_compare(&g_ptrCmpReceiver, &mockB, &mockA),
                  "icomparer dispatch is sign-symmetric");
        ExpectI32(il2cpp::il2cpp_icomparer_compare(&g_ptrCmpReceiver, &mockA, &mockA), 0,
                  "icomparer dispatch equal elements are 0");

        // System.Collections.Comparer.Default: the singleton + the net48
        // static-readonly-field storage symbol alias it.
        il2cpp::Il2CppObject* cd = il2cpp::il2cpp_comparer_default();
        Expect(cd != nullptr, "Comparer.Default returns a non-null singleton");
        Expect(il2cpp::il2cpp_comparer_default() == cd,
               "Comparer.Default getter returns the singleton");
        Expect(::System_Collections_Comparer_sf_Default == cd,
               "Comparer.Default field storage aliases the comparer singleton");
        ExpectI32(il2cpp::il2cpp_comparer_compare(cd, cd, nullptr), 1,
                  "default comparer sorts null first (non-null > null)");
        // Default comparer on boxed int32 (value dispatch).
        il2cpp::Il2CppObject* b3 = il2cpp::il2cpp_box_int32(&System_Int32_class, 3);
        il2cpp::Il2CppObject* b5 = il2cpp::il2cpp_box_int32(&System_Int32_class, 5);
        ExpectI32(il2cpp::il2cpp_comparer_compare(cd, b3, b5), -1,
                  "default comparer orders boxed int32 ascending");
        ExpectI32(il2cpp::il2cpp_comparer_compare(cd, b5, b3), 1,
                  "default comparer orders boxed int32 reverse");
        ExpectI32(il2cpp::il2cpp_comparer_compare(cd, b3, b3), 0,
                  "default comparer equal boxed int32 is 0");
        ExpectI32(il2cpp::il2cpp_comparer_compare(cd, nullptr, b3), -1,
                  "default comparer null before a boxed value");
        ExpectI32(il2cpp::il2cpp_comparer_compare(cd, nullptr, nullptr), 0,
                  "default comparer null==null is 0");

        // G2 (D3): boxed-BUILTIN IComparable.CompareTo dispatch through the
        // RAW VALUE TABLE (TryCompareBoxedBuiltin -- a box klass has no
        // reflected vtable). Boxed int32/double + string receivers.
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(b3, b5), -1,
                  "icomparable on boxed int32 orders ascending");
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(b5, b3), 1,
                  "icomparable on boxed int32 orders reverse");
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(b3, b3), 0,
                  "icomparable on boxed int32 equal is 0");
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(b3, nullptr), 1,
                  "icomparable on boxed int32 vs null is 1 (value > null)");
        ExpectThrows(kArgumentType,
                     "icomparable on boxed int32 vs a foreign type throws ArgumentException",
                     [&] { (void)il2cpp::il2cpp_icomparable_compare_to(b3, S("nope")); });
        il2cpp::Il2CppObject* boxD1 = il2cpp::runtime::icall_box_double(2.5);
        il2cpp::Il2CppObject* boxD2 = il2cpp::runtime::icall_box_double(2.4);
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(boxD1, boxD2), 1,
                  "icomparable on boxed double orders ascending");
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(boxD1, boxD1), 0,
                  "icomparable on boxed double equal is 0");
        ExpectThrows(kArgumentType,
                     "icomparable on boxed double vs boxed int32 throws ArgumentException",
                     [&] { (void)il2cpp::il2cpp_icomparable_compare_to(boxD1, b3); });
        // Cross-type with the RECEIVER double, foreign int32 operand.
        ExpectThrows(kArgumentType,
                     "icomparable on boxed int32 vs boxed double throws ArgumentException",
                     [&] { (void)il2cpp::il2cpp_icomparable_compare_to(b3, boxD1); });

        // Int64 / Char / Boolean boxed receivers (the long/short-width value
        // arms of the raw table).
        il2cpp::Il2CppObject* boxL9 = il2cpp::runtime::icall_box_long(9);
        il2cpp::Il2CppObject* boxL10 = il2cpp::runtime::icall_box_long(10);
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(boxL9, boxL10), -1,
                  "icomparable on boxed int64 orders ascending");
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(boxL10, boxL9), 1,
                  "icomparable on boxed int64 orders reverse");
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(boxL9, nullptr), 1,
                  "icomparable on boxed int64 vs null is 1");
        ExpectThrows(kArgumentType,
                     "icomparable on boxed int64 vs boxed int32 throws ArgumentException",
                     [&] { (void)il2cpp::il2cpp_icomparable_compare_to(boxL9, b3); });
        il2cpp::Il2CppObject* boxC = il2cpp::runtime::icall_box_char(L'A');
        il2cpp::Il2CppObject* boxB = il2cpp::runtime::icall_box_char(L'B');
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(boxC, boxB), -1,
                  "icomparable on boxed char orders ascending");
        il2cpp::Il2CppObject* boxT = il2cpp::runtime::icall_box_bool(true);
        il2cpp::Il2CppObject* boxF = il2cpp::runtime::icall_box_bool(false);
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(boxT, boxF), 1,
                  "icomparable on boxed bool true > false");
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(boxF, boxF), 0,
                  "icomparable on boxed bool equal is 0");

        // Default comparer on strings (ordinal) + the user IComparable
        // fallback (the mock receiver's CompareTo).
        il2cpp::Il2CppObject* sa = S("a");
        il2cpp::Il2CppObject* sb = S("b");
        Expect(il2cpp::il2cpp_comparer_compare(cd, sa, sb) < 0,
               "default comparer orders strings ascending");
        ExpectI32(il2cpp::il2cpp_icomparable_compare_to(sa, nullptr), 1,
                  "icomparable on a string receiver vs null is 1 (string > null)");
        ExpectI32(il2cpp::il2cpp_comparer_compare(cd, &mockA, &mockB),
                  il2cpp::il2cpp_icomparable_compare_to(&mockA, &mockB),
                  "default comparer falls back to the receiver's IComparable");

        // Array.Sort(Array, IComparer) with a USER comparer (vtable dispatch).
        il2cpp::Il2CppArray* da = NewInt32Array(5);
        const int32_t dv[5] = { 3, 1, 4, 1, 5 };
        FillInt32(da, dv, 5);
        il2cpp::il2cpp_array_sort_icomparer(da, &g_descIntReceiver);
        const int32_t dSorted[5] = { 5, 4, 3, 1, 1 };
        ExpectInt32Content(da, dSorted, 5,
                           "user IComparer drives the int32 comparer sort (descending)");

        // Array.Sort(Array, IComparer) with the DEFAULT comparer (ascending)
        // and with a null comparer (net48: null means the default comparer).
        il2cpp::Il2CppArray* nd = NewInt32Array(5);
        FillInt32(nd, dv, 5);
        il2cpp::il2cpp_array_sort_icomparer(nd, il2cpp::il2cpp_comparer_default());
        const int32_t ndSorted[5] = { 1, 1, 3, 4, 5 };
        ExpectInt32Content(nd, ndSorted, 5,
                           "default-comparer object sorts int32 ascending");
        il2cpp::Il2CppArray* nd2 = NewInt32Array(5);
        FillInt32(nd2, dv, 5);
        il2cpp::il2cpp_array_sort_icomparer(nd2, nullptr);
        ExpectInt32Content(nd2, ndSorted, 5,
                           "null comparer sorts int32 ascending (net48 default)");

        // Reference-array comparer sort (string slots, user comparer).
        il2cpp::Il2CppArray* rs = NewRefArray(3);
        il2cpp::Il2CppObject* rv[3] = { S("pear"), S("apple"), S("cherry") };
        FillRefs(rs, rv, 3);
        il2cpp::il2cpp_array_sort_icomparer(rs, &g_descStrReceiver);
        il2cpp::Il2CppObject* rsorted[3] = { rv[0], rv[2], rv[1] };
        ExpectRefs(rs, rsorted, 3,
                   "user IComparer drives the reference comparer sort (descending)");

        // Array.Sort<T>(T[], Comparison<T>) -- int32 delegate (static leaf).
        il2cpp::Il2CppArray* i4d = NewInt32Array(5);
        const int32_t i4v[5] = { 3, 1, 4, 1, 5 };
        FillInt32(i4d, i4v, 5);
        il2cpp::il2cpp_array_sort_int32_comparison(
            i4d, reinterpret_cast<il2cpp::Il2CppObject*>(&g_staticIntCmp));
        ExpectInt32Content(i4d, dSorted, 5,
                           "int32 Comparison<int> delegate sorts descending");

        // int32 delegate with an instance-bound leaf (target in the this slot).
        il2cpp::Il2CppArray* i4t = NewInt32Array(3);
        const int32_t i4tv[3] = { 2, 1, 3 };
        FillInt32(i4t, i4tv, 3);
        il2cpp::il2cpp_array_sort_int32_comparison(
            i4t, reinterpret_cast<il2cpp::Il2CppObject*>(&g_thisIntCmp));
        const int32_t i4ts[3] = { 3, 2, 1 };
        ExpectInt32Content(i4t, i4ts, 3,
                           "int32 Comparison<int> delegate with an instance target sorts descending");

        // Reference-array delegate sort (string slots, descending ordinal).
        il2cpp::Il2CppArray* sd = NewRefArray(3);
        il2cpp::Il2CppObject* sdv[3] = { rv[0], rv[1], rv[2] };
        FillRefs(sd, sdv, 3);
        il2cpp::il2cpp_array_sort_ref_comparison(
            sd, reinterpret_cast<il2cpp::Il2CppObject*>(&g_staticStrCmp));
        ExpectRefs(sd, rsorted, 3,
                   "ref Comparison<T> delegate sorts strings descending");

        // Error paths: null delegate / null comparer (ArgumentNullException),
        // wrong element kind (NotSupportedException).
        ExpectThrows(kArgNullType,
                     "int32 delegate sort of a null comparison throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_array_sort_int32_comparison(i4d, nullptr); });
        ExpectThrows(kArgNullType,
                     "ref delegate sort of a null comparison throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_array_sort_ref_comparison(sd, nullptr); });
        // Null IComparer on a reference array: net48 means the DEFAULT
        // comparer (no throw) -- sorts the strings ascending, null first.
        il2cpp::Il2CppArray* nrd = NewRefArray(3);
        il2cpp::Il2CppObject* nrv[3] = { S("pear"), S("apple"), nullptr };
        FillRefs(nrd, nrv, 3);
        il2cpp::il2cpp_array_sort_icomparer(nrd, nullptr);
        il2cpp::Il2CppObject* nrs[3] = { nullptr, nrv[1], nrv[0] };
        ExpectRefs(nrd, nrs, 3,
                   "null IComparer on a reference array sorts with the default comparer");
    }

    // --- A3: DateTime[]/TimeSpan[] element-kind entry points ---
    // The runtime stores a DateTime cell as kind<<62|ticks; every comparison
    // and equality below must use the TICK count only (net48 CompareTo/Equals
    // ignore DateTimeKind).
    {
        auto* dt = il2cpp::il2cpp_array_new_typed(8, 6, 3);
        int64_t* cells = reinterpret_cast<int64_t*>(dt->data);
        const int64_t t2019 = 636820416000000000LL;
        const int64_t t2020 = 636856608000000000LL;
        // 2019 Unspecified, 2020 Local (kind bits 10 -> 0x8000...), 2020 Utc (01)
        cells[0] = t2019;
        cells[1] = static_cast<int64_t>(static_cast<uint64_t>(t2020) | (2ULL << 62));
        cells[2] = static_cast<int64_t>(static_cast<uint64_t>(t2020) | (1ULL << 62));
        il2cpp::il2cpp_array_sort_any(dt);
        cells = reinterpret_cast<int64_t*>(dt->data);
        Expect((cells[0] & il2cpp::runtime::kDateTimeArrayTickMask) == t2019 &&
                   (cells[1] & il2cpp::runtime::kDateTimeArrayTickMask) == t2020 &&
                   (cells[2] & il2cpp::runtime::kDateTimeArrayTickMask) == t2020,
               "sort_any orders DateTime[] by ticks and keeps the kind bits");

        ExpectI32(il2cpp::il2cpp_array_index_of_datetime(dt, il2cpp::runtime::Il2CppDateTime(t2019, 2)),
                  0, "IndexOf<DateTime> compares ticks (Kind ignored)");
        ExpectI32(il2cpp::il2cpp_array_index_of_datetime(dt, il2cpp::runtime::Il2CppDateTime(12345)),
                  -1, "IndexOf<DateTime> misses an absent tick");
        ExpectI32(il2cpp::il2cpp_array_binary_search_datetime(
                      dt, il2cpp::runtime::Il2CppDateTime(t2020, 0)),
                  1, "BinarySearch<DateTime> finds by ticks with a different Kind");
        ExpectI32(il2cpp::il2cpp_array_binary_search_datetime(
                      dt, il2cpp::runtime::Il2CppDateTime(t2020 + 1000)),
                  -4, "BinarySearch<DateTime> returns the tail complement");
        ExpectI32(il2cpp::il2cpp_array_index_of_datetime_range(
                      dt, il2cpp::runtime::Il2CppDateTime(t2019), 1, 2),
                  -1, "IndexOf<DateTime> range excludes the head element");
        ExpectI32(il2cpp::il2cpp_array_binary_search_datetime_range(
                      dt, 1, 2, il2cpp::runtime::Il2CppDateTime(t2020, 1)),
                  1, "BinarySearch<DateTime> range searches only [1,3)");

        auto* ts = il2cpp::il2cpp_array_new_typed(8, 7, 3);
        int64_t* tcells = reinterpret_cast<int64_t*>(ts->data);
        tcells[0] = -30000000;
        tcells[1] = 0;
        tcells[2] = 864000000000LL;
        il2cpp::il2cpp_array_sort_any(ts);
        tcells = reinterpret_cast<int64_t*>(ts->data);
        Expect(tcells[0] == -30000000 && tcells[1] == 0 && tcells[2] == 864000000000LL,
               "sort_any orders TimeSpan[] by raw ticks (negative first)");
        ExpectI32(il2cpp::il2cpp_array_index_of_timespan(ts, il2cpp::runtime::Il2CppTimeSpan(0)),
                  1, "IndexOf<TimeSpan> compares raw ticks");
        ExpectI32(il2cpp::il2cpp_array_binary_search_timespan(
                      ts, il2cpp::runtime::Il2CppTimeSpan(864000000000LL)),
                  2, "BinarySearch<TimeSpan> finds the last element");
        ExpectI32(il2cpp::il2cpp_array_index_of_timespan_range(
                      ts, il2cpp::runtime::Il2CppTimeSpan(-30000000), 1, 2),
                  -1, "IndexOf<TimeSpan> range excludes the head element");
        ExpectI32(il2cpp::il2cpp_array_binary_search_timespan_range(
                      ts, 0, 2, il2cpp::runtime::Il2CppTimeSpan(0)),
                  1, "BinarySearch<TimeSpan> range searches only [0,2)");

        // C2: the NON-generic (object needle) scans are LOUD -- no DateTime
        // box helper exists in this runtime, so a silent -1 is never returned.
        ExpectThrows(kNotSupportedType,
                     "IndexOf(Array, object) over a DateTime[] is LOUD",
                     [&] { il2cpp::il2cpp_array_index_of_any_start(dt, nullptr, 0); });
        ExpectThrows(kNotSupportedType,
                     "BinarySearch(Array, object) over a DateTime[] is LOUD",
                     [&] { il2cpp::il2cpp_array_binary_search_any(dt, nullptr); });
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d arraysort assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime arraysort tests passed\n");
    return 0;
}
