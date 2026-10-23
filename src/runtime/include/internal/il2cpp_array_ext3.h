#pragma once
//------------------------------------------------------------------------------
// il2cpp_array_ext3.h -- System.Array GENERIC FAMILY surface (element-kind
// unified cell ABI).
//
// The first two arrayext domains wired the PREDICATE/CONVERTER family
// (Array.Find/FindIndex/FindAll/Exists/TrueForAll/ConvertAll/Resize) for the
// int32 element type ONLY (il2cpp_array_ext2.h). net48 declares every one of
// those methods for an ARBITRARY T, so the int32-only surface is a mapped
// hole, not a language limit. This domain closes it with ONE unified cell ABI
// instead of an N x M icall explosion: every entry takes an ELEMENT FAMILY
// code (ArrElemFamily) plus the family's stride/typing, reads the element
// through the array's real cell, and invokes the predicate/converter through
// the matching managed delegate ABI.
//
// Families (the transpiler passes the code as a literal):
//   kArrFamI4       int32/uint32/char/bool/byte/sbyte/short/ushort
//                   (cell width from the array stride: 1/2/4; signed narrow
//                   kinds sign-extend into the int32_t callback slot)
//   kArrFamI8       int64/uint64          (int64_t callback slot)
//   kArrFamD        double/single         (double callback slot; a 4-byte
//                                          single cell widens exactly like
//                                          il2cpp_array_get_elem_float_as_double)
//   kArrFamRef      string/object/class   (Il2CppObject* slot)
//   kArrFamDateTime System.DateTime       (Il2CppDateTime by value)
//   kArrFamTimeSpan System.TimeSpan       (Il2CppTimeSpan by value)
//   kArrFamGuid     System.Guid           (Il2CppGuid by value)
//   kArrFamDecimal  System.Decimal        (Il2CppDecimal by value)
//   kArrFamStruct   user value type       (T by value -- TEMPLATE entries,
//                                          see the *_struct_t helpers below)
//
// Delegate ABI: the same single-cast methodPtr contract as il2cpp_array_ext2.h
// (a target-bound leaf receives its target in the `this` slot). The predicate
// returns bool in the int32_t carrier (the generated method's C++ return is
// int32_t). Validation order is net48's: array null / startIndex / count /
// callback null (ArgumentException/ArgumentOutOfRange/ArgumentNull).
//
// GC contract: the callback may allocate, so the array, the delegate and any
// output array are static-rooted across the loop and re-read through the
// rooted slots after every callback (arrayext2.cpp RootScopeExt2 precedent).
//
// Exceptions (managed, through il2cpp_throw_*):
//   null array                 -> ArgumentNullException("array")
//   startIndex/count out       -> ArgumentOutOfRangeException
//   null callback              -> ArgumentNullException(paramName)
//   multicast callback         -> InvalidOperationException
//   non-delegate callback      -> ArgumentException
//   unsupported cell family    -> NotSupportedException (defensive)
//------------------------------------------------------------------------------

#include "decimal.h"
#include "guid.h"
#include "il2cpp_array.h"
#include "il2cpp_delegate.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"
#include "il2cpp_time.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// Element-family codes (ArrElemFamily). The numeric values are part of the
// transpiler contract (MethodMap.Array.cs emits them as literals).
enum ArrElemFamily : int32_t
{
    kArrFamI4 = 1,
    kArrFamI8 = 2,
    kArrFamD = 3,
    kArrFamRef = 4,
    kArrFamDateTime = 5,
    kArrFamTimeSpan = 6,
    kArrFamGuid = 7,
    kArrFamDecimal = 8,
    kArrFamStruct = 9
};

//------------------------------------------------------------------------------
// Array.FindIndex<T> family. signExtend applies to kArrFamI4 cells narrower
// than 4 bytes (sbyte/int16 sign-extend, byte/ushort/char/bool zero-extend).
//------------------------------------------------------------------------------
int32_t il2cpp_array_find_index_cell(Il2CppArray* arr, Il2CppObject* predicate,
                                     int32_t startIndex, int32_t count,
                                     int32_t family, int32_t signExtend);
int32_t il2cpp_array_find_index_cell_2(Il2CppArray* arr, Il2CppObject* predicate,
                                       int32_t family, int32_t signExtend);
int32_t il2cpp_array_find_index_cell_3(Il2CppArray* arr, Il2CppObject* predicate,
                                       int32_t startIndex,
                                       int32_t family, int32_t signExtend);

