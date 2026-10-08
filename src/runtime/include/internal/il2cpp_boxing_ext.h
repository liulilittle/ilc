#pragma once
//------------------------------------------------------------------------------
// il2cpp_boxing_ext.h -- extended boxing/unboxing icalls (bool/char/double).
//
// The M2 runtime boxes only int32 (see boxing.cpp): a boxed value is a
// MANAGED OBJECT with the payload at byte offset 8 (slot 1 -- one 8-byte-
// aligned slot after the Il2CppObject header). These extensions keep the SAME
// layout for every additional primitive:
//
//     offset 0: klass      (Il2CppObject header)
//     offset 8: int32_t    (bool: 0/1; char: UTF-16 code unit; Int32: value)
//     offset 8: double     (IEEE-754 value, exactly one aligned slot)
//
// instanceSize is 16 for every box class (header + one aligned slot), so an
// object created by any box icall is byte-compatible with the Int32 box
// objects created by il2cpp_box_int32: same header, same payload offset, same
// GC scanning (refFieldBitmap == 0 -- the payload is never a GC reference).
//
// Each primitive has its OWN registered class descriptor ("System.Boolean",
// "System.Char", "System.Double"; the Int32 descriptor lives in boxing.cpp).
// .NET semantics: the runtime type of a boxed value is the primitive's type,
// and unboxing performs an exact-type check -- a mismatch throws
// InvalidCastException, null throws NullReferenceException (both through the
// M3 exception carrier, il2cpp_throw_new; generated try/catch and the
// top-level guard handle them like any other managed throw).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

//------------------------------------------------------------------------------
// Boxing icalls -- allocate a boxed primitive on the managed heap.
//
//   icall_box_bool   : payload is int32 0/1 (the canonical .NET boolean
//                      representation); true -> 1, false -> 0
//   icall_box_char   : 'utf16' is the UTF-16 code unit, stored as int32
//   icall_box_double : 8-byte IEEE-754 value at the payload slot
//
// All return a GC reference whose klass is the primitive's registered box
// class; the caller must keep the reference reachable until used.
//------------------------------------------------------------------------------
Il2CppObject* icall_box_bool(bool value);
Il2CppObject* icall_box_char(int32_t utf16);
Il2CppObject* icall_box_double(double value);

//------------------------------------------------------------------------------
// Unboxing icalls -- extract the payload of a boxed primitive.
//
//   icall_unbox_int32  : accepts objects whose klass is the runtime's Int32
//                        box class (boxing.cpp) or a subtype of it
//   icall_unbox_bool   : returns payload != 0
//   icall_unbox_char   : returns the int32 UTF-16 code unit
//   icall_unbox_double : returns the 8-byte IEEE-754 payload
//
//   Error contract (never silently returns garbage):
//     null input         -> NullReferenceException
//     wrong runtime type -> InvalidCastException
//------------------------------------------------------------------------------
int32_t icall_unbox_int32(Il2CppObject* obj);
bool icall_unbox_bool(Il2CppObject* obj);
int32_t icall_unbox_char(Il2CppObject* obj);
double icall_unbox_double(Il2CppObject* obj);

//------------------------------------------------------------------------------
// icall_boxed_equals -- value equality for boxed values (Object.Equals helper
// for generated code).
//
//   a == b (identical reference, incl. both null)     -> true
//   one null, the other not                           -> false
//   different runtime types                           -> false
//   same boxed primitive type                         -> unbox both, compare
//        int32/bool/char: payload equality
//        double: .NET Double.Equals semantics -- NaN == NaN, +0.0 == -0.0
//   anything else (e.g. strings)                      -> reference equality
//        (already covered by the a == b rule; distinct instances of the
//        same non-primitive type are unequal)
//------------------------------------------------------------------------------
bool icall_boxed_equals(Il2CppObject* a, Il2CppObject* b);

//------------------------------------------------------------------------------
// Unboxed value-EQUALS icalls (D5 cov2): the primitive `T.Equals(T)`
// instance surface when the receiver arrives as an ldloca ADDRESS (an
// UNBOXED value-type receiver -- the boxed-receiver cases keep
// icall_boxed_equals). Each takes `self` = address of the receiver slot
// (the value_semantics carrier convention: int32 family / int64 family /
// double; Single rides a double slot) and the compared value.
//   i4: int32/uint32/bool/char/i1/u1/i2/u2 (bitwise equality)
//   i8: int64/uint64
//   f8: double, f4: single -- .NET Equals NaN-fold (NaN == NaN true)
//------------------------------------------------------------------------------
bool icall_value_equals_i4(int32_t* self, int32_t value);
bool icall_value_equals_i8(int64_t* self, int64_t value);
bool icall_value_equals_f8(double* self, double value);
bool icall_value_equals_f4(double* self, double value);

// GN-N3 (p-iface): Int32.CompareTo(int32) -- net48 sign verdict (-1/0/+1).
// Receiver is the value-slot ADDRESS (ldflda on an int field); same receiver
// ABI as icall_value_equals_i4 (cast "").
int32_t icall_int32_compare_to(int32_t* self, int32_t value);

} // namespace runtime
} // namespace il2cpp
