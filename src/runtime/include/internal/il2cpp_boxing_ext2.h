#pragma once
//------------------------------------------------------------------------------
// il2cpp_boxing_ext2.h -- boxing/unboxing icalls for the remaining primitives
// (byte/sbyte/short/ushort/uint/long/ulong/float).
//
// Boxed-value model (MUST stay byte-compatible with boxing.cpp's Int32 box and
// boxingext.cpp's bool/char/double boxes -- never change one without the
// others):
//
//     offset 0: klass      (Il2CppObject header)
//     offset 8: int32_t    (byte/sbyte/short/ushort/uint: zero-extended low
//                           bits; the unbox reinterpretation sign-extends)
//     offset 8: int64_t    (long / ulong: the full 8-byte payload slot)
//     offset 8: double     (float: the IEEE-754 value, stored as double)
//
// instanceSize is 16 for EVERY box class (header + one aligned payload slot;
// the decimal box is 32 -- header + a 16-byte slot), refFieldBitmap == 0 (the
// payload is never a GC reference), no vtable. Each box class carries its
// .NET interface table (the numerics: IConvertible + IComparable +
// IFormattable, mirroring the metadata.cpp builtin box descriptors) and the
// System.Object baseClass, so `boxed value is IConvertible` / `is object`
// resolve exactly as net48 -- identical to the existing box classes, so
// objects created by any box icall in the runtime are interchangeable at the
// layout level.
//
// The payload-width design decision:
//   - byte/sbyte/short/ushort: the .NET boxed object holds a 1-/2-byte value,
//     but the shared box layout gives every small type the SAME int32 slot at
//     offset 8 (the Int32 box's slot). Storage is uniform (zero-extended low
//     bits: byte/sbyte store value & 0xFF, short/ushort store value & 0xFFFF);
//     only the UNBOX step applies the width interpretation (sbyte/short
//     sign-extend their low bits back). This keeps the byte pattern of a
//     boxed byte identical to the Int32 box's slot semantics and makes the
//     comparison rules uniform (same-width payload equality).
//   - uint: a 32-bit value occupies the SAME int32 slot as the Int32 box
//     (full bit pattern -- no masking, unlike the small types). The M4 ABI
//     carries the value as int32_t; the unsigned interpretation is applied at
//     the call site (the transpiler emits uint32_t casts for unsigned
//     semantics). uint32 and int32 bit patterns are bijective, so payload
//     equality covers both.
//   - long/ulong: an 8-byte value needs an 8-byte slot; the slot at offset 8
//     is exactly that (8-byte aligned), instanceSize stays 16.
//   - float: the M4 type system has no native float -- the ABI carries float
//     as double (box/unbox icalls take and return double). The box STORES the
//     double at the aligned 8-byte slot. float -> double is exact (no lossy
//     widening), so the payload round-trips every float bit pattern including
//     NaN and -0.0, and .NET Single.Equals semantics (NaN == NaN, +0.0 ==
//     -0.0) hold on the stored double.
//
// Each primitive has its OWN registered class descriptor ("System.Byte",
// "System.SByte", "System.Int16", "System.UInt16", "System.UInt32",
// "System.Int64", "System.UInt64", "System.Single" -- the first five's full
// names match .NET, NOT their C++ storage widths). Registration is lazy
// (first box/unbox call), matching the Int32 box class's lifecycle;
// il2cpp_class_by_name sees a box class exactly when a boxed instance of it
// can exist.
//
// Error contract (mirrors boxingext.cpp, which mirrors boxing.cpp):
//   null input         -> NullReferenceException
//   wrong runtime type -> InvalidCastException
// Both throw through il2cpp_throw_new (the M3 exception carrier); generated
// try/catch and the top-level guard handle them like any other managed throw
// -- a wrong-type unbox NEVER silently returns garbage.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include "decimal.h" // Il2CppDecimal (16-byte box payload)

#include <cstdint>

