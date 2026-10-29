//------------------------------------------------------------------------------
// test_boxedtostring.cpp -- boxed value-type ToString through the object
// dispatch (icall_object_to_string, valuesemantics.cpp).
//
// ORACLE: net48 reference run (captured 2026-08-03):
//   (object)5            -> "5"
//   (object)1234567890123L -> "1234567890123"
//   (object)3.14          -> "3.14"
//   (object)'A'           -> "A"
//   (object)true          -> "True"
//   (object)false         -> "False"
//   (object)4000000000u   -> "4000000000"   (uint32 beyond int32 range)
//   (object)0u            -> "0"
//   (object)ulong.MaxValue -> "18446744073709551615"
//   (object)3.14f         -> "3.14"          (Single.ToString: float G7 text)
//   (object)(1.0f/3.0f)   -> "0.3333333"     (miscfmt: float G7, not G15)
//   (object)(-0.0)        -> "0"             (G15 fixed point)
//   (object)double.NaN    -> "NaN"
//   (object)1E-05         -> "1E-05"         (G15 scientific)
//   (object)(0.1 + 0.2)   -> "0.3"           (plain G15, net48)
// Every primitive box renders the VALUE, never the klass name; non-primitive
// objects keep the type-name fallback (net48 Object.ToString).
//
// Plain executable + minimal assertion harness (see test_valuesemantics.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_value_semantics.h"

#include "il2cpp_boxing_ext.h"
#include "il2cpp_boxing_ext2.h"
#include "il2cpp_exception.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>

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

const char* kNreChain =
    "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception";

template <typename Fn>
bool ThrowsDotNet(Fn fn, const char* expectedChain)
{
    try
    {
        fn();
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedChain) == 0;
    }
}

il2cpp::Il2CppObject* BoxInt(int32_t v)
{
    return il2cpp::il2cpp_box_int32(nullptr, v);
}

il2cpp::Il2CppObject* BoxLong(int64_t v)
{
    return il2cpp::runtime::icall_box_long(v);
}

il2cpp::Il2CppObject* BoxULong(uint64_t v)
{
    return il2cpp::runtime::icall_box_ulong(v);
}

il2cpp::Il2CppObject* BoxDouble(double v)
{
    return il2cpp::runtime::icall_box_double(v);
}

// UInt32 box fixture: the runtime boxes uint32 through il2cpp_box_int32 with
// the System.UInt32 klass descriptor (payload at offset 8, same contract as
// boxing.cpp's Int32 box -- only the klass name differs).
il2cpp::Il2CppClass g_uint32BoxClass = {
    "System.UInt32", // name
    16,              // instanceSize: header + aligned payload slot
    0u,              // refFieldBitmap
    0,               // refFieldCount
    nullptr,         // baseClass
    nullptr,         // vtable
    0,               // vtableSize
    nullptr,         // interfaces
    0,               // interfaceCount
};

il2cpp::Il2CppObject* BoxUInt(uint32_t v)
{
    return il2cpp::il2cpp_box_int32(&g_uint32BoxClass, static_cast<int32_t>(v));
}

double DoubleFromBits(uint64_t bits)
{
    double d = 0.0;
    std::memcpy(&d, &bits, sizeof(d));
    return d;
}

// Content check against an ASCII literal (tests use ASCII only).
bool IsContent(il2cpp::Il2CppString* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const size_t len = std::strlen(ascii);
    if (static_cast<size_t>(s->length) != len)
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(ascii[i]))
        {
            return false;
        }
    }
    return true;
}

void ExpectToString(il2cpp::Il2CppObject* boxed, const char* expected,
                    const char* what)
{
    Expect(IsContent(il2cpp::runtime::icall_object_to_string(boxed), expected),
           what);
}

void TestTaskOracle()
{
    ExpectToString(BoxInt(5), "5",
                   "(object)5 ToString is \"5\" (net48 oracle)");
    ExpectToString(BoxLong(INT64_C(1234567890123)), "1234567890123",
                   "(object)1234567890123L ToString is \"1234567890123\"");
    ExpectToString(BoxDouble(3.14), "3.14",
                   "(object)3.14 ToString is \"3.14\"");
    ExpectToString(il2cpp::runtime::icall_box_char('A'), "A",
                   "(object)'A' ToString is \"A\"");
    ExpectToString(il2cpp::runtime::icall_box_bool(true), "True",
                   "(object)true ToString is \"True\"");
    ExpectToString(il2cpp::runtime::icall_box_bool(false), "False",
                   "(object)false ToString is \"False\"");
}

