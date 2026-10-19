//------------------------------------------------------------------------------
// boxingext.cpp -- extended boxing/unboxing icalls (bool/char/double).
//
// Boxed-value model (MUST stay byte-compatible with boxing.cpp's Int32 box):
//   A boxed primitive is a MANAGED OBJECT with the value payload at byte
//   offset 8 (slot 1 -- one 8-byte-aligned slot after the Il2CppObject
//   header). instanceSize 16, no reference fields (refFieldBitmap == 0), no
//   vtable, no interfaces. The collector scans these objects as pure headers
//   -- the payload is never a GC reference.
//
//     offset 0: klass      (Il2CppObject header)
//     offset 8: int32_t    (bool: 0/1; char: UTF-16 code unit)
//     offset 8: double     (IEEE-754 value, exactly one aligned slot)
//
//   Every primitive maps to its BUILTIN metadata.cpp class descriptor
//   (System.Boolean / System.Char / System.Double -- the same externs
//   generated isinst/castclass code compares against, each with its .NET
//   interface table). Registration is lazy (first box/unbox call), matching
//   the Int32 box class's lifecycle; il2cpp_class_by_name sees a box class
//   exactly when a boxed instance of it can exist.
//
// Type-check contract (mirrors boxing.cpp's unbox):
//   null                       -> NullReferenceException
//   klass is not the box class -> InvalidCastException
//   Both throw through il2cpp_throw_new (M3 exception carrier) so generated
//   try/catch and the top-level guard handle them like any other managed
//   throw -- a wrong-type unbox NEVER silently returns garbage.
//------------------------------------------------------------------------------

#include "il2cpp_boxing_ext.h"

#include "il2cpp_boxing_ext2.h" // nb9 E1: icall_unbox_long / icall_unbox_float
#include "il2cpp_exception.h"
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h" // il2cpp_class_register / il2cpp_class_by_name /
                            // il2cpp_is_instance / il2cpp_gc_alloc

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

// Box class descriptors: the BUILTIN metadata.cpp descriptors (extern in
// il2cpp_metadata.h) -- System_Boolean_class / System_Char_class /
// System_Double_class. These are the SAME descriptors the transpiler's
// generated isinst/castclass code compares against, so pointer identity
// holds for boxed objects; each carries its .NET interface table
// (Boolean/Char: IConvertible + IComparable; Double: + IFormattable) and the
// System.Object baseClass. Registered (with all builtin System.* descriptors)
// through il2cpp_ensure_builtin_system_classes(), lazily on the first
// box/unbox call.

// Register the builtin box classes once (idempotent; the registry dedupes).
// Called before every box/unbox so class_by_name and isinst always resolve.
void EnsureBoxClassesRegistered()
{
    il2cpp_ensure_builtin_system_classes();
}

// The Int32 box class descriptor is the builtin System_Int32_class
// (metadata.cpp; extern in il2cpp_metadata.h). A boxed Int32 can only exist
// after boxing.cpp's lazy registration ran, so no null lookup is possible --
// the pointer identity is guaranteed by the shared extern.
Il2CppClass* Int32BoxClass()
{
    return &System_Int32_class;
}

// .NET unbox failure semantics (mirrors boxing.cpp): null is
// NullReferenceException, a wrong runtime type is InvalidCastException.
// Chain format follows the transpiler's ExceptionChain convention
// (CodeGenerator.cs): "{Type},{Type},{Base1},..." -- the repeated exact type
// makes a catch of the thrown type ITSELF match (il2cpp_exception_matches
// walks the segments after the first comma), and the base segments make
// base-type catches (System.SystemException / System.Exception) match.
[[noreturn]] void ThrowUnboxNull()
{
    il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                     "Object reference not set to an instance of an object.");
}

void ThrowInvalidCast(const char* expected)
{
    il2cpp_throw_new("System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
                     expected);
}

