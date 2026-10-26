//------------------------------------------------------------------------------
// boxingext2.cpp -- boxing/unboxing icalls for the remaining primitives
// (byte/sbyte/short/ushort/uint/long/ulong/float).
//
// Boxed-value model (MUST stay byte-compatible with boxing.cpp's Int32 box and
// boxingext.cpp's bool/char/double boxes -- never change one without the
// other):
//
//     offset 0: klass      (Il2CppObject header)
//     offset 8: int32_t    (byte/sbyte/short/ushort: zero-extended low bits;
//                           uint: the FULL 32-bit pattern)
//     offset 8: int64_t    (long / ulong: the full 8-byte payload slot)
//     offset 8: double     (float: the IEEE-754 value, stored as double)
//
// instanceSize 16 for every box class (header + one aligned payload slot; the
// decimal box is 32 -- header + a 16-byte slot), refFieldBitmap == 0 (the
// collector scans these objects as pure headers -- the payload is never a GC
// reference), no vtable. Each box class carries its .NET interface table (the
// numerics: IConvertible/IComparable/IFormattable -- the same set as
// metadata.cpp's builtin System_Int32_class / System_Double_class) and the
// System.Object baseClass, so `boxed byte is IConvertible` / `boxed long is
// object` resolve exactly as net48. The descriptors THEMSELVES live in
// metadata.cpp (one per .NET primitive; g_*BoxClass here are references to
// them) -- see the box-identity note further down.
//
// Payload-width decisions (see il2cpp_boxing_ext2.h for the full rationale):
//   - byte/sbyte/short/ushort: UNIFORM int32 storage of the zero-extended low
//     bits (byte/sbyte: value & 0xFF; short/ushort: value & 0xFFFF). The
//     UNBOX step applies the width interpretation: sbyte sign-extends the low
//     8 bits, short sign-extends the low 16 bits, byte/ushort zero-extend.
//     A boxed sbyte -1 and a boxed byte 0xFF therefore share the same payload
//     byte pattern but are distinct runtime types (their klass differs), and
//     payload equality is uniform across the small types.
//   - uint: the full 32-bit pattern in the SAME int32 slot as the Int32 box
//     (no masking). The M4 ABI carries the value as int32_t; the unsigned
//     interpretation (uint32_t cast at the call site) is applied by the
//     consumer. uint32 and int32 bit patterns are bijective, so the payload
//     comparison covers both.
//   - long/ulong: 8-byte payload slot at offset 8 (the slot is 8-byte aligned
//     by construction), instanceSize stays 16.
//   - float: the M4 type system has no native float -- the ABI carries float
//     as double (the icalls take/return double). The box STORES the double at
//     the aligned 8-byte slot. float -> double is exact, so every float bit
//     pattern (NaN, -0.0, FLT_MAX) round-trips losslessly, and .NET
//     Single.Equals semantics (NaN == NaN, +0.0 == -0.0) hold on the stored
//     double. Unboxing returns the double; a 32-bit float target narrows at
//     the call site.
//
// Type-check contract (mirrors boxingext.cpp): null -> NullReferenceException,
// a wrong runtime type -> InvalidCastException, both through il2cpp_throw_new
// (the M3 exception carrier) with the ",System.Exception" base chain so
// il2cpp_exception_matches accepts a catch of System.Exception.
//------------------------------------------------------------------------------

#include "il2cpp_boxing_ext2.h"

#include "il2cpp_boxing_ext.h" // icall_boxed_equals (legacy set delegation)
#include "enumext.h"           // il2cpp_enum_is_registered / il2cpp_enum_equals
                               // (boxed-enum value equality)
#include "guid.h"              // Il2CppGuid (the Guid box payload, nullable GN-2)
#include "il2cpp_time.h"       // icall_datetime_equals / icall_timespan_equals
#include "il2cpp_exception.h"
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_exception (decimal object-compare)
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h" // il2cpp_class_by_name / il2cpp_is_instance /
                            // il2cpp_gc_alloc / il2cpp_gc_alloc_size
#include "typeinfo.h"       // il2cpp_typeinfo_lookup / Il2CppMethodInfo (the
                            // user-override Equals(object) vtable dispatch)
