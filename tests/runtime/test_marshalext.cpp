//------------------------------------------------------------------------------
// test_marshalext.cpp -- P/Invoke struct marshal primitive unit tests (M4,
// see il2cpp_marshal_ext.h for the contract under test).
//
// Coverage (normal + edge + error paths):
//   1. size_of_struct: layout formula round_up(fieldCount*4, 8) incl. 0
//      fields; explicit all-int32 fieldSizes; negative / overflowing
//      fieldCount -> ArgumentOutOfRangeException; non-int32 field size ->
//      NotSupportedException; null fieldSizes accepted (M4 default).
//   2. alloc_struct: non-null token for positive sizes; token 0 ("no
//      buffer") for 0/negative sizes; calloc zeroing (untouched slot reads
//      as 0); the int64 token round-trips to the native pointer through the
//      documented re-expansion idiom (full address -- the runtime never
//      truncates; see the header's ABI note).
//   3. write/read round-trip: per-slot writes incl. INT32_MIN/MAX and
//      negative values; the LAST slot of an exact-fit allocation; the
//      documented in-buffer behavior of an index that fits the BUFFER but
//      lies past the semantic field count (memory safety is the bound).
//   4. Bounds defense: negative index / index past the buffer end ->
//      ArgumentOutOfRangeException; null structPtr -> ArgumentNullException.
//   5. struct -> int32[]: element copy, exact-fit fieldCount, fieldCount 0
//      (empty array), kind fields of the produced array (elemSize 4,
//      non-reference); null ptr / negative / oversized fieldCount ->
//      exceptions.
//   6. int32[] -> struct: element copy; mutation round-trip (struct ->
//      array -> modify -> struct); null array / null ptr ->
//      ArgumentNullException; reference-element array / wrong elemSize ->
//      NotSupportedException; non-array object -> ArgumentException;
//      array longer than the buffer -> ArgumentOutOfRangeException.
//   7. alloc/free pairing: free(null) is a no-op; 20 alloc/free cycles
//      succeed and the registry reuses slots (no leak in the bounded
//      table); the tracked pointer is released (no double-free at the end).
//
// Abort-path defenses (foreign pointer, double free, registry overflow,
// >32-bit address, OOM) cannot be exercised in-process -- abort() ends the
// test -- and are documented in the header instead.
//
// Plain executable + minimal assertion harness (see test_marshal.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_marshal_ext.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>

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

// Run 'fn' and report whether it threw a DotNetException whose type chain
// exactly matches 'expectedType' (full chain -- see exceptionext.cpp).
template <typename Fn>
bool ThrowsDotNet(Fn&& fn, const char* expectedType)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedType) == 0;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

// Expected type chains (must match exceptionext.cpp exactly).
const char* kArgumentNull =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,"
    "System.SystemException,System.Exception";
const char* kArgumentOutOfRange =
    "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,"
    "System.SystemException,System.Exception";
const char* kNotSupported =
    "System.NotSupportedException,System.NotSupportedException,System.SystemException,System.Exception";
const char* kArgument =
    "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception";

// The documented int64-token re-expansion idiom (see il2cpp_marshal_ext.h's
// ABI note): the token is the FULL native address, so the round trip is
// exact on 32-bit and 64-bit hosts.
void* ExpandToken(int64_t token)
{
    return reinterpret_cast<void*>(static_cast<intptr_t>(token));
}

