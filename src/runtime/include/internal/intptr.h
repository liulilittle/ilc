#pragma once
//------------------------------------------------------------------------------
// intptr.h -- System.IntPtr / System.UIntPtr pointer-arithmetic runtime support.
//
// The transpiler maps IntPtr/UIntPtr arithmetic and conversions to these
// runtime helpers. A pointer value is carried as an intptr_t -- a SIGNED
// integer of the native pointer width: 64 bits on 64-bit targets, 32 bits
// on 32-bit targets. All operations below are pure integer math on the
// address bit pattern; nothing is dereferenced, so no memory access is
// involved (the value may be any bit pattern, even an invalid address).
//
// Naming follows the flat runtime-helper convention of il2cpp_math.h, with
// the user-specified il2cpp_intptr_ prefix:
//   il2cpp_intptr_add         IntPtr.Add(IntPtr, int)
//   il2cpp_intptr_subtract    IntPtr.Subtract(IntPtr, int)
//   il2cpp_intptr_to_int64    IntPtr.ToInt64()
//   il2cpp_intptr_to_int32    IntPtr.ToInt32()
//   il2cpp_intptr_op_equality IntPtr.op_Equality(IntPtr, IntPtr)
//   il2cpp_intptr_op_inequality IntPtr.op_Inequality(IntPtr, IntPtr)
//   il2cpp_intptr_compare     IntPtr.CompareTo(IntPtr)
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- the project's reference
// baseline for behaviour:
//   * Add/Subtract add the int32 offset to the pointer. The offset is a
//     SIGNED int32: it is sign-extended to the pointer width before the
//     addition, so Add(p, int.MinValue) moves p DOWN by 0x80000000 (see
//     icall note below -- this is exactly how C# widens int to IntPtr).
//   * ToInt64 sign-extends the pointer value (CLI III.3.4: pointer<->integer
//     conversions are sign-preserving). On 64-bit targets it is the
//     identity; on 32-bit targets a value with the top bit set becomes a
//     negative int64.
//   * ToInt32 is a CHECKED conversion: net48's IntPtr.ToInt32() executes
//     `(int)m_value` in a checked context, so a pointer value outside
//     [INT32_MIN, INT32_MAX] throws System.OverflowException instead of
//     truncating (probe-verified against csc net48 on 2026-08-09). On
//     32-bit targets intptr_t is int32 and the range check never fires.
//   * op_Equality compares the raw bit patterns.
//   * CompareTo orders pointers as UNSIGNED addresses (C# void* relational
//     operators and IntPtr.CompareTo both use the unsigned address value):
//     returns -1 / 0 / 1.
//
// UIntPtr reuse: the same helpers serve UIntPtr values -- Add/Subtract/
// op_Equality/CompareTo behave identically on the bit pattern, and the
// bit pattern of a UIntPtr passes through an intptr_t parameter unchanged.
// (UIntPtr has no ToInt64/ToInt32; its unsigned conversions are plain
// zero-extension casts that the transpiler emits directly.)
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp { namespace runtime {

// IntPtr(int32) ctor: the int32 value IS the pointer carrier (sign-extended
// to the pointer width -- C# `new IntPtr(1)` stores value 1).
intptr_t il2cpp_intptr_ctor(intptr_t value);
int32_t il2cpp_intptr_get_size(void);

// IntPtr.Add(IntPtr, int): pointer + sign-extended int32 offset.
intptr_t il2cpp_intptr_add(intptr_t pointer, int32_t offset);

// IntPtr.Subtract(IntPtr, int): pointer - sign-extended int32 offset.
intptr_t il2cpp_intptr_subtract(intptr_t pointer, int32_t offset);

// IntPtr.ToInt64(): pointer value sign-extended to int64 (identity on
// 64-bit targets).
int64_t il2cpp_intptr_to_int64(intptr_t value);

// IntPtr.ToInt32(): CHECKED conversion to int32 -- a pointer value outside
// [INT32_MIN, INT32_MAX] throws System.OverflowException (net48 semantics:
// `(int)m_value` runs in a checked context), never truncates. On 32-bit
// targets the range check never fires.
int32_t il2cpp_intptr_to_int32(intptr_t value);

// IntPtr.op_Equality(IntPtr, IntPtr): bit-pattern equality.
bool il2cpp_intptr_op_equality(intptr_t a, intptr_t b);

// IntPtr.op_Inequality(IntPtr, IntPtr): negation of the bit-pattern
// equality (`a != b` iff `!(a == b)`, net48 semantics).
bool il2cpp_intptr_op_inequality(intptr_t a, intptr_t b);

// IntPtr.CompareTo(IntPtr): -1 / 0 / 1, ordering pointers as unsigned
// addresses (a value with the top bit set sorts HIGHER than any positive
// value).
int32_t il2cpp_intptr_compare(intptr_t a, intptr_t b);

}} // namespace il2cpp::runtime