// Payload accessors -- the shared layout contract with boxing.cpp.
int32_t BoxPayloadInt32(Il2CppObject* obj)
{
    return *reinterpret_cast<int32_t*>(reinterpret_cast<char*>(obj) + kBoxPayloadOffset);
}

double BoxPayloadDouble(Il2CppObject* obj)
{
    return *reinterpret_cast<double*>(reinterpret_cast<char*>(obj) + kBoxPayloadOffset);
}

// Allocate a boxed object whose payload is an int32 at the shared slot
// (bool/char go through the int32 form so the byte pattern is identical to
// the Int32 box). Zero-init already cleared the slot; then store the value.
Il2CppObject* AllocBoxedInt32(Il2CppClass* klass, int32_t payload)
{
    Il2CppObject* boxed = il2cpp_gc_alloc(klass);
    *reinterpret_cast<int32_t*>(reinterpret_cast<char*>(boxed) + kBoxPayloadOffset) = payload;
    return boxed;
}

} // namespace

//------------------------------------------------------------------------------
// icall_box_bool -- box a boolean as int32 0/1 (see header).
//------------------------------------------------------------------------------
Il2CppObject* icall_box_bool(bool value)
{
    EnsureBoxClassesRegistered();
    return AllocBoxedInt32(&System_Boolean_class, value ? 1 : 0);
}

//------------------------------------------------------------------------------
// icall_box_char -- box a UTF-16 code unit (see header).
//------------------------------------------------------------------------------
Il2CppObject* icall_box_char(int32_t utf16)
{
    EnsureBoxClassesRegistered();
    return AllocBoxedInt32(&System_Char_class, utf16);
}

//------------------------------------------------------------------------------
// icall_box_double -- box an IEEE-754 double (see header).
//------------------------------------------------------------------------------
Il2CppObject* icall_box_double(double value)
{
    EnsureBoxClassesRegistered();
    Il2CppObject* boxed = il2cpp_gc_alloc(&System_Double_class);
    *reinterpret_cast<double*>(reinterpret_cast<char*>(boxed) + kBoxPayloadOffset) = value;
    return boxed;
}

//------------------------------------------------------------------------------
// icall_unbox_int32 -- extract the Int32 box payload (see header).
//------------------------------------------------------------------------------
int32_t icall_unbox_int32(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBoxClassesRegistered();
    Il2CppClass* box = Int32BoxClass();
    if (box == nullptr || (obj->klass != box && !il2cpp_is_instance(obj, box)))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed Int32.");
    }
    return BoxPayloadInt32(obj);
}

//------------------------------------------------------------------------------
// icall_unbox_bool -- extract the int32 0/1 payload as a boolean (see header).
//------------------------------------------------------------------------------
bool icall_unbox_bool(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBoxClassesRegistered();
    if (obj->klass != &System_Boolean_class && !il2cpp_is_instance(obj, &System_Boolean_class))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed Boolean.");
    }
    return BoxPayloadInt32(obj) != 0;
}

//------------------------------------------------------------------------------
// icall_unbox_char -- extract the int32 UTF-16 payload (see header).
//------------------------------------------------------------------------------
int32_t icall_unbox_char(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBoxClassesRegistered();
    if (obj->klass != &System_Char_class && !il2cpp_is_instance(obj, &System_Char_class))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed Char.");
    }
    return BoxPayloadInt32(obj);
}

//------------------------------------------------------------------------------
// icall_unbox_double -- extract the IEEE-754 payload (see header).
//------------------------------------------------------------------------------
double icall_unbox_double(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowUnboxNull();
    }
    EnsureBoxClassesRegistered();
    if (obj->klass != &System_Double_class && !il2cpp_is_instance(obj, &System_Double_class))
    {
        ThrowInvalidCast("Specified cast is not valid: object is not a boxed Double.");
    }
    return BoxPayloadDouble(obj);
}