#include "versionuri.h"     // System_Uri_class / icall_uri_equals (URI-FIX
                            // P1b fenced Uri value branch below)

#include <cmath>
#include <cstdint>
#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

// Payload slot offset: one 8-byte-aligned slot after the object header
// (identical to boxing.cpp's Int32 box layout -- never change one without the
// other).
constexpr int32_t kBoxPayloadOffset = 8;

// Box class descriptors: ONE descriptor per .NET primitive, owned by
// metadata.cpp and exposed through il2cpp_metadata.h. boxingext2.cpp only
// ALIASES them (g_*BoxClass below) -- it must never define a second
// descriptor with the same name.
//
// Why (box-identity / objectops `box_long_is=False`): generated `is`/`as` on a
// box-only primitive is il2cpp_is_instance(obj, il2cpp_class_by_name(name))
// (CodeGenerator.BoxClassDescriptorCpp) and il2cpp_is_instance is a POINTER
// compare. File-local duplicates (g_longBoxClass / g_floatBoxClass) carried
// the same names as metadata.cpp's System_Int64_class / System_Single_class,
// so class_by_name returned the metadata descriptor while the box stamped the
// file-local one -> `boxed long is long` was False.
//
// The UInt32 alias below is the pre-existing precedent for this pattern.
Il2CppClass& g_byteBoxClass = System_Byte_class;
Il2CppClass& g_sbyteBoxClass = System_SByte_class;
Il2CppClass& g_shortBoxClass = System_Int16_class;
Il2CppClass& g_ushortBoxClass = System_UInt16_class;

// The UInt32 box class descriptor is the GLOBAL builtin System_UInt32_class
// (metadata.cpp; extern in il2cpp_metadata.h) -- the SAME descriptor generated
// isinst/castclass code and il2cpp_class_by_name("System.UInt32") resolve to.
Il2CppClass& g_uintBoxClass = System_UInt32_class;

Il2CppClass& g_longBoxClass = System_Int64_class;
Il2CppClass& g_ulongBoxClass = System_UInt64_class;
Il2CppClass& g_floatBoxClass = System_Single_class;
Il2CppClass& g_decimalBoxClass = System_Decimal_class;

// Register the builtin box classes once (idempotent; the registry dedupes).
// Called before every box/unbox so class_by_name and isinst always resolve.
// All primitive box descriptors live in metadata.cpp now, so this is the
// shared builtin registration (no boxingext2-owned descriptors to add).
void EnsureBox2ClassesRegistered()
{
    il2cpp_ensure_builtin_system_classes();
}

// .NET unbox failure semantics (mirrors boxingext.cpp): null is
// NullReferenceException, a wrong runtime type is InvalidCastException. The
// base-chain suffix (",System.Exception") is the transpiler's chain format --
// it lets il2cpp_exception_matches accept a catch of System.Exception.
[[noreturn]] void ThrowUnboxNull()
{
    il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                     "Object reference not set to an instance of an object.");
}

void ThrowInvalidCast(const char* expected)
{
    il2cpp_throw_new("System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception", expected);
}

// P25b: is this descriptor a USER value type (its base chain reaches
// System.ValueType)? The transpiler wires every generated user-struct {T}_class
// to &System_ValueType_class; the builtin primitive box classes (Int32 >=
// baseClass System.Object) and enum boxes (base System.Enum chain) do NOT
// reach it, so `boxed int is ValueType` stays false (the runtime-primitive
// unbox helpers own those) while `boxed Point is ValueType` is true.
bool IsValueTypeClass(Il2CppClass* klass)
{
    for (Il2CppClass* cur = klass; cur != nullptr; cur = cur->baseClass)
    {
        if (cur == &System_ValueType_class)
        {
            return true;
        }
    }
    return false;
}

// Payload accessors -- the shared layout contract with boxing.cpp.
int32_t BoxPayloadInt32(Il2CppObject* obj)
{
    return *reinterpret_cast<int32_t*>(reinterpret_cast<char*>(obj) + kBoxPayloadOffset);
}

int64_t BoxPayloadInt64(Il2CppObject* obj)
{
    return *reinterpret_cast<int64_t*>(reinterpret_cast<char*>(obj) + kBoxPayloadOffset);
}

