#include "bitconverter.h"
#include "il2cpp_exception_ext.h"

#include <cstring>
#include <vector>

// Compile-time host byte-order guard (G12 contract, the encoding.cpp pattern):
// every GetBytes/ToT byte layout below is the little-endian representation.
// GCC/Clang expose __BYTE_ORDER__ (no std::endian in C++17 -- <bit> is
// C++20), so a big-endian GCC/Clang host fails the build below; MSVC does not
// define the macro but every supported MSVC target (x86/x64/ARM64) is
// little-endian, so the guard is a no-op there.
#if defined(__BYTE_ORDER__) && (__BYTE_ORDER__ == __ORDER_BIG_ENDIAN__)
#error "BitConverter byte layout is little-endian and requires a little-endian host (G12 contract)"
#endif

namespace il2cpp { namespace runtime {

namespace {

//------------------------------------------------------------------------------
// Shared little-endian byte-layout helpers.
//
// The target platform is Windows x86-64 (little-endian), so copying a value
// into a byte buffer with memcpy yields the .NET little-endian layout
// directly (LSB at index 0) -- no manual byte shuffling required. The
// BitConverter.IsLittleEndian contract is asserted at test time with a
// native probe; a big-endian host would need explicit byte reversal here.
//------------------------------------------------------------------------------

// GetBytes<T>: a new managed byte[] of sizeof(T) holding the value's native
// (little-endian) representation. Zero-initialized by il2cpp_array_new; the
// memcpy overwrites every payload byte.
template <typename T>
Il2CppArray* GetBytes(T value)
{
    Il2CppArray* arr = il2cpp_array_new(nullptr, 1, false,
                                        static_cast<int32_t>(sizeof(T)));
    std::memcpy(arr->data, &value, sizeof(T));
    return arr;
}

// ToValue<T>: the first sizeof(T) bytes of the array, little-endian. .NET
// validation (net48 and .NET Core agree, probe-verified): null ->
// ArgumentNullException; an EMPTY array -> ArgumentOutOfRangeException
// (startIndex 0 >= length 0); a non-empty array shorter than sizeof(T) ->
// ArgumentException. All three throw unconditionally on the failure path,
// so the success path below is the only one that executes past the checks.
template <typename T>
T ToValue(Il2CppArray* value, const char* paramName, const char* message)
{
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception(paramName);
    }
    if (value->length == 0)
    {
        il2cpp_throw_argument_out_of_range_exception("startIndex", nullptr);
    }
    if (value->length < static_cast<int32_t>(sizeof(T)))
    {
        il2cpp_throw_argument_exception(message);
    }
    T result = T{};
    std::memcpy(&result, value->data, sizeof(T));
    return result;
}

// ToValueOffset<T>: net48 ToT(byte[] value, int startIndex) -- reads sizeof(T)
// bytes little-endian starting at startIndex. Validation (net48):
// null value -> ArgumentNullException("value"); startIndex outside
// [0, length - sizeof(T)] -> ArgumentOutOfRangeException("startIndex")
// (which derives from ArgumentException, so callers catching ArgumentException
// observe the same behavior). D-A/D-B fix (2026-09-19): the startIndex was
// previously DROPPED by the mapper and the runtime read from index 0.
template <typename T>
T ToValueOffset(Il2CppArray* value, int32_t startIndex, const char* message)
{
    (void)message; // net48 uses type-specific messages only for the short-array case
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
    }
    if (startIndex < 0 || startIndex > value->length - static_cast<int32_t>(sizeof(T)))
    {
        il2cpp_throw_argument_out_of_range_exception("startIndex", nullptr);
    }
    T result = T{};
    std::memcpy(&result, reinterpret_cast<const unsigned char*>(value->data) + startIndex,
                sizeof(T));
    return result;
}

// BitBridge<From, To>: pure bit-pattern reinterpretation (memcpy). This is
// endianness-agnostic by construction: it moves the host representation
// unchanged, exactly like .NET's DoubleToInt64Bits family.
template <typename From, typename To>
To BitBridge(From value)
{
    static_assert(sizeof(From) == sizeof(To), "bit bridge requires equal widths");
    To result;
    std::memcpy(&result, &value, sizeof(To));
    return result;
}

constexpr const char* kNeed2Bytes = "the byte array must contain at least 2 bytes.";
constexpr const char* kNeed4Bytes = "the byte array must contain at least 4 bytes.";
constexpr const char* kNeed8Bytes = "the byte array must contain at least 8 bytes.";

} // namespace