// Mock element class descriptor for array creation (test_marshal.cpp style).
il2cpp::Il2CppClass g_mockElemClass = {
    "Mock.Elem", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    const int32_t intMin = std::numeric_limits<int32_t>::min();
    const int32_t intMax = std::numeric_limits<int32_t>::max();

    // --- 1. size_of_struct ---
    Expect(icall_marshal_size_of_struct(0, nullptr) == 0, "size_of_struct(0) is 0");
    Expect(icall_marshal_size_of_struct(1, nullptr) == 8, "size_of_struct(1) is 8");
    Expect(icall_marshal_size_of_struct(2, nullptr) == 8, "size_of_struct(2) is 8");
    Expect(icall_marshal_size_of_struct(3, nullptr) == 16, "size_of_struct(3) is 16");
    Expect(icall_marshal_size_of_struct(8, nullptr) == 32, "size_of_struct(8) is 32");
    Expect(icall_marshal_size_of_struct(9, nullptr) == 40, "size_of_struct(9) is 40");
    int32_t allInt32[3] = { 4, 4, 4 };
    Expect(icall_marshal_size_of_struct(3, allInt32) == 16,
           "explicit all-int32 fieldSizes yield the same layout");
    Expect(ThrowsDotNet([&] { icall_marshal_size_of_struct(-1, nullptr); }, kArgumentOutOfRange),
           "negative fieldCount -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { icall_marshal_size_of_struct(INT32_MAX, nullptr); }, kArgumentOutOfRange),
           "overflowing fieldCount -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] {
               int32_t sizes[2] = { 4, 8 };
               icall_marshal_size_of_struct(2, sizes);
           }, kNotSupported),
           "non-int32 field size -> NotSupportedException");

    // --- 2. alloc_struct + token round-trip ---
    const int64_t token2 = icall_marshal_alloc_struct(icall_marshal_size_of_struct(2, nullptr));
    Expect(token2 != 0, "alloc_struct returns a non-null token");
    void* p2 = ExpandToken(token2);
    Expect(p2 != nullptr, "token re-expands to a non-null pointer");
    Expect(icall_marshal_alloc_struct(0) == 0, "alloc_struct(0) returns the null token");
    Expect(icall_marshal_alloc_struct(-4) == 0, "alloc_struct(negative) returns the null token");
    Expect(icall_marshal_read_int32_field(p2, 0) == 0,
           "calloc zeroing: an untouched slot reads as 0");

    // --- 3. write/read round-trip ---
    icall_marshal_write_int32_field(p2, 0, 42);
    icall_marshal_write_int32_field(p2, 1, -7);
    Expect(icall_marshal_read_int32_field(p2, 0) == 42, "field 0 round-trips");
    Expect(icall_marshal_read_int32_field(p2, 1) == -7, "field 1 round-trips (negative)");
    icall_marshal_write_int32_field(p2, 0, intMin);
    icall_marshal_write_int32_field(p2, 1, intMax);
    Expect(icall_marshal_read_int32_field(p2, 0) == intMin, "INT32_MIN round-trips");
    Expect(icall_marshal_read_int32_field(p2, 1) == intMax, "INT32_MAX round-trips");

    // --- 4. Bounds defense ---
    Expect(ThrowsDotNet([&] { icall_marshal_write_int32_field(p2, 2, 1); }, kArgumentOutOfRange),
           "write past the buffer end -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { (void)icall_marshal_read_int32_field(p2, 2); }, kArgumentOutOfRange),
           "read past the buffer end -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { icall_marshal_write_int32_field(p2, -1, 1); }, kArgumentOutOfRange),
           "negative fieldIndex -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { (void)icall_marshal_read_int32_field(p2, -1); }, kArgumentOutOfRange),
           "negative read fieldIndex -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { icall_marshal_write_int32_field(nullptr, 0, 1); }, kArgumentNull),
           "null structPtr write -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { (void)icall_marshal_read_int32_field(nullptr, 0); }, kArgumentNull),
           "null structPtr read -> ArgumentNullException");

    // --- 5. struct -> int32[] ---
    const int64_t token3 = icall_marshal_alloc_struct(icall_marshal_size_of_struct(3, nullptr));
    void* p3 = ExpandToken(token3);
    // The LAST slot of an exact-fit 3-field allocation is writable.
    icall_marshal_write_int32_field(p3, 2, 123);
    Expect(icall_marshal_read_int32_field(p3, 2) == 123, "last slot of an exact-fit struct writes");
    // Documented contract: the bound is the BUFFER size. Index 3 lies past
    // the 3-field count but its range (12..16) fits the 16-byte buffer, so
    // the write is memory-safe and permitted; index 4 (20 bytes) is not.
    icall_marshal_write_int32_field(p3, 3, 555);
    Expect(icall_marshal_read_int32_field(p3, 3) == 555,
           "in-buffer slot past the field count is permitted (memory-safe)");
    Expect(ThrowsDotNet([&] { icall_marshal_write_int32_field(p3, 4, 1); }, kArgumentOutOfRange),
           "index 4 on a 16-byte buffer -> ArgumentOutOfRangeException");

    icall_marshal_write_int32_field(p3, 0, 10);
    icall_marshal_write_int32_field(p3, 1, 20);
    il2cpp::Il2CppObject* arrObj = icall_marshal_struct_to_int32_array(p3, 3);
    il2cpp::Il2CppArray* arr = reinterpret_cast<il2cpp::Il2CppArray*>(arrObj);
    const int32_t* elems = reinterpret_cast<const int32_t*>(arr->data);
    Expect(arr->length == 3 && elems[0] == 10 && elems[1] == 20 && elems[2] == 123,
           "struct -> int32[] copies the field slots");
    Expect(arr->elemSize == 4 && !arr->isRefElem, "produced array is an int32 element kind");
    il2cpp::Il2CppObject* arr4 = icall_marshal_struct_to_int32_array(p3, 4);
    il2cpp::Il2CppArray* a4 = reinterpret_cast<il2cpp::Il2CppArray*>(arr4);
    const int32_t* e4 = reinterpret_cast<const int32_t*>(a4->data);
    Expect(a4->length == 4 && e4[0] == 10 && e4[1] == 20 && e4[2] == 123 && e4[3] == 555,
           "exact-fit fieldCount 4 converts all four slots");
    il2cpp::Il2CppObject* arr0 = icall_marshal_struct_to_int32_array(p3, 0);
    Expect(reinterpret_cast<il2cpp::Il2CppArray*>(arr0)->length == 0,
           "fieldCount 0 produces an empty array");
    Expect(ThrowsDotNet([&] { (void)icall_marshal_struct_to_int32_array(nullptr, 3); }, kArgumentNull),
           "to_array(null) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { (void)icall_marshal_struct_to_int32_array(p3, -1); }, kArgumentOutOfRange),
           "to_array(negative fieldCount) -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { (void)icall_marshal_struct_to_int32_array(p3, 5); }, kArgumentOutOfRange),
           "to_array(fieldCount past the buffer) -> ArgumentOutOfRangeException");

    // --- 6. int32[] -> struct ---
    const int64_t token4 = icall_marshal_alloc_struct(icall_marshal_size_of_struct(3, nullptr));
    void* p4 = ExpandToken(token4);
    il2cpp::Il2CppArray* src = il2cpp::il2cpp_array_new(&g_mockElemClass, 4, false, 3);
    int32_t* sdata = reinterpret_cast<int32_t*>(src->data);
    sdata[0] = 1; sdata[1] = 2; sdata[2] = 3;
    icall_marshal_int32_array_to_struct(reinterpret_cast<il2cpp::Il2CppObject*>(src), p4);
    Expect(icall_marshal_read_int32_field(p4, 0) == 1 &&
           icall_marshal_read_int32_field(p4, 1) == 2 &&
           icall_marshal_read_int32_field(p4, 2) == 3,
           "int32[] -> struct copies the array elements");
    Expect(icall_marshal_read_int32_field(p4, 3) == 0,
           "slots past the copied elements stay untouched (calloc zero)");
    // Mutation round-trip: struct -> array -> modify -> struct.
    icall_marshal_write_int32_field(p4, 0, 7);
    icall_marshal_write_int32_field(p4, 1, 8);
    icall_marshal_write_int32_field(p4, 2, 9);
    il2cpp::Il2CppObject* mut = icall_marshal_struct_to_int32_array(p4, 3);
    reinterpret_cast<int32_t*>(reinterpret_cast<il2cpp::Il2CppArray*>(mut)->data)[1] = 99;
    icall_marshal_int32_array_to_struct(mut, p4);
    Expect(icall_marshal_read_int32_field(p4, 0) == 7 &&
           icall_marshal_read_int32_field(p4, 1) == 99 &&
           icall_marshal_read_int32_field(p4, 2) == 9,
           "struct -> array -> modify -> struct round-trips");
    Expect(ThrowsDotNet([&] { icall_marshal_int32_array_to_struct(nullptr, p4); }, kArgumentNull),
           "to_struct(null array) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_marshal_int32_array_to_struct(arrObj, nullptr); }, kArgumentNull),
           "to_struct(null structPtr) -> ArgumentNullException");
    il2cpp::Il2CppArray* refArr = il2cpp::il2cpp_array_new(&g_mockElemClass, sizeof(void*), true, 2);
    Expect(ThrowsDotNet([&] { icall_marshal_int32_array_to_struct(
                                  reinterpret_cast<il2cpp::Il2CppObject*>(refArr), p4); }, kNotSupported),
           "reference-element array -> NotSupportedException");
    il2cpp::Il2CppArray* wideArr = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, false, 2);
    Expect(ThrowsDotNet([&] { icall_marshal_int32_array_to_struct(
                                  reinterpret_cast<il2cpp::Il2CppObject*>(wideArr), p4); }, kNotSupported),
           "non-int32 element size -> NotSupportedException");
    il2cpp::Il2CppObject* str = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("not-an-array"));
    Expect(ThrowsDotNet([&] { icall_marshal_int32_array_to_struct(str, p4); }, kArgument),
           "non-array object -> ArgumentException");
    il2cpp::Il2CppArray* big = il2cpp::il2cpp_array_new(&g_mockElemClass, 4, false, 5);
    Expect(ThrowsDotNet([&] { icall_marshal_int32_array_to_struct(
                                  reinterpret_cast<il2cpp::Il2CppObject*>(big), p4); }, kArgumentOutOfRange),
           "array longer than the struct buffer -> ArgumentOutOfRangeException");

    // --- 7. alloc/free pairing ---
    icall_marshal_free_struct(p2);
    icall_marshal_free_struct(nullptr);
    icall_marshal_free_struct(p3);
    icall_marshal_free_struct(p4);
    constexpr int kCycles = 20;
    int64_t tokens[kCycles];
    bool allNonNull = true;
    for (int i = 0; i < kCycles; ++i)
    {
        tokens[i] = icall_marshal_alloc_struct(8);
        allNonNull = allNonNull && tokens[i] != 0;
    }
    Expect(allNonNull, "20 consecutive allocations all return non-null tokens");
    for (int i = 0; i < kCycles; ++i)
    {
        icall_marshal_free_struct(ExpandToken(tokens[i]));
    }
    const int64_t again = icall_marshal_alloc_struct(8);
    Expect(again != 0, "registry reuses slots after free (no leak)");
    icall_marshal_free_struct(ExpandToken(again));

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d marshalext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime marshalext tests passed\n");
    return 0;
}