uint64_t BoxPayloadUInt64(Il2CppObject* obj)
{
    return *reinterpret_cast<uint64_t*>(reinterpret_cast<char*>(obj) + kBoxPayloadOffset);
}

double BoxPayloadDouble(Il2CppObject* obj)
{
    return *reinterpret_cast<double*>(reinterpret_cast<char*>(obj) + kBoxPayloadOffset);
}

// nullable (GN-2): the DateTime/TimeSpan/Guid box payloads (the descriptors
// live in metadata.cpp; the boxes are created by il2cpp_nullable.h's icalls).
Il2CppDateTime BoxedDateTime(Il2CppObject* obj)
{
    Il2CppDateTime value;
    std::memcpy(&value, reinterpret_cast<char*>(obj) + kBoxPayloadOffset, sizeof(value));
    return value;
}

Il2CppTimeSpan BoxedTimeSpan(Il2CppObject* obj)
{
    Il2CppTimeSpan value;
    std::memcpy(&value, reinterpret_cast<char*>(obj) + kBoxPayloadOffset, sizeof(value));
    return value;
}

Il2CppGuid BoxedGuid(Il2CppObject* obj)
{
    Il2CppGuid value;
    std::memcpy(&value, reinterpret_cast<char*>(obj) + kBoxPayloadOffset, sizeof(value));
    return value;
}

// Allocate a boxed object whose payload is an int32 at the shared slot.
// Zero-init already cleared the slot; then store the value.
Il2CppObject* AllocBoxedInt32(Il2CppClass* klass, int32_t payload)
{
    Il2CppObject* boxed = il2cpp_gc_alloc(klass);
    *reinterpret_cast<int32_t*>(reinterpret_cast<char*>(boxed) + kBoxPayloadOffset) = payload;
    return boxed;
}

// Allocate a boxed object whose payload is an 8-byte value at the shared slot
// (long/ulong/float boxes).
Il2CppObject* AllocBoxed64(Il2CppClass* klass, int64_t payload)
{
    Il2CppObject* boxed = il2cpp_gc_alloc(klass);
    *reinterpret_cast<int64_t*>(reinterpret_cast<char*>(boxed) + kBoxPayloadOffset) = payload;
    return boxed;
}

} // namespace

//------------------------------------------------------------------------------
// icall_box_byte -- box a value as System.Byte (int32 payload = low 8 bits).
//------------------------------------------------------------------------------
Il2CppObject* icall_box_byte(int32_t value)
{
    EnsureBox2ClassesRegistered();
    return AllocBoxedInt32(&g_byteBoxClass, value & 0xFF);
}

//------------------------------------------------------------------------------
// icall_box_sbyte -- box a value as System.SByte (int32 payload = low 8 bits;
// the unbox side applies the signed interpretation).
//------------------------------------------------------------------------------
Il2CppObject* icall_box_sbyte(int32_t value)
{
    EnsureBox2ClassesRegistered();
    return AllocBoxedInt32(&g_sbyteBoxClass, value & 0xFF);
}

//------------------------------------------------------------------------------
// icall_box_short -- box a value as System.Int16 (int32 payload = low 16 bits).
//------------------------------------------------------------------------------
Il2CppObject* icall_box_short(int32_t value)
{
    EnsureBox2ClassesRegistered();
    return AllocBoxedInt32(&g_shortBoxClass, value & 0xFFFF);
}

//------------------------------------------------------------------------------
// icall_box_ushort -- box a value as System.UInt16 (int32 payload = low 16
// bits).
//------------------------------------------------------------------------------
Il2CppObject* icall_box_ushort(int32_t value)
{
    EnsureBox2ClassesRegistered();
    return AllocBoxedInt32(&g_ushortBoxClass, value & 0xFFFF);
}

//------------------------------------------------------------------------------
// icall_box_uint -- box a value as System.UInt32 (the full 32-bit pattern in
// the int32 payload slot -- no masking, unlike the small types). The M4 ABI
// carries the value as int32_t; the unsigned interpretation (uint32_t cast at
// the call site) is applied by the consumer.
//------------------------------------------------------------------------------
Il2CppObject* icall_box_uint(int32_t value)
{
    EnsureBox2ClassesRegistered();
    return AllocBoxedInt32(&g_uintBoxClass, value);
}