//------------------------------------------------------------------------------
// icall_boxed_equals -- value equality for boxed values (see header).
//------------------------------------------------------------------------------
bool icall_boxed_equals(Il2CppObject* a, Il2CppObject* b)
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
    EnsureBoxClassesRegistered();
    const Il2CppClass* klass = a->klass;
    if (klass == &System_Boolean_class || klass == &System_Char_class || klass == &System_Int32_class)
    {
        return BoxPayloadInt32(a) == BoxPayloadInt32(b);
    }
    if (klass == &System_Double_class)
    {
        // .NET Double.Equals semantics: NaN equals NaN, +0.0 equals -0.0
        // (IEEE == already yields true for equal magnitudes incl. -0.0/+0.0).
        const double va = BoxPayloadDouble(a);
        const double vb = BoxPayloadDouble(b);
        return va == vb || (std::isnan(va) && std::isnan(vb));
    }
    // Non-primitive box classes (strings, user objects): reference equality
    // only -- a != b was already established, so distinct instances of the
    // same non-primitive type are unequal.
    return false;
}

//------------------------------------------------------------------------------
// value Semantics Equals -- the UNBOXED primitive Equals(T) instance surface
// (D5 cov2 bx2 fix). net48 implements `int.Equals(int)` as `return this ==
// value` inline; for the value receiver the fold delivers the implicit-this
// as an ldloca ADDRESS of the slot, so each icall takes `T* self` (receiver
// address crosses with cast "") plus the value. Comparison is BITS-EQUAL for
// the integer family (so i4 covers int32/uint32/bool/char/i1/u1/i2/u2 and
// i8 covers int64/uint64 -- unsigned vs signed equality is bitwise in 2's
// complement); the floating family honors net48 Double/Single.Equals NaN
// semantics (NaN equals NaN, +0.0 equals -0.0 -- IEEE ==). Single values
// live in DOUBLE slots (the value_semantics carrier convention below).
//------------------------------------------------------------------------------
bool icall_value_equals_i4(int32_t* self, int32_t value)
{
    return *self == value;
}

// GN-N3 (p-iface): Int32.CompareTo(int32) -- the net48 sign verdict (-1/0/+1,
// NOT the raw difference). Receiver arrives as the value-slot ADDRESS (csc
// `N.CompareTo(it.N)` emits ldflda on the int field); the icall reads the
// slot and compares against the by-value argument, mirroring the
// icall_value_equals_i4 receiver ABI (cast "").
int32_t icall_int32_compare_to(int32_t* self, int32_t value)
{
    return *self < value ? -1 : (*self > value ? 1 : 0);
}

