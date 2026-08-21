//------------------------------------------------------------------------------
// test_boxingext2.cpp -- boxing/unboxing icall unit tests for the remaining
// primitives (byte/sbyte/short/ushort/uint/long/ulong/float).
//
// Coverage (normal + edge + error paths):
//   1. byte round-trip: 0 / 255 / 128 / 0xAA; payload is the zero-extended
//      low 8 bits at offset 8 (layout check).
//   2. sbyte round-trip: 0 / 127 / -128 / -1; low 8-bit SIGNED interpretation
//      (box_sbyte(0xFF) unboxes to -1, box_sbyte(128) to -128).
//   3. short round-trip: 0 / 32767 / -32768 / -1; low 16-bit SIGNED
//      interpretation (box_short(0xFFFF) unboxes to -1).
//   4. ushort round-trip: 0 / 65535 / 0x8000 (zero-extended low 16 bits).
//   5. uint round-trip: 0 / UINT32_MAX / 0x80000000 / 0xDEADBEEF; the full
//      32-bit pattern in the int32 slot (no masking) at offset 8.
//   6. long round-trip: 0 / INT64_MIN / INT64_MAX / -1; the 8-byte payload
//      slot at offset 8 (layout check) and instanceSize 16.
//   7. ulong round-trip: 0 / UINT64_MAX / 0x8000000000000000.
//   8. float round-trip: 1.5 / FLT_MAX / NaN / -0.0 (stored as double at the
//      aligned slot; sign bit preserved; instanceSize 16).
//   9. Layout compatibility with the Int32 box (boxing.cpp contract): every
//      new box class has instanceSize 16, payload at offset 8, refFieldBitmap
//      0, and is registered under the primitive's full type name after first
//      use.
//   10. Type mismatch -> InvalidCastException (cross-box-pair matrix).
//   11. Null unbox -> NullReferenceException (all eight unbox icalls).
//   12. icall_boxed_equals_ext2: per-type value equality (incl. uint/long/
//       ulong extremes), .NET Single.Equals NaN / -0.0 semantics, cross-type
//       inequality, null handling, and delegation to the legacy
//       icall_boxed_equals (int32/bool/char/double + string reference
//       equality).
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_boxing_ext2.h"

#include "il2cpp_boxing_ext.h" // icall_boxed_equals (legacy delegation checks)
#include "il2cpp_exception.h"
#include "il2cpp_runtime.h" // il2cpp_box_int32 / il2cpp_class_by_name
#include "il2cpp_string.h"  // il2cpp_string_new_from_cs

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Run 'fn' and report whether it threw a DotNetException whose type string
// (with base chain) exactly matches 'expectedType'.
template <typename Fn>
bool ThrowsDotNet(Fn&& fn, const char* expectedType)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedType) == 0;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

