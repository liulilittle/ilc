#pragma once
//------------------------------------------------------------------------------
// arrayextra.h -- System.Array direct-call API, extra batch (transpiler-facing
// domain).
//
// This is the GENERATED-CODE-facing surface for the System.Array operations
// the BCL wiring needs beyond arraysort.h (Reverse / Clear / GetValue /
// SetValue): the MethodRefResolver / CodeGenerator layer calls these directly
// with typed values -- no Il2CppObject* icall indirection. The icall-layer
// equivalents (icall_array_reverse / icall_array_reverse_range /
// icall_array_clear_int32 in il2cpp_array_ext.h / il2cpp_array_ext2.h) remain
// the managed-semantics reference for validation order and exception mapping;
// this domain mirrors those contracts with typed signatures:
//
//   il2cpp_array_reverse_byte/int32/int64/string/object
//                              -- Array.Reverse(T[]): in-place two-pointer
//                                 reversal (value arrays swap raw elements;
//                                 reference arrays swap Il2CppObject* slots
//                                 with write barriers)
//   il2cpp_array_clear_byte/int32
//                              -- Array.Clear(T[], int, int): zero 'length'
//                                 elements starting at 'index'
//   il2cpp_array_get_value      -- Array.GetValue(int): returns the element
//                                 as a boxed object (value arrays) or the
//                                 slot itself (reference arrays)
//   il2cpp_array_set_value      -- Array.SetValue(object, int): unboxes the
//                                 object into a value array or stores the
//                                 reference (with a write barrier) into a
//                                 reference array
//
// Element-kind contract (array.cpp's M3 model):
//   - value arrays:  isRefElem == false, elemSize == 1 (byte), 4 (int32) or
//                    8 (int64) -- any other stride throws NotSupportedException
//   - reference arrays: isRefElem == true (elements are Il2CppObject* slots);
//                    reverse_string / reverse_object treat the slots as
//                    opaque references (no element read: reversal is
//                    comparison-free, so no string payload access is needed)
//
// GetValue/SetValue boxed-value contract (shared box layout, boxing.cpp):
//   - byte  elements box as System.Byte    (icall_box_byte / icall_unbox_byte)
//   - int32 elements box as System.Int32   (il2cpp_box_int32 / icall_unbox_int32)
//   - int64 elements box as System.Int64   (icall_box_long / icall_unbox_long)
//   SetValue unboxes through the same icalls, so a wrong boxed type is an
//   InvalidCastException and null on a value array is an ArgumentNullException
//   (matching .NET Array.SetValue); null on a reference array stores a null
//   slot (legal, matching .NET).
//
// Exception mapping (through the DotNetException carrier, same chains as the
// icall domains):
//   null array                        -> ArgumentNullException
//   non-array object                  -> ArgumentException (caller bug, never UB)
//   negative index / length           -> ArgumentOutOfRangeException
//   index + length beyond the array   -> ArgumentException
//   index beyond the array            -> IndexOutOfRangeException (Get/SetValue)
//   element-kind mismatch (Reverse/Clear) -> NotSupportedException
//   unsupported element stride (Get/SetValue) -> NotSupportedException
//   wrong boxed type (SetValue)       -> InvalidCastException
//   null value on a value array (SetValue) -> ArgumentNullException
//
// GC contract (reference-element moves): every written slot of a reference
// array goes through il2cpp_write_barrier; the operations allocate nothing,
// so no collection can interleave and no rooting is required.
//
// All entry points receive Il2CppArray* and validate the array layout
// internally (klass name '[' prefix, array.cpp convention) before any array
// field is dereferenced.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"

#include <cstdint>

namespace il2cpp {

// Array.Reverse(byte[]): in-place two-pointer reversal. byte arrays only
// (isRefElem == false, elemSize == 1); anything else throws
// System.NotSupportedException. An empty/single-element array is a no-op.
void il2cpp_array_reverse_byte(Il2CppArray* arr);

// Array.Reverse(int32[]): int32 variant of il2cpp_array_reverse_byte
// (isRefElem == false, elemSize == 4).
void il2cpp_array_reverse_int32(Il2CppArray* arr);

// Array.Reverse(int64[]): int64 variant of il2cpp_array_reverse_byte
// (isRefElem == false, elemSize == 8).
void il2cpp_array_reverse_int64(Il2CppArray* arr);

// W7 (T2): Array.Reverse(double[]): the 8-byte twin of the int64 reverser
// (slot reversal is value-agnostic -- no NaN concern). double arrays only
// (isRefElem == false, elemSize == 8).
void il2cpp_array_reverse_double(Il2CppArray* arr);

// Array.Reverse(string[]): in-place two-pointer reversal of the reference
// slots (identity -- never the pointees, no string comparison). Reference
// arrays only; null slots are legal and reverse like any other element.
void il2cpp_array_reverse_string(Il2CppArray* arr);

// Array.Reverse(object[]): generic reference-array variant of
// il2cpp_array_reverse_string (same slot reversal, write barriers on every
// swap). Reference arrays only.
void il2cpp_array_reverse_object(Il2CppArray* arr);

// Array.Clear(byte[], int index, int length): zero 'length' byte elements
// starting at 'index'. Validation order: null array, negative index/length
// (ArgumentOutOfRangeException), index + length > Length in 64-bit
// (ArgumentException) -- then memset. length == 0 is a validated no-op (a
// legal one-past-end index passes, matching .NET). byte arrays only.
void il2cpp_array_clear_byte(Il2CppArray* arr, int32_t index, int32_t length);

// Array.Clear(int32[], int index, int length): int32 variant of
// il2cpp_array_clear_byte (stride 4). int32 arrays only.
void il2cpp_array_clear_int32(Il2CppArray* arr, int32_t index, int32_t length);

// Array.GetValue(int index): the element as an object. Reference arrays
// return the slot itself (may be null); byte/int32/int64 value arrays return
// the element BOXED with the array's primitive type (see the header comment
// for the box mapping); any other stride throws System.NotSupportedException.
// A null array throws ArgumentNullException, an out-of-range index throws
// IndexOutOfRangeException.
Il2CppObject* il2cpp_array_get_value(Il2CppArray* arr, int32_t index);

// Array.SetValue(object value, int index): store 'value' into the element at
// 'index'. Reference arrays store the reference itself (null legal, write
// barrier always run); byte/int32/int64 value arrays UNBOX the object with
// the array's primitive type (a wrong boxed type throws
// System.InvalidCastException, null throws System.ArgumentNullException --
// matching .NET SetValue). An unsupported element stride is validated BEFORE
// the value's null/type checks (a domain-contract violation outranks value
// semantics, so a bad array always reports System.NotSupportedException).
// A null array throws ArgumentNullException, an out-of-range index throws
// IndexOutOfRangeException.
void il2cpp_array_set_value(Il2CppArray* arr, Il2CppObject* value, int32_t index);

} // namespace il2cpp