//------------------------------------------------------------------------------
// icall_box_long -- box a value as System.Int64 (full 8-byte payload slot).
//------------------------------------------------------------------------------
Il2CppObject* icall_box_long(int64_t value)
{
    EnsureBox2ClassesRegistered();
    return AllocBoxed64(&g_longBoxClass, value);
}

//------------------------------------------------------------------------------
// icall_box_ulong -- box a value as System.UInt64 (full 8-byte payload slot).
//------------------------------------------------------------------------------
Il2CppObject* icall_box_ulong(uint64_t value)
{
    EnsureBox2ClassesRegistered();
    // The 8-byte slot is stored through the int64 form so the byte pattern is
    // the raw bit pattern -- the unsigned interpretation only matters at
    // unbox/compare time (int64/uint64 bit patterns are bijective).
    return AllocBoxed64(&g_ulongBoxClass,
                        static_cast<int64_t>(value));
}

//------------------------------------------------------------------------------
// icall_box_float -- box a value as System.Single.
//
// The M4 ABI carries float as double (no native float in the type system);
// the box stores the double at the aligned 8-byte slot. float -> double is
// exact, so the payload round-trips every float bit pattern losslessly.
//------------------------------------------------------------------------------
Il2CppObject* icall_box_float(double value)
{
    EnsureBox2ClassesRegistered();
    int64_t bits;
    std::memcpy(&bits, &value, sizeof(bits));
    return AllocBoxed64(&g_floatBoxClass, bits);
}

//------------------------------------------------------------------------------
// icall_box_enum -- box an ENUM value with the ENUM'S OWN class descriptor.
//
// Root-cause fix (D-ENM-3/D-ENM-5, enumdeep2/3): boxing an enum through the
// int64 carrier (icall_box_long) stamps the box with the Int64 box class, so
// the boxed enum LOSES its enum type identity -- enumext's boxed ToString/
// Equals dispatch on klass->name and see "System.Int64", not the enum. The
// dedicated enum box keeps the shared 8-byte payload slot (offset 8, the
// long-box layout -- unbox:l still reads it) but stamps the box with the
// enum's own name-bearing descriptor, so the klass-name-keyed enumext
// registry lookup (FindInfo) resolves the enum metadata.
//
// The transpiler must emit this for enum boxes (CodeGenerator.cs box_ switch
// enum arm -- currently icall_box_long; REPORTED, transpiler read-only in
// this task). The runtime side is complete and unit-tested here.
//------------------------------------------------------------------------------
Il2CppObject* icall_box_enum(Il2CppClass* enumClass, int64_t value)
{
    EnsureBox2ClassesRegistered();
    if (enumClass == nullptr)
    {
        // Defensive: a null descriptor falls back to the Int64 carrier (the
        // pre-fix behavior) -- never a null-klass object.
        enumClass = &g_longBoxClass;
    }
    // The box always needs the 8-byte payload slot at offset 8, even when the
    // enum descriptor's instanceSize is smaller than 16 (test fixtures use 4).
    Il2CppObject* boxed = il2cpp_gc_alloc_size(enumClass, kBoxPayloadOffset + 8);
    *reinterpret_cast<int64_t*>(reinterpret_cast<char*>(boxed) + kBoxPayloadOffset) = value;
    return boxed;
}

//------------------------------------------------------------------------------
// icall_box_decimal -- box a value as System.Decimal (16-byte payload copied
// from the caller's value slot -- the pointer-shaped icall parameter surface,
// decimal.h convention).
//------------------------------------------------------------------------------
Il2CppObject* icall_box_decimal(const Il2CppDecimal* value)
{
    EnsureBox2ClassesRegistered();
    Il2CppObject* boxed = il2cpp_gc_alloc(&g_decimalBoxClass);
    std::memcpy(reinterpret_cast<char*>(boxed) + kBoxPayloadOffset, value,
                sizeof(Il2CppDecimal));
    return boxed;
}

