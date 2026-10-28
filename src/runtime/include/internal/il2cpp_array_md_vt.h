#pragma once
//------------------------------------------------------------------------------
// il2cpp_array_md_vt.h -- VALUE-ELEMENT multidim array accessors (b5-gaps 13).
//
// The allocation layer (il2cpp_array_new_multi) always carried the element
// stride; what the multidim surface lacked is the width/kind-correct element
// ACCESS for the value families (the i4 accessors in il2cpp_array.h read 4
// bytes of every cell). The families here mirror the SZARRAY token-accessor
// conventions:
//   * narrow cells (u1/i1/u2/i2/u4) widen to the int32 carrier on read and
//     narrow the int32 value on write (the enum int32 carrier model);
//   * i8/f4/f8 keep their native cell width;
//   * DateTime/TimeSpan cells are the 8-byte packed image (the kind lives in
//     the top bits -- il2cpp_datetime_from/to_array_bits);
//   * Guid/Decimal/DateTimeOffset cells move the full 16-byte runtime value
//     struct.
// Every accessor is per-dimension bounds checked (MultidimFlatOffset in
// array.cpp: a null array throws NullReferenceException, an out-of-range
// index throws IndexOutOfRangeException -- both catchable, .NET semantics).
//
// The declarations live OUTSIDE il2cpp_array.h because guid.h includes
// il2cpp_array.h (a direct include here would re-enter the guard before
// Il2CppGuid is defined); this header is included after both.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"

#include "decimal.h"
#include "guid.h"
#include "il2cpp_dtoffset.h"
#include "il2cpp_time.h"

namespace il2cpp {

// -- narrow / 8-byte / float cells -----------------------------------------
int32_t il2cpp_array_get_u1_2d(Il2CppArray* arr, int32_t i, int32_t j);
int32_t il2cpp_array_get_i1_2d(Il2CppArray* arr, int32_t i, int32_t j);
int32_t il2cpp_array_get_u2_2d(Il2CppArray* arr, int32_t i, int32_t j);
int32_t il2cpp_array_get_i2_2d(Il2CppArray* arr, int32_t i, int32_t j);
int32_t il2cpp_array_get_u4_2d(Il2CppArray* arr, int32_t i, int32_t j);
int64_t il2cpp_array_get_i8_2d(Il2CppArray* arr, int32_t i, int32_t j);
float   il2cpp_array_get_f4_2d(Il2CppArray* arr, int32_t i, int32_t j);
double  il2cpp_array_get_f8_2d(Il2CppArray* arr, int32_t i, int32_t j);
void    il2cpp_array_set_u1_2d(Il2CppArray* arr, int32_t i, int32_t j, int32_t value);
void    il2cpp_array_set_i1_2d(Il2CppArray* arr, int32_t i, int32_t j, int32_t value);
void    il2cpp_array_set_u2_2d(Il2CppArray* arr, int32_t i, int32_t j, int32_t value);
void    il2cpp_array_set_i2_2d(Il2CppArray* arr, int32_t i, int32_t j, int32_t value);
void    il2cpp_array_set_u4_2d(Il2CppArray* arr, int32_t i, int32_t j, int32_t value);
void    il2cpp_array_set_i8_2d(Il2CppArray* arr, int32_t i, int32_t j, int64_t value);
void    il2cpp_array_set_f4_2d(Il2CppArray* arr, int32_t i, int32_t j, float value);
void    il2cpp_array_set_f8_2d(Il2CppArray* arr, int32_t i, int32_t j, double value);
int32_t il2cpp_array_get_u1_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
int32_t il2cpp_array_get_i1_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
int32_t il2cpp_array_get_u2_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
int32_t il2cpp_array_get_i2_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
int32_t il2cpp_array_get_u4_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
int64_t il2cpp_array_get_i8_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
float   il2cpp_array_get_f4_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
double  il2cpp_array_get_f8_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
void    il2cpp_array_set_u1_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, int32_t value);
void    il2cpp_array_set_i1_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, int32_t value);
void    il2cpp_array_set_u2_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, int32_t value);
void    il2cpp_array_set_i2_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, int32_t value);
void    il2cpp_array_set_u4_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, int32_t value);
void    il2cpp_array_set_i8_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, int64_t value);
void    il2cpp_array_set_f4_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, float value);
void    il2cpp_array_set_f8_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, double value);

// -- the 8-byte packed-image families ---------------------------------------
runtime::Il2CppDateTime il2cpp_array_get_dt_2d(Il2CppArray* arr, int32_t i, int32_t j);
runtime::Il2CppTimeSpan  il2cpp_array_get_ts_2d(Il2CppArray* arr, int32_t i, int32_t j);
void il2cpp_array_set_dt_2d(Il2CppArray* arr, int32_t i, int32_t j, runtime::Il2CppDateTime value);
void il2cpp_array_set_ts_2d(Il2CppArray* arr, int32_t i, int32_t j, runtime::Il2CppTimeSpan value);
runtime::Il2CppDateTime il2cpp_array_get_dt_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
runtime::Il2CppTimeSpan  il2cpp_array_get_ts_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
void il2cpp_array_set_dt_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, runtime::Il2CppDateTime value);
void il2cpp_array_set_ts_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, runtime::Il2CppTimeSpan value);

// -- the 16-byte value-struct families --------------------------------------
runtime::Il2CppDecimal      il2cpp_array_get_dec_2d(Il2CppArray* arr, int32_t i, int32_t j);
Il2CppGuid                il2cpp_array_get_guid_2d(Il2CppArray* arr, int32_t i, int32_t j);
runtime::Il2CppDateTimeOffset il2cpp_array_get_dto_2d(Il2CppArray* arr, int32_t i, int32_t j);
void il2cpp_array_set_dec_2d(Il2CppArray* arr, int32_t i, int32_t j, runtime::Il2CppDecimal value);
void il2cpp_array_set_guid_2d(Il2CppArray* arr, int32_t i, int32_t j, Il2CppGuid value);
void il2cpp_array_set_dto_2d(Il2CppArray* arr, int32_t i, int32_t j, runtime::Il2CppDateTimeOffset value);
runtime::Il2CppDecimal      il2cpp_array_get_dec_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
Il2CppGuid                il2cpp_array_get_guid_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
runtime::Il2CppDateTimeOffset il2cpp_array_get_dto_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
void il2cpp_array_set_dec_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, runtime::Il2CppDecimal value);
void il2cpp_array_set_guid_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, Il2CppGuid value);
void il2cpp_array_set_dto_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, runtime::Il2CppDateTimeOffset value);

// -- the element-ADDRESS form (the multidim ldelema) ------------------------
intptr_t il2cpp_array_md_elem_ptr_2d(Il2CppArray* arr, int32_t i, int32_t j);
intptr_t il2cpp_array_md_elem_ptr_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);

} // namespace il2cpp