//------------------------------------------------------------------------------
// BitConverter.GetBytes family: each returns a NEW byte[].
//------------------------------------------------------------------------------

Il2CppArray* il2cpp_bitconverter_get_bytes_bool(bool value)
{
    return GetBytes(value);
}

Il2CppArray* il2cpp_bitconverter_get_bytes_char(char16_t value)
{
    return GetBytes(value);
}

Il2CppArray* il2cpp_bitconverter_get_bytes_int16(int16_t value)
{
    return GetBytes(value);
}

Il2CppArray* il2cpp_bitconverter_get_bytes_uint16(uint16_t value)
{
    return GetBytes(value);
}

Il2CppArray* il2cpp_bitconverter_get_bytes_int32(int32_t value)
{
    return GetBytes(value);
}

Il2CppArray* il2cpp_bitconverter_get_bytes_uint32(uint32_t value)
{
    return GetBytes(value);
}

Il2CppArray* il2cpp_bitconverter_get_bytes_int64(int64_t value)
{
    return GetBytes(value);
}

Il2CppArray* il2cpp_bitconverter_get_bytes_uint64(uint64_t value)
{
    return GetBytes(value);
}

Il2CppArray* il2cpp_bitconverter_get_bytes_single(float value)
{
    return GetBytes(value);
}

Il2CppArray* il2cpp_bitconverter_get_bytes_double(double value)
{
    return GetBytes(value);
}

//------------------------------------------------------------------------------
// BitConverter.ToT family: read the first sizeof(T) bytes (little-endian).
//------------------------------------------------------------------------------

bool il2cpp_bitconverter_to_bool(Il2CppArray* value)
{
    return ToValue<uint8_t>(value, "value", "the byte array must contain at least 1 byte.") != 0;
}

char16_t il2cpp_bitconverter_to_char(Il2CppArray* value)
{
    return ToValue<char16_t>(value, "value", kNeed2Bytes);
}

int16_t il2cpp_bitconverter_to_int16(Il2CppArray* value)
{
    return ToValue<int16_t>(value, "value", kNeed2Bytes);
}

uint16_t il2cpp_bitconverter_to_uint16(Il2CppArray* value)
{
    return ToValue<uint16_t>(value, "value", kNeed2Bytes);
}

int32_t il2cpp_bitconverter_to_int32(Il2CppArray* value)
{
    return ToValue<int32_t>(value, "value", kNeed4Bytes);
}

uint32_t il2cpp_bitconverter_to_uint32(Il2CppArray* value)
{
    return ToValue<uint32_t>(value, "value", kNeed4Bytes);
}

int64_t il2cpp_bitconverter_to_int64(Il2CppArray* value)
{
    return ToValue<int64_t>(value, "value", kNeed8Bytes);
}

uint64_t il2cpp_bitconverter_to_uint64(Il2CppArray* value)
{
    return ToValue<uint64_t>(value, "value", kNeed8Bytes);
}

float il2cpp_bitconverter_to_single(Il2CppArray* value)
{
    return ToValue<float>(value, "value", kNeed4Bytes);
}

double il2cpp_bitconverter_to_double(Il2CppArray* value)
{
    return ToValue<double>(value, "value", kNeed8Bytes);
}

// ToT(byte[], int startIndex) -- the offset forms (D-A/D-B fix, 2026-09-19).
// bool reads ONE byte as uint8_t then normalizes (never memcpy an arbitrary
// byte into a C++ bool: any value other than 0/1 is UB, and clang folds it
// to False -- net48 is non-zero -> True, so {42} must read True).
bool il2cpp_bitconverter_to_bool_offset(Il2CppArray* value, int32_t startIndex)
{
    return ToValueOffset<uint8_t>(value, startIndex, kNeed2Bytes) != 0;
}

