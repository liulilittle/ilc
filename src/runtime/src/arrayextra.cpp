//------------------------------------------------------------------------------
// arrayextra.cpp -- System.Array direct-call API, extra batch (see arrayextra.h
// for the full contract: supported element kinds, validation order, exception
// mapping, the GetValue/SetValue box mapping and the GC/write-barrier rule).
//
// Implementation notes:
//   - Every entry point receives Il2CppArray* (the generated code's array
//     reference) and validates the array layout before dereferencing any
//     field: null -> ArgumentNullException, a non-array object (klass name
//     without the '[' array-class marker, array.cpp convention) ->
//     ArgumentException. Validation helpers are file-local with an Extra
//     suffix so they never collide with the icall domains' helpers or the
//     arraysort domain's helpers.
//   - The value reversals share ONE templated two-pointer core (byte/int32/
//     int64 differ only in the element type); the reference reversals
//     (string / object) share ONE slot-swap core. Every slot swap runs the
//     write barrier on both slots (GC contract, see il2cpp_gc.h); the
//     operations allocate nothing, so no collection can interleave and no
//     rooting is needed.
//   - Clear validates the range in 64-bit (index + length can never overflow
//     int32 and bypass the bounds check, mirroring the ext2 icalls), then
//     zeroes the range with a single memset. length == 0 is a validated
//     no-op (index == Length is legal, matching .NET Array.Clear).
//   - GetValue/SetValue use the shared box layout (boxing.cpp / boxingext2.cpp):
//     byte boxes as System.Byte (icall_box_byte / icall_unbox_byte), int32 as
//     System.Int32 (il2cpp_box_int32 / icall_unbox_int32), int64 as
//     System.Int64 (icall_box_long / icall_unbox_long). SetValue checks null
//     BEFORE unboxing on a value array (ArgumentNullException, matching .NET
//     Array.SetValue); the unbox icalls themselves raise
//     InvalidCastException for a wrong boxed type.
//------------------------------------------------------------------------------

#include "arrayextra.h"

#include "il2cpp_boxing_ext.h"
#include "il2cpp_boxing_ext2.h"
#include "il2cpp_exception.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstring>

namespace il2cpp {

namespace {

// The '[' prefix marks an array class descriptor (array.cpp convention).
const char kArrayClassMarker = '[';

//------------------------------------------------------------------------------
// RequireArrayExtra -- validate a direct-call array argument.
//   null                -> ArgumentNullException
//   non-array object    -> ArgumentException (caller bug, never UB)
//------------------------------------------------------------------------------
Il2CppArray* RequireArrayExtra(Il2CppArray* arr)
{
    if (arr == nullptr)
    {
        il2cpp_throw_argument_null_exception("array");
    }
    if (arr->obj.klass == nullptr || arr->obj.klass->name == nullptr ||
        arr->obj.klass->name[0] != kArrayClassMarker)
    {
        il2cpp_throw_argument_exception("Object is not an array.");
    }
    return arr;
}

//------------------------------------------------------------------------------
// RequireValueArrayExtra -- value-array domain contract: isRefElem == false
// and the exact element stride (1 for byte, 4 for int32, 8 for int64).
// Anything else is unsupported in this domain.
//------------------------------------------------------------------------------
Il2CppArray* RequireValueArrayExtra(Il2CppArray* arr, int32_t elemSize)
{
    if (arr->isRefElem || arr->elemSize != elemSize)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional value-element arrays of the expected stride are supported.");
    }
    return arr;
}

//------------------------------------------------------------------------------
// RequireRefArrayExtra -- reference-array-only domain contract (string /
// object reversals, GetValue/SetValue reference storage). The elements are
// Il2CppObject* slots; the element class descriptor is NOT consulted (tests
// and generated code may create reference arrays with a null element
// descriptor).
//------------------------------------------------------------------------------
Il2CppArray* RequireRefArrayExtra(Il2CppArray* arr)
{
    if (!arr->isRefElem)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional reference-element arrays are supported.");
    }
    return arr;
}

//------------------------------------------------------------------------------
// RequireRange -- shared Clear validation (.NET semantics, same order as the
// ext2 icalls): negative index/length -> ArgumentOutOfRangeException, then
// index + length > Length in 64-bit -> ArgumentException (the 64-bit sum can
// never be bypassed by int32 overflow). length == 0 with index == Length is a
// legal no-op.
//------------------------------------------------------------------------------
void RequireRange(Il2CppArray* arr, int32_t index, int32_t length)
{
    if (index < 0 || length < 0 ||
        static_cast<int64_t>(index) + static_cast<int64_t>(length) >
            static_cast<int64_t>(arr->length))
    {
        il2cpp_throw_index_out_of_range_exception("索引超出了数组界限。");
    }
}