//------------------------------------------------------------------------------
// icall_box_user_struct -- box a USER value type (P25b). The value arrives as
// a pointer to a plain C++ struct (the generated {T}); the box is allocated
// with the user type's OWN descriptor (&{T}_class -- the boxed object's klass
// carries the user struct identity, so isinst/castclass against {T} / object /
// ValueType resolve through the base chain) and the struct bytes are copied
// verbatim into the payload at kBoxPayloadOffset. payloadSize is the struct
// byte size (sizeof({T}), emitted by the transpiler) -- the physical box size
// is header + payloadSize (the descriptor's own instanceSize may be smaller,
// exactly like the enum box precedent). Reference-free MVP: the transpiler
// rejects structs with GC reference fields at box sites, so the box is never
// scanned for references.
//------------------------------------------------------------------------------
Il2CppObject* icall_box_user_struct(Il2CppClass* klass, const void* value, size_t payloadSize)
{
    if (klass == nullptr || value == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.ArgumentNullException,System.SystemException,System.Exception",
                         "Value does not fall within the expected range.");
    }
    Il2CppObject* boxed = il2cpp_gc_alloc_size(klass, kBoxPayloadOffset + payloadSize);
    std::memcpy(reinterpret_cast<char*>(boxed) + kBoxPayloadOffset, value, payloadSize);
    return boxed;
}

//------------------------------------------------------------------------------
// il2cpp_unbox_user_struct -- return the ADDRESS of a boxed user struct's
// payload (P25b). Unlike the primitive unbox helpers (which extract a scalar),
// a user struct has unknown compound shape at runtime -- the caller (the
// generated code) casts the returned payload pointer to {T}* and copies/reads
// the struct. Safety contract (mirrors the primitive unboxes):
//     null input   -> NullReferenceException
//     klass not a  -> InvalidCastException (the payload is NOT a user struct's
//     user struct     value -- casting it would read garbage). A klass whose
//                     base chain reaches System.ValueType is a value-type
//                     descriptor (user structs -- enums box through their own
//                     descriptors too; their base is System.Enum, NOT
//                     System.ValueType, so this check excludes enum boxes).
// Never returns garbage: every failure path throws.
//------------------------------------------------------------------------------
void* il2cpp_unbox_user_struct(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    if (obj->klass == nullptr || !IsValueTypeClass(obj->klass))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed user value type.");
    }
    return reinterpret_cast<char*>(obj) + kBoxPayloadOffset;
}

//------------------------------------------------------------------------------
// icall_unbox_byte -- extract the System.Byte payload (zero-extended low 8
// bits).
//------------------------------------------------------------------------------
int32_t icall_unbox_byte(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBox2ClassesRegistered();
    if (obj->klass != &g_byteBoxClass && !il2cpp_is_instance(obj, &g_byteBoxClass))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed Byte.");
    }
    return BoxPayloadInt32(obj) & 0xFF;
}

//------------------------------------------------------------------------------
// icall_unbox_sbyte -- extract the System.SByte payload (low 8 bits with the
// signed interpretation: sign-extended back to the int32 result).
//------------------------------------------------------------------------------
int32_t icall_unbox_sbyte(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBox2ClassesRegistered();
    if (obj->klass != &g_sbyteBoxClass && !il2cpp_is_instance(obj, &g_sbyteBoxClass))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed SByte.");
    }
    // Low 8 bits, signed interpretation: the narrowing cast takes the low
    // byte and the int8_t -> int32_t return conversion sign-extends it.
    return static_cast<int8_t>(BoxPayloadInt32(obj) & 0xFF);
}

//------------------------------------------------------------------------------
// icall_unbox_short -- extract the System.Int16 payload (low 16 bits with the
// signed interpretation: sign-extended back to the int32 result).
//------------------------------------------------------------------------------
int32_t icall_unbox_short(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBox2ClassesRegistered();
    if (obj->klass != &g_shortBoxClass && !il2cpp_is_instance(obj, &g_shortBoxClass))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed Int16.");
    }
    // Low 16 bits, signed interpretation: the narrowing cast takes the low
    // half-word and the int16_t -> int32_t return conversion sign-extends it.
    return static_cast<int16_t>(BoxPayloadInt32(obj) & 0xFFFF);
}

//------------------------------------------------------------------------------
// icall_unbox_ushort -- extract the System.UInt16 payload (zero-extended low
// 16 bits).
//------------------------------------------------------------------------------
int32_t icall_unbox_ushort(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBox2ClassesRegistered();
    if (obj->klass != &g_ushortBoxClass && !il2cpp_is_instance(obj, &g_ushortBoxClass))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed UInt16.");
    }
    return BoxPayloadInt32(obj) & 0xFFFF;
}

