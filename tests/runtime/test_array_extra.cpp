//------------------------------------------------------------------------------
// test_array_extra.cpp -- System.Array direct-call API (arrayextra domain) tests.
//
// Coverage (normal + edge + error paths):
//   1. il2cpp_array_reverse_byte -- order reversal, 255 boundary, empty,
//      single, null array (ArgumentNullException), reference array
//      (NotSupported), wrong-stride value array (NotSupported).
//   2. il2cpp_array_reverse_int32 -- reversal with negatives and INT32_MIN/MAX
//      boundaries, odd length, empty/single.
//   3. il2cpp_array_reverse_int64 -- INT64_MIN/MAX boundaries.
//   4. il2cpp_array_reverse_string -- slot reversal (identity preserved),
//      null slots reverse like any other element.
//   5. il2cpp_array_reverse_object -- same slot reversal on the generic
//      entry, null slots legal.
//   6. il2cpp_array_clear_byte -- full/partial zeroing, length 0 no-op
//      (index == Length legal), index+length overflow (ArgumentException),
//      negative index/length (ArgumentOutOfRangeException), null array.
//   7. il2cpp_array_clear_int32 -- partial zeroing with boundary values.
//   8. il2cpp_array_get_value -- boxed round-trip for byte/int32/int64 (box
//      klass name + unboxed payload), reference arrays return the slot
//      itself (incl. null), out-of-range/negative index
//      (IndexOutOfRangeException), null array (ArgumentNullException),
//      non-array object (ArgumentException), unsupported stride
//      (NotSupported).
//   9. il2cpp_array_set_value -- unboxed store round-trip for byte/int32/
//      int64, reference storage (incl. null, legal), wrong boxed type
//      (InvalidCastException), null on a value array (ArgumentNullException),
//      out-of-range index (IndexOutOfRangeException), unsupported stride
//      (NotSupported).
//
// Plain executable + minimal assertion harness (see test_array.cpp).
//------------------------------------------------------------------------------

#include "arrayextra.h"
#include "il2cpp_array.h"
#include "il2cpp_boxing_ext.h"
#include "il2cpp_boxing_ext2.h"
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
    "Test.ArrayExtraElem", // name
    24,                    // instanceSize
    0b00000010u,           // refFieldBitmap: slot 1 is a GC reference
    1,                     // refFieldCount
    nullptr,               // baseClass
    nullptr,               // vtable
    0,                     // vtableSize
    nullptr,               // interfaces
    0,                     // interfaceCount
};

// Managed exception type names (exact chain head, as thrown by arrayextra.cpp
// and the box/unbox icalls).
const char kArgNullType[]       = "System.ArgumentNullException";
const char kArgOutOfRangeType[] = "System.ArgumentOutOfRangeException";
const char kArgumentType[]      = "System.ArgumentException";
const char kNotSupportedType[]  = "System.NotSupportedException";
const char kInvalidCastType[]   = "System.InvalidCastException";
const char kIndexOutOfRangeType[] = "System.IndexOutOfRangeException";

il2cpp::Il2CppArray* NewByteArray(int32_t length)
{
    return il2cpp::il2cpp_array_new(nullptr, 1, false, length);
}

il2cpp::Il2CppArray* NewInt32Array(int32_t length)
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

// An unsupported-stride value array (elemSize 2): GetValue/SetValue/reverse
// must throw NotSupportedException, never read garbage.
il2cpp::Il2CppArray* NewStride2Array(int32_t length)
{
    return il2cpp::il2cpp_array_new(nullptr, 2, false, length);
}

void FillBytes(il2cpp::Il2CppArray* arr, const uint8_t* values, int32_t count)
{
    uint8_t* data = reinterpret_cast<uint8_t*>(arr->data);
    for (int32_t i = 0; i < count; ++i)
    {
        data[i] = values[i];
    }
}

void FillInt32(il2cpp::Il2CppArray* arr, const int32_t* values, int32_t count)
{
    int32_t* data = reinterpret_cast<int32_t*>(arr->data);
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

void ExpectByteContent(il2cpp::Il2CppArray* arr, const uint8_t* expected, int32_t count, const char* what)
{
    const uint8_t* data = reinterpret_cast<const uint8_t*>(arr->data);
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
            std::printf("%s%u", i > 0 ? " " : "", static_cast<unsigned>(data[i]));
        }
        std::printf(")\n");
        ++g_failures;
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

// Managed string literal -> Il2CppObject* slot (Il2CppString* and
// Il2CppObject* are unrelated structs; the object arrays need the base type).
il2cpp::Il2CppObject* S(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs(ascii));
}