//------------------------------------------------------------------------------
// RequireIndex -- GetValue/SetValue bounds check (.NET IndexOutOfRangeException
// semantics for a negative or beyond-the-end index).
//------------------------------------------------------------------------------
void RequireIndex(Il2CppArray* arr, int32_t index)
{
    if (index < 0 || index >= arr->length)
    {
        il2cpp_throw_index_out_of_range_exception(nullptr);
    }
}

//------------------------------------------------------------------------------
// ReverseValues -- templated two-pointer reversal on raw element values
// (byte/int32/int64 share this core; the swap is raw element bytes).
//------------------------------------------------------------------------------
template <typename T>
void ReverseValuesExtra(T* data, int32_t length)
{
    for (int32_t i = 0, j = length - 1; i < j; ++i, --j)
    {
        const T tmp = data[i];
        data[i] = data[j];
        data[j] = tmp;
    }
}

//------------------------------------------------------------------------------
// ReverseSlots -- two-pointer reversal of Il2CppObject* slots (string / object
// reversals; identity -- never the pointees). Every swap runs the write
// barrier on both slots (an old-generation array's card must be marked; no
// allocation happens, so no collection can interleave).
//------------------------------------------------------------------------------
void ReverseSlotsExtra(Il2CppArray* arr)
{
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(arr->data);
    for (int32_t i = 0, j = arr->length - 1; i < j; ++i, --j)
    {
        Il2CppObject* tmp = slots[i];
        slots[i] = slots[j];
        slots[j] = tmp;
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[j]), slots[j]);
    }
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_array_reverse_byte/int32/int64 -- Array.Reverse(T[]) (see header).
// In-place two-pointer reversal of the raw element values. An
// empty/single-element array is a no-op.
//------------------------------------------------------------------------------
void il2cpp_array_reverse_byte(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, 1);
    if (a->length > 1)
    {
        ReverseValuesExtra(reinterpret_cast<uint8_t*>(a->data), a->length);
    }
}

void il2cpp_array_reverse_int32(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, sizeof(int32_t));
    if (a->length > 1)
    {
        ReverseValuesExtra(reinterpret_cast<int32_t*>(a->data), a->length);
    }
}

void il2cpp_array_reverse_int64(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, sizeof(int64_t));
    if (a->length > 1)
    {
        ReverseValuesExtra(reinterpret_cast<int64_t*>(a->data), a->length);
    }
}

// W7: the double 1-arg reverser (8-byte slot reversal, same contract as
// the int64 twin above).
void il2cpp_array_reverse_double(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, sizeof(double));
    if (a->length > 1)
    {
        ReverseValuesExtra(reinterpret_cast<double*>(a->data), a->length);
    }
}

// The float / narrow-integral 1-arg reversers: the same raw two-pointer swap,
// one per element stride (float 4, the narrow integrals 1/2/4). byte already
// has its own entry above.
void il2cpp_array_reverse_float(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, sizeof(float));
    if (a->length > 1)
    {
        ReverseValuesExtra(reinterpret_cast<float*>(a->data), a->length);
    }
}

void il2cpp_array_reverse_sbyte(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, 1);
    if (a->length > 1)
    {
        ReverseValuesExtra(reinterpret_cast<int8_t*>(a->data), a->length);
    }
}

void il2cpp_array_reverse_bool(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, sizeof(bool));
    if (a->length > 1)
    {
        ReverseValuesExtra(reinterpret_cast<bool*>(a->data), a->length);
    }
}

void il2cpp_array_reverse_short(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, sizeof(int16_t));
    if (a->length > 1)
    {
        ReverseValuesExtra(reinterpret_cast<int16_t*>(a->data), a->length);
    }
}

void il2cpp_array_reverse_ushort(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, sizeof(uint16_t));
    if (a->length > 1)
    {
        ReverseValuesExtra(reinterpret_cast<uint16_t*>(a->data), a->length);
    }
}

void il2cpp_array_reverse_char(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, sizeof(char16_t));
    if (a->length > 1)
    {
        ReverseValuesExtra(reinterpret_cast<char16_t*>(a->data), a->length);
    }
}

void il2cpp_array_reverse_uint(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, sizeof(uint32_t));
    if (a->length > 1)
    {
        ReverseValuesExtra(reinterpret_cast<uint32_t*>(a->data), a->length);
    }
}

