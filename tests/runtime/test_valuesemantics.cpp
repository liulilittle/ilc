//------------------------------------------------------------------------------
// test_valuesemantics.cpp -- value-semantics runtime unit tests
// (il2cpp_value_semantics.h).
//
// The hash VALUES below are the net48 oracle (tests/cases/valuesem reference
// run, captured 2026-08-03):
//   Int64.GetHashCode  = low 32 ^ high 32 (0x0123456789ABCDEF -> -2004318072,
//                        INT64_MAX -> INT32_MIN, -1L -> 0)
//   Double.GetHashCode = bits ^ bits>>32 with the d == 0 shortcut
//                        (0.0/-0.0 -> 0; 1.5 -> 1073217536; -1.5 ->
//                        -1074266112; canonical NaN -> -524288; exotic NaN
//                        payloads hash DIFFERENTLY -- net48 does NOT unify)
//   Boolean.GetHashCode = true -> 1, false -> 0
// String hashes are process-deterministic: net48 console apps use the fixed
// 5381-seed legacy algorithm (probe-verified cross-process stable,
// 2026-08-04), so RAW net48 string values are pinned in
// test_stringhash.cpp. Here the string hash tests only pin same-process
// stability + content-consistency (equal content -> equal hash).
//
// Coverage (normal + edge + error paths):
//   1. string hash: same content (distinct instances) -> same hash; distinct
//      content -> different; empty string stable; repeated calls stable;
//      the OBJECT dispatch path agrees with the direct string icall.
//   2. int32 hash: 0/1/-1/INT32_MAX/INT32_MIN -> the value itself.
//   3. int64 hash: the net48 oracle values above (incl. boundary).
//   4. double hash: 0.0/-0.0 -> 0; 1.5/-1.5 oracle; NaN constant (-524288);
//      exotic NaN payloads differ (net48-faithful, NOT unified).
//   5. bool hash: true -> 1, false -> 0.
//   6. object GetHashCode dispatch: string -> string hash; boxed int32 ->
//      the value; boxed bool -> 1/0 (net48 Boolean.GetHashCode value hash);
//      plain object -> address hash (ptr >> 3); null ->
//      NullReferenceException.
//   7. object ToString dispatch: string identity (same pointer returned);
//      boxed int32 -> decimal; boxed bool -> "True"/"False"; plain/custom
//      object -> type name; boxed double -> the G15 value text (e.g. "1.5";
//      the full primitive box matrix lives in test_boxedtostring.cpp); null
//      -> NRE.
//   8. object Equals matrix: null pairs; identity; string ordinal
//      (equal/different content, different lengths); boxed int32 by value;
//      boxed int32 vs int64 -> false (different box types); boxed int64 by
//      value; boxed bool; boxed double incl. NaN==NaN (net48) and 0.0==-0.0;
//      string vs boxed -> false; plain objects -> reference only.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_value_semantics.h"

#include "il2cpp_boxing_ext.h"
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

// The NRE factory chain (see il2cpp_exception_ext.h): type_name() of a
// caught DotNetException is the whole chain as thrown.
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