// Boxed-object klass-name check (box identity: System.Byte / System.Int32 /
// System.Int64).
bool BoxHasType(il2cpp::Il2CppObject* obj, const char* typeName)
{
    return obj != nullptr && obj->klass != nullptr && obj->klass->name != nullptr &&
           std::strcmp(obj->klass->name, typeName) == 0;
}

} // namespace

int main()
{
    // --- 1. Reverse byte ---
    {
        il2cpp::Il2CppArray* a = NewByteArray(5);
        const uint8_t vals[5] = { 0, 1, 2, 3, 4 };
        FillBytes(a, vals, 5);
        il2cpp::il2cpp_array_reverse_byte(a);
        const uint8_t reversed[5] = { 4, 3, 2, 1, 0 };
        ExpectByteContent(a, reversed, 5, "reverse_byte reverses the element order");

        il2cpp::Il2CppArray* b = NewByteArray(3);
        const uint8_t bounds[3] = { 255, 0, 127 };
        FillBytes(b, bounds, 3);
        il2cpp::il2cpp_array_reverse_byte(b);
        const uint8_t boundsRev[3] = { 127, 0, 255 };
        ExpectByteContent(b, boundsRev, 3, "reverse_byte handles the 255/0 boundary");

        il2cpp::Il2CppArray* empty = NewByteArray(0);
        il2cpp::il2cpp_array_reverse_byte(empty);
        ExpectI32(il2cpp::il2cpp_array_get_length(empty), 0, "reverse_byte of an empty array is a no-op");

        il2cpp::Il2CppArray* single = NewByteArray(1);
        const uint8_t one[1] = { 42 };
        FillBytes(single, one, 1);
        il2cpp::il2cpp_array_reverse_byte(single);
        ExpectByteContent(single, one, 1, "reverse_byte of a single-element array is a no-op");

        ExpectThrows(kArgNullType, "reverse_byte of null throws ArgumentNullException",
                     [] { il2cpp::il2cpp_array_reverse_byte(nullptr); });
        ExpectThrows(kNotSupportedType, "reverse_byte of a reference array throws NotSupportedException",
                     [] { il2cpp::il2cpp_array_reverse_byte(NewRefArray(2)); });
        ExpectThrows(kNotSupportedType, "reverse_byte of an int32 array throws NotSupportedException",
                     [] { il2cpp::il2cpp_array_reverse_byte(NewInt32Array(2)); });
    }

    // --- 2. Reverse int32 ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(7);
        const int32_t vals[7] = { 1, -2, 3, INT32_MIN, 5, INT32_MAX, 7 };
        FillInt32(a, vals, 7);
        il2cpp::il2cpp_array_reverse_int32(a);
        const int32_t reversed[7] = { 7, INT32_MAX, 5, INT32_MIN, 3, -2, 1 };
        ExpectInt32Content(a, reversed, 7, "reverse_int32 reverses an odd-length array (INT32_MIN/MAX boundaries)");

        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        il2cpp::il2cpp_array_reverse_int32(empty);
        ExpectI32(il2cpp::il2cpp_array_get_length(empty), 0, "reverse_int32 of an empty array is a no-op");

        il2cpp::Il2CppArray* single = NewInt32Array(1);
        const int32_t one[1] = { -1 };
        FillInt32(single, one, 1);
        il2cpp::il2cpp_array_reverse_int32(single);
        ExpectInt32Content(single, one, 1, "reverse_int32 of a single-element array is a no-op");

        ExpectThrows(kArgNullType, "reverse_int32 of null throws ArgumentNullException",
                     [] { il2cpp::il2cpp_array_reverse_int32(nullptr); });
        ExpectThrows(kNotSupportedType, "reverse_int32 of an int64 array throws NotSupportedException",
                     [] { il2cpp::il2cpp_array_reverse_int32(NewInt64Array(2)); });
    }

    // --- 3. Reverse int64 ---
    {
        il2cpp::Il2CppArray* a = NewInt64Array(4);
        const int64_t vals[4] = { INT64_MIN, 1LL, 2LL, INT64_MAX };
        FillInt64(a, vals, 4);
        il2cpp::il2cpp_array_reverse_int64(a);
        const int64_t reversed[4] = { INT64_MAX, 2LL, 1LL, INT64_MIN };
        ExpectInt64Content(a, reversed, 4, "reverse_int64 reverses (INT64_MIN/MAX boundaries)");

        ExpectThrows(kNotSupportedType, "reverse_int64 of a byte array throws NotSupportedException",
                     [] { il2cpp::il2cpp_array_reverse_int64(NewByteArray(2)); });
    }

    // --- 4. Reverse string ---
    {
        il2cpp::Il2CppObject* apple = S("apple");
        il2cpp::Il2CppObject* banana = S("banana");
        il2cpp::Il2CppArray* a = NewRefArray(4);
        il2cpp::Il2CppObject* vals[4] = { apple, nullptr, banana, nullptr };
        FillRefs(a, vals, 4);
        il2cpp::il2cpp_array_reverse_string(a);
        il2cpp::Il2CppObject* reversed[4] = { nullptr, banana, nullptr, apple };
        ExpectRefs(a, reversed, 4, "reverse_string reverses slots (null slots included, identity preserved)");

        ExpectThrows(kNotSupportedType, "reverse_string of an int32 array throws NotSupportedException",
                     [] { il2cpp::il2cpp_array_reverse_string(NewInt32Array(2)); });
    }

    // --- 5. Reverse object ---
    {
        il2cpp::Il2CppObject* apple = S("apple");
        il2cpp::Il2CppObject* banana = S("banana");
        il2cpp::Il2CppArray* a = NewRefArray(3);
        il2cpp::Il2CppObject* vals[3] = { apple, banana, nullptr };
        FillRefs(a, vals, 3);
        il2cpp::il2cpp_array_reverse_object(a);
        il2cpp::Il2CppObject* reversed[3] = { nullptr, banana, apple };
        ExpectRefs(a, reversed, 3, "reverse_object reverses slots (null last element moves first)");

        il2cpp::Il2CppArray* empty = NewRefArray(0);
        il2cpp::il2cpp_array_reverse_object(empty);
        ExpectI32(il2cpp::il2cpp_array_get_length(empty), 0, "reverse_object of an empty array is a no-op");
    }

    // --- 6. Clear byte ---
    {
        il2cpp::Il2CppArray* a = NewByteArray(5);
        const uint8_t vals[5] = { 1, 2, 3, 4, 5 };
        FillBytes(a, vals, 5);
        il2cpp::il2cpp_array_clear_byte(a, 0, 5);
        const uint8_t zeros[5] = { 0, 0, 0, 0, 0 };
        ExpectByteContent(a, zeros, 5, "clear_byte zeroes the whole array");

        il2cpp::Il2CppArray* b = NewByteArray(6);
        const uint8_t partial[6] = { 9, 8, 7, 6, 5, 4 };
        FillBytes(b, partial, 6);
        il2cpp::il2cpp_array_clear_byte(b, 2, 2);
        const uint8_t middle[6] = { 9, 8, 0, 0, 5, 4 };
        ExpectByteContent(b, middle, 6, "clear_byte zeroes only the requested range");

        il2cpp::Il2CppArray* c = NewByteArray(3);
        const uint8_t keep[3] = { 1, 2, 3 };
        FillBytes(c, keep, 3);
        il2cpp::il2cpp_array_clear_byte(c, 3, 0);
        ExpectByteContent(c, keep, 3, "clear_byte with length 0 is a no-op (index == Length legal)");

        ExpectThrows(kArgOutOfRangeType, "clear_byte with a negative index throws ArgumentOutOfRangeException",
                     [&] { il2cpp::il2cpp_array_clear_byte(c, -1, 1); });
        ExpectThrows(kArgOutOfRangeType, "clear_byte with a negative length throws ArgumentOutOfRangeException",
                     [&] { il2cpp::il2cpp_array_clear_byte(c, 0, -1); });
        ExpectThrows(kArgumentType, "clear_byte with index + length beyond the array throws ArgumentException",
                     [&] { il2cpp::il2cpp_array_clear_byte(c, 2, 2); });
        ExpectThrows(kArgumentType, "clear_byte with an overflowing index + length throws ArgumentException",
                     [&] { il2cpp::il2cpp_array_clear_byte(c, INT32_MAX, 1); });
        ExpectThrows(kArgNullType, "clear_byte of null throws ArgumentNullException",
                     [] { il2cpp::il2cpp_array_clear_byte(nullptr, 0, 1); });
        ExpectThrows(kNotSupportedType, "clear_byte of a reference array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_clear_byte(NewRefArray(2), 0, 1); });
    }

    // --- 7. Clear int32 ---
    {
        il2cpp::Il2CppArray* a = NewInt32Array(5);
        const int32_t vals[5] = { INT32_MAX, -1, 0, 1, INT32_MIN };
        FillInt32(a, vals, 5);
        il2cpp::il2cpp_array_clear_int32(a, 1, 3);
        const int32_t middle[5] = { INT32_MAX, 0, 0, 0, INT32_MIN };
        ExpectInt32Content(a, middle, 5, "clear_int32 zeroes the requested range (boundary values kept)");

        ExpectThrows(kArgOutOfRangeType, "clear_int32 with a negative index throws ArgumentOutOfRangeException",
                     [&] { il2cpp::il2cpp_array_clear_int32(a, -1, 0); });
        ExpectThrows(kArgumentType, "clear_int32 with index + length beyond the array throws ArgumentException",
                     [&] { il2cpp::il2cpp_array_clear_int32(a, 4, 2); });
        ExpectThrows(kNotSupportedType, "clear_int32 of a byte array throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_clear_int32(NewByteArray(2), 0, 1); });
    }

    // --- 8. GetValue ---
    {
        il2cpp::Il2CppArray* i32 = NewInt32Array(3);
        const int32_t iv[3] = { 10, -20, 30 };
        FillInt32(i32, iv, 3);
        il2cpp::Il2CppObject* b1 = il2cpp::il2cpp_array_get_value(i32, 0);
        Expect(BoxHasType(b1, "System.Int32"), "get_value boxes an int32 element as System.Int32");
        ExpectI32(il2cpp::runtime::icall_unbox_int32(b1), 10, "get_value int32 payload round-trips");
        Expect(il2cpp::il2cpp_array_get_value(i32, 2) != nullptr,
               "get_value of the last int32 element returns a box");

        il2cpp::Il2CppArray* by = NewByteArray(3);
        const uint8_t bv[3] = { 0, 127, 255 };
        FillBytes(by, bv, 3);
        il2cpp::Il2CppObject* bb = il2cpp::il2cpp_array_get_value(by, 2);
        Expect(BoxHasType(bb, "System.Byte"), "get_value boxes a byte element as System.Byte");
        ExpectI32(il2cpp::runtime::icall_unbox_byte(bb), 255, "get_value byte payload round-trips");

        il2cpp::Il2CppArray* i64 = NewInt64Array(2);
        const int64_t lv[2] = { INT64_MIN, INT64_MAX };
        FillInt64(i64, lv, 2);
        il2cpp::Il2CppObject* lb = il2cpp::il2cpp_array_get_value(i64, 0);
        Expect(BoxHasType(lb, "System.Int64"), "get_value boxes an int64 element as System.Int64");
        Expect(il2cpp::runtime::icall_unbox_long(lb) == INT64_MIN,
               "get_value int64 payload round-trips");

        il2cpp::Il2CppObject* apple = S("apple");
        il2cpp::Il2CppArray* refs = NewRefArray(2);
        il2cpp::Il2CppObject* rv[2] = { apple, nullptr };
        FillRefs(refs, rv, 2);
        Expect(il2cpp::il2cpp_array_get_value(refs, 0) == apple,
               "get_value on a reference array returns the slot itself");
        Expect(il2cpp::il2cpp_array_get_value(refs, 1) == nullptr,
               "get_value on a reference array may return null");

        ExpectThrows(kIndexOutOfRangeType, "get_value with an out-of-range index throws IndexOutOfRangeException",
                     [&] { il2cpp::il2cpp_array_get_value(i32, 3); });
        ExpectThrows(kIndexOutOfRangeType, "get_value with a negative index throws IndexOutOfRangeException",
                     [&] { il2cpp::il2cpp_array_get_value(i32, -1); });
        ExpectThrows(kArgNullType, "get_value of null throws ArgumentNullException",
                     [] { il2cpp::il2cpp_array_get_value(nullptr, 0); });

        il2cpp::Il2CppObject* nonArray = il2cpp::il2cpp_gc_alloc(&g_mockElemClass);
        ExpectThrows(kArgumentType, "get_value of a non-array object throws ArgumentException",
                     [&] { il2cpp::il2cpp_array_get_value(reinterpret_cast<il2cpp::Il2CppArray*>(nonArray), 0); });

        ExpectThrows(kNotSupportedType, "get_value on an unsupported stride throws NotSupportedException",
                     [] { il2cpp::il2cpp_array_get_value(NewStride2Array(2), 0); });
    }

    // --- 9. SetValue ---
    {
        il2cpp::Il2CppArray* i32 = NewInt32Array(3);
        il2cpp::il2cpp_array_set_value(i32, il2cpp::il2cpp_box_int32(nullptr, 42), 1);
        const int32_t after[3] = { 0, 42, 0 };
        ExpectInt32Content(i32, after, 3, "set_value stores an unboxed int32 into the array");
        ExpectI32(il2cpp::runtime::icall_unbox_int32(il2cpp::il2cpp_array_get_value(i32, 1)), 42,
                  "set_value/get_value int32 round-trips");

        il2cpp::Il2CppArray* by = NewByteArray(2);
        il2cpp::il2cpp_array_set_value(by, il2cpp::runtime::icall_box_byte(255), 0);
        const uint8_t afterByte[2] = { 255, 0 };
        ExpectByteContent(by, afterByte, 2,
                          "set_value stores an unboxed byte into the array");

        il2cpp::Il2CppArray* i64 = NewInt64Array(2);
        il2cpp::il2cpp_array_set_value(i64, il2cpp::runtime::icall_box_long(INT64_MIN), 0);
        const int64_t after64[2] = { INT64_MIN, 0 };
        ExpectInt64Content(i64, after64, 2, "set_value stores an unboxed int64 into the array");

        il2cpp::Il2CppObject* apple = S("apple");
        il2cpp::Il2CppArray* refs = NewRefArray(2);
        il2cpp::il2cpp_array_set_value(refs, apple, 0);
        il2cpp::il2cpp_array_set_value(refs, nullptr, 1);
        Expect(il2cpp::il2cpp_array_get_value(refs, 0) == apple,
               "set_value on a reference array stores the reference");
        Expect(il2cpp::il2cpp_array_get_value(refs, 1) == nullptr,
               "set_value of null on a reference array stores a null slot (legal)");

        ExpectThrows(kInvalidCastType, "set_value of a boxed Int32 into a byte array throws InvalidCastException",
                     [&] { il2cpp::il2cpp_array_set_value(by, il2cpp::il2cpp_box_int32(nullptr, 1), 1); });
        ExpectThrows(kInvalidCastType, "set_value of a boxed Byte into an int32 array throws InvalidCastException",
                     [&] { il2cpp::il2cpp_array_set_value(i32, il2cpp::runtime::icall_box_byte(1), 0); });
        ExpectThrows(kArgNullType, "set_value of null on a value array throws ArgumentNullException",
                     [&] { il2cpp::il2cpp_array_set_value(i32, nullptr, 0); });
        ExpectThrows(kIndexOutOfRangeType, "set_value with an out-of-range index throws IndexOutOfRangeException",
                     [&] { il2cpp::il2cpp_array_set_value(i32, il2cpp::il2cpp_box_int32(nullptr, 1), 3); });
        ExpectThrows(kArgNullType, "set_value on null throws ArgumentNullException",
                     [] { il2cpp::il2cpp_array_set_value(nullptr, nullptr, 0); });
        ExpectThrows(kNotSupportedType, "set_value on an unsupported stride throws NotSupportedException",
                     [&] { il2cpp::il2cpp_array_set_value(NewStride2Array(2), nullptr, 0); });
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d arrayextra assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime arrayextra tests passed\n");
    return 0;
}
