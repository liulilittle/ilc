//------------------------------------------------------------------------------
// test_linq_sort.cpp -- System.Linq OrderBy (linq_sort domain) tests.
//
// Coverage (normal + edge + error paths, design T3 card):
//   1. il2cpp_linq_order_by_i4 -- unsorted ascending, duplicates grouping,
//      INT32_MIN boundary, already-sorted, reverse-sorted, single element,
//      empty, source NOT modified (new-array semantics), result metadata.
//   2. STABILITY -- index-marked keyed pairs ("index-marking method"): every
//      element carries its original index; the comparator orders by the KEY
//      only; after the sort the equal-key elements must appear in ascending
//      original-index order (net48 OrderBy is stable). Also a string-level
//      stability check with two distinct equal-content strings.
//   3. Comparator direction -- a negated comparator sorts descending; a
//      +/-100 comparator sorts identically to a +/-1 one (only the sign of
//      the result matters, .NET Comparison<T> contract).
//   4. Callback invocation count -- the comparator is actually called
//      (count > 0) and stays within the merge-sort comparison bound
//      (<= n*ceil(log2 n) - 2^ceil(log2 n) + 1 = 17 for n = 8; an O(n^2)
//      sort would need up to 28).
//   5. il2cpp_linq_order_by_key_i4 -- the D-LINQ-1 KEY-SELECTOR surface
//      (Func<int,int>, NOT a 2-arg Comparison): unsorted -> ascending by the
//      identity key, STABLE within equal-key groups (parity key), empty /
//      single-element, source untouched, new-array semantics.
//   6. Key evaluation EXACTLY ONCE per element (net48 EnumerableSorter): a
//      counting selector sees exactly n calls for an n-element array.
//   7. Instance-bound key selector (linqib dispatch -- the bound payload is
//      read through the `this` slot; different payloads drive different
//      orders).
//   8. il2cpp_linq_order_by_ref -- keyed stability, source untouched.
//   9. il2cpp_linq_order_by_string -- ordinal comparator over string slots,
//      null elements ARE passed to the comparator (net48 contract; the
//      comparator here sorts nulls first), equal-content distinct strings
//      keep their source order.
//  10. Error paths -- null source / null comparator / null key selector
//      (ArgumentNullException), multicast container / instance-bound
//      comparator / non-delegate key selector
//      (InvalidOperationException / ArgumentException), element-family
//      mismatch (NotSupportedException).
//
// Plain executable + minimal assertion harness (see test_array.cpp).
//------------------------------------------------------------------------------

#include "linq_sort.h"
#include "il2cpp_array.h"
#include "il2cpp_delegate.h"
#include "il2cpp_delegate_ext.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"

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

// Element fixture class for plain reference arrays (one reference slot).
il2cpp::Il2CppClass g_mockElemClass = {
    "Test.LinqSortElem",  // name
    24,                   // instanceSize
    0b00000010u,          // refFieldBitmap: slot 1 is a GC reference
    1,                    // refFieldCount
    nullptr,              // baseClass
    nullptr,              // vtable
    0,                    // vtableSize
    nullptr,              // interfaces
    0,                    // interfaceCount
};

// Index-marked element (the stability oracle, "index-marking method"): a key
// the comparator orders by + the element's ORIGINAL index. A stable sort must
// emit equal-key elements in ascending index order.
struct KeyedItem
{
    il2cpp::Il2CppObject obj; // object header (klass pointer)
    int32_t key;              // sort key
    int32_t index;            // original position
};

il2cpp::Il2CppClass g_keyedClass = {
    "Test.LinqSortKeyed",                 // name
    static_cast<int32_t>(sizeof(KeyedItem)), // instanceSize
    0u,                                   // refFieldBitmap: no reference fields
    0,                                    // refFieldCount
    nullptr,                              // baseClass
    nullptr,                              // vtable
    0,                                    // vtableSize
    nullptr,                              // interfaces
    0,                                    // interfaceCount
};