constexpr int32_t kPayloadOffset = 8;

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    const int64_t int64Min = std::numeric_limits<int64_t>::min();
    const int64_t int64Max = std::numeric_limits<int64_t>::max();
    const uint64_t uint64Max = std::numeric_limits<uint64_t>::max();
    const double nan = std::numeric_limits<double>::quiet_NaN();
    const float fltMax = std::numeric_limits<float>::max();

    // --- 1. byte round-trip + payload layout ---
    Expect(icall_unbox_byte(icall_box_byte(0)) == 0, "boxed byte 0 round-trips");
    Expect(icall_unbox_byte(icall_box_byte(255)) == 255, "boxed byte 255 round-trips");
    Expect(icall_unbox_byte(icall_box_byte(128)) == 128, "boxed byte 128 round-trips");
    Expect(icall_unbox_byte(icall_box_byte(0xAA)) == 0xAA, "boxed byte 0xAA round-trips");
    Il2CppObject* b255 = icall_box_byte(255);
    Expect(*reinterpret_cast<int32_t*>(reinterpret_cast<char*>(b255) + kPayloadOffset) == 255,
           "byte box payload is the zero-extended low 8 bits at offset 8");

    // --- 2. sbyte round-trip (low 8-bit SIGNED interpretation) ---
    Expect(icall_unbox_sbyte(icall_box_sbyte(0)) == 0, "boxed sbyte 0 round-trips");
    Expect(icall_unbox_sbyte(icall_box_sbyte(127)) == 127, "boxed sbyte 127 round-trips");
    Expect(icall_unbox_sbyte(icall_box_sbyte(-128)) == -128, "boxed sbyte -128 round-trips");
    Expect(icall_unbox_sbyte(icall_box_sbyte(-1)) == -1, "boxed sbyte -1 round-trips");
    Expect(icall_unbox_sbyte(icall_box_sbyte(0xFF)) == -1, "sbyte 0xFF input unboxes to -1 (signed low bits)");
    Expect(icall_unbox_sbyte(icall_box_sbyte(128)) == -128, "sbyte 128 input unboxes to -128 (signed low bits)");
    Il2CppObject* sbNeg = icall_box_sbyte(-1);
    Expect(*reinterpret_cast<int32_t*>(reinterpret_cast<char*>(sbNeg) + kPayloadOffset) == 0xFF,
           "sbyte -1 box payload is the zero-extended low 8 bits (0xFF)");

    // --- 3. short round-trip (low 16-bit SIGNED interpretation) ---
    Expect(icall_unbox_short(icall_box_short(0)) == 0, "boxed short 0 round-trips");
    Expect(icall_unbox_short(icall_box_short(32767)) == 32767, "boxed short 32767 round-trips");
    Expect(icall_unbox_short(icall_box_short(-32768)) == -32768, "boxed short -32768 round-trips");
    Expect(icall_unbox_short(icall_box_short(-1)) == -1, "boxed short -1 round-trips");
    Expect(icall_unbox_short(icall_box_short(0xFFFF)) == -1, "short 0xFFFF input unboxes to -1 (signed low bits)");

    // --- 4. ushort round-trip (zero-extended low 16 bits) ---
    Expect(icall_unbox_ushort(icall_box_ushort(0)) == 0, "boxed ushort 0 round-trips");
    Expect(icall_unbox_ushort(icall_box_ushort(65535)) == 65535, "boxed ushort 65535 round-trips");
    Expect(icall_unbox_ushort(icall_box_ushort(0x8000)) == 0x8000, "boxed ushort 0x8000 round-trips");

    // --- 5. uint round-trip (full 32-bit pattern in the int32 slot) ---
    const int32_t uint32MaxAsI32 = static_cast<int32_t>(0xFFFFFFFFu);
    const int32_t uint32HalfAsI32 = static_cast<int32_t>(0x80000000u);
    Expect(icall_unbox_uint(icall_box_uint(0)) == 0, "boxed uint 0 round-trips");
    Expect(static_cast<uint32_t>(icall_unbox_uint(icall_box_uint(uint32MaxAsI32))) == 0xFFFFFFFFu,
           "boxed uint UINT32_MAX round-trips (unsigned reinterpretation)");
    Expect(static_cast<uint32_t>(icall_unbox_uint(icall_box_uint(uint32HalfAsI32))) == 0x80000000u,
           "boxed uint 0x80000000 round-trips (unsigned reinterpretation)");
    Expect(icall_unbox_uint(icall_box_uint(static_cast<int32_t>(0xDEADBEEFu))) == static_cast<int32_t>(0xDEADBEEFu),
           "boxed uint 0xDEADBEEF round-trips (bit pattern preserved)");
    Il2CppObject* uBig = icall_box_uint(uint32MaxAsI32);
    Expect(*reinterpret_cast<int32_t*>(reinterpret_cast<char*>(uBig) + kPayloadOffset) == -1,
           "uint box payload is the full 32-bit pattern at offset 8 (0xFFFFFFFF == -1 as int32)");
    Expect(uBig->klass->instanceSize == 16, "uint box instanceSize is 16 (header + aligned slot)");

    // --- 6. long round-trip + 8-byte payload slot layout ---
    Expect(icall_unbox_long(icall_box_long(0)) == 0, "boxed long 0 round-trips");
    Expect(icall_unbox_long(icall_box_long(int64Min)) == int64Min, "boxed long INT64_MIN round-trips");
    Expect(icall_unbox_long(icall_box_long(int64Max)) == int64Max, "boxed long INT64_MAX round-trips");
    Expect(icall_unbox_long(icall_box_long(-1)) == -1, "boxed long -1 round-trips");
    Expect(icall_unbox_long(icall_box_long(123456789012345LL)) == 123456789012345LL,
           "boxed long 123456789012345 round-trips");
    Il2CppObject* lBig = icall_box_long(int64Max);
    Expect(*reinterpret_cast<int64_t*>(reinterpret_cast<char*>(lBig) + kPayloadOffset) == int64Max,
           "long box payload is the full 8-byte int64 at offset 8");
    Expect(lBig->klass->instanceSize == 16, "long box instanceSize is 16 (8-byte header + 8-byte slot)");

    // --- 7. ulong round-trip ---
    Expect(icall_unbox_ulong(icall_box_ulong(0)) == 0, "boxed ulong 0 round-trips");
    Expect(icall_unbox_ulong(icall_box_ulong(uint64Max)) == uint64Max, "boxed ulong UINT64_MAX round-trips");
    Expect(icall_unbox_ulong(icall_box_ulong(0x8000000000000000ULL)) == 0x8000000000000000ULL,
           "boxed ulong 0x8000000000000000 round-trips");

    // --- 8. float round-trip (stored as double at the aligned slot) ---
    Expect(icall_unbox_float(icall_box_float(1.5)) == 1.5, "boxed float 1.5 round-trips (as double)");
    Expect(icall_unbox_float(icall_box_float(static_cast<double>(fltMax))) == static_cast<double>(fltMax),
           "boxed FLT_MAX round-trips losslessly (float -> double is exact)");
    Expect(std::isnan(icall_unbox_float(icall_box_float(nan))), "boxed float NaN round-trips as NaN");
    Expect(std::signbit(icall_unbox_float(icall_box_float(-0.0))), "boxed float -0.0 preserves its sign bit");
    Il2CppObject* f15 = icall_box_float(1.5);
    Expect(*reinterpret_cast<double*>(reinterpret_cast<char*>(f15) + kPayloadOffset) == 1.5,
           "float box payload is the double 1.5 at offset 8");
    Expect(f15->klass->instanceSize == 16, "float box instanceSize is 16 (header + aligned double slot)");

    // --- 9. layout compatibility with the Int32 box (boxing.cpp contract) ---
    Il2CppObject* i42 = il2cpp_box_int32(nullptr, 42);
    Il2CppObject* boxes[] = { b255, sbNeg, icall_box_short(-1), icall_box_ushort(65535),
                              uBig, lBig, icall_box_ulong(uint64Max), f15 };
    const char* names[] = { "System.Byte", "System.SByte", "System.Int16", "System.UInt16",
                            "System.UInt32", "System.Int64", "System.UInt64", "System.Single" };
    for (int i = 0; i < 8; ++i)
    {
        Expect(boxes[i]->klass->instanceSize == 16 && boxes[i]->klass->refFieldBitmap == 0u,
               "every new box class: 16 bytes, no reference fields");
        Expect(std::strcmp(boxes[i]->klass->name, names[i]) == 0, "new box class identity matches .NET full name");
        Expect(il2cpp_class_by_name(names[i]) != nullptr, "new box class registered after first use");
    }
    Expect(i42->klass->instanceSize == 16, "Int32 box stays 16 bytes (shared layout contract)");

    // --- 10. type mismatch -> InvalidCastException (cross-box-pair matrix) ---
    Expect(ThrowsDotNet([&] { icall_unbox_byte(i42); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed int32 as byte throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_sbyte(i42); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed int32 as sbyte throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_short(i42); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed int32 as short throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_ushort(i42); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed int32 as ushort throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_uint(i42); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed int32 as uint throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_long(i42); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed int32 as long throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_ulong(i42); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed int32 as ulong throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_float(i42); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed int32 as float throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_uint(b255); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed byte as uint throws InvalidCastException (class identity)");
    Expect(ThrowsDotNet([&] { icall_unbox_ushort(uBig); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed uint as ushort throws InvalidCastException (class identity)");
    Expect(ThrowsDotNet([&] { icall_unbox_long(b255); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed byte as long throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_byte(lBig); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed long as byte throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_float(lBig); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed long as float throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_sbyte(b255); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed byte as sbyte throws InvalidCastException (class identity)");

    // --- 11. null unbox -> NullReferenceException (all eight icalls) ---
    Expect(ThrowsDotNet([&] { icall_unbox_byte(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "unbox of null as byte throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_unbox_sbyte(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "unbox of null as sbyte throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_unbox_short(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "unbox of null as short throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_unbox_ushort(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "unbox of null as ushort throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_unbox_uint(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "unbox of null as uint throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_unbox_long(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "unbox of null as long throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_unbox_ulong(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "unbox of null as ulong throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_unbox_float(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "unbox of null as float throws NullReferenceException");

    // --- 12. icall_boxed_equals_ext2 ---
    // Small types: per-type payload equality.
    Expect(icall_boxed_equals_ext2(icall_box_byte(255), icall_box_byte(255)), "boxed byte 255 equals boxed byte 255");
    Expect(!icall_boxed_equals_ext2(icall_box_byte(255), icall_box_byte(254)), "boxed byte 255 != boxed byte 254");
    Expect(icall_boxed_equals_ext2(icall_box_sbyte(-1), icall_box_sbyte(-1)), "boxed sbyte -1 equals boxed sbyte -1");
    Expect(!icall_boxed_equals_ext2(icall_box_sbyte(-1), icall_box_sbyte(1)), "boxed sbyte -1 != boxed sbyte 1");
    Expect(icall_boxed_equals_ext2(icall_box_short(-32768), icall_box_short(-32768)),
           "boxed short -32768 equals itself");
    Expect(icall_boxed_equals_ext2(icall_box_ushort(65535), icall_box_ushort(65535)),
           "boxed ushort 65535 equals itself");
    // uint: full 32-bit pattern equality incl. the extreme bit patterns.
    Expect(icall_boxed_equals_ext2(icall_box_uint(uint32MaxAsI32), icall_box_uint(uint32MaxAsI32)),
           "boxed uint UINT32_MAX equals itself");
    Expect(!icall_boxed_equals_ext2(icall_box_uint(uint32MaxAsI32), icall_box_uint(uint32HalfAsI32)),
           "boxed uint UINT32_MAX != 0x80000000");
    Expect(!icall_boxed_equals_ext2(icall_box_uint(uint32MaxAsI32), il2cpp_box_int32(nullptr, -1)),
           "boxed uint UINT32_MAX != boxed int32 -1 (cross-type, identical payload)");
    // long / ulong: 8-byte payload equality incl. extremes.
    Expect(icall_boxed_equals_ext2(icall_box_long(int64Max), icall_box_long(int64Max)),
           "boxed long INT64_MAX equals itself");
    Expect(!icall_boxed_equals_ext2(icall_box_long(int64Max), icall_box_long(int64Min)),
           "boxed long INT64_MAX != INT64_MIN");
    Expect(icall_boxed_equals_ext2(icall_box_ulong(uint64Max), icall_box_ulong(uint64Max)),
           "boxed ulong UINT64_MAX equals itself");
    Expect(!icall_boxed_equals_ext2(icall_box_ulong(uint64Max), icall_box_ulong(0)),
           "boxed ulong UINT64_MAX != ulong 0");
    // float: .NET Single.Equals edge semantics on the stored double.
    Expect(icall_boxed_equals_ext2(icall_box_float(1.5), icall_box_float(1.5)), "boxed float 1.5 equals boxed float 1.5");
    Expect(!icall_boxed_equals_ext2(icall_box_float(1.5), icall_box_float(2.5)), "boxed float 1.5 != boxed float 2.5");
    Expect(icall_boxed_equals_ext2(icall_box_float(nan), icall_box_float(nan)),
           "boxed float NaN equals NaN (Single.Equals)");
    Expect(icall_boxed_equals_ext2(icall_box_float(-0.0), icall_box_float(0.0)),
           "boxed float -0.0 equals +0.0 (Single.Equals)");
    // Cross-type: same numeric value, different box class -> unequal.
    Expect(!icall_boxed_equals_ext2(icall_box_byte(5), il2cpp_box_int32(nullptr, 5)),
           "boxed byte 5 != boxed int 5 (cross-type)");
    Expect(!icall_boxed_equals_ext2(icall_box_sbyte(-1), icall_box_byte(0xFF)),
           "boxed sbyte -1 != boxed byte 0xFF (cross-type, identical payload)");
    Expect(!icall_boxed_equals_ext2(icall_box_long(5), icall_box_double(5.0)),
           "boxed long 5 != boxed double 5.0 (cross-type)");
    Expect(!icall_boxed_equals_ext2(icall_box_ushort(65), icall_box_char(65)),
           "boxed ushort 65 != boxed char 65 (cross-type)");
    // Null handling.
    Expect(icall_boxed_equals_ext2(nullptr, nullptr), "null equals null (ext2)");
    Expect(!icall_boxed_equals_ext2(nullptr, icall_box_byte(1)), "null != boxed value (ext2)");
    Expect(!icall_boxed_equals_ext2(icall_box_byte(1), nullptr), "boxed value != null (ext2)");
    // Legacy delegation: the superset covers the boxingext.cpp set too.
    Expect(icall_boxed_equals_ext2(icall_box_bool(true), icall_box_bool(true)),
           "boxed bool true equals boxed bool true (legacy delegation)");
    Expect(icall_boxed_equals_ext2(il2cpp_box_int32(nullptr, 42), il2cpp_box_int32(nullptr, 42)),
           "boxed 42 equals boxed 42 (legacy delegation)");
    Expect(icall_boxed_equals_ext2(icall_box_double(nan), icall_box_double(nan)),
           "boxed double NaN equals NaN (legacy Double.Equals)");
    Expect(!icall_boxed_equals_ext2(icall_box_double(3.14), icall_box_float(3.14)),
           "boxed double 3.14 != boxed float 3.14 (cross-type)");
    // Strings: reference equality only (distinct instances unequal; a string
    // compared to itself is equal). The string's object header is its first
    // member (offset 0), so the reinterpret_cast is the established
    // string->object up-cast pattern.
    Il2CppString* s1 = il2cpp_string_new_from_cs("hello");
    Il2CppString* s2 = il2cpp_string_new_from_cs("hello");
    Il2CppObject* s1o = reinterpret_cast<Il2CppObject*>(s1);
    Il2CppObject* s2o = reinterpret_cast<Il2CppObject*>(s2);
    Expect(icall_boxed_equals_ext2(s1o, s1o), "same string reference equals itself (ext2)");
    Expect(!icall_boxed_equals_ext2(s1o, s2o),
           "distinct strings with equal content are unequal (reference equality, ext2)");

    // --- 13. net48 semantics of the boxed UInt32 descriptor (meta audit) ---
    // The UInt32 box class is the GLOBAL System_UInt32_class (metadata.cpp;
    // extern in il2cpp_metadata.h) -- NOT a file-local descriptor. boxed uints
    // stamp pointer identity: klass IS &System_UInt32_class. net48: a boxed
    // uint IS-A System.Object (baseClass chain) and implements IConvertible /
    // IComparable / IFormattable, exactly like the Int32 box.
    il2cpp_ensure_builtin_system_classes();
    Expect(il2cpp_class_by_name("System.UInt32") == &System_UInt32_class,
           "System.UInt32 resolves to the GLOBAL descriptor via class_by_name");
    Expect(il2cpp_is_instance(uBig, &System_UInt32_class),
           "a boxed uint IS-A System.UInt32 (pointer identity + base chain)");
    Expect(il2cpp_is_instance(uBig, &System_Object_class),
           "a boxed uint IS-A System.Object (net48 baseClass chain)");
    Expect(il2cpp_is_instance(uBig, &System_IConvertible_class),
           "a boxed uint implements IConvertible (net48 interface table)");
    Expect(il2cpp_is_instance(uBig, &System_IComparable_class),
           "a boxed uint implements IComparable (net48 interface table)");
    Expect(il2cpp_is_instance(uBig, &System_IFormattable_class),
           "a boxed uint implements IFormattable (net48 interface table)");
    Expect(uBig->klass == &System_UInt32_class,
           "boxed uint klass pointer IS &System_UInt32_class (identity with global)");
    Expect(uBig->klass->baseClass == &System_Object_class,
           "System_UInt32_class baseClass is System_Object (net48)");
    Expect(uBig->klass->interfaceCount == 3,
           "System_UInt32_class carries the 3 numeric interfaces (net48)");
    Expect(!il2cpp_is_instance(i42, &System_UInt32_class),
           "a boxed int32 is NOT a System.UInt32 instance (cross-type)");
    Expect(!il2cpp_is_instance(uBig, &System_Int32_class),
           "a boxed uint is NOT a System.Int32 instance (cross-type)");
    Expect(uBig->klass->instanceSize == 16 && uBig->klass->refFieldBitmap == 0u,
           "System_UInt32_class stays 16 bytes, no reference fields (shared box layout)");
    // The box icall keeps the same behavior as before the promotion.
    Expect(icall_unbox_uint(uBig) == uint32MaxAsI32,
           "boxed uint UINT32_MAX still unboxes to the full 32-bit pattern");

    // --- 14. net48 descriptor interface tables for the OTHER box classes ---
    // net48: EVERY boxed numeric implements IConvertible/IComparable/
    // IFormattable and IS-A System.Object (the same interface set and base
    // chain as the Int32 box). Verify each box class resolves the three
    // numeric interfaces through il2cpp_is_instance and carries the Object
    // baseClass + interface table.
    struct BoxProbe { Il2CppObject* obj; const char* name; };
    const BoxProbe probes[] = {
        { b255, "System.Byte" },
        { sbNeg, "System.SByte" },
        { icall_box_short(-1), "System.Int16" },
        { icall_box_ushort(65535), "System.UInt16" },
        { lBig, "System.Int64" },
        { icall_box_ulong(uint64Max), "System.UInt64" },
        { f15, "System.Single" },
    };
    for (const BoxProbe& p : probes)
    {
        Expect(il2cpp_is_instance(p.obj, &System_Object_class),
               "boxed numeric IS-A System.Object (baseClass chain)");
        Expect(il2cpp_is_instance(p.obj, &System_IConvertible_class),
               "boxed numeric implements IConvertible (net48 interface table)");
        Expect(il2cpp_is_instance(p.obj, &System_IComparable_class),
               "boxed numeric implements IComparable (net48 interface table)");
        Expect(il2cpp_is_instance(p.obj, &System_IFormattable_class),
               "boxed numeric implements IFormattable (net48 interface table)");
        Expect(p.obj->klass->baseClass == &System_Object_class,
               "box class baseClass is System_Object (net48)");
        Expect(p.obj->klass->interfaceCount == 3,
               "box class carries the 3 numeric interfaces (net48)");
        // Exact-box-type identity still holds: the box class is the object's
        // klass, and class_by_name resolves to the same descriptor.
        Expect(il2cpp_class_by_name(p.name) == p.obj->klass,
               "box class registered under the .NET full name (identity)");
    }
    // Cross-type: a boxed byte is NOT a boxed int (exact klass identity).
    Expect(!il2cpp_is_instance(b255, &System_Int32_class),
           "boxed byte is NOT a System.Int32 instance (cross-type)");
    Expect(!il2cpp_is_instance(lBig, il2cpp_class_by_name("System.Byte")),
           "boxed long is NOT a System.Byte instance (cross-type)");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d boxing-ext2 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime boxing-ext2 tests passed\n");
    return 0;
}