namespace il2cpp {
namespace runtime {

//------------------------------------------------------------------------------
// Boxing icalls -- allocate a boxed primitive on the managed heap.
//
//   icall_box_byte   : stores 'value' & 0xFF in the int32 payload slot
//   icall_box_sbyte  : stores 'value' & 0xFF (the low 8 bits; the unbox side
//                      applies the signed interpretation)
//   icall_box_short  : stores 'value' & 0xFFFF (low 16 bits)
//   icall_box_ushort : stores 'value' & 0xFFFF
//   icall_box_uint   : stores the full 32-bit pattern in the int32 slot (the
//                      M4 ABI carries it as int32_t; the unsigned
//                      interpretation is applied at the call site)
//   icall_box_long   : stores the full int64 in the 8-byte payload slot
//   icall_box_ulong  : stores the full uint64 in the 8-byte payload slot
//   icall_box_float  : stores the double (the float arrives widened through
//                      the M4 ABI; see the header comment for the rationale)
//
// All return a GC reference whose klass is the primitive's registered box
// class; the caller must keep the reference reachable until used.
//------------------------------------------------------------------------------
Il2CppObject* icall_box_byte(int32_t value);
Il2CppObject* icall_box_sbyte(int32_t value);
Il2CppObject* icall_box_short(int32_t value);
Il2CppObject* icall_box_ushort(int32_t value);
Il2CppObject* icall_box_uint(int32_t value);
Il2CppObject* icall_box_long(int64_t value);
Il2CppObject* icall_box_ulong(uint64_t value);
Il2CppObject* icall_box_float(double value);

//   icall_box_enum   : boxes an ENUM value with the ENUM'S OWN class
//                      descriptor -- the enum identity is carried by the
//                      klass (name-bearing), NOT the Int64/Int32 carrier, so
//                      enumext's klass-name-keyed registry lookup (FindInfo)
//                      resolves the metadata. The 8-byte payload slot at
//                      offset 8 keeps the long-box layout (unbox:l still
//                      reads it). A null klass falls back to the Int64
//                      carrier box (defensive). D-ENM-3/D-ENM-5 fix.
Il2CppObject* icall_box_enum(Il2CppClass* enumClass, int64_t value);

//   icall_box_decimal  : copies the 16-byte decimal image from the caller's
//                        value slot (Il2CppDecimal*) into a new System.Decimal
//                        box (instanceSize 32 -- header + 16-byte payload)
Il2CppObject* icall_box_decimal(const Il2CppDecimal* value);

//   icall_box_user_struct : P25b user-struct boxing. Copies a USER value type
//                        (an application TypeDef -- a plain C++ struct with no
//                        runtime kind) into a boxed object whose klass is the
//                        user type's generated descriptor (&{T}_class). The
//                        payload starts at kBoxPayloadOffset (8) and holds
//                        payloadSize bytes (the transpiler passes
//                        sizeof({T})). refFieldBitmap is 0 (reference-free MVP
//                        -- the transpiler rejects structs with GC reference
//                        fields loudly at box sites).
Il2CppObject* icall_box_user_struct(Il2CppClass* klass, const void* value, size_t payloadSize);

//------------------------------------------------------------------------------
// Unboxing icalls -- extract the payload of a boxed primitive.
//
//   icall_unbox_byte   : returns payload & 0xFF (0..255)
//   icall_unbox_sbyte  : sign-extends the low 8 bits (low 8-bit signed
//                        interpretation)
//   icall_unbox_short  : sign-extends the low 16 bits
//   icall_unbox_ushort : returns payload & 0xFFFF (0..65535)
//   icall_unbox_uint   : returns the full 32-bit payload as int32_t (the
//                        unsigned interpretation -- (uint32_t) result -- is
//                        applied at the call site)
//   icall_unbox_long   : returns the 8-byte int64 payload
//   icall_unbox_ulong  : returns the 8-byte uint64 payload
//   icall_unbox_float  : returns the stored double (the M4 ABI's float
//                        representation; the caller narrows to float if the
//                        target is a 32-bit float)
//
//   Error contract (never silently returns garbage):
//     null input         -> NullReferenceException
//     wrong runtime type -> InvalidCastException
//------------------------------------------------------------------------------
int32_t icall_unbox_byte(Il2CppObject* obj);
int32_t icall_unbox_sbyte(Il2CppObject* obj);
int32_t icall_unbox_short(Il2CppObject* obj);
int32_t icall_unbox_ushort(Il2CppObject* obj);
int32_t icall_unbox_uint(Il2CppObject* obj);
int64_t icall_unbox_long(Il2CppObject* obj);
uint64_t icall_unbox_ulong(Il2CppObject* obj);
double icall_unbox_float(Il2CppObject* obj);

//   icall_unbox_decimal : returns the boxed 16-byte decimal image (by value --
//                         the 16-byte struct rides the register path here
//                         because it is a BY-VALUE return, unlike the icall
//                         parameter surface, which is pointer-shaped)
Il2CppDecimal icall_unbox_decimal(Il2CppObject* obj);

//   il2cpp_unbox_user_struct : returns the ADDRESS of the boxed user-struct
//                        payload (the box's klass must be a user-struct
//                        descriptor; the caller casts the void* payload and
//                        copies/reads the struct). Null or a non-user-struct
//                        box throws (NRE/InvalidCast) -- never garbage.
void* il2cpp_unbox_user_struct(Il2CppObject* obj);

//------------------------------------------------------------------------------
// icall_boxed_equals_ext2 -- superset value equality for boxed values.
//
// This file CANNOT extend icall_boxed_equals in place (boxingext.cpp owns
// that symbol and covers int32/bool/char/double); the transpiler-facing
// entry point is this superset helper instead:
//
//   a == b (identical reference, incl. both null)     -> true
//   one null, the other not                           -> false
//   different runtime types                           -> false
//   same boxed primitive type                         -> unbox both, compare
//        byte/sbyte/short/ushort/uint/int32/bool/char: payload equality
//        long/ulong: 8-byte payload equality (bit-pattern compare)
//        double/float: .NET Double/Single.Equals semantics --
//                      NaN == NaN, +0.0 == -0.0
//   everything else (the legacy set handled by boxingext.cpp and
//   non-primitive classes such as strings)          -> delegated to
//        icall_boxed_equals (reference equality for non-primitives)
//------------------------------------------------------------------------------
bool icall_boxed_equals_ext2(Il2CppObject* a, Il2CppObject* b);

} // namespace runtime
} // namespace il2cpp