// Managed exception type names (exact chain heads, as thrown by linq_sort.cpp).
const char kArgNullType[]      = "System.ArgumentNullException";
const char kArgumentException[] = "System.ArgumentException";
const char kInvalidOpType[]    = "System.InvalidOperationException";
const char kNotSupportedType[] = "System.NotSupportedException";

il2cpp::Il2CppArray* NewInt32Array(int32_t length)
{
    return il2cpp::il2cpp_array_new(nullptr, 4, false, length);
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

// Managed string literal -> Il2CppObject* slot.
il2cpp::Il2CppObject* S(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs(ascii));
}

// Comparator wrapper: a static-method single-cast delegate holding the fn
// pointer (the LINQ callback contract -- the runtime reads methodPtr). The
// function-pointer -> void* cast is the transpiler's ldftn pattern
// (CodeGenerator emits il2cpp_delegate_new((void*)(&<method>))).
template <typename Fn>
il2cpp::Il2CppObject* Del(Fn fn)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(fn)));
}

KeyedItem* NewKeyed(int32_t key, int32_t index)
{
    KeyedItem* item = reinterpret_cast<KeyedItem*>(il2cpp::il2cpp_gc_alloc(&g_keyedClass));
    item->key = key;
    item->index = index;
    return item;
}

//------------------------------------------------------------------------------
// Comparators (delegate-callable free functions).
//------------------------------------------------------------------------------

// Ascending int32, sign of the difference (the .NET Comparison<T> shape).
int32_t AscI4(int32_t left, int32_t right)
{
    return (left > right) - (left < right);
}

// Descending int32: the negated comparison.
int32_t DescI4(int32_t left, int32_t right)
{
    return AscI4(right, left);
}

// Ascending int32 returning +/-100 instead of +/-1: only the SIGN of the
// comparator result may matter (design T3 test point).
int32_t SignOnlyI4(int32_t left, int32_t right)
{
    return AscI4(left, right) * 100;
}

// Ascending int32 that also counts every invocation.
int32_t g_cmpCalls = 0;
int32_t CountingAscI4(int32_t left, int32_t right)
{
    ++g_cmpCalls;
    return AscI4(left, right);
}

// Ordinal string comparison over Il2CppObject* slots (null < any non-null,
// null == null); null elements ARE handed to the comparator by the runtime.
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

// Orders KeyedItems by KEY only -- the stability oracle: equal keys must
// come out in ascending original-index order.
int32_t KeyOnlyCompare(il2cpp::Il2CppObject* left, il2cpp::Il2CppObject* right)
{
    const KeyedItem* a = reinterpret_cast<const KeyedItem*>(left);
    const KeyedItem* b = reinterpret_cast<const KeyedItem*>(right);
    return (a->key > b->key) - (a->key < b->key);
}

//------------------------------------------------------------------------------
// Key selectors (delegate-callable free functions, Func<int,int> ABI).
//------------------------------------------------------------------------------

// Identity key: the element IS the key -> ascending element order.
int32_t KeyIdentity(int32_t x) { return x; }

// Parity key: groups evens (0) before odds (1); within a group the STABLE
// sort must preserve the elements' original index order.
int32_t KeyParity(int32_t x) { return x & 1; }

// Instance-bound key selector: reads the bound KeyedItem's payload 'key' as a
// multiplier -- proves the linqib dispatch hands the target in the `this` slot.
int32_t KeyedInstKey(il2cpp::Il2CppObject* self, int32_t x)
{
    const KeyedItem* k = reinterpret_cast<const KeyedItem*>(self);
    return x * k->key;
}

// Key selector that also counts every invocation (the exactly-once-per-element
// proof -- net48 EnumerableSorter caches keys, so n elements = n calls).
int32_t g_keyCalls = 0;
int32_t CountingKey(int32_t x)
{
    ++g_keyCalls;
    return x;
}

//------------------------------------------------------------------------------
// Two-key selectors (D-LO2-1, the fused ThenBy composite sort): the tens and
// units digits of a two-digit value as the PRIMARY / SECONDARY key.
//------------------------------------------------------------------------------

