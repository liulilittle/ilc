//------------------------------------------------------------------------------
// test_arrayext.cpp -- System.Array method implementations (icall layer) tests.
//
// Coverage (normal + edge + error paths):
//   1. GetLength / GetUpperBound (instance methods, one-dimensional):
//      dimension 0, empty arrays, out-of-range dimensions, null receiver.
//   2. icall_array_copy -- int32 arrays: basic, partial, full, zero-length,
//      same-array overlap (both directions, memmove semantics), boundary
//      (srcIndex == length with length 0).
//   3. icall_array_copy -- error paths: null src/dst (ArgumentNullException),
//      negative index/length (ArgumentOutOfRangeException), range overflow
//      (ArgumentException), int32-vs-reference kind mismatch
//      (ArrayTypeMismatchException).
//   4. icall_array_copy -- reference arrays + GC interaction: reference
//      (identity) copy, null element copy, write-barrier card marking into
//      an OLD-generation destination, element survival across promotion, and
//      a partial reference copy.
//   5. icall_array_sort -- ascending order, already-sorted input, duplicates,
//      negatives (incl. INT32_MIN), empty, single, null, reference arrays.
//   6. icall_array_reverse -- odd/even lengths, empty, single, null,
//      reference arrays.
//   7. icall_array_index_of -- first-match semantics, absent -> -1, null
//      value -> -1, non-int32-boxed value -> -1, empty array, null array,
//      reference arrays.
//
// Plain executable + minimal assertion harness (see test_array.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_array_ext.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"
#include "il2cpp_value_semantics.h" // the per-kind value-type hash icalls

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "arrayext test fixture assumes 8-byte pointers");

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
// equals 'expectedType' (the thrown chain is "Type,Base1,Base2,..."; only the
// head is asserted so a base-chain extension does not break the test). Any
// other outcome is a recorded failure.
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
    "Test.ArrayExtElem", // name
    24,                  // instanceSize
    0b00000010u,         // refFieldBitmap: slot 1 is a GC reference
    1,                   // refFieldCount
    nullptr,             // baseClass
    nullptr,             // vtable
    0,                   // vtableSize
    nullptr,             // interfaces
    0,                   // interfaceCount
};

// Managed exception type names (exact chain head, as thrown by arrayext.cpp).
const char kArgNullType[]       = "System.ArgumentNullException";
const char kArgOutOfRangeType[] = "System.ArgumentOutOfRangeException";
const char kArgumentType[]      = "System.ArgumentException";
const char kTypeMismatchType[]  = "System.ArrayTypeMismatchException";
const char kNotSupportedType[]  = "System.NotSupportedException";
const char kIndexOutOfRangeType[] = "System.IndexOutOfRangeException";
const char kNullRefType[]       = "System.NullReferenceException";

il2cpp::Il2CppObject* AsObject(il2cpp::Il2CppArray* arr)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(arr);
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

} // namespace