void TestIntegerBoxes()
{
    ExpectToString(BoxInt(0), "0", "(object)0 ToString is \"0\"");
    ExpectToString(BoxInt(-7), "-7", "(object)(-7) ToString is \"-7\"");
    ExpectToString(BoxInt(INT32_MAX), "2147483647",
                   "(object)int.MaxValue ToString is \"2147483647\"");
    ExpectToString(BoxInt(INT32_MIN), "-2147483648",
                   "(object)int.MinValue ToString is \"-2147483648\"");
    ExpectToString(BoxLong(0), "0", "(object)0L ToString is \"0\"");
    ExpectToString(BoxLong(INT64_MAX), "9223372036854775807",
                   "(object)long.MaxValue ToString is \"9223372036854775807\"");
    ExpectToString(BoxLong(INT64_MIN), "-9223372036854775808",
                   "(object)long.MinValue ToString is \"-9223372036854775808\"");
    ExpectToString(il2cpp::runtime::icall_box_byte(255), "255",
                   "(object)(byte)255 ToString is \"255\"");
    ExpectToString(il2cpp::runtime::icall_box_byte(0), "0",
                   "(object)(byte)0 ToString is \"0\"");
    ExpectToString(il2cpp::runtime::icall_box_sbyte(-5), "-5",
                   "(object)(sbyte)(-5) ToString is \"-5\"");
    ExpectToString(il2cpp::runtime::icall_box_short(-12), "-12",
                   "(object)(short)(-12) ToString is \"-12\"");
    ExpectToString(il2cpp::runtime::icall_box_ushort(65535), "65535",
                   "(object)(ushort)65535 ToString is \"65535\"");
    ExpectToString(il2cpp::runtime::icall_box_ushort(0), "0",
                   "(object)(ushort)0 ToString is \"0\"");
}

void TestUnsignedRange()
{
    ExpectToString(BoxUInt(0), "0",
                   "(object)0u ToString is \"0\"");
    ExpectToString(BoxUInt(4000000000u), "4000000000",
                   "(object)4000000000u ToString is \"4000000000\" (beyond int32)");
    ExpectToString(BoxUInt(UINT32_MAX), "4294967295",
                   "(object)uint.MaxValue ToString is \"4294967295\"");
    ExpectToString(BoxULong(0), "0", "(object)0UL ToString is \"0\"");
    ExpectToString(BoxULong(INT64_C(1234567890123)), "1234567890123",
                   "(object)1234567890123UL ToString is \"1234567890123\"");
    ExpectToString(BoxULong(UINT64_MAX), "18446744073709551615",
                   "(object)ulong.MaxValue ToString is \"18446744073709551615\"");
}

void TestDoubleAndFloatBoxes()
{
    ExpectToString(BoxDouble(1.5), "1.5", "(object)1.5 ToString is \"1.5\"");
    ExpectToString(BoxDouble(-0.0), "0",
                   "(object)(-0.0) ToString is \"0\" (G15 fixed point)");
    ExpectToString(BoxDouble(0.0001), "0.0001",
                   "(object)0.0001 ToString is \"0.0001\" (G15 boundary)");
    ExpectToString(BoxDouble(1e-05), "1E-05",
                   "(object)1E-05 ToString is \"1E-05\" (G15 scientific)");
    ExpectToString(BoxDouble(DoubleFromBits(UINT64_C(0xFFF8000000000000))),
                   "NaN", "(object)double.NaN ToString is \"NaN\"");
    ExpectToString(BoxDouble(0.1 + 0.2), "0.3",
                   "(object)(0.1 + 0.2) ToString is \"0.3\" (plain G15)");
    ExpectToString(il2cpp::runtime::icall_box_float(3.14), "3.14",
                   "(object)3.14f ToString is \"3.14\" (widened to double)");
    // miscfmt: a boxed Single calls Single.ToString() in net48 -- the FLOAT
    // G7 text of the value, NOT the double G15 text of the widened float.
    // Oracle (probe-miscfmt, net48 host capture 2026-10-29): (object)(1.0f/3f)
    // -> "0.3333333" (the pre-miscfmt G15 path printed "0.333333343267441").
    ExpectToString(il2cpp::runtime::icall_box_float(1.0f / 3.0f),
                   "0.3333333",
                   "(object)(1.0f / 3.0f) ToString is the net48 float G7 text");
}

void TestCharBox()
{
    il2cpp::Il2CppString* nul = il2cpp::runtime::icall_object_to_string(
        il2cpp::runtime::icall_box_char(0));
    Expect(nul != nullptr && nul->length == 1 && nul->chars[0] == 0,
           "(object)'\\0' ToString is a 1-unit NUL string");
}

void TestFallbackAndNull()
{
    // Non-primitive boxes keep the .NET type-name fallback.
    il2cpp::Il2CppClass g_customClass = {
        "My.Custom", // name
        8,           // instanceSize: header only
        0u,          // refFieldBitmap
        0,           // refFieldCount
        nullptr,     // baseClass
        nullptr,     // vtable
        0,           // vtableSize
        nullptr,     // interfaces
        0,           // interfaceCount
    };
    il2cpp::Il2CppObject* custom = il2cpp::il2cpp_object_new(&g_customClass);
    ExpectToString(custom, "My.Custom",
                   "non-primitive box ToString stays the type name");
    Expect(ThrowsDotNet(
               [&] { il2cpp::runtime::icall_object_to_string(nullptr); },
               kNreChain),
           "null ToString receiver throws NullReferenceException");
}

} // namespace

int main()
{
    TestTaskOracle();
    TestIntegerBoxes();
    TestUnsignedRange();
    TestDoubleAndFloatBoxes();
    TestCharBox();
    TestFallbackAndNull();

    if (g_failures != 0)
    {
        std::printf("%d boxed-tostring assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime boxed-tostring tests passed\n");
    return 0;
}