//------------------------------------------------------------------------------
// icall_unbox_uint -- extract the System.UInt32 payload (the full 32-bit
// pattern as int32_t; the unsigned interpretation -- (uint32_t) result -- is
// applied at the call site).
//------------------------------------------------------------------------------
int32_t icall_unbox_uint(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBox2ClassesRegistered();
    if (obj->klass != &g_uintBoxClass && !il2cpp_is_instance(obj, &g_uintBoxClass))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed UInt32.");
    }
    return BoxPayloadInt32(obj);
}

//------------------------------------------------------------------------------
// icall_unbox_long -- extract the System.Int64 payload (the 8-byte slot).
// A BOXED ENUM is accepted too (audit H3 closure, batch 148): the enum box
// carries the same 8-byte carrier slot, and the width-aware payload read
// (il2cpp_enum_boxed_payload) keeps Int64/UInt64-backed enums exact.
//------------------------------------------------------------------------------
int64_t icall_unbox_long(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBox2ClassesRegistered();
    if (obj->klass != &g_longBoxClass && !il2cpp_is_instance(obj, &g_longBoxClass))
    {
        if (il2cpp_enum_is_registered(obj->klass))
        {
            return il2cpp_enum_boxed_payload(obj);
        }
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed Int64.");
    }
    return BoxPayloadInt64(obj);
}

//------------------------------------------------------------------------------
// icall_unbox_ulong -- extract the System.UInt64 payload (the 8-byte slot).
//------------------------------------------------------------------------------
uint64_t icall_unbox_ulong(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBox2ClassesRegistered();
    if (obj->klass != &g_ulongBoxClass && !il2cpp_is_instance(obj, &g_ulongBoxClass))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed UInt64.");
    }
    return BoxPayloadUInt64(obj);
}

//------------------------------------------------------------------------------
// icall_unbox_float -- extract the System.Single payload as the stored double
// (the M4 ABI's float representation; a 32-bit float target narrows at the
// call site).
//------------------------------------------------------------------------------
double icall_unbox_float(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBox2ClassesRegistered();
    if (obj->klass != &g_floatBoxClass && !il2cpp_is_instance(obj, &g_floatBoxClass))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed Single.");
    }
    return BoxPayloadDouble(obj);
}

//------------------------------------------------------------------------------
// icall_unbox_decimal -- extract the System.Decimal payload (the 16-byte image
// at the shared payload slot; returned BY VALUE -- the struct copy rides the
// register/stack return path, unlike the pointer-shaped parameter surface).
//------------------------------------------------------------------------------
Il2CppDecimal icall_unbox_decimal(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBox2ClassesRegistered();
    if (obj->klass != &g_decimalBoxClass && !il2cpp_is_instance(obj, &g_decimalBoxClass))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed Decimal.");
    }
    Il2CppDecimal value;
    std::memcpy(&value, reinterpret_cast<char*>(obj) + kBoxPayloadOffset,
                sizeof(value));
    return value;
}

//------------------------------------------------------------------------------
// icall_decimal_equals_object / icall_decimal_compare_object -- the boxed-
// decimal member surface (net48 decimal.cs:930 / :1001). The decimal receiver
// is pointer-shaped ("dec"); the object argument is the boxed System.Decimal
// (or any object). The box payload sits right after the object header (the
// same offset the icall_box_decimal / icall_unbox_decimal pair uses).
//------------------------------------------------------------------------------
bool icall_decimal_equals_object(const Il2CppDecimal* value, Il2CppObject* obj)
{
    // net48 Decimal.Equals(object) (decimal.cs:930): null -> false, a non-
    // Decimal object -> false, a Decimal box -> 16-byte VALUE equality.
    if (obj == nullptr || obj->klass != &g_decimalBoxClass)
    {
        return false;
    }
    const Il2CppDecimal other = icall_unbox_decimal(obj);
    return icall_decimal_equals(value, &other);
}