// Primary key: the tens digit.
int32_t KeyTens(int32_t x) { return x / 10; }

// Secondary key: the units digit.
int32_t KeyUnits(int32_t x) { return x % 10; }

// Secondary key that counts every invocation (the exactly-once contract for
// the THEN-BY selector too -- net48 EnumerableSorter caches both keys once).
int32_t g_key2Calls = 0;
int32_t CountingKey2(int32_t x)
{
    ++g_key2Calls;
    return x % 10;
}

} // namespace

int main()
{
    // --- 1. OrderBy int32: unsorted -> ascending, duplicates, source untouched ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(8);
        const int32_t unsorted[8] = { 3, -1, 7, 0, -5, 2, 3, 9 };
        FillInt32(a, unsorted, 8);
        il2cpp::Il2CppArray* r = il2cpp::il2cpp_linq_order_by_i4(a, Del(AscI4));
        const int32_t sorted[8] = { -5, -1, 0, 2, 3, 3, 7, 9 };
        ExpectInt32Content(r, sorted, 8, "order_by_i4 sorts an unsorted array ascending (duplicates grouped)");
        ExpectInt32Content(a, unsorted, 8, "order_by_i4 leaves the source array untouched");
        Expect(r != a, "order_by_i4 returns a NEW array (not the source)");
        ExpectI32(r->length, 8, "order_by_i4 result length matches the source");
        Expect(!r->isRefElem && r->elemSize == 4,
               "order_by_i4 result is an int32 array (isRefElem false, elemSize 4)");

        il2cpp::Il2CppArray* minmax = NewInt32Array(4);
        const int32_t extremes[4] = { -1, INT32_MIN, -3, -2 };
        FillInt32(minmax, extremes, 4);
        il2cpp::Il2CppArray* rm = il2cpp::il2cpp_linq_order_by_i4(minmax, Del(AscI4));
        const int32_t extremeSorted[4] = { INT32_MIN, -3, -2, -1 };
        ExpectInt32Content(rm, extremeSorted, 4, "order_by_i4 handles INT32_MIN");

        il2cpp::Il2CppArray* pre = NewInt32Array(5);
        const int32_t already[5] = { 1, 2, 3, 4, 5 };
        FillInt32(pre, already, 5);
        il2cpp::Il2CppArray* rp = il2cpp::il2cpp_linq_order_by_i4(pre, Del(AscI4));
        ExpectInt32Content(rp, already, 5, "order_by_i4 of an already-sorted array is identity");

        il2cpp::Il2CppArray* rev = NewInt32Array(5);
        const int32_t reverse[5] = { 5, 4, 3, 2, 1 };
        FillInt32(rev, reverse, 5);
        il2cpp::Il2CppArray* rr = il2cpp::il2cpp_linq_order_by_i4(rev, Del(AscI4));
        ExpectInt32Content(rr, already, 5, "order_by_i4 of a reverse-sorted array is ascending");

        il2cpp::Il2CppArray* dup = NewInt32Array(5);
        const int32_t dupKeys[5] = { 2, 1, 2, 1, 2 };
        FillInt32(dup, dupKeys, 5);
        il2cpp::Il2CppArray* rd = il2cpp::il2cpp_linq_order_by_i4(dup, Del(AscI4));
        const int32_t dupSorted[5] = { 1, 1, 2, 2, 2 };
        ExpectInt32Content(rd, dupSorted, 5, "order_by_i4 groups duplicate keys");

        il2cpp::Il2CppArray* single = NewInt32Array(1);
        const int32_t one[1] = { 9 };
        FillInt32(single, one, 1);
        il2cpp::Il2CppArray* rs = il2cpp::il2cpp_linq_order_by_i4(single, Del(AscI4));
        ExpectInt32Content(rs, one, 1, "order_by_i4 of a single-element array");
        Expect(rs != single, "order_by_i4 of a single-element array still returns a NEW array");

        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        il2cpp::Il2CppArray* re = il2cpp::il2cpp_linq_order_by_i4(empty, Del(AscI4));
        ExpectI32(re->length, 0, "order_by_i4 of an empty array returns an empty array");
        Expect(re != empty, "order_by_i4 of an empty array returns a NEW array");
    }

    // --- 2. STABILITY: index-marked keyed pairs (ref entry) ---
    {
        il2cpp::Il2CppArray* kArr = NewRefArray(5);
        il2cpp::Il2CppObject* items[5];
        items[0] = reinterpret_cast<il2cpp::Il2CppObject*>(NewKeyed(2, 0));
        items[1] = reinterpret_cast<il2cpp::Il2CppObject*>(NewKeyed(1, 1));
        items[2] = reinterpret_cast<il2cpp::Il2CppObject*>(NewKeyed(3, 2));
        items[3] = reinterpret_cast<il2cpp::Il2CppObject*>(NewKeyed(1, 3));
        items[4] = reinterpret_cast<il2cpp::Il2CppObject*>(NewKeyed(2, 4));
        FillRefs(kArr, items, 5);
        il2cpp::Il2CppArray* r = il2cpp::il2cpp_linq_order_by_ref(kArr, Del(KeyOnlyCompare));
        // Stable ascending by key: (1,1) (1,3) (2,0) (2,4) (3,2) -- the two
        // key-1 pairs AND the two key-2 pairs keep their original index order.
        il2cpp::Il2CppObject* expected[5] = { items[1], items[3], items[0], items[4], items[2] };
        ExpectRefs(r, expected, 5, "order_by_ref is STABLE: equal-key pairs keep their original index order");
        ExpectRefs(kArr, items, 5, "order_by_ref leaves the source array untouched");
        Expect(r->isRefElem && r->elemSize == 8,
               "order_by_ref result is a reference array (isRefElem true, elemSize 8)");
        Expect(r->elemClass == kArr->elemClass,
               "order_by_ref result mirrors the source element class");

        // Descending keys: stability must hold there as well.
        il2cpp::Il2CppArray* r2 = il2cpp::il2cpp_linq_order_by_ref(kArr, Del(KeyOnlyCompare));
        ExpectRefs(r2, expected, 5, "order_by_ref stability survives a second pass");
    }

    // --- 3. Comparator direction + sign-only semantics (i4) ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(8);
        const int32_t unsorted[8] = { 3, -1, 7, 0, -5, 2, 3, 9 };
        FillInt32(a, unsorted, 8);
        il2cpp::Il2CppArray* desc = il2cpp::il2cpp_linq_order_by_i4(a, Del(DescI4));
        const int32_t descending[8] = { 9, 7, 3, 3, 2, 0, -1, -5 };
        ExpectInt32Content(desc, descending, 8, "a negated comparator sorts descending");

        il2cpp::Il2CppArray* sign = il2cpp::il2cpp_linq_order_by_i4(a, Del(SignOnlyI4));
        const int32_t ascending[8] = { -5, -1, 0, 2, 3, 3, 7, 9 };
        ExpectInt32Content(sign, ascending, 8, "comparator results of +/-100 order identically to +/-1 (only the sign matters)");
    }

    // --- 4. Callback invocation count (i4) ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(8);
        const int32_t unsorted[8] = { 3, -1, 7, 0, -5, 2, 3, 9 };
        FillInt32(a, unsorted, 8);
        g_cmpCalls = 0;
        il2cpp::il2cpp_linq_order_by_i4(a, Del(CountingAscI4));
        Expect(g_cmpCalls > 0, "the comparator callback is invoked during the sort");
        // Merge sort of 8 elements performs at most 17 comparisons (an O(n^2)
        // sort would need up to 28): the count proves callback-driven merging.
        Expect(g_cmpCalls <= 17, "comparator invocation count stays within the merge-sort bound (<= 17 for n = 8)");
    }

    // --- 5. OrderBy key-selector (Func<int,int>): ascending, stable by key, source untouched ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(8);
        const int32_t unsorted[8] = { 3, -1, 7, 0, -5, 2, 3, 9 };
        FillInt32(a, unsorted, 8);
        il2cpp::Il2CppArray* r = il2cpp::il2cpp_linq_order_by_key_i4(a, Del(KeyIdentity));
        const int32_t sorted[8] = { -5, -1, 0, 2, 3, 3, 7, 9 };
        ExpectInt32Content(r, sorted, 8, "order_by_key_i4 sorts ascending by the identity key");
        ExpectInt32Content(a, unsorted, 8, "order_by_key_i4 leaves the source array untouched");
        Expect(r != a, "order_by_key_i4 returns a NEW array (not the source)");
        ExpectI32(r->length, 8, "order_by_key_i4 result length matches the source");
        Expect(!r->isRefElem && r->elemSize == 4,
               "order_by_key_i4 result is an int32 array (isRefElem false, elemSize 4)");

        // key = parity: { 4, 1, 3, 2, 8, 7 } -> keys { 0, 1, 1, 0, 0, 1 }. The
        // evens (0) sort before the odds (1); WITHIN each key group the stable
        // sort must preserve original index order: 4(idx0) 2(idx3) 8(idx4) | 1(idx1) 3(idx2) 7(idx5).
        il2cpp::Il2CppArray* p = NewInt32Array(6);
        const int32_t parInput[6] = { 4, 1, 3, 2, 8, 7 };
        FillInt32(p, parInput, 6);
        il2cpp::Il2CppArray* rp = il2cpp::il2cpp_linq_order_by_key_i4(p, Del(KeyParity));
        const int32_t parSorted[6] = { 4, 2, 8, 1, 3, 7 };
        ExpectInt32Content(rp, parSorted, 6, "order_by_key_i4 is STABLE within equal-key groups");

        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        il2cpp::Il2CppArray* re = il2cpp::il2cpp_linq_order_by_key_i4(empty, Del(KeyIdentity));
        ExpectI32(re->length, 0, "order_by_key_i4 of an empty array returns an empty array");
        Expect(re != empty, "order_by_key_i4 of an empty array returns a NEW array");

        il2cpp::Il2CppArray* single = NewInt32Array(1);
        const int32_t one[1] = { 9 };
        FillInt32(single, one, 1);
        il2cpp::Il2CppArray* rs = il2cpp::il2cpp_linq_order_by_key_i4(single, Del(KeyIdentity));
        ExpectInt32Content(rs, one, 1, "order_by_key_i4 of a single-element array");
        Expect(rs != single, "order_by_key_i4 of a single-element array still returns a NEW array");
    }

    // --- 6. Key evaluation EXACTLY ONCE per element (net48 EnumerableSorter) ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(8);
        const int32_t unsorted[8] = { 3, -1, 7, 0, -5, 2, 3, 9 };
        FillInt32(a, unsorted, 8);
        g_keyCalls = 0;
        il2cpp::il2cpp_linq_order_by_key_i4(a, Del(CountingKey));
        ExpectI32(g_keyCalls, 8, "the key selector is called EXACTLY ONCE per element (8 calls for n=8, never re-invoked)");
    }

    // --- 7. Instance-bound key selector (linqib dispatch: bound payload in `this`) ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(5);
        const int32_t vals[5] = { 3, 1, 4, 1, 5 };
        FillInt32(a, vals, 5);

        // Payload multiplier +1 -> identity keys -> ascending elements.
        il2cpp::Il2CppObject* ascSelf = reinterpret_cast<il2cpp::Il2CppObject*>(NewKeyed(1, 0));
        il2cpp::Il2CppObject* ascDel = Del(KeyedInstKey);
        reinterpret_cast<il2cpp::Il2CppDelegate*>(ascDel)->target = ascSelf;
        il2cpp::Il2CppArray* ra = il2cpp::il2cpp_linq_order_by_key_i4(a, ascDel);
        const int32_t ascending[5] = { 1, 1, 3, 4, 5 };
        ExpectInt32Content(ra, ascending, 5, "an instance-bound key selector reads the bound payload (ascending)");

        // Payload multiplier -1 -> negated keys -> descending elements.
        il2cpp::Il2CppObject* descSelf = reinterpret_cast<il2cpp::Il2CppObject*>(NewKeyed(-1, 0));
        il2cpp::Il2CppObject* descDel = Del(KeyedInstKey);
        reinterpret_cast<il2cpp::Il2CppDelegate*>(descDel)->target = descSelf;
        il2cpp::Il2CppArray* rd = il2cpp::il2cpp_linq_order_by_key_i4(a, descDel);
        const int32_t descending[5] = { 5, 4, 3, 1, 1 };
        ExpectInt32Content(rd, descending, 5, "an instance-bound key selector reads the bound payload (descending)");
    }

    // --- 8. OrderBy string: ordinal comparator, nulls passed to the callback ---
    {
        il2cpp::Il2CppObject* pear = S("pear");
        il2cpp::Il2CppObject* apple = S("apple");
        il2cpp::Il2CppObject* banana = S("banana");
        il2cpp::Il2CppObject* apple2 = S("apple");
        il2cpp::Il2CppArray* a = NewRefArray(4);
        il2cpp::Il2CppObject* withDup[4] = { apple2, pear, apple, banana };
        FillRefs(a, withDup, 4);
        il2cpp::Il2CppArray* r = il2cpp::il2cpp_linq_order_by_string(a, Del(OrdinalCompare));
        // apple2 and apple compare EQUAL (same content, distinct objects):
        // stability keeps apple2 (index 0) before apple (index 2).
        il2cpp::Il2CppObject* stable[4] = { apple2, apple, banana, pear };
        ExpectRefs(r, stable, 4, "order_by_string sorts ordinal and is STABLE for equal-content strings");

        il2cpp::Il2CppArray* n = NewRefArray(4);
        il2cpp::Il2CppObject* withNull[4] = { pear, nullptr, apple, banana };
        FillRefs(n, withNull, 4);
        il2cpp::Il2CppArray* rn = il2cpp::il2cpp_linq_order_by_string(n, Del(OrdinalCompare));
        il2cpp::Il2CppObject* nullFirst[4] = { nullptr, apple, banana, pear };
        ExpectRefs(rn, nullFirst, 4, "order_by_string passes null elements to the comparator (nulls first here)");
        ExpectRefs(n, withNull, 4, "order_by_string leaves the source array untouched");

        il2cpp::Il2CppArray* empty = NewRefArray(0);
        il2cpp::Il2CppArray* re = il2cpp::il2cpp_linq_order_by_string(empty, Del(OrdinalCompare));
        ExpectI32(re->length, 0, "order_by_string of an empty array returns an empty array");
    }

    // --- 9. ThenBy / ThenByDescending (D-LO2-1): the FUSED composite sort ---
    {
        // { 53, 54, 51, 12, 11 }: primary = tens (5,5,5,1,1), secondary =
        // units (3,4,1,2,1). ThenBy orders (tens asc, units asc):
        // [11, 12, 51, 53, 54] -- the net48 OrderBy(tens).ThenBy(units)
        // probe. ThenByDescending flips only the secondary key:
        // [12, 11, 54, 53, 51].
        il2cpp::Il2CppArray* a = NewInt32Array(5);
        const int32_t vals[5] = { 53, 54, 51, 12, 11 };
        FillInt32(a, vals, 5);

        il2cpp::Il2CppArray* r = il2cpp::il2cpp_linq_then_by_key_i4(a, Del(KeyTens), Del(KeyUnits));
        const int32_t composite[5] = { 11, 12, 51, 53, 54 };
        ExpectInt32Content(r, composite, 5, "then_by_key_i4 fuses OrderBy(tens).ThenBy(units): (k1 asc, k2 asc)");
        ExpectInt32Content(a, vals, 5, "then_by_key_i4 leaves the source array untouched");
        Expect(r != a, "then_by_key_i4 returns a NEW array (not the source)");
        ExpectI32(r->length, 5, "then_by_key_i4 result length matches the source");

        il2cpp::Il2CppArray* rd = il2cpp::il2cpp_linq_then_by_key_desc_i4(a, Del(KeyTens), Del(KeyUnits));
        const int32_t compositeDesc[5] = { 12, 11, 54, 53, 51 };
        ExpectInt32Content(rd, compositeDesc, 5, "then_by_key_desc_i4 flips ONLY the secondary key: (k1 asc, k2 desc)");

        // Within-primary-group secondary ordering (the "equal primary keys ->
        // secondary key order" probe) on a larger interleaved input: the
        // naive whole-array secondary re-sort would interleave groups; the
        // fused composite keeps the lexicographic (k1, k2) order.
        il2cpp::Il2CppArray* b = NewInt32Array(9);
        const int32_t inter[9] = { 53, 14, 78, 32, 61, 97, 45, 89, 26 };
        FillInt32(b, inter, 9);
        il2cpp::Il2CppArray* ri = il2cpp::il2cpp_linq_then_by_key_i4(b, Del(KeyTens), Del(KeyUnits));
        const int32_t interSorted[9] = { 14, 26, 32, 45, 53, 61, 78, 89, 97 };
        ExpectInt32Content(ri, interSorted, 9, "then_by_key_i4 keeps the lexicographic (k1, k2) order on interleaved input");

        // Empty and single-element arrays are validated no-ops.
        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        il2cpp::Il2CppArray* re = il2cpp::il2cpp_linq_then_by_key_i4(empty, Del(KeyTens), Del(KeyUnits));
        ExpectI32(re->length, 0, "then_by_key_i4 of an empty array returns an empty array");
        il2cpp::Il2CppArray* single = NewInt32Array(1);
        const int32_t one[1] = { 42 };
        FillInt32(single, one, 1);
        il2cpp::Il2CppArray* rs = il2cpp::il2cpp_linq_then_by_key_desc_i4(single, Del(KeyTens), Del(KeyUnits));
        ExpectInt32Content(rs, one, 1, "then_by_key_desc_i4 of a single-element array");

        // Each selector is invoked EXACTLY ONCE per element (net48
        // EnumerableSorter caches BOTH keys).
        g_keyCalls = 0;
        g_key2Calls = 0;
        il2cpp::il2cpp_linq_then_by_key_i4(b, Del(CountingKey), Del(CountingKey2));
        ExpectI32(g_keyCalls, 9, "the PRIMARY key selector is called EXACTLY ONCE per element (9 calls for n=9)");
        ExpectI32(g_key2Calls, 9, "the SECONDARY key selector is called EXACTLY ONCE per element (9 calls for n=9)");

        // Error paths: null selectors, wrong element family.
        ExpectThrows(kArgNullType, "then_by_key_i4 with a null primary selector throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_linq_then_by_key_i4(b, nullptr, Del(KeyUnits)); });
        ExpectThrows(kArgNullType, "then_by_key_i4 with a null secondary selector throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_linq_then_by_key_i4(b, Del(KeyTens), nullptr); });
        ExpectThrows(kArgNullType, "then_by_key_desc_i4 with a null primary selector throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_linq_then_by_key_desc_i4(b, nullptr, Del(KeyUnits)); });
        il2cpp::Il2CppArray* ref = NewRefArray(2);
        ExpectThrows(kNotSupportedType, "then_by_key_i4 of a reference array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_linq_then_by_key_i4(ref, Del(KeyTens), Del(KeyUnits)); });
        ExpectThrows(kNotSupportedType, "then_by_key_desc_i4 of a reference array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_linq_then_by_key_desc_i4(ref, Del(KeyTens), Del(KeyUnits)); });
        il2cpp::Il2CppObject* keyMulti = il2cpp::runtime::icall_delegate_combine(
            Del(KeyTens), Del(KeyTens));
        ExpectThrows(kInvalidOpType, "a multicast primary selector throws InvalidOperationException",
                     [&] { il2cpp::il2cpp_linq_then_by_key_i4(b, keyMulti, Del(KeyUnits)); });
        ExpectThrows(kInvalidOpType, "a multicast secondary selector throws InvalidOperationException",
                     [&] { il2cpp::il2cpp_linq_then_by_key_desc_i4(b, Del(KeyTens), keyMulti); });
    }

    // --- 10. Error paths ---
    {
        il2cpp::Il2CppArray* i4 = NewInt32Array(2);
        const int32_t two[2] = { 2, 1 };
        FillInt32(i4, two, 2);
        il2cpp::Il2CppArray* ref = NewRefArray(2);

        ExpectThrows(kArgNullType, "order_by_i4 with a null source throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_linq_order_by_i4(nullptr, Del(AscI4)); });
        ExpectThrows(kArgNullType, "order_by_ref with a null source throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_linq_order_by_ref(nullptr, Del(KeyOnlyCompare)); });
        ExpectThrows(kArgNullType, "order_by_string with a null source throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_linq_order_by_string(nullptr, Del(OrdinalCompare)); });
        ExpectThrows(kArgNullType, "order_by_key_i4 with a null source throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_linq_order_by_key_i4(nullptr, Del(KeyIdentity)); });
        ExpectThrows(kArgNullType, "order_by_i4 with a null comparator throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_linq_order_by_i4(i4, nullptr); });
        ExpectThrows(kArgNullType, "order_by_ref with a null comparator throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_linq_order_by_ref(ref, nullptr); });
        ExpectThrows(kArgNullType, "order_by_key_i4 with a null key selector throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_linq_order_by_key_i4(i4, nullptr); });

        // A multicast composition has no single methodPtr: loud rejection.
        il2cpp::Il2CppObject* multi = il2cpp::runtime::icall_delegate_combine(
            Del(AscI4), Del(AscI4));
        ExpectThrows(kInvalidOpType, "a multicast comparator throws InvalidOperationException",
                     [&] { il2cpp::il2cpp_linq_order_by_i4(i4, multi); });
        il2cpp::Il2CppObject* keyMulti = il2cpp::runtime::icall_delegate_combine(
            Del(KeyIdentity), Del(KeyIdentity));
        ExpectThrows(kInvalidOpType, "a multicast key selector throws InvalidOperationException",
                     [&] { il2cpp::il2cpp_linq_order_by_key_i4(i4, keyMulti); });

        // A non-delegate object is a caller bug: ArgumentException.
        il2cpp::Il2CppObject* notDel = reinterpret_cast<il2cpp::Il2CppObject*>(NewKeyed(1, 0));
        ExpectThrows(kArgumentException, "a non-delegate key selector throws ArgumentException",
                     [&] { il2cpp::il2cpp_linq_order_by_key_i4(i4, notDel); });

        // An instance-bound delegate carries no target binding in this stage.
        il2cpp::Il2CppObject* bound = Del(AscI4);
        reinterpret_cast<il2cpp::Il2CppDelegate*>(bound)->target =
            reinterpret_cast<il2cpp::Il2CppObject*>(NewKeyed(1, 0));
        ExpectThrows(kInvalidOpType, "an instance-bound comparator throws InvalidOperationException",
                     [&] { il2cpp::il2cpp_linq_order_by_i4(i4, bound); });

        // Element-family mismatches: the entry must reject the wrong kind.
        ExpectThrows(kNotSupportedType, "order_by_i4 of a reference array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_linq_order_by_i4(ref, Del(AscI4)); });
        ExpectThrows(kNotSupportedType, "order_by_ref of an int32 array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_linq_order_by_ref(i4, Del(KeyOnlyCompare)); });
        ExpectThrows(kNotSupportedType, "order_by_string of an int32 array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_linq_order_by_string(i4, Del(OrdinalCompare)); });
        ExpectThrows(kNotSupportedType, "order_by_key_i4 of a reference array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_linq_order_by_key_i4(ref, Del(KeyIdentity)); });
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d linq_sort assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime linq_sort tests passed\n");
    return 0;
}
