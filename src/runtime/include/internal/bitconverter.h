#pragma once
//------------------------------------------------------------------------------
// bitconverter.h -- System.BitConverter static-method runtime support.
//
// The transpiler maps System.BitConverter calls to these runtime helpers.
// Like the math domain, BitConverter members have no IL body of their own:
// they are [MethodImplOptions.InternalCall]-style intrinsics delegating to
// byte-layout logic in C++.
//
// Naming: il2cpp_bitconverter_<operation>_<type-suffix> with the type suffix
// spelled out (bool/int16/int32/int64/uint16/uint32/uint64/single/double/
// char) so the transpiler can resolve overloads unambiguously. The bit-bridge
// helpers are named after the .NET static methods they back:
// DoubleToInt64Bits / Int64BitsToDouble / SingleToInt32Bits /
// Int32BitsToSingle.
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) on a LITTLE-ENDIAN platform
// (Windows x86-64 -- the project's only supported runtime target):
//   * GetBytes(T) returns a NEW byte[] whose elements hold the value's
//     native representation in little-endian order (LSB at index 0).
//   * ToT(byte[]) reads the first sizeof(T) bytes in little-endian order;
//     null -> ArgumentNullException, fewer than sizeof(T) bytes ->
//     ArgumentException (net48 throws for value.Length < 2 with the same
//     exception type). This simplified form has no startIndex parameter;
//     the offset-free .NET overloads GetBytes are the exact counterpart.
//   * The bits bridges (DoubleToInt64Bits etc.) are PURE bit-pattern
//     reinterpretations: no rounding, no overflow, no endianness concern
//     (they operate on the host representation, exactly like .NET).
//   * BitConverter.IsLittleEndian is true on this platform; the unit test
//     asserts it with a native probe before trusting any byte layout.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_string.h"
#include "il2cpp_exception_ext.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// BitConverter.GetBytes(bool): 1 byte (0x00 / 0x01), length 1.
Il2CppArray* il2cpp_bitconverter_get_bytes_bool(bool value);

// BitConverter.GetBytes(char): 2 bytes, little-endian UTF-16 code unit.
Il2CppArray* il2cpp_bitconverter_get_bytes_char(char16_t value);

// BitConverter.GetBytes(int16): 2 bytes, little-endian.
Il2CppArray* il2cpp_bitconverter_get_bytes_int16(int16_t value);

// BitConverter.GetBytes(uint16): 2 bytes, little-endian.
Il2CppArray* il2cpp_bitconverter_get_bytes_uint16(uint16_t value);

// BitConverter.GetBytes(int32): 4 bytes, little-endian.
Il2CppArray* il2cpp_bitconverter_get_bytes_int32(int32_t value);

// BitConverter.GetBytes(uint32): 4 bytes, little-endian.
Il2CppArray* il2cpp_bitconverter_get_bytes_uint32(uint32_t value);

// BitConverter.GetBytes(int64): 8 bytes, little-endian.
Il2CppArray* il2cpp_bitconverter_get_bytes_int64(int64_t value);

// BitConverter.GetBytes(uint64): 8 bytes, little-endian.
Il2CppArray* il2cpp_bitconverter_get_bytes_uint64(uint64_t value);

// BitConverter.GetBytes(float): 4 bytes, IEEE-754 single, little-endian.
Il2CppArray* il2cpp_bitconverter_get_bytes_single(float value);

// BitConverter.GetBytes(double): 8 bytes, IEEE-754 double, little-endian.
Il2CppArray* il2cpp_bitconverter_get_bytes_double(double value);

// BitConverter.ToBoolean(byte[]): bytes[0] != 0; null -> ArgumentNullException,
// empty array -> ArgumentException.
bool il2cpp_bitconverter_to_bool(Il2CppArray* value);

// BitConverter.ToChar(byte[]): little-endian char16_t from the first 2 bytes;
// null -> ArgumentNullException, length < 2 -> ArgumentException.
char16_t il2cpp_bitconverter_to_char(Il2CppArray* value);

// BitConverter.ToInt16(byte[]): little-endian int16 from the first 2 bytes;
// null -> ArgumentNullException, length < 2 -> ArgumentException.
int16_t il2cpp_bitconverter_to_int16(Il2CppArray* value);

// BitConverter.ToUInt16(byte[]): little-endian uint16 from the first 2 bytes;
// null -> ArgumentNullException, length < 2 -> ArgumentException.
uint16_t il2cpp_bitconverter_to_uint16(Il2CppArray* value);

// BitConverter.ToInt32(byte[]): little-endian int32 from the first 4 bytes;
// null -> ArgumentNullException, length < 4 -> ArgumentException.
int32_t il2cpp_bitconverter_to_int32(Il2CppArray* value);

// BitConverter.ToUInt32(byte[]): little-endian uint32 from the first 4 bytes;
// null -> ArgumentNullException, length < 4 -> ArgumentException.
uint32_t il2cpp_bitconverter_to_uint32(Il2CppArray* value);

// BitConverter.ToInt64(byte[]): little-endian int64 from the first 8 bytes;
// null -> ArgumentNullException, length < 8 -> ArgumentException.
int64_t il2cpp_bitconverter_to_int64(Il2CppArray* value);

// BitConverter.ToUInt64(byte[]): little-endian uint64 from the first 8 bytes;
// null -> ArgumentNullException, length < 8 -> ArgumentException.
uint64_t il2cpp_bitconverter_to_uint64(Il2CppArray* value);

// BitConverter.ToSingle(byte[]): IEEE-754 single from the first 4 bytes;
// null -> ArgumentNullException, length < 4 -> ArgumentException.
float il2cpp_bitconverter_to_single(Il2CppArray* value);

// BitConverter.ToDouble(byte[]): IEEE-754 double from the first 8 bytes;
// null -> ArgumentNullException, length < 8 -> ArgumentException.
double il2cpp_bitconverter_to_double(Il2CppArray* value);

// BitConverter.ToString(byte[]): uppercase two-digit hex pairs separated by
// '-' ("00-FF-80-12-AB"), the net48 canonical form; empty array -> ""; null
// -> ArgumentNullException ("value"). Returns a managed string.
Il2CppString* il2cpp_bitconverter_to_hex_string(Il2CppArray* bytes);

// BitConverter.DoubleToInt64Bits(double): bit-for-bit reinterpretation of a
// double as int64 (memcpy semantics -- no UB, preserves NaN payloads).
int64_t il2cpp_bitconverter_double_to_int64_bits(double value);

// BitConverter.Int64BitsToDouble(int64): bit-for-bit reinterpretation of an
// int64 as double (memcpy semantics).
double il2cpp_bitconverter_int64_bits_to_double(int64_t value);

// BitConverter.SingleToInt32Bits(float): bit-for-bit reinterpretation of a
// float as int32 (memcpy semantics; 1.0f -> 0x3F800000).
int32_t il2cpp_bitconverter_single_to_int32_bits(float value);

// BitConverter.Int32BitsToSingle(int32): bit-for-bit reinterpretation of an
// int32 as float (memcpy semantics; 0x3F800000 -> 1.0f).
float il2cpp_bitconverter_int32_bits_to_single(int32_t value);

}} // namespace il2cpp::runtime