// nb9 E1: the wide primitive CompareTo(T) / CompareTo(object) entries. The
// receiver is the value-slot ADDRESS; the value argument is by value. net48
// Double/Single semantics: NaN sorts before every non-NaN and equals NaN.
namespace {
int32_t CompareDoublesNet(double a, double b)
{
    if (std::isnan(a)) return std::isnan(b) ? 0 : -1;
    if (std::isnan(b)) return 1;
    return a < b ? -1 : (a > b ? 1 : 0);
}
bool KlassNamed(const Il2CppObject* obj, const char* name)
{
    return obj != nullptr && obj->klass != nullptr && obj->klass->name != nullptr &&
           std::strcmp(obj->klass->name, name) == 0;
}
} // namespace
int32_t icall_int64_compare_to(int64_t* self, int64_t value)
{
    return *self < value ? -1 : (*self > value ? 1 : 0);
}
int32_t icall_uint64_compare_to(uint64_t* self, uint64_t value)
{
    return *self < value ? -1 : (*self > value ? 1 : 0);
}
int32_t icall_uint32_compare_to(uint32_t* self, uint32_t value)
{
    return *self < value ? -1 : (*self > value ? 1 : 0);
}
int32_t icall_double_compare_to(double* self, double value)
{
    return CompareDoublesNet(*self, value);
}
int32_t icall_single_compare_to(double* self, double value)
{
    return CompareDoublesNet(*self, value);
}
int32_t icall_int64_compare_to_obj(int64_t* self, Il2CppObject* other)
{
    if (other == nullptr) return 1;
    if (!KlassNamed(other, "System.Int64")) il2cpp_throw_argument_exception("Object must be of type Int64.");
    return icall_int64_compare_to(self, icall_unbox_long(other));
}
int32_t icall_uint64_compare_to_obj(uint64_t* self, Il2CppObject* other)
{
    if (other == nullptr) return 1;
    if (!KlassNamed(other, "System.UInt64")) il2cpp_throw_argument_exception("Object must be of type UInt64.");
    return icall_uint64_compare_to(self, static_cast<uint64_t>(icall_unbox_long(other)));
}
int32_t icall_double_compare_to_obj(double* self, Il2CppObject* other)
{
    if (other == nullptr) return 1;
    if (!KlassNamed(other, "System.Double")) il2cpp_throw_argument_exception("Object must be of type Double.");
    return icall_double_compare_to(self, icall_unbox_double(other));
}
int32_t icall_single_compare_to_obj(double* self, Il2CppObject* other)
{
    if (other == nullptr) return 1;
    if (!KlassNamed(other, "System.Single")) il2cpp_throw_argument_exception("Object must be of type Single.");
    return icall_single_compare_to(self, icall_unbox_float(other));
}
int32_t icall_int32_compare_to_obj(int32_t* self, Il2CppObject* other, const char* expectedName)
{
    if (other == nullptr) return 1;
    if (expectedName != nullptr && !KlassNamed(other, expectedName))
        il2cpp_throw_argument_exception("Object must be of the expected type.");
    // nb9 E1: unbox through the family-specific helper (the box classes are
    // distinct; icall_unbox_int32 rejects a boxed byte/char/etc.).
    int32_t v;
    bool difference = false;
    if (expectedName != nullptr && std::strcmp(expectedName, "System.Byte") == 0)
    {
        v = icall_unbox_byte(other);
        difference = true;
    }
    else if (expectedName != nullptr && std::strcmp(expectedName, "System.SByte") == 0)
    {
        v = icall_unbox_sbyte(other);
        difference = true;
    }
    else if (expectedName != nullptr && std::strcmp(expectedName, "System.Int16") == 0)
    {
        v = icall_unbox_short(other);
        difference = true;
    }
    else if (expectedName != nullptr && std::strcmp(expectedName, "System.UInt16") == 0)
    {
        v = icall_unbox_ushort(other);
        difference = true;
    }
    else if (expectedName != nullptr && std::strcmp(expectedName, "System.Char") == 0)
    {
        v = icall_unbox_char(other);
        difference = true;
    }
    else if (expectedName != nullptr && std::strcmp(expectedName, "System.Boolean") == 0)
    {
        v = icall_unbox_bool(other) ? 1 : 0;
    }
    else
    {
        v = icall_unbox_int32(other);
    }
    return difference ? (*self - v) : icall_int32_compare_to(self, v);
}

// nb9 E1: Byte/SByte/Int16/UInt16/Char.CompareTo(T) return the raw numeric
// DIFFERENCE (net48), not the -1/0/1 sign.
int32_t icall_primitive_compare_to_diff(int32_t* self, int32_t value)
{
    return *self - value;
}
int32_t icall_uint32_compare_to_obj(uint32_t* self, Il2CppObject* other, const char* expectedName)
{
    if (other == nullptr) return 1;
    if (expectedName != nullptr && !KlassNamed(other, expectedName))
        il2cpp_throw_argument_exception("Object must be of the expected type.");
    return icall_uint32_compare_to(self, static_cast<uint32_t>(icall_unbox_uint(other)));
}

bool icall_value_equals_i8(int64_t* self, int64_t value)
{
    return *self == value;
}

bool icall_value_equals_f8(double* self, double value)
{
    return *self == value || (std::isnan(*self) && std::isnan(value));
}

bool icall_value_equals_f4(double* self, double value)
{
    return *self == value || (std::isnan(*self) && std::isnan(value));
}

} // namespace runtime
} // namespace il2cpp