int main()
{
    // --- 1. GetLength / GetUpperBound (instance methods) ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(5);
        ExpectI32(il2cpp::runtime::icall_array_get_length(AsObject(a), 0), 5,
                  "GetLength(0) equals Length");
        ExpectI32(il2cpp::runtime::icall_array_get_upper_bound(AsObject(a), 0), 4,
                  "GetUpperBound(0) equals Length - 1");

        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        ExpectI32(il2cpp::runtime::icall_array_get_length(AsObject(empty), 0), 0,
                  "GetLength(0) of an empty array is 0");
        ExpectI32(il2cpp::runtime::icall_array_get_upper_bound(AsObject(empty), 0), -1,
                  "GetUpperBound(0) of an empty array is -1");

        il2cpp::Il2CppArray* one = NewInt32Array(1);
        ExpectI32(il2cpp::runtime::icall_array_get_upper_bound(AsObject(one), 0), 0,
                  "GetUpperBound(0) of a single-element array is 0");

        ExpectThrows(kIndexOutOfRangeType, "GetLength(1) throws IndexOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_get_length(AsObject(a), 1); });
        ExpectThrows(kIndexOutOfRangeType, "GetLength(-1) throws IndexOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_get_length(AsObject(a), -1); });
        ExpectThrows(kIndexOutOfRangeType, "GetUpperBound(1) throws IndexOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_get_upper_bound(AsObject(a), 1); });
        ExpectThrows(kNullRefType, "GetLength on null throws NullReferenceException",
                     [] { il2cpp::runtime::icall_array_get_length(nullptr, 0); });
        ExpectThrows(kNullRefType, "GetUpperBound on null throws NullReferenceException",
                     [] { il2cpp::runtime::icall_array_get_upper_bound(nullptr, 0); });
    }

    // --- 2. Copy -- int32 arrays ---
    {
        il2cpp::Il2CppArray* src = NewInt32Array(5);
        const int32_t srcVals[5] = { 1, 2, 3, 4, 5 };
        FillInt32(src, srcVals, 5);

        // Zero-length copy leaves the destination untouched.
        il2cpp::Il2CppArray* z = NewInt32Array(5);
        il2cpp::runtime::icall_array_copy(AsObject(src), 0, AsObject(z), 0, 0);
        const int32_t zeros[5] = { 0, 0, 0, 0, 0 };
        ExpectInt32Content(z, zeros, 5, "zero-length copy leaves the destination untouched");

        // Partial copy: src[1..3] -> dst[0..2].
        il2cpp::Il2CppArray* dst = NewInt32Array(5);
        il2cpp::runtime::icall_array_copy(AsObject(src), 1, AsObject(dst), 0, 3);
        const int32_t partial[5] = { 2, 3, 4, 0, 0 };
        ExpectInt32Content(dst, partial, 5, "partial copy copies the requested range only");

        // Full copy.
        il2cpp::runtime::icall_array_copy(AsObject(src), 0, AsObject(dst), 0, 5);
        ExpectInt32Content(dst, srcVals, 5, "full copy replicates the source");

        // Overlap, forward direction (memmove semantics: no clobbering).
        il2cpp::Il2CppArray* ov = NewInt32Array(5);
        FillInt32(ov, srcVals, 5);
        il2cpp::runtime::icall_array_copy(AsObject(ov), 0, AsObject(ov), 1, 4);
        const int32_t shifted[5] = { 1, 1, 2, 3, 4 };
        ExpectInt32Content(ov, shifted, 5, "overlapping copy (forward) behaves like memmove");

        // Overlap, backward direction.
        il2cpp::Il2CppArray* ov2 = NewInt32Array(5);
        FillInt32(ov2, srcVals, 5);
        il2cpp::runtime::icall_array_copy(AsObject(ov2), 1, AsObject(ov2), 0, 4);
        const int32_t shifted2[5] = { 2, 3, 4, 5, 5 };
        ExpectInt32Content(ov2, shifted2, 5, "overlapping copy (backward) behaves like memmove");

        // Identity copy (same array, same range) is a no-op.
        il2cpp::runtime::icall_array_copy(AsObject(ov2), 0, AsObject(ov2), 0, 5);
        ExpectInt32Content(ov2, shifted2, 5, "identity copy leaves the array unchanged");

        // Boundary: srcIndex == length is legal when length == 0.
        il2cpp::runtime::icall_array_copy(AsObject(ov2), 5, AsObject(ov2), 5, 0);
        ExpectInt32Content(ov2, shifted2, 5, "copy at the one-past-end index with length 0 is a no-op");
    }

    // --- 3. Copy -- error paths ---
    {
        il2cpp::Il2CppArray* src = NewInt32Array(3);
        il2cpp::Il2CppArray* dst = NewInt32Array(3);
        il2cpp::Il2CppArray* refArr = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, 3);

        ExpectThrows(kArgNullType, "copy with null src throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_copy(nullptr, 0, AsObject(dst), 0, 1); });
        ExpectThrows(kArgNullType, "copy with null dst throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_copy(AsObject(src), 0, nullptr, 0, 1); });
        ExpectThrows(kArgOutOfRangeType, "negative srcIndex throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_copy(AsObject(src), -1, AsObject(dst), 0, 1); });
        ExpectThrows(kArgOutOfRangeType, "negative dstIndex throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_copy(AsObject(src), 0, AsObject(dst), -1, 1); });
        ExpectThrows(kArgOutOfRangeType, "negative length throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::icall_array_copy(AsObject(src), 0, AsObject(dst), 0, -1); });
        ExpectThrows(kArgumentType, "srcIndex + length beyond the source throws ArgumentException",
                     [&] { il2cpp::runtime::icall_array_copy(AsObject(src), 2, AsObject(dst), 0, 2); });
        ExpectThrows(kArgumentType, "dstIndex + length beyond the destination throws ArgumentException",
                     [&] { il2cpp::runtime::icall_array_copy(AsObject(src), 0, AsObject(dst), 2, 2); });
        ExpectThrows(kArgumentType, "srcIndex beyond the source throws ArgumentException",
                     [&] { il2cpp::runtime::icall_array_copy(AsObject(src), 4, AsObject(dst), 0, 0); });
        ExpectThrows(kTypeMismatchType, "int32 array into reference array throws ArrayTypeMismatchException",
                     [&] { il2cpp::runtime::icall_array_copy(AsObject(src), 0, AsObject(refArr), 0, 1); });
        ExpectThrows(kTypeMismatchType, "reference array into int32 array throws ArrayTypeMismatchException",
                     [&] { il2cpp::runtime::icall_array_copy(AsObject(refArr), 0, AsObject(dst), 0, 1); });
    }

    // --- 4. Copy -- reference arrays + GC interaction ---
    // NOTE (cross-domain blocker, see report): array.cpp's ArrayAllocSize
    // uses sizeof(Il2CppArray) (32) as the header base while the payload
    // lives at byte offset 25 (offsetof(Il2CppArray, data)), so reference
    // arrays are allocated ONE BYTE SHORT: the last element's last byte
    // overlaps the NEXT block's klass header. A collection scanning such an
    // array then reads a garbage reference (the klass pointer's LSB, e.g.
    // 0x40) and crashes. This test therefore (a) stores every element
    // explicitly (which zeroes the overhang byte when the array is the last
    // allocation), (b) roots the element objects, and (c) runs exactly ONE
    // collection -- a second one would re-expose the under-allocation after
    // compaction (the copied block's overhang byte lands on the next copied
    // block's klass byte). Once array.cpp is fixed, the extra elements of
    // this section can be re-enabled.
    {
        // Destination array + elements are allocated FIRST so that the
        // source array (allocated after the collection) can be the last
        // allocation: its overhang byte sits past the segment bump and is
        // deterministically zeroed by the explicit element stores below.
        il2cpp::Il2CppArray* raDst = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, 3);
        il2cpp::Il2CppObject* e0 = il2cpp::il2cpp_gc_alloc(&g_mockElemClass);
        il2cpp::Il2CppObject* e1 = il2cpp::il2cpp_gc_alloc(&g_mockElemClass);
        il2cpp::Il2CppObject** rdst = reinterpret_cast<il2cpp::Il2CppObject**>(raDst->data);
        rdst[0] = nullptr;
        rdst[1] = nullptr;
        rdst[2] = nullptr;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&raDst));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&e0));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&e1));

        // Promote raDst/e0/e1 to generation 1 (cards are cleared at the
        // start of each collection, so the card table is clean now). Gen0
        // compaction MOVES the objects -- re-fetch the data pointer after.
        il2cpp::il2cpp_gc_collect();
        rdst = reinterpret_cast<il2cpp::Il2CppObject**>(raDst->data);
        Expect(!il2cpp::il2cpp_gc_is_card_marked(&rdst[0]),
               "card table is clean before the reference copy (post-collection)");

        // Source array (gen0) is the LAST allocation; every element is
        // stored explicitly so its overhang byte is zero.
        il2cpp::Il2CppArray* raSrc = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, 3);
        il2cpp::Il2CppObject** rsrc = reinterpret_cast<il2cpp::Il2CppObject**>(raSrc->data);
        rsrc[0] = e0;
        rsrc[1] = e1;
        rsrc[2] = nullptr;

        // The copy into the OLD-generation destination must mark the card
        // (write-barrier contract: a young-only collection would need to
        // rescan this region for young references).
        il2cpp::runtime::icall_array_copy(AsObject(raSrc), 0, AsObject(raDst), 0, 3);
        Expect(il2cpp::il2cpp_gc_is_card_marked(&rdst[0]),
               "reference copy into an old-generation destination marks the card");

        // References are copied by identity (same objects, not a value copy).
        Expect(rdst[0] == rsrc[0] && rdst[0] != nullptr && rdst[0]->klass == &g_mockElemClass,
               "reference copy preserves element identity (element 0)");
        Expect(rdst[1] == rsrc[1] && rdst[1] != nullptr && rdst[1]->klass == &g_mockElemClass,
               "reference copy preserves element identity (element 1)");
        Expect(rdst[2] == nullptr, "null elements copy as null");

        // Partial reference copy: src[1..2] -> dst[0..1].
        il2cpp::runtime::icall_array_copy(AsObject(raSrc), 1, AsObject(raDst), 0, 2);
        Expect(rdst[0] == rsrc[1] && rdst[1] == nullptr,
               "partial reference copy maps the requested range");
    }

    // --- 5. Sort ---
    {
        il2cpp::Il2CppArray* s = NewInt32Array(5);
        const int32_t unsorted[5] = { 5, 3, 4, 1, 2 };
        FillInt32(s, unsorted, 5);
        il2cpp::runtime::icall_array_sort(AsObject(s));
        const int32_t sorted[5] = { 1, 2, 3, 4, 5 };
        ExpectInt32Content(s, sorted, 5, "sort orders an unsorted array ascending");

        il2cpp::Il2CppArray* pre = NewInt32Array(3);
        const int32_t preVals[3] = { 1, 2, 3 };
        FillInt32(pre, preVals, 3);
        il2cpp::runtime::icall_array_sort(AsObject(pre));
        ExpectInt32Content(pre, preVals, 3, "sort is stable-safe on already-sorted input");

        il2cpp::Il2CppArray* dup = NewInt32Array(4);
        const int32_t dupVals[4] = { 3, 1, 3, 1 };
        FillInt32(dup, dupVals, 4);
        il2cpp::runtime::icall_array_sort(AsObject(dup));
        const int32_t dupSorted[4] = { 1, 1, 3, 3 };
        ExpectInt32Content(dup, dupSorted, 4, "sort handles duplicates");

        il2cpp::Il2CppArray* neg = NewInt32Array(5);
        const int32_t negVals[5] = { -1, 5, -3, 0, INT32_MIN };
        FillInt32(neg, negVals, 5);
        il2cpp::runtime::icall_array_sort(AsObject(neg));
        const int32_t negSorted[5] = { INT32_MIN, -3, -1, 0, 5 };
        ExpectInt32Content(neg, negSorted, 5, "sort handles negatives and INT32_MIN");

        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        il2cpp::runtime::icall_array_sort(AsObject(empty));
        ExpectI32(il2cpp::il2cpp_array_get_length(empty), 0, "sort of an empty array is a no-op");

        il2cpp::Il2CppArray* single = NewInt32Array(1);
        const int32_t singleVal[1] = { 7 };
        FillInt32(single, singleVal, 1);
        il2cpp::runtime::icall_array_sort(AsObject(single));
        ExpectInt32Content(single, singleVal, 1, "sort of a single-element array is a no-op");

        ExpectThrows(kArgNullType, "sort of null throws ArgumentNullException",
                     [] { il2cpp::runtime::icall_array_sort(nullptr); });
        il2cpp::Il2CppArray* refArr = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, 2);
        ExpectThrows(kNotSupportedType, "sort of a reference array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_sort(AsObject(refArr)); });
    }

    // --- 6. Reverse ---
    {
        il2cpp::Il2CppArray* r = NewInt32Array(5);
        const int32_t five[5] = { 1, 2, 3, 4, 5 };
        FillInt32(r, five, 5);
        il2cpp::runtime::icall_array_reverse(AsObject(r));
        const int32_t revFive[5] = { 5, 4, 3, 2, 1 };
        ExpectInt32Content(r, revFive, 5, "reverse flips an odd-length array");

        il2cpp::Il2CppArray* e = NewInt32Array(4);
        const int32_t four[4] = { 1, 2, 3, 4 };
        FillInt32(e, four, 4);
        il2cpp::runtime::icall_array_reverse(AsObject(e));
        const int32_t revFour[4] = { 4, 3, 2, 1 };
        ExpectInt32Content(e, revFour, 4, "reverse flips an even-length array");

        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        il2cpp::runtime::icall_array_reverse(AsObject(empty));
        ExpectI32(il2cpp::il2cpp_array_get_length(empty), 0, "reverse of an empty array is a no-op");

        il2cpp::Il2CppArray* single = NewInt32Array(1);
        const int32_t one[1] = { 9 };
        FillInt32(single, one, 1);
        il2cpp::runtime::icall_array_reverse(AsObject(single));
        ExpectInt32Content(single, one, 1, "reverse of a single-element array is a no-op");

        ExpectThrows(kArgNullType, "reverse of null throws ArgumentNullException",
                     [] { il2cpp::runtime::icall_array_reverse(nullptr); });
        il2cpp::Il2CppArray* refArr = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, 2);
        ExpectThrows(kNotSupportedType, "reverse of a reference array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_reverse(AsObject(refArr)); });
    }

    // --- 7. IndexOf ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(5);
        const int32_t vals[5] = { 10, 20, 30, 20, 40 };
        FillInt32(a, vals, 5);

        il2cpp::Il2CppObject* boxed20 = il2cpp::il2cpp_box_int32(nullptr, 20);
        ExpectI32(il2cpp::runtime::icall_array_index_of(AsObject(a), boxed20), 1,
                  "index of a present boxed value is the first occurrence");
        il2cpp::Il2CppObject* boxed40 = il2cpp::il2cpp_box_int32(nullptr, 40);
        ExpectI32(il2cpp::runtime::icall_array_index_of(AsObject(a), boxed40), 4,
                  "index of the last element is found");
        il2cpp::Il2CppObject* boxed99 = il2cpp::il2cpp_box_int32(nullptr, 99);
        ExpectI32(il2cpp::runtime::icall_array_index_of(AsObject(a), boxed99), -1,
                  "an absent value returns -1");

        ExpectI32(il2cpp::runtime::icall_array_index_of(AsObject(a), nullptr), -1,
                  "null value never matches an int32 element");
        il2cpp::Il2CppObject* nonInt = il2cpp::il2cpp_gc_alloc(&g_mockElemClass);
        ExpectI32(il2cpp::runtime::icall_array_index_of(AsObject(a), nonInt), -1,
                  "a non-int32-boxed object never matches");

        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        ExpectI32(il2cpp::runtime::icall_array_index_of(AsObject(empty), boxed20), -1,
                  "index of in an empty array is -1");

        ExpectThrows(kArgNullType, "index of with null array throws ArgumentNullException",
                     [] { il2cpp::runtime::icall_array_index_of(nullptr, nullptr); });
        il2cpp::Il2CppArray* refArr = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, 2);
        ExpectThrows(kNotSupportedType, "index of a reference array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_index_of(AsObject(refArr), boxed20); });
    }

    // --- 8. get_Rank (Array.get_Rank, audit gap #4) ---
    // net48: a one-dimensional array returns 1, a multi-dimensional array
    // returns its rank. The allocator stamps 1 on every array (the
    // one-dimensional scope); the rank FIELD carries the multi-dimensional
    // value once creation lands -- setting it directly here exercises that
    // path.
    {
        il2cpp::Il2CppArray* a = NewInt32Array(5);
        ExpectI32(il2cpp::runtime::icall_array_get_rank(AsObject(a)), 1,
                  "one-dimensional array rank is 1");

        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        ExpectI32(il2cpp::runtime::icall_array_get_rank(AsObject(empty)), 1,
                  "empty one-dimensional array rank is 1");

        a->rank = 3;
        ExpectI32(il2cpp::runtime::icall_array_get_rank(AsObject(a)), 3,
                  "the rank field carries a multi-dimensional rank");
        a->rank = 1;

        ExpectThrows(kNullRefType, "get_Rank on null throws NullReferenceException",
                     [] { il2cpp::runtime::icall_array_get_rank(nullptr); });
    }

    // --- 9. ValueType.GetHashCode default (audit gap #5) ---
    // The transpiler selects a per-kind runtime target from the value type's
    // first-instance-field kind (MethodMap.ValueType.cs). The REFERENCE arm
    // (icall_value_type_get_hash_code_ref) hashes the object at data[0]
    // through the Object.GetHashCode dispatch (string -> ordinal content,
    // boxed Int32 -> the value) with null -> 0; the no-fields arm
    // (icall_value_type_get_hash_code with injected 0/0 -> fieldKind 0)
    // returns typeHash 0; the value arms reuse the per-type hash icalls on
    // the deref'd slot.
    {
        // Reference arm -- string first field: the object hash dispatch.
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new_from_utf8("hello");
        il2cpp::Il2CppObject* obj = reinterpret_cast<il2cpp::Il2CppObject*>(s);
        il2cpp::Il2CppObject* slot = obj;
        ExpectI32(il2cpp::runtime::icall_value_type_get_hash_code_ref(&slot),
                  il2cpp::runtime::icall_object_get_hash_code(obj),
                  "reference arm hashes the referenced object (string)");

        // Reference arm -- boxed Int32 first field: the value hash.
        il2cpp::Il2CppObject* boxed = il2cpp::il2cpp_box_int32(nullptr, 42);
        il2cpp::Il2CppObject* boxSlot = boxed;
        ExpectI32(il2cpp::runtime::icall_value_type_get_hash_code_ref(&boxSlot),
                  il2cpp::runtime::icall_int32_get_hash_code(42),
                  "reference arm hashes a boxed Int32 as its value");

        // Reference arm -- null field -> 0 (the net48 documented null->0 rule).
        il2cpp::Il2CppObject* nullSlot = nullptr;
        ExpectI32(il2cpp::runtime::icall_value_type_get_hash_code_ref(&nullSlot), 0,
                  "reference arm with a null field returns 0");

        // No-fields arm: the resolver emits (0, 0, data) -> fieldKind 0 ->
        // typeHash 0 (the per-type constant is not injectable; documented).
        int32_t dummy = 0;
        ExpectI32(il2cpp::runtime::icall_value_type_get_hash_code(0, 0, &dummy), 0,
                  "no-fields value type hashes to 0 (typeHash arm)");

        // Value arm -- the generated deref shape: first int32 field identity.
        struct Pt { int32_t x; int32_t y; };
        Pt pt{ 42, 7 };
        ExpectI32(il2cpp::runtime::icall_int32_get_hash_code(
                      *reinterpret_cast<const int32_t*>(&pt)), 42,
                  "value arm: the first int32 field hashes to its value");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d arrayext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime arrayext tests passed\n");
    return 0;
}