// Array.Find<T>: the VALUE of the first match, default(T) when none. One
// return-typed entry per family (the C++ return type is the family's).
int32_t il2cpp_array_find_cell_i4(Il2CppArray* arr, Il2CppObject* match,
                                  int32_t family, int32_t signExtend);
int64_t il2cpp_array_find_cell_i8(Il2CppArray* arr, Il2CppObject* match);
double il2cpp_array_find_cell_d(Il2CppArray* arr, Il2CppObject* match);
Il2CppObject* il2cpp_array_find_cell_ref(Il2CppArray* arr, Il2CppObject* match);
Il2CppDateTime il2cpp_array_find_cell_dt(Il2CppArray* arr, Il2CppObject* match);
Il2CppTimeSpan il2cpp_array_find_cell_ts(Il2CppArray* arr, Il2CppObject* match);
Il2CppGuid il2cpp_array_find_cell_guid(Il2CppArray* arr, Il2CppObject* match);
Il2CppDecimal il2cpp_array_find_cell_dec(Il2CppArray* arr, Il2CppObject* match);

// Array.FindAll<T>: a NEW T[] with every match (exactly one predicate call per
// element). outIsRef/outTypedKind describe the result array the transpiler
// expects (stride + element kind recorded at allocation).
Il2CppArray* il2cpp_array_find_all_cell(Il2CppArray* arr, Il2CppObject* match,
                                        int32_t family, int32_t signExtend);

// Array.Exists<T> / Array.TrueForAll<T> (1/0).
int32_t il2cpp_array_exists_cell(Il2CppArray* arr, Il2CppObject* match,
                                 int32_t family, int32_t signExtend);
int32_t il2cpp_array_true_for_all_cell(Il2CppArray* arr, Il2CppObject* match,
                                       int32_t family, int32_t signExtend);

// Array.Resize<T>(ref T[] array, int newSize): 'stride'/'isRef'/'typedKind'
// describe the T[] the caller's static type expects (the runtime cannot infer
// a value element's stride from the old array alone -- a null array has none).
void il2cpp_array_resize_cell(Il2CppArray** arraySlot, int32_t newSize,
                              int32_t stride, int32_t isRef, int32_t typedKind,
                              int32_t family, int32_t signExtend);

//------------------------------------------------------------------------------
// Array.ConvertAll<TInput,TOutput>: a NEW TOutput[] of the same length. The
// source element rides 'inFamily', the result is written as 'outFamily' with
// 'outStride'/'outIsRef'/'outTypedKind' describing the destination array.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_array_convert_all_cell(Il2CppArray* arr, Il2CppObject* converter,
                                           int32_t inFamily, int32_t inSignExtend,
                                           int32_t outFamily, int32_t outSignExtend,
                                           int32_t outStride, int32_t outIsRef,
                                           int32_t outTypedKind);

//------------------------------------------------------------------------------
// USER VALUE TYPE family (kArrFamStruct): the callback takes T by value, so
// the invocation must be instantiated at the call site (the runtime cannot
// name a user struct). The transpiler emits `..._struct_t<{Cpp}>`.
//
// The cell is copied out of the array as a raw T (memcpy of sizeof(T)), so a
// user struct element array must be allocated with stride == sizeof(T) by the
// transpiler's newarr (it is: ArrayElementType/user-struct arm).
//------------------------------------------------------------------------------
template <typename T>
int32_t il2cpp_array_find_index_struct_t(Il2CppArray* arr, Il2CppObject* predicate);
template <typename T>
int32_t il2cpp_array_find_index_struct_t_2(Il2CppArray* arr, Il2CppObject* predicate);
template <typename T>
int32_t il2cpp_array_find_index_struct_t_3(Il2CppArray* arr, Il2CppObject* predicate,
                                           int32_t startIndex);
template <typename T>
T il2cpp_array_find_struct_t(Il2CppArray* arr, Il2CppObject* match);
template <typename T>
Il2CppArray* il2cpp_array_find_all_struct_t(Il2CppArray* arr, Il2CppObject* match);
template <typename T>
int32_t il2cpp_array_exists_struct_t(Il2CppArray* arr, Il2CppObject* match);
template <typename T>
int32_t il2cpp_array_true_for_all_struct_t(Il2CppArray* arr, Il2CppObject* match);
template <typename T>
Il2CppArray* il2cpp_array_convert_all_struct_t(Il2CppArray* arr, Il2CppObject* converter,
                                               int32_t outFamily, int32_t outSignExtend,
                                               int32_t outStride, int32_t outIsRef,
                                               int32_t outTypedKind);

} // namespace runtime
} // namespace il2cpp