int32_t icall_decimal_compare_object(const Il2CppDecimal* value, Il2CppObject* obj)
{
    // net48 Decimal.CompareTo(object) (decimal.cs:1001): null -> 1, a non-
    // Decimal object throws ArgumentException, a Decimal box -> the value
    // compare.
    if (obj == nullptr)
    {
        return 1;
    }
    if (obj->klass != &g_decimalBoxClass)
    {
        il2cpp_throw_argument_exception("Object must be of type System.Decimal.");
    }
    const Il2CppDecimal other = icall_unbox_decimal(obj);
    return icall_decimal_compare(value, &other);
}

//------------------------------------------------------------------------------
// icall_boxed_equals_ext2 -- superset value equality for boxed values (see
// header). The legacy set (int32/bool/char/double, owned by boxingext.cpp)
// and non-primitives delegate to icall_boxed_equals; the eight new box types
// compare natively.
//------------------------------------------------------------------------------
bool icall_boxed_equals_ext2(Il2CppObject* a, Il2CppObject* b)
{
    // Identical reference (also covers both-null and same-string): equal.
    if (a == b)
    {
        return true;
    }
    // Exactly one side null: unequal (Object.Equals(a, null) is false).
    if (a == nullptr || b == nullptr)
    {
        return false;
    }
    // Boxed values of different runtime types never compare equal.
    if (a->klass != b->klass)
    {
        return false;
    }
    // URI-FIX P1b: System.Uri VALUE equality, fenced by a runtime klass
    // compare -- fires only when the (same-klass, per the gate above)
    // operand IS a Uri instance (exact klass or a Uri subclass, via
    // il2cpp_is_instance); every other klass takes the pre-existing paths
    // below unchanged. net48 Uri.Equals compares the normalized
    // AbsoluteUri (P1: verbatim rebuild); relatives compare OriginalString
    // -- icall_uri_equals is the single source of truth (versionuri.cpp).
    // WHY HERE (G2): Roslyn emits instance Equals calls with parent
    // System.Object (StackFold.cs delechain note), and the hint-less first
    // pass resolves Object::Equals to THIS icall; the receiver-aware
    // re-resolve gate (StackFold.cs:2578) triggers on resolved==null,
    // non-il2cpp:: names, object_to_string/hash_code and enumerator cases
    // -- but NOT on icall_boxed_equals_ext2 -- so a tracked Uri receiver
    // never re-resolves to the Uri domain arm and Uri operands ALWAYS
    // arrive here (evidence: gen Cases_Prog.cpp:393). The fence keeps the
    // change to Uri operands only (compile-time hint absent at these
    // sites, so the guard is the runtime klass -- per parent ruling 1).
    if (il2cpp_is_instance(a, &System_Uri_class))
    {
        return icall_uri_equals(a, b);
    }
    EnsureBox2ClassesRegistered();
    const Il2CppClass* klass = a->klass;
    if (klass == &g_byteBoxClass || klass == &g_sbyteBoxClass ||
        klass == &g_shortBoxClass || klass == &g_ushortBoxClass ||
        klass == &g_uintBoxClass)
    {
        // Small types + uint: uniform int32 payload slots (zero-extended low
        // bits; uint stores the full 32-bit pattern) -- equality is plain
        // payload equality (sbyte -1 vs byte 0xFF can never meet here: their
        // klass already differs; the uint32/int32 bit patterns are bijective).
        return BoxPayloadInt32(a) == BoxPayloadInt32(b);
    }
    if (klass == &g_longBoxClass || klass == &g_ulongBoxClass)
    {
        // 8-byte payload equality. int64 and uint64 bit patterns are
        // bijective, so the int64 comparison is exact for both.
        return BoxPayloadInt64(a) == BoxPayloadInt64(b);
    }
    if (klass == &g_floatBoxClass)
    {
        // .NET Single.Equals semantics, evaluated on the stored double:
        // NaN equals NaN, +0.0 equals -0.0 (IEEE == already yields true for
        // equal magnitudes incl. -0.0/+0.0).
        const double va = BoxPayloadDouble(a);
        const double vb = BoxPayloadDouble(b);
        return va == vb || (std::isnan(va) && std::isnan(vb));
    }
    if (klass == &g_decimalBoxClass)
    {
        // .NET Decimal.Equals: VALUE equality on the 16-byte payload (scale
        // differences are canonicalized -- 1.0m equals 1.00m; net48 probe:
        // object.Equals(1.5m, (object)1.5m) is True). The decimal domain's
        // icall_decimal_equals is the single source of truth.
        Il2CppDecimal da;
        Il2CppDecimal db;
        std::memcpy(&da, reinterpret_cast<const char*>(a) + kBoxPayloadOffset,
                    sizeof(da));
        std::memcpy(&db, reinterpret_cast<const char*>(b) + kBoxPayloadOffset,
                    sizeof(db));
        return icall_decimal_equals(&da, &db);
    }
    // nullable (GN-2): the DateTime/TimeSpan/Guid boxes compare by VALUE
    // (net48 value-type semantics; the same-klass gate above already proved
    // both operands are the same type).
    if (klass == &System_DateTime_class)
    {
        const Il2CppDateTime va = BoxedDateTime(b);
        const Il2CppDateTime vb = BoxedDateTime(a);
        return icall_datetime_equals(va, vb);
    }
    if (klass == &System_TimeSpan_class)
    {
        const Il2CppTimeSpan va = BoxedTimeSpan(b);
        const Il2CppTimeSpan vb = BoxedTimeSpan(a);
        return icall_timespan_equals(va, vb);
    }
    if (klass == &System_Guid_class)
    {
        const Il2CppGuid va = BoxedGuid(b);
        const Il2CppGuid vb = BoxedGuid(a);
        return std::memcmp(va.b, vb.b, sizeof(va.b)) == 0;
    }
    // Enum-klass boxes (icall_box_enum): the klass is the enum's OWN
    // descriptor (name-bearing, so the enumext registry resolves it). The
    // same-klass gate above already proved a and b are the same enum type;
    // il2cpp_enum_equals does the width-aware payload compare (int64-backed
    // enums read the full 8-byte slot -- the D-ENM-5 cross-type gate is the
    // klass-name mismatch, which the gate above rejects). Cross-type and
    // non-enum boxes never reach here.
    if (il2cpp_enum_is_registered(klass))
    {
        return il2cpp_enum_equals(a, b) != 0;
    }
    // caudit2: USER-type boxes -- net48's ((object)a).Equals((object)b) is
    // the VIRTUAL Equals(object) dispatch (a user override), not reference or
    // payload comparison. The reflected method table (typeinfo.h) carries the
    // (r:System.Object) override and its vtable slot; the receiver is the box
    // PAYLOAD address for a value type (the generated value-type method takes
    // the value's address -- the il2cpp_unbox_user_struct contract) or the
    // object itself for a translated class. Types without an override (and
    // every BCL box, which has no typeinfo) fall through to the legacy
    // delegation below -- the pre-existing reference-equality fallback stays.
    if (const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(klass->name))
    {
        const Il2CppMethodInfo* equalsOverride = nullptr;
        for (int32_t i = 0; i < info->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = info->methods[i];
            if (std::strcmp(m.name, "Equals") == 0 && m.paramCount == 1 &&
                std::strstr(m.signature, "(r:System.Object)") != nullptr)
            {
                equalsOverride = &m;
                break;
            }
        }
        if (equalsOverride != nullptr && equalsOverride->virtualSlot >= 0 &&
            klass->vtable != nullptr && equalsOverride->virtualSlot < klass->vtableSize &&
            klass->vtable[equalsOverride->virtualSlot] != nullptr)
        {
            using EqualsObjectFn = int32_t (*)(Il2CppObject*, Il2CppObject*);
            Il2CppObject* receiver = IsValueTypeClass(const_cast<Il2CppClass*>(klass))
                ? reinterpret_cast<Il2CppObject*>(reinterpret_cast<char*>(a) + kBoxPayloadOffset)
                : a;
            return reinterpret_cast<EqualsObjectFn>(
                klass->vtable[equalsOverride->virtualSlot])(receiver, b) != 0;
        }
    }
    // Legacy set (int32/bool/char/double) and non-primitive box classes
    // (strings, user objects): boxingext.cpp's icall_boxed_equals is the
    // single source of truth (int32/bool/char payload equality, Double.Equals
    // semantics, reference equality for non-primitives).
    return icall_boxed_equals(a, b);
}

} // namespace runtime
} // namespace il2cpp
