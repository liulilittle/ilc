//------------------------------------------------------------------------------
// valuesemantics.cpp -- value-semantics runtime icalls (see
// il2cpp_value_semantics.h for the full hash strategy table and the dispatch
// contracts; the net48 hash VALUES are pinned by test_stringhash.cpp and
// test_valuesemantics.cpp).
//
// Consistency contract: the string hash below is BYTE-IDENTICAL to the
// hashtable runtime's HashStringOrdinal (hashtable.cpp) -- an equal string
// must hash identically whether it is a hashtable key or an Object.GetHashCode
// receiver. The address-hash path (ptr >> 3) is likewise the hashtable's
// address-key rule. If one changes, the other must follow.
//
// All klass identities are established by name comparison (strcmp on
// klass->name), the same mechanism hashtable.cpp uses for its key classes --
// the runtime's own descriptors own these names ("System.String" from
// string.cpp, "System.Int32" from boxing.cpp, "System.Boolean"/"System.Double"
// from boxingext.cpp), and the transpiled BCL descriptors carry the same
// names.
//------------------------------------------------------------------------------

#include "il2cpp_value_semantics.h"

#include "arraysort.h" // FindVirtualSlotByName (Object.Equals virtual fallback)
#include "il2cpp_delegate_ext.h" // BUG-3: icall_delegate_get_hash_code (the delegate hash branch below)
#include "enumext.h" // il2cpp_enum_is_registered / il2cpp_enum_boxed_get_hash_code /
                     // il2cpp_enum_equals (boxed-enum value hash/equality)
#include "il2cpp_exception_ext.h"
#include "il2cpp_format.h"
#include "il2cpp_metadata.h"
#include "appdomain.h" // adm1 D2: System_AppDomain_class (Object.ToString LOUD throw)
#include "il2cpp_object.h"
#include "versionuri.h" // icall_uri_to_string (untracked-Uri ToString branch below)

// decimal_format.h owns the il2cpp::runtime::Il2CppDecimal definition AND the
// T2 formatting icalls (its struct is the decimal_format.h/decimal.cpp side
// of the in-flight T1/T2 surface; decimal.h's il2cpp::Il2CppDecimal is a
// separate type -- do NOT include both).
#include "decimal_format.h"

#include <cmath>
#include <cstdint>
#include <cstring>