// Plain managed string from an ASCII literal (1:1 transcode).
il2cpp::Il2CppString* Str(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_utf8(ascii);
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

il2cpp::Il2CppObject* BoxInt(int32_t v)
{
    return il2cpp::il2cpp_box_int32(nullptr, v);
}

il2cpp::Il2CppObject* BoxBool(bool v)
{
    return il2cpp::runtime::icall_box_bool(v);
}

il2cpp::Il2CppObject* BoxDouble(double v)
{
    return il2cpp::runtime::icall_box_double(v);
}

double DoubleFromBits(uint64_t bits)
{
    double d = 0.0;
    std::memcpy(&d, &bits, sizeof(d));
    return d;
}

// Descriptors for plain (non-boxed, non-string) test objects. Same shape as
// the runtime's own descriptors; NOT registered (no type queries in these
// tests) -- allocation only needs the instanceSize.
il2cpp::Il2CppClass g_objectClass = {
    "System.Object", // name
    8,               // instanceSize: header only
    0u,              // refFieldBitmap
    0,               // refFieldCount
    nullptr,         // baseClass
    nullptr,         // vtable
    0,               // vtableSize
    nullptr,         // interfaces
    0,               // interfaceCount
};

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

il2cpp::Il2CppObject* NewPlain(const char* name)
{
    il2cpp::Il2CppClass* klass =
        (std::strcmp(name, "System.Object") == 0) ? &g_objectClass : &g_customClass;
    return il2cpp::il2cpp_object_new(klass);
}

// Int64 box fixture: same layout contract as the runtime's own boxes
// (klass at 0, 8-byte payload at offset 8, instanceSize 16). Built here so
// the equality tests do not depend on the boxingext2 domain's icalls.
il2cpp::Il2CppClass g_int64BoxClass = {
    "System.Int64", // name
    16,             // instanceSize: header + aligned 8-byte payload slot
    0u,             // refFieldBitmap
    0,              // refFieldCount
    nullptr,        // baseClass
    nullptr,        // vtable
    0,              // vtableSize
    nullptr,        // interfaces
    0,              // interfaceCount
};

il2cpp::Il2CppObject* BoxLong(int64_t v)
{
    il2cpp::Il2CppObject* boxed = il2cpp::il2cpp_object_new(&g_int64BoxClass);
    *reinterpret_cast<int64_t*>(reinterpret_cast<char*>(boxed) + 8) = v;
    return boxed;
}

void TestStringHash()
{
    il2cpp::Il2CppString* a = Str("abc");
    il2cpp::Il2CppString* b = Str("abc");
    il2cpp::Il2CppString* c = Str("abd");
    il2cpp::Il2CppString* empty1 = Str("");
    il2cpp::Il2CppString* empty2 = Str("");
    il2cpp::Il2CppString* six1 = Str("abcdef");
    il2cpp::Il2CppString* six2 = Str("abcdef");

    Expect(il2cpp::runtime::icall_string_get_hash_code(
               reinterpret_cast<il2cpp::Il2CppObject*>(a)) ==
           il2cpp::runtime::icall_string_get_hash_code(
               reinterpret_cast<il2cpp::Il2CppObject*>(b)),
           "equal content -> equal string hash (distinct instances)");
    Expect(il2cpp::runtime::icall_string_get_hash_code(
               reinterpret_cast<il2cpp::Il2CppObject*>(six1)) ==
           il2cpp::runtime::icall_string_get_hash_code(
               reinterpret_cast<il2cpp::Il2CppObject*>(six2)),
           "6-unit string hash is content-consistent (both accumulators)");
    Expect(il2cpp::runtime::icall_string_get_hash_code(
               reinterpret_cast<il2cpp::Il2CppObject*>(a)) !=
           il2cpp::runtime::icall_string_get_hash_code(
               reinterpret_cast<il2cpp::Il2CppObject*>(c)),
           "distinct content -> different string hash");
    Expect(il2cpp::runtime::icall_string_get_hash_code(
               reinterpret_cast<il2cpp::Il2CppObject*>(empty1)) ==
           il2cpp::runtime::icall_string_get_hash_code(
               reinterpret_cast<il2cpp::Il2CppObject*>(empty2)),
           "empty string hash is stable");
    Expect(il2cpp::runtime::icall_string_get_hash_code(
               reinterpret_cast<il2cpp::Il2CppObject*>(a)) ==
           il2cpp::runtime::icall_string_get_hash_code(
               reinterpret_cast<il2cpp::Il2CppObject*>(a)),
           "string hash is deterministic across calls");
    Expect(il2cpp::runtime::icall_object_get_hash_code(
               reinterpret_cast<il2cpp::Il2CppObject*>(a)) ==
           il2cpp::runtime::icall_string_get_hash_code(
               reinterpret_cast<il2cpp::Il2CppObject*>(a)),
           "object dispatch on a string agrees with the string icall");
}

void TestInt32Hash()
{
    Expect(il2cpp::runtime::icall_int32_get_hash_code(0) == 0,
           "int32 hash of 0 is 0");
    Expect(il2cpp::runtime::icall_int32_get_hash_code(1) == 1,
           "int32 hash of 1 is 1");
    Expect(il2cpp::runtime::icall_int32_get_hash_code(-1) == -1,
           "int32 hash of -1 is -1");
    Expect(il2cpp::runtime::icall_int32_get_hash_code(INT32_MAX) == INT32_MAX,
           "int32 hash of INT32_MAX is itself");
    Expect(il2cpp::runtime::icall_int32_get_hash_code(INT32_MIN) == INT32_MIN,
           "int32 hash of INT32_MIN is itself");
}

void TestInt64Hash()
{
    Expect(il2cpp::runtime::icall_int64_get_hash_code(0) == 0,
           "int64 hash of 0 is 0");
    Expect(il2cpp::runtime::icall_int64_get_hash_code(1) == 1,
           "int64 hash of 1 is 1");
    Expect(il2cpp::runtime::icall_int64_get_hash_code(-1) == 0,
           "int64 hash of -1 is 0 (low ^ high = 0xFFFFFFFF ^ 0xFFFFFFFF)");
    Expect(il2cpp::runtime::icall_int64_get_hash_code(INT64_C(0x0123456789ABCDEF)) ==
               -2004318072,
           "int64 hash of 0x0123456789ABCDEF is -2004318072 (net48 oracle)");
    Expect(il2cpp::runtime::icall_int64_get_hash_code(INT64_MAX) == INT32_MIN,
           "int64 hash of INT64_MAX is INT32_MIN (0xFFFFFFFF ^ 0x7FFFFFFF)");
    Expect(il2cpp::runtime::icall_int64_get_hash_code(INT64_MIN) == INT32_MIN,
           "int64 hash of INT64_MIN is INT32_MIN (0 ^ 0x80000000)");
}

void TestDoubleHash()
{
    Expect(il2cpp::runtime::icall_double_get_hash_code(0.0) == 0,
           "double hash of 0.0 is 0");
    Expect(il2cpp::runtime::icall_double_get_hash_code(-0.0) == 0,
           "double hash of -0.0 is 0 (net48 d == 0 shortcut)");
    Expect(il2cpp::runtime::icall_double_get_hash_code(1.5) == 1073217536,
           "double hash of 1.5 is 1073217536 (net48 oracle)");
    Expect(il2cpp::runtime::icall_double_get_hash_code(-1.5) == -1074266112,
           "double hash of -1.5 is -1074266112 (net48 oracle)");
    // Canonical C# NaN pattern (0xFFF8000000000000 -- what every common
    // NaN source on x86 produces) hashes to -524288 (net48 oracle); the
    // test uses an explicit bit pattern because std::nan("") is
    // platform-dependent (MinGW yields a different pattern).
    const int32_t nanHash = il2cpp::runtime::icall_double_get_hash_code(
        DoubleFromBits(UINT64_C(0xFFF8000000000000)));
    Expect(nanHash == -524288,
           "canonical NaN hashes to -524288 (net48 oracle)");
    Expect(nanHash == il2cpp::runtime::icall_double_get_hash_code(
               DoubleFromBits(UINT64_C(0xFFF8000000000000))),
           "NaN hash is constant for the canonical pattern");
    Expect(il2cpp::runtime::icall_double_get_hash_code(
               DoubleFromBits(UINT64_C(0x7FF8000000000001))) == 2146959361,
           "exotic NaN payload hashes independently (net48 does NOT unify)");
    Expect(il2cpp::runtime::icall_double_get_hash_code(
               DoubleFromBits(UINT64_C(0xFFF0000000000000))) == -1048576,
           "second exotic NaN payload hashes to -1048576 (net48 oracle)");
}

void TestBoolHash()
{
    Expect(il2cpp::runtime::icall_bool_get_hash_code(true) == 1,
           "bool hash of true is 1");
    Expect(il2cpp::runtime::icall_bool_get_hash_code(false) == 0,
           "bool hash of false is 0");
}

void TestObjectGetHashCode()
{
    il2cpp::Il2CppObject* plain = NewPlain("System.Object");
    Expect(il2cpp::runtime::icall_object_get_hash_code(plain) ==
               static_cast<int32_t>(reinterpret_cast<uintptr_t>(plain) >> 3),
           "plain object hash is the address hash (ptr >> 3)");
    il2cpp::Il2CppObject* boxed = BoxInt(5);
    Expect(il2cpp::runtime::icall_object_get_hash_code(boxed) == 5,
           "boxed int32 hashes to its value through the object dispatch");
    Expect(il2cpp::runtime::icall_object_get_hash_code(BoxInt(INT32_MIN)) ==
               INT32_MIN,
           "boxed INT32_MIN hashes to itself");
    // net48 Boolean.GetHashCode is the value hash (true -> 1, false -> 0);
    // batch 45 (GAP-BOX-6) filled the boxed-bool object path to match.
    Expect(il2cpp::runtime::icall_object_get_hash_code(BoxBool(true)) == 1,
           "boxed bool true hashes to 1 (net48 value hash)");
    Expect(il2cpp::runtime::icall_object_get_hash_code(BoxBool(false)) == 0,
           "boxed bool false hashes to 0 (net48 value hash)");
    Expect(ThrowsDotNet(
               [&] { il2cpp::runtime::icall_object_get_hash_code(nullptr); },
               kNreChain),
           "null GetHashCode receiver throws NullReferenceException");
}

void TestObjectToString()
{
    il2cpp::Il2CppString* s = Str("abc");
    Expect(il2cpp::runtime::icall_object_to_string(
               reinterpret_cast<il2cpp::Il2CppObject*>(s)) == s,
           "string ToString returns the string itself (identity)");
    Expect(IsContent(il2cpp::runtime::icall_object_to_string(BoxInt(42)),
                     "42"),
           "boxed int32 ToString is decimal");
    Expect(IsContent(il2cpp::runtime::icall_object_to_string(BoxInt(-7)),
                     "-7"),
           "boxed negative int32 ToString is decimal");
    Expect(IsContent(il2cpp::runtime::icall_object_to_string(BoxInt(0)), "0"),
           "boxed zero ToString is \"0\"");
    Expect(IsContent(
               il2cpp::runtime::icall_object_to_string(BoxInt(INT32_MIN)),
               "-2147483648"),
           "boxed INT32_MIN ToString is \"-2147483648\"");
    Expect(IsContent(il2cpp::runtime::icall_object_to_string(BoxBool(true)),
                     "True"),
           "boxed bool ToString is \"True\"");
    Expect(IsContent(il2cpp::runtime::icall_object_to_string(BoxBool(false)),
                     "False"),
           "boxed bool ToString is \"False\"");
    Expect(IsContent(il2cpp::runtime::icall_object_to_string(
                         NewPlain("System.Object")),
                     "System.Object"),
           "plain object ToString is the type name");
    Expect(IsContent(il2cpp::runtime::icall_object_to_string(
                         NewPlain("My.Custom")),
                     "My.Custom"),
           "custom object ToString is the type name");
    Expect(IsContent(il2cpp::runtime::icall_object_to_string(BoxDouble(1.5)),
                     "1.5"),
           "boxed double ToString is the G15 value text");
    Expect(ThrowsDotNet(
               [&] { il2cpp::runtime::icall_object_to_string(nullptr); },
               kNreChain),
           "null ToString receiver throws NullReferenceException");
}

void TestObjectEquals()
{
    // null pairs (net48: object.Equals(null, null) is true).
    Expect(il2cpp::runtime::icall_object_equals(nullptr, nullptr),
           "Equals(null, null) is true");
    il2cpp::Il2CppObject* plain = NewPlain("System.Object");
    Expect(!il2cpp::runtime::icall_object_equals(nullptr, plain),
           "Equals(null, obj) is false");
    Expect(!il2cpp::runtime::icall_object_equals(plain, nullptr),
           "Equals(obj, null) is false");

    // Reference identity.
    Expect(il2cpp::runtime::icall_object_equals(plain, plain),
           "Equals same pointer is true");

    // Strings: ordinal content equality.
    Expect(il2cpp::runtime::icall_object_equals(
               reinterpret_cast<il2cpp::Il2CppObject*>(Str("abc")),
               reinterpret_cast<il2cpp::Il2CppObject*>(Str("abc"))),
           "Equals equal-content strings is true");
    Expect(!il2cpp::runtime::icall_object_equals(
               reinterpret_cast<il2cpp::Il2CppObject*>(Str("abc")),
               reinterpret_cast<il2cpp::Il2CppObject*>(Str("abd"))),
           "Equals different strings is false");
    Expect(!il2cpp::runtime::icall_object_equals(
               reinterpret_cast<il2cpp::Il2CppObject*>(Str("abc")),
               reinterpret_cast<il2cpp::Il2CppObject*>(Str("abcd"))),
           "Equals different-length strings is false");

    // Boxed values: same box type -> value equality.
    Expect(il2cpp::runtime::icall_object_equals(BoxInt(5), BoxInt(5)),
           "Equals boxed 5 vs boxed 5 is true");
    Expect(!il2cpp::runtime::icall_object_equals(BoxInt(5), BoxInt(6)),
           "Equals boxed 5 vs boxed 6 is false");
    Expect(!il2cpp::runtime::icall_object_equals(BoxInt(5), BoxInt(-5)),
           "Equals boxed 5 vs boxed -5 is false");
    Expect(il2cpp::runtime::icall_object_equals(BoxBool(true), BoxBool(true)),
           "Equals boxed true vs boxed true is true");
    Expect(!il2cpp::runtime::icall_object_equals(BoxBool(true),
                                                  BoxBool(false)),
           "Equals boxed true vs boxed false is false");
    Expect(il2cpp::runtime::icall_object_equals(BoxDouble(1.5),
                                                BoxDouble(1.5)),
           "Equals boxed 1.5 vs boxed 1.5 is true");
    Expect(!il2cpp::runtime::icall_object_equals(BoxDouble(1.5),
                                                  BoxDouble(2.5)),
           "Equals boxed 1.5 vs boxed 2.5 is false");
    Expect(il2cpp::runtime::icall_object_equals(
               BoxDouble(DoubleFromBits(UINT64_C(0xFFF8000000000000))),
               BoxDouble(DoubleFromBits(UINT64_C(0x7FF8000000000001)))),
           "Equals boxed NaN vs boxed NaN is true (net48, any payload)");
    Expect(!il2cpp::runtime::icall_object_equals(
               BoxDouble(DoubleFromBits(UINT64_C(0xFFF8000000000000))),
               BoxDouble(1.5)),
           "Equals boxed NaN vs boxed 1.5 is false");
    Expect(il2cpp::runtime::icall_object_equals(BoxDouble(0.0),
                                                BoxDouble(-0.0)),
           "Equals boxed 0.0 vs boxed -0.0 is true");

    // Different box types are never equal (.NET: Int32.Equals(Long) false --
    // net48 probe "equals boxed int vs long: False").
    Expect(!il2cpp::runtime::icall_object_equals(BoxInt(5), BoxLong(5)),
           "Equals boxed int vs boxed long is false (different box types)");
    Expect(il2cpp::runtime::icall_object_equals(BoxLong(5), BoxLong(5)),
           "Equals boxed long 5 vs boxed long 5 is true");
    Expect(il2cpp::runtime::icall_object_equals(BoxLong(INT64_MAX),
                                                BoxLong(INT64_MAX)),
           "Equals boxed long INT64_MAX pair is true");
    Expect(!il2cpp::runtime::icall_object_equals(BoxLong(5), BoxLong(-5)),
           "Equals boxed long 5 vs boxed long -5 is false");
    Expect(!il2cpp::runtime::icall_object_equals(BoxLong(5), BoxInt(5)),
           "Equals boxed long vs boxed int is false (order-independent)");

    // string vs boxed value -> false.
    Expect(!il2cpp::runtime::icall_object_equals(
               reinterpret_cast<il2cpp::Il2CppObject*>(Str("abc")), BoxInt(3)),
           "Equals string vs boxed int is false");

    // Plain objects: reference-only equality.
    Expect(!il2cpp::runtime::icall_object_equals(NewPlain("System.Object"),
                                                  NewPlain("System.Object")),
           "Equals distinct plain objects is false");
}

} // namespace

int main()
{
    TestStringHash();
    TestInt32Hash();
    TestInt64Hash();
    TestDoubleHash();
    TestBoolHash();
    TestObjectGetHashCode();
    TestObjectToString();
    TestObjectEquals();

    if (g_failures != 0)
    {
        std::printf("%d valuesemantics assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime valuesemantics tests passed\n");
    return 0;
}