// W7 (T2): the uint64 1-arg reverser (8-byte slot reversal, same contract as
// the int64 twin above).
void il2cpp_array_reverse_uint64(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, sizeof(uint64_t));
    if (a->length > 1)
    {
        ReverseValuesExtra(reinterpret_cast<uint64_t*>(a->data), a->length);
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_reverse_string/object -- Array.Reverse(T[]) reference variants
// (see header). In-place reversal of the Il2CppObject* slots (identity --
// never the pointees); null slots are legal and reverse like any other
// element.
//------------------------------------------------------------------------------
void il2cpp_array_reverse_string(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireRefArrayExtra(a);
    if (a->length > 1)
    {
        ReverseSlotsExtra(a);
    }
}

void il2cpp_array_reverse_object(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireRefArrayExtra(a);
    if (a->length > 1)
    {
        ReverseSlotsExtra(a);
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_clear_byte -- Array.Clear(byte[], int, int) (see header).
// Validation per RequireRange, then a single memset of 'length' bytes at
// 'index'. length == 0 is a validated no-op.
//------------------------------------------------------------------------------
void il2cpp_array_clear_byte(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, 1);
    RequireRange(a, index, length);
    if (length > 0)
    {
        std::memset(a->data + index, 0, static_cast<size_t>(length));
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_clear_int32 -- Array.Clear(int32[], int, int) (see header).
// int32 variant of il2cpp_array_clear_byte (stride 4).
//------------------------------------------------------------------------------
void il2cpp_array_clear_int32(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireValueArrayExtra(a, sizeof(int32_t));
    RequireRange(a, index, length);
    if (length > 0)
    {
        std::memset(a->data + static_cast<size_t>(index) * sizeof(int32_t), 0,
                    static_cast<size_t>(length) * sizeof(int32_t));
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_get_value -- Array.GetValue(int) (see header).
//   Reference arrays return the slot itself (may be null). Value arrays box
//   the element with the array's primitive type (byte -> System.Byte via
//   icall_box_byte, int32 -> System.Int32 via il2cpp_box_int32, int64 ->
//   System.Int64 via icall_box_long); any other stride throws
//   NotSupportedException.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_array_get_value(Il2CppArray* arr, int32_t index)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireIndex(a, index);
    if (a->isRefElem)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
        return slots[index];
    }
    switch (a->elemSize)
    {
        case 1:
        {
            const uint8_t v = *reinterpret_cast<const uint8_t*>(a->data + index);
            return runtime::icall_box_byte(v);
        }
        case sizeof(int32_t):
        {
            const int32_t v = *reinterpret_cast<const int32_t*>(
                a->data + static_cast<size_t>(index) * sizeof(int32_t));
            return il2cpp_box_int32(nullptr, v);
        }
        case sizeof(int64_t):
        {
            const int64_t v = *reinterpret_cast<const int64_t*>(
                a->data + static_cast<size_t>(index) * sizeof(int64_t));
            return runtime::icall_box_long(v);
        }
        default:
            il2cpp_throw_not_supported_exception(
                "GetValue supports only byte, int32 and int64 one-dimensional arrays.");
            return nullptr;
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_set_value -- Array.SetValue(object, int) (see header).
//   Reference arrays store the reference itself (null legal, write barrier
//   always run). Value arrays check null FIRST (ArgumentNullException,
//   matching .NET), then unbox through the array's primitive type (a wrong
//   boxed type raises InvalidCastException through the unbox icall); any
//   other stride throws NotSupportedException.
//------------------------------------------------------------------------------
void il2cpp_array_set_value(Il2CppArray* arr, Il2CppObject* value, int32_t index)
{
    Il2CppArray* a = RequireArrayExtra(arr);
    RequireIndex(a, index);
    if (a->isRefElem)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
        slots[index] = value;
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[index]), value);
        return;
    }
    // Domain-contract validation BEFORE value semantics: an unsupported
    // element stride is a caller bug and outranks the value's null/type
    // checks (matching the tests' NotSupportedException expectation).
    if (a->elemSize != 1 && a->elemSize != sizeof(int32_t) &&
        a->elemSize != sizeof(int64_t))
    {
        il2cpp_throw_not_supported_exception(
            "SetValue supports only byte, int32 and int64 one-dimensional arrays.");
    }
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
    }
    switch (a->elemSize)
    {
        case 1:
        {
            *reinterpret_cast<uint8_t*>(a->data + index) =
                static_cast<uint8_t>(runtime::icall_unbox_byte(value));
            return;
        }
        case sizeof(int32_t):
        {
            *reinterpret_cast<int32_t*>(
                a->data + static_cast<size_t>(index) * sizeof(int32_t)) =
                runtime::icall_unbox_int32(value);
            return;
        }
        case sizeof(int64_t):
        {
            *reinterpret_cast<int64_t*>(
                a->data + static_cast<size_t>(index) * sizeof(int64_t)) =
                runtime::icall_unbox_long(value);
            return;
        }
        default:
            il2cpp_throw_not_supported_exception(
                "SetValue supports only byte, int32 and int64 one-dimensional arrays.");
    }
}

} // namespace il2cpp