namespace il2cpp {
namespace runtime {

// vtype2: reflected-override dispatch helpers (defined below
// icall_object_get_hash_code -- forward-declared for that caller).
Il2CppString* TryReflectedToStringDispatch(Il2CppObject* obj);
bool TryReflectedGetHashCodeDispatch(Il2CppObject* obj, int32_t* result);

namespace {

// The boxed-value payload slot: one 8-byte-aligned slot after the object
// header (identical to boxing.cpp's Int32 box and boxingext.cpp's
// bool/double boxes -- never change one without the other).
constexpr int32_t kBoxPayloadOffset = 8;

// Klass-name identity checks (see the file header for the name contract).
bool HasKlassName(const Il2CppObject* obj, const char* name)
{
    return obj->klass != nullptr && std::strcmp(obj->klass->name, name) == 0;
}

bool IsString(const Il2CppObject* obj)    { return HasKlassName(obj, "System.String"); }
bool IsBoxedInt32(const Il2CppObject* o)  { return HasKlassName(o, "System.Int32"); }
bool IsBoxedBoolean(const Il2CppObject* o){ return HasKlassName(o, "System.Boolean"); }

// 4-byte payload at byte offset 8 (Int32 box; bool/char boxes store their
// value in the same slot).
int32_t BoxedInt32Value(const Il2CppObject* boxed)
{
    return *reinterpret_cast<const int32_t*>(
        reinterpret_cast<const char*>(boxed) + kBoxPayloadOffset);
}

// 8-byte payload at byte offset 8 (Int64 box).
int64_t BoxedInt64Value(const Il2CppObject* boxed)
{
    return *reinterpret_cast<const int64_t*>(
        reinterpret_cast<const char*>(boxed) + kBoxPayloadOffset);
}

// 8-byte payload read via memcpy (strict-aliasing-safe bit view of the
// double box; Single boxes store their widened value the same way).
double BoxedDoubleValue(const Il2CppObject* boxed)
{
    double value = 0.0;
    std::memcpy(&value, reinterpret_cast<const char*>(boxed) + kBoxPayloadOffset,
                sizeof(value));
    return value;
}

// 16-byte payload at byte offset 8 (the System.Decimal box, boxingext2.cpp:
// instanceSize 32 -- header + the aligned 16-byte value slot).
Il2CppDecimal BoxedDecimalValue(const Il2CppObject* boxed)
{
    Il2CppDecimal value;
    std::memcpy(&value, reinterpret_cast<const char*>(boxed) + kBoxPayloadOffset,
                sizeof(value));
    return value;
}

//------------------------------------------------------------------------------
// Ordinal string hash -- BYTE-IDENTICAL to hashtable.cpp's HashStringOrdinal
// (MUST stay in sync with it): EXACTLY the net48 string.GetHashCode algorithm
// (COMString::GetHashCode, fixed-seed legacy form; probe-verified against
// csc.exe net48 on 2026-08-04 -- cross-process deterministic for plain
// console apps): two DJBR-style accumulators (base 5381, step *33) walking
// the even and odd units independently, combined with the 0x5D588B65
// secondary multiplier. Unsigned arithmetic keeps the wraps well-defined (no
// signed overflow UB). The seed is the plain 5381, NOT (5381<<16)+5381 -- a
// shifted seed breaks byte-compatibility with net48 (see test_stringhash.cpp
// for the pinned oracle values). Unrandomized by design: AOT output must be
// deterministic and this hash is not a security boundary.
//------------------------------------------------------------------------------
int32_t HashStringOrdinal(const Il2CppString* s)
{
    uint32_t hash1 = 5381u;
    uint32_t hash2 = hash1;
    for (int32_t i = 0; i < s->length; i += 2)
    {
        hash1 = ((hash1 << 5) + hash1) ^ static_cast<uint32_t>(s->chars[i]);
        if (i + 1 < s->length)
        {
            hash2 = ((hash2 << 5) + hash2) ^ static_cast<uint32_t>(s->chars[i + 1]);
        }
    }
    return static_cast<int32_t>(hash1 + hash2 * 1566083941u);
}

// net48 Int64.GetHashCode: unchecked((int)value) ^ (int)(value >> 32) --
// the uint64 view makes the low/high split explicit and well-defined.
int32_t HashInt64(int64_t value)
{
    const uint64_t u = static_cast<uint64_t>(value);
    return static_cast<int32_t>(u) ^ static_cast<int32_t>(u >> 32);
}

// net48 Double.GetHashCode (probe-verified, see the header):
//   0.0 / -0.0          -> 0 (the d == 0 shortcut -- both hash the same);
//   otherwise           -> unchecked((int)bits) ^ (int)(bits >> 32).
// NaN payloads are NOT unified (net48 probe: an exotic payload hashes
// differently from double.NaN); the canonical C# NaN (0xFFF8000000000000,
// produced by every common NaN source on x86) hashes to -524288.
int32_t HashDouble(double value)
{
    if (value == 0.0)
    {
        return 0;
    }
    uint64_t bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    return static_cast<int32_t>(bits) ^ static_cast<int32_t>(bits >> 32);
}

// .NET ordinal string equality: equal unit counts AND equal units.
bool StringOrdinalEquals(const Il2CppString* a, const Il2CppString* b)
{
    return a->length == b->length &&
           std::memcmp(a->chars, b->chars,
                       static_cast<size_t>(a->length) * sizeof(char16_t)) == 0;
}

// Value equality of two boxes whose klass names are IDENTICAL (the caller
// proves the names match first -- .NET value-type Equals never compares
// across different box types, probe: boxed 5 vs boxed 5L is false):
//   * Double / Single: value comparison with the NaN == NaN special case
//     (net48 Double.Equals returns TRUE for two NaNs -- probe-verified;
//     Single boxes store their widened value in the 8-byte slot);
//   * Int64 / UInt64: 8-byte slot comparison;
//   * Int32/Boolean/Char/Byte/SByte/Int16/UInt16/UInt32: 4-byte slot
//     comparison (bool/char store their value in the int32 slot);
//   * Decimal: value equality through icall_decimal_equals (the 16-byte
//     payload; net48 object.Equals(1.5m, (object)1.5m) is TRUE --
//     probe-verified 2026-08-10);
//   * any other same-named type: no value equality (reference identity was
//     already handled by the caller).
bool BoxedValueEquals(const Il2CppObject* a, const Il2CppObject* b)
{
    if (HasKlassName(a, "System.Double") || HasKlassName(a, "System.Single"))
    {
        const double da = BoxedDoubleValue(a);
        const double db = BoxedDoubleValue(b);
        return da == db || (std::isnan(da) && std::isnan(db));
    }
    if (HasKlassName(a, "System.Int64") || HasKlassName(a, "System.UInt64"))
    {
        return BoxedInt64Value(a) == BoxedInt64Value(b);
    }
    if (HasKlassName(a, "System.Decimal"))
    {
        const Il2CppDecimal da = BoxedDecimalValue(a);
        const Il2CppDecimal db = BoxedDecimalValue(b);
        return icall_decimal_equals(&da, &db);
    }
    if (HasKlassName(a, "System.Int32") ||
        HasKlassName(a, "System.Boolean") ||
        HasKlassName(a, "System.Char") ||
        HasKlassName(a, "System.Byte") ||
        HasKlassName(a, "System.SByte") ||
        HasKlassName(a, "System.Int16") ||
        HasKlassName(a, "System.UInt16") ||
        HasKlassName(a, "System.UInt32"))
    {
        return BoxedInt32Value(a) == BoxedInt32Value(b);
    }
    // Boxed enums (icall_box_enum stamps the enum's own name-bearing
    // descriptor): net48 object.Equals on two boxed enums of the SAME type
    // compares their underlying VALUES (probe: EInt.A vs EInt.A -> True,
    // EInt.A vs EInt.B -> False, EInt vs ELong -> False -- the klass-name
    // match already gates the cross-type case). The enumext registry's
    // width-aware boxed-enum equality is the single source of truth.
    if (il2cpp_enum_is_registered(a->klass))
    {
        return il2cpp_enum_equals(const_cast<Il2CppObject*>(a),
                                  const_cast<Il2CppObject*>(b)) != 0;
    }
    return false; // same-named non-primitive: no value equality
}

// baudit2: does the receiver's class chain reach System.ValueType (a boxed
// user value type)? Determines the `this` ABI of a reflected override: the
// generated value-type method takes the box PAYLOAD address, a reference
// type takes the object itself.
bool IsValueTypeKlass(const Il2CppClass* klass)
{
    for (const Il2CppClass* cur = klass; cur != nullptr; cur = cur->baseClass)
    {
        if (cur == &System_ValueType_class)
        {
            return true;
        }
    }
    return false;
}

// baudit2: net48 static Object.Equals(a, b) IS the VIRTUAL a.Equals(b) -- the
// reflected Equals(object) override (picked by the (r:System.Object)
// signature, never the IEquatable<T> overload's slot) is dispatched through
// the receiver's vtable. Returns the override's 0/1 result, or -1 when the
// type has no reflected override (the caller keeps its fast paths / reference
// fallback). The receiver is the box PAYLOAD address for a value type (the
// il2cpp_unbox_user_struct contract) and the object itself for a class.
int32_t TryReflectedEqualsDispatch(Il2CppObject* a, Il2CppObject* b)
{
    const int32_t slot = FindVirtualSlotByNameAndSignature(a, "Equals", "(r:System.Object)");
    if (slot < 0 || a->klass->vtable == nullptr || slot >= a->klass->vtableSize ||
        a->klass->vtable[slot] == nullptr)
    {
        return -1;
    }
    using EqualsFn = bool (*)(Il2CppObject*, Il2CppObject*);
    Il2CppObject* receiver = IsValueTypeKlass(a->klass)
        ? reinterpret_cast<Il2CppObject*>(reinterpret_cast<char*>(a) + kBoxPayloadOffset)
        : a;
    return reinterpret_cast<EqualsFn>(a->klass->vtable[slot])(receiver, b) ? 1 : 0;
}

// Null-receiver guard (defensive contract, mirrors stringext.cpp/hashtable.cpp):
// generated code null-checks first; the icall turns a null into the .NET NRE.
void ThrowIfNull(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

// Decimal text of a uint64 BEYOND int64 range (values <= INT64_MAX go through
// icall_int64_to_string): a manual digit loop with the same shape as
// format.cpp's FormatInt64Decimal -- if format.cpp's loop changes, this must
// follow (the string factory contract is identical: ASCII digits transcode
// 1:1 through il2cpp_string_new_from_utf8).
Il2CppString* UInt64ToString(uint64_t value)
{
    char buf[24]; // max 20 digits + terminator
    char* p = buf + sizeof(buf);
    *--p = '\0';
    do
    {
        *--p = static_cast<char>('0' + (value % 10));
        value /= 10;
    } while (value != 0);
    return il2cpp_string_new_from_utf8(p);
}

} // namespace

//------------------------------------------------------------------------------
// icall_object_get_hash_code -- Object.GetHashCode virtual dispatch:
//   string           -> ordinal content hash (hashtable-consistent);
//   boxed primitives -> the net48 VALUE-based per-type hash (GAP-BOX-6 fill,
//                       probe-verified against csc.exe net48 on 2026-08-10):
//     Int32    -> the value;  UInt32 -> the full 32-bit pattern;
//     Boolean  -> 1 / 0;      Char   -> c | (c << 16) (net48 Char.GetHashCode);
//     Byte     -> value;      UInt16 -> value;
//     SByte    -> v ^ (v << 8)  (v = sign-extended low 8 bits);
//     Int16    -> (ushort)v | (v << 16)  (v = sign-extended low 16 bits);
//     Int64/UInt64 -> low 32 XOR high 32 (HashInt64);
//     Double   -> bits XOR halves with the d == 0 shortcut (HashDouble);
//     Single   -> the 32-bit float pattern (widened double narrowed back,
//                 exact round-trip), with the f == 0 shortcut;
//     Decimal  -> icall_decimal_get_hash_code (the 16-byte payload);
//   anything else    -> address hash (ptr >> 3, the hashtable address rule);
//   null             -> NullReferenceException.
//------------------------------------------------------------------------------
int32_t icall_object_get_hash_code(Il2CppObject* obj)
{
    ThrowIfNull(obj);
    if (IsString(obj))
    {
        return HashStringOrdinal(reinterpret_cast<const Il2CppString*>(obj));
    }
    if (obj->klass == nullptr || obj->klass->name == nullptr)
    {
        // Malformed descriptor: safest fallback is the address hash.
        return static_cast<int32_t>(reinterpret_cast<uintptr_t>(obj) >> 3);
    }
    if (IsBoxedInt32(obj))
    {
        return BoxedInt32Value(obj);
    }
    if (IsBoxedBoolean(obj))
    {
        return BoxedInt32Value(obj) != 0 ? 1 : 0;
    }
    if (HasKlassName(obj, "System.Char"))
    {
        // net48 Char.GetHashCode: (int)c | ((int)c << 16) -- probe-verified
        // ('A' -> 4259905, '\0' -> 0).
        const int32_t c = BoxedInt32Value(obj);
        return c | (c << 16);
    }
    if (HasKlassName(obj, "System.Byte"))
    {
        return BoxedInt32Value(obj) & 0xFF;
    }
    if (HasKlassName(obj, "System.SByte"))
    {
        // net48 SByte.GetHashCode: v ^ (v << 8) with v = the signed 8-bit value
        // (probe: (sbyte)-5 -> 1275, (sbyte)127 -> 32639).
        const int32_t v = static_cast<int8_t>(BoxedInt32Value(obj));
        return v ^ (v << 8);
    }
    if (HasKlassName(obj, "System.Int16"))
    {
        // net48 Int16.GetHashCode: (ushort)v | (v << 16) with v = the signed
        // 16-bit value (probe: (short)-12 -> -720908, 32767 -> 2147450879).
        const int32_t v = static_cast<int16_t>(BoxedInt32Value(obj));
        return static_cast<uint16_t>(v) | (v << 16);
    }
    if (HasKlassName(obj, "System.UInt16"))
    {
        return BoxedInt32Value(obj) & 0xFFFF;
    }
    if (HasKlassName(obj, "System.UInt32"))
    {
        // The box stores the full 32-bit pattern; net48 UInt32.GetHashCode is
        // the value itself (probe: 4000000000u -> -294967296, max -> -1).
        return BoxedInt32Value(obj);
    }
    if (HasKlassName(obj, "System.Int64") || HasKlassName(obj, "System.UInt64"))
    {
        return HashInt64(BoxedInt64Value(obj));
    }
    if (HasKlassName(obj, "System.Double"))
    {
        return HashDouble(BoxedDoubleValue(obj));
    }
    if (HasKlassName(obj, "System.Single"))
    {
        // net48 Single.GetHashCode is the 32-bit float pattern (probe:
        // 1.5f -> 1069547520 = 0x3FC00000, NaN -> 0xFFC00000) with the f == 0
        // shortcut (-0.0f -> 0). The Single box stores the WIDENED double;
        // narrowing back to float is exact (float -> double -> float
        // round-trips every float bit pattern).
        const float f = static_cast<float>(BoxedDoubleValue(obj));
        int32_t bits = 0;
        std::memcpy(&bits, &f, sizeof(bits));
        return f == 0.0f ? 0 : bits;
    }
    if (HasKlassName(obj, "System.Decimal"))
    {
        Il2CppDecimal value = BoxedDecimalValue(obj);
        return icall_decimal_get_hash_code(&value);
    }
    // BUG-3 (batch 177b): DELEGATES (single-cast klass "System.Delegate",
    // multicast container "System.MulticastDelegate") must NOT fall into the
    // address-hash fallback (ptr >> 3) below -- a compacting GC moves the
    // object, so the hash would change across collections. The net48 delegate
    // hash is type-derived and GC-stable (probe-verified: same method +
    // different targets -> same hash); the runtime delegate hash (delegateext
    // icall_delegate_get_hash_code) is FNV-1a over the flattened methodPtr
    // list -- code addresses never move, and equal delegates (same
    // methodPtr+target per leaf) hash identically. The container name is the
    // g_multicastClass descriptor's own name (delegateext.cpp).
    if (HasKlassName(obj, "System.Delegate") || HasKlassName(obj, "System.MulticastDelegate"))
    {
        return icall_delegate_get_hash_code(obj);
    }
    // Boxed enums (icall_box_enum stamps the enum's own name-bearing
    // descriptor): net48 Enum.GetHashCode is the UNDERLYING value's hash --
    // the value itself for Int32-width enums, low^high for Int64-backed
    // (probe: (object)EInt.A -> 1, (object)ELong.X -> 5). The enumext
    // registry's width-aware boxed-enum hash is the single source of truth.
    if (il2cpp_enum_is_registered(obj->klass))
    {
        return il2cpp_enum_boxed_get_hash_code(obj);
    }
    // vtype2: a boxed USER STRUCT (or a translated class) with its own
    // GetHashCode override dispatches the override (net48 virtual call); a
    // type without one keeps the address-hash default below.
    int32_t reflectedHash = 0;
    if (TryReflectedGetHashCodeDispatch(obj, &reflectedHash))
    {
        return reflectedHash;
    }
    return static_cast<int32_t>(reinterpret_cast<uintptr_t>(obj) >> 3);
}

//------------------------------------------------------------------------------
// vtype2: net48 Object.ToString / Object.GetHashCode on a BOXED USER VALUE TYPE
// is the VIRTUAL dispatch to the struct's OWN override, not the klass-name /
// address fallbacks. The reflected method table (typeinfo.h) carries the
// override and its vtable slot; the receiver is the box PAYLOAD address (the
// generated value-type method takes the value's address -- the
// il2cpp_unbox_user_struct contract), exactly like TryReflectedEqualsDispatch.
// Returns nullptr / false when the type declares no reflected override, so the
// caller keeps its pre-existing fallback (a struct without an override prints
// its type name / hashes by address, which is the net48 default behaviour).
//------------------------------------------------------------------------------
Il2CppString* TryReflectedToStringDispatch(Il2CppObject* obj)
{
    if (obj == nullptr || obj->klass == nullptr)
    {
        return nullptr;
    }
    // "r:System.String()" matches ONLY the parameterless ToString -- a
    // ToString(string format) / ToString(IFormatProvider) signature carries a
    // non-empty parameter list and never matches.
    const int32_t slot = FindVirtualSlotByNameAndSignature(obj, "ToString", "r:System.String()");
    if (slot < 0 || obj->klass->vtable == nullptr || slot >= obj->klass->vtableSize ||
        obj->klass->vtable[slot] == nullptr)
    {
        return nullptr;
    }
    using ToStringFn = Il2CppString* (*)(Il2CppObject*);
    Il2CppObject* receiver = IsValueTypeKlass(obj->klass)
        ? reinterpret_cast<Il2CppObject*>(reinterpret_cast<char*>(obj) + kBoxPayloadOffset)
        : obj;
    return reinterpret_cast<ToStringFn>(obj->klass->vtable[slot])(receiver);
}

bool TryReflectedGetHashCodeDispatch(Il2CppObject* obj, int32_t* result)
{
    if (obj == nullptr || obj->klass == nullptr)
    {
        return false;
    }
    const int32_t slot = FindVirtualSlotByNameAndSignature(obj, "GetHashCode", "i()");
    if (slot < 0 || obj->klass->vtable == nullptr || slot >= obj->klass->vtableSize ||
        obj->klass->vtable[slot] == nullptr)
    {
        return false;
    }
    using GetHashCodeFn = int32_t (*)(Il2CppObject*);
    Il2CppObject* receiver = IsValueTypeKlass(obj->klass)
        ? reinterpret_cast<Il2CppObject*>(reinterpret_cast<char*>(obj) + kBoxPayloadOffset)
        : obj;
    *result = reinterpret_cast<GetHashCodeFn>(obj->klass->vtable[slot])(receiver);
    return true;
}

//------------------------------------------------------------------------------
// icall_object_to_string -- Object.ToString virtual dispatch:
//   string           -> the string ITSELF (identity);
//   boxed primitives -> the value's .NET text (the format.cpp icalls):
//     Int32 / Byte / SByte / Int16 / UInt16 -> decimal (int32 payload);
//     UInt32                                -> decimal via int64 (the full
//                                             0..2^32-1 range);
//     Int64                                 -> decimal (8-byte payload);
//     UInt64                                -> decimal (8-byte payload;
//                                             values beyond int64 range go
//                                             through the local digit loop);
//     Boolean                               -> "True" / "False" (int32
//                                              payload 0/1);
//     Char                                  -> the UTF-16 unit as a 1-unit
//                                             string (int32 payload);
//     Double / Single                       -> net48 "G15" text (8-byte
//                                             widened payload);
//   any other object   -> the type's full name (klass->name), EXCEPT an
//                          untracked System.Uri, which renders its URL text
//                          (icall_uri_to_string -- diffuri001 tostring.queue:
//                          Queue<Uri>.Peek() returns object-typed, so the
//                          resolver cannot fold the Uri arm and lands here;
//                          net48 prints the URL, never "System.Uri");
//   null               -> NullReferenceException.
//------------------------------------------------------------------------------
Il2CppString* icall_object_to_string(Il2CppObject* obj)
{
    ThrowIfNull(obj);
    if (IsString(obj))
    {
        return reinterpret_cast<Il2CppString*>(obj);
    }
    if (IsBoxedInt32(obj))
    {
        return icall_int32_to_string(BoxedInt32Value(obj));
    }
    if (HasKlassName(obj, "System.Int64"))
    {
        return icall_int64_to_string(BoxedInt64Value(obj));
    }
    if (HasKlassName(obj, "System.UInt64"))
    {
        const uint64_t u = static_cast<uint64_t>(BoxedInt64Value(obj));
        return u <= static_cast<uint64_t>(INT64_MAX)
                   ? icall_int64_to_string(static_cast<int64_t>(u))
                   : UInt64ToString(u);
    }
    if (HasKlassName(obj, "System.Double") ||
        HasKlassName(obj, "System.Single"))
    {
        return icall_double_to_string(BoxedDoubleValue(obj));
    }
    if (IsBoxedBoolean(obj))
    {
        return icall_bool_to_string(BoxedInt32Value(obj) != 0);
    }
    if (HasKlassName(obj, "System.Char"))
    {
        return icall_char_to_string(BoxedInt32Value(obj));
    }
    if (HasKlassName(obj, "System.UInt32"))
    {
        return icall_int64_to_string(static_cast<int64_t>(
            static_cast<uint32_t>(BoxedInt32Value(obj))));
    }
    if (HasKlassName(obj, "System.Byte"))
    {
        return icall_int32_to_string(BoxedInt32Value(obj) & 0xFF);
    }
    if (HasKlassName(obj, "System.SByte"))
    {
        // The box stores the masked 8-bit pattern (boxingext2.cpp); ToString
        // needs the signed interpretation.
        return icall_int32_to_string(
            static_cast<int8_t>(BoxedInt32Value(obj)));
    }
    if (HasKlassName(obj, "System.Int16"))
    {
        // Same masked-payload contract: sign-extend the low 16 bits.
        return icall_int32_to_string(
            static_cast<int16_t>(BoxedInt32Value(obj)));
    }
    if (HasKlassName(obj, "System.UInt16"))
    {
        return icall_int32_to_string(BoxedInt32Value(obj) & 0xFFFF);
    }
    if (HasKlassName(obj, "System.Decimal"))
    {
        // decimal-domain T3: the boxed decimal formats via the G-default
        // icall (decimal_format.cpp) on the 16-byte payload.
        Il2CppDecimal value = BoxedDecimalValue(obj);
        return icall_decimal_to_string(&value);
    }
    if (il2cpp_enum_is_registered(obj->klass))
    {
        // boxed enums -> the member NAME (or the numeric/builtin fallback
        // inside il2cpp_enum_boxed_to_string for unnamed values): net48
        // Object.ToString on a boxed enum prints the member name, and
        // Console.WriteLine(object) routes here (batch 148 enumparseic).
        return il2cpp_enum_boxed_to_string(obj);
    }
    // vtype2: a BOXED USER STRUCT with its own ToString override dispatches
    // the override (net48 virtual call); a struct without one falls through
    // to the klass-name text below, which IS the net48 ValueType.ToString
    // result (the type's full name).
    if (Il2CppString* reflected = TryReflectedToStringDispatch(obj))
    {
        return reflected;
    }
    if (HasKlassName(obj, "System.Uri"))
    {
        return reinterpret_cast<Il2CppString*>(icall_uri_to_string(obj));
    }
    if (obj->klass == &System_AppDomain_class)
    {
        // adm1 D2 fix: Object.ToString on the AppDomain singleton GENUINELY
        // throws NotSupportedException (the adm1 LOUD contract). A real
        // `ad.ToString()` call site resolves to this fallback (Roslyn emits
        // callvirt Object::ToString and the resolver path carries no tracked
        // receiver), and printing the klass name ("System.AppDomain") was the
        // false-LOUD the D2 review flagged -- the runtime has no localized
        // name implementation, so the honest surface is the throw.
        il2cpp_throw_not_supported_exception(
            "AppDomain.ToString requires a culture-localized name (W7: the net48 zh-CN text is not implemented).");
    }
    return il2cpp_string_new_from_utf8(obj->klass->name);
}

//------------------------------------------------------------------------------
// icall_object_equals -- static Object.Equals(a, b):
//   reference identity wins first (a == b, incl. null == null);
//   two strings        -> ordinal content equality;
//   two boxes with the SAME klass name -> value equality (see
//     BoxedValueEquals -- double NaN == NaN is TRUE, net48);
//   everything else    -> false.
//------------------------------------------------------------------------------
bool icall_object_equals(Il2CppObject* a, Il2CppObject* b)
{
    if (a == b)
    {
        return true;
    }
    if (a == nullptr || b == nullptr)
    {
        return false;
    }
    if (IsString(a) && IsString(b))
    {
        return StringOrdinalEquals(reinterpret_cast<const Il2CppString*>(a),
                                   reinterpret_cast<const Il2CppString*>(b));
    }
    if (a->klass != nullptr && b->klass != nullptr &&
        std::strcmp(a->klass->name, b->klass->name) == 0)
    {
        // baudit2: a same-klass receiver with a reflected Equals(object)
        // override is that override's call -- NOT a boxed-value-table miss
        // (the old early return made user types fall through to `false`).
        // Only the builtin value paths (primitive/decimal/enum boxes) have
        // no typeinfo, so they keep BoxedValueEquals untouched.
        const int32_t reflectedSame = TryReflectedEqualsDispatch(a, b);
        if (reflectedSame >= 0)
        {
            return reflectedSame != 0;
        }
        return BoxedValueEquals(a, b);
    }
    // baudit2: net48 static Object.Equals(a, b) IS the virtual
    // a.Equals(b) -- once the builtin fast paths (identity / string /
    // same-klass boxes) miss, dispatch the receiver's reflected Equals
    // virtual (a user override; the typeinfo carries the vtable slot).
    const int32_t reflectedCross = TryReflectedEqualsDispatch(a, b);
    if (reflectedCross >= 0)
    {
        return reflectedCross != 0;
    }
    return false;
}

//------------------------------------------------------------------------------
// icall_int32_get_hash_code -- Int32.GetHashCode: the value itself.
//------------------------------------------------------------------------------
int32_t icall_int32_get_hash_code(int32_t value)
{
    return value;
}

//------------------------------------------------------------------------------
// icall_int64_get_hash_code -- Int64.GetHashCode: low 32 ^ high 32.
//------------------------------------------------------------------------------
int32_t icall_int64_get_hash_code(int64_t value)
{
    return HashInt64(value);
}

//------------------------------------------------------------------------------
// icall_double_get_hash_code -- Double.GetHashCode (see HashDouble).
//------------------------------------------------------------------------------
int32_t icall_double_get_hash_code(double value)
{
    return HashDouble(value);
}

//------------------------------------------------------------------------------
// icall_string_get_hash_code -- string.GetHashCode: ordinal content hash.
//------------------------------------------------------------------------------
int32_t icall_string_get_hash_code(Il2CppObject* str)
{
    ThrowIfNull(str);
    return HashStringOrdinal(reinterpret_cast<const Il2CppString*>(str));
}

//------------------------------------------------------------------------------
// icall_bool_get_hash_code -- Boolean.GetHashCode: true -> 1, false -> 0.
//------------------------------------------------------------------------------
int32_t icall_bool_get_hash_code(bool value)
{
    return value ? 1 : 0;
}

//------------------------------------------------------------------------------
// icall_value_type_get_hash_code -- System.ValueType.GetHashCode default
// implementation for a translated value type (see il2cpp_value_semantics.h
// for the field-kind contract and the documented-net48 semantic baseline).
// The transpiler injects typeHash + fieldKind as compile-time constants (the
// runtime has no value-type descriptors); 'data' is the value-type
// receiver's ADDRESS whose FIRST field always sits at offset 0.
//------------------------------------------------------------------------------
int32_t icall_value_type_get_hash_code(int32_t typeHash, int32_t fieldKind, const void* data)
{
    switch (fieldKind)
    {
        case 0:
            // No instance fields: the type hash the transpiler computed from
            // the full type name (djb2) -- net48's "no fields -> type hash".
            return typeHash;
        case 1:
            // First field is a 4-byte value slot: its GetHashCode is the
            // value itself (Int32.GetHashCode identity; bool/char/byte slots
            // store 0/1 and the UTF-16 unit -- both already int-canonical).
            return *reinterpret_cast<const int32_t*>(data);
        case 2:
        {
            // First field is a GC reference: hash the pointed object through
            // the Object.GetHashCode dispatch (string -> ordinal hash, boxed
            // Int32 -> value, other -> address hash); a null field -> 0.
            Il2CppObject* obj = *reinterpret_cast<Il2CppObject* const*>(data);
            return obj != nullptr ? icall_object_get_hash_code(obj) : 0;
        }
        default:
            // The transpiler only emits 0/1/2; anything else is a caller bug.
            // Fall through to the value path (defensive -- never UB).
            return *reinterpret_cast<const int32_t*>(data);
    }
}

} // namespace runtime
} // namespace il2cpp