char16_t il2cpp_bitconverter_to_char_offset(Il2CppArray* value, int32_t startIndex)
{
    return ToValueOffset<char16_t>(value, startIndex, kNeed2Bytes);
}

int16_t il2cpp_bitconverter_to_int16_offset(Il2CppArray* value, int32_t startIndex)
{
    return ToValueOffset<int16_t>(value, startIndex, kNeed2Bytes);
}

uint16_t il2cpp_bitconverter_to_uint16_offset(Il2CppArray* value, int32_t startIndex)
{
    return ToValueOffset<uint16_t>(value, startIndex, kNeed2Bytes);
}

int32_t il2cpp_bitconverter_to_int32_offset(Il2CppArray* value, int32_t startIndex)
{
    return ToValueOffset<int32_t>(value, startIndex, kNeed4Bytes);
}

uint32_t il2cpp_bitconverter_to_uint32_offset(Il2CppArray* value, int32_t startIndex)
{
    return ToValueOffset<uint32_t>(value, startIndex, kNeed4Bytes);
}

int64_t il2cpp_bitconverter_to_int64_offset(Il2CppArray* value, int32_t startIndex)
{
    return ToValueOffset<int64_t>(value, startIndex, kNeed8Bytes);
}

uint64_t il2cpp_bitconverter_to_uint64_offset(Il2CppArray* value, int32_t startIndex)
{
    return ToValueOffset<uint64_t>(value, startIndex, kNeed8Bytes);
}

float il2cpp_bitconverter_to_single_offset(Il2CppArray* value, int32_t startIndex)
{
    return ToValueOffset<float>(value, startIndex, kNeed4Bytes);
}

double il2cpp_bitconverter_to_double_offset(Il2CppArray* value, int32_t startIndex)
{
    return ToValueOffset<double>(value, startIndex, kNeed8Bytes);
}

//------------------------------------------------------------------------------
// BitConverter.ToString(byte[]): the net48 canonical hex formatter --
// uppercase two-digit pairs, '-' between bytes ("00-FF-80"). The output is
// 3*length - 1 UTF-16 units; built in ONE scratch pass and finished by a
// single il2cpp_string_new (the concat.cpp contract). Null -> the ToT null
// path (ArgumentNullException, paramName "value"); empty array -> "" (the
// net48 ToString(byte[]) has no startIndex, so the empty case is legal --
// probe-verified against csc.exe net48 on 2026-08-05).
//------------------------------------------------------------------------------

Il2CppString* il2cpp_bitconverter_to_hex_string(Il2CppArray* bytes)
{
    if (bytes == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
    }
    const int32_t n = bytes->length;
    if (n == 0)
    {
        return il2cpp_string_new(nullptr, 0);
    }
    static constexpr char16_t kHexDigits[] = u"0123456789ABCDEF";
    std::vector<char16_t> buf(static_cast<size_t>(n) * 3u - 1u);
    int32_t o = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        if (i > 0)
        {
            buf[static_cast<size_t>(o++)] = u'-';
        }
        const uint8_t b = static_cast<uint8_t>(bytes->data[i]);
        buf[static_cast<size_t>(o++)] = kHexDigits[b >> 4];
        buf[static_cast<size_t>(o++)] = kHexDigits[b & 0x0F];
    }
    return il2cpp_string_new(buf.data(), o);
}

//------------------------------------------------------------------------------
// BitConverter bit bridges: pure bit-pattern reinterpretations.
//------------------------------------------------------------------------------

int64_t il2cpp_bitconverter_double_to_int64_bits(double value)
{
    return BitBridge<double, int64_t>(value);
}

double il2cpp_bitconverter_int64_bits_to_double(int64_t value)
{
    return BitBridge<int64_t, double>(value);
}

int32_t il2cpp_bitconverter_single_to_int32_bits(float value)
{
    return BitBridge<float, int32_t>(value);
}

float il2cpp_bitconverter_int32_bits_to_single(int32_t value)
{
    return BitBridge<int32_t, float>(value);
}

}} // namespace il2cpp::runtime
