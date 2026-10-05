//------------------------------------------------------------------------------
// test_typecheck.cpp -- runtime type-check icall unit tests.
//
// Coverage (normal + edge + error paths; see the header for the conformance
// model -- base chain / interface table / '['-prefix array rule):
//   1. isinst -- exact type: obj->klass == klass -> 1.
//   2. isinst -- base chain: deep/derived vs base -> 1 (2-level walk).
//   3. isinst -- unrelated class -> 0.
//   4. isinst -- null object -> 0 (and null klass -> 0).
//   5. isinst -- interface: direct implementor -> 1; inherited through the
//      base chain -> 1; non-implementor -> 0.
//   6. isinst -- arrays: int32[] vs "[System.Int32" -> 1; vs "[System.Double"
//      -> 1 (M4 prefix-only simplification, documented); array vs non-array
//      class -> 0; string vs array class -> 0.
//   7. castclass -- match returns the SAME pointer; mismatch returns null;
//      null input returns null.
//   8. equals -- same descriptor -> 1; distinct descriptors with the same
//      name -> 1; different names -> 0; null vs non-null -> 0; null/null -> 1.
//   9. is_subclass_of -- 3-level chain walk; self is NOT a subclass of itself
//      (.NET IsSubclassOf excludes self); wrong direction -> 0; null derived
//      -> NullReferenceException (net48 null receiver); null base ->
//      ArgumentNullException (net48 IsSubclassOf(null), probe-verified).
//  10. get_type_code -- the FULL net48 boxed-primitive sweep (values from the
//      csc probe + typecheckcase reference): Int32=9, Boolean=3, Char=4,
//      Double=14, Single=13, SByte=5, Byte=6, Int16=7, UInt16=8, UInt32=10,
//      Int64=11, UInt64=12, String=18; user class -> 1, array -> 1; null ->
//      NullReferenceException. Edge VALUES (Int64/Byte/UInt16/Single max etc.)
//      exercise the same klass-name path with extreme payloads.
//  11. get_type -- returns the runtime type NAME as a managed string:
//      "System.Int32" / "System.String" / "T.Impl"; null -> NRE.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_typecheck.h"

#include "il2cpp_array.h"     // il2cpp_array_new (real array objects)
#include "il2cpp_boxing_ext.h" // icall_box_bool / char / double
#include "il2cpp_boxing_ext2.h" // icall_box_byte/sbyte/short/ushort/uint/long/ulong/float
#include "il2cpp_exception.h"  // DotNetException
#include "il2cpp_metadata.h"
#include "il2cpp_runtime.h"    // il2cpp_box_int32
#include "il2cpp_string.h"     // il2cpp_string_new_from_cs / Il2CppString

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

//------------------------------------------------------------------------------
// Test fixture: a 3-level user class hierarchy + an interface.
//
//   T.Base          (no base, no interfaces)
//   T.IFoo          (interface descriptor; 2 virtual slots, no impl table)
//   T.ImplBase      (derives T.Base; implements T.IFoo -- own interface map)
//   T.Derived       (derives T.ImplBase; NO own interface map -- the
//                    interface must be found through the base chain)
//   T.Deep          (derives T.Derived -- 3-level chain for walk tests)
//   T.Other         (unrelated class)
//------------------------------------------------------------------------------

il2cpp::Il2CppClass baseClass = {
    "T.Base", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppClass ifaceClass = {
    "T.IFoo", 0, 0u, 0, nullptr, nullptr, 2, nullptr, 0,
};

void* implBaseVtable[2] = {nullptr, nullptr};
int32_t implBaseIfaceMap[2] = {1, 1};
il2cpp::InterfaceMapEntry implBaseEntries[1] = {{&ifaceClass, implBaseIfaceMap}};
il2cpp::Il2CppClass implBaseClass = {
    "T.ImplBase", 16, 0u, 0, &baseClass, implBaseVtable, 2, implBaseEntries, 1,
};

il2cpp::Il2CppClass derivedClass = {
    "T.Derived", 16, 0u, 0, &implBaseClass, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppClass deepClass = {
    "T.Deep", 16, 0u, 0, &derivedClass, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppClass otherClass = {
    "T.Other", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

// Fake managed objects (klass only -- type checks never touch payload).
il2cpp::Il2CppObject baseObj = {&baseClass};
il2cpp::Il2CppObject implBaseObj = {&implBaseClass};
il2cpp::Il2CppObject derivedObj = {&derivedClass};
il2cpp::Il2CppObject deepObj = {&deepClass};
il2cpp::Il2CppObject otherObj = {&otherClass};

// Array-class descriptors for the '['-prefix rule (mirror array.cpp names).
il2cpp::Il2CppClass int32ArrayClass = {
    "[System.Int32", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppClass doubleArrayClass = {
    "[System.Double", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

// Compare a managed string returned by icall_object_get_type with an ASCII
// literal (the name strings are ASCII by construction).
bool ManagedStringEqualsAscii(il2cpp::Il2CppObject* s, const char* ascii)
{
    il2cpp::Il2CppString* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    const size_t len = std::strlen(ascii);
    if (str == nullptr || static_cast<size_t>(str->length) != len)
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (str->chars[i] != static_cast<char16_t>(ascii[i]))
        {
            return false;
        }
    }
    return true;
}

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    // --- 1. isinst: exact type ---
    Expect(icall_type_is_instance(&implBaseObj, &implBaseClass) == 1,
           "isinst exact type -> 1");
    Expect(icall_type_is_instance(&baseObj, &baseClass) == 1,
           "isinst exact type (root) -> 1");
    Expect(icall_type_is_instance(&otherObj, &otherClass) == 1,
           "isinst exact type (unrelated class) -> 1");

    // --- 2. isinst: base chain ---
    Expect(icall_type_is_instance(&deepObj, &baseClass) == 1,
           "isinst 3-level base chain (deep vs base) -> 1");
    Expect(icall_type_is_instance(&derivedObj, &baseClass) == 1,
           "isinst 2-level base chain (derived vs base) -> 1");
    Expect(icall_type_is_instance(&implBaseObj, &baseClass) == 1,
           "isinst 1-level base chain (implbase vs base) -> 1");
    Expect(icall_type_is_instance(&deepObj, &derivedClass) == 1,
           "isinst mid-chain (deep vs derived) -> 1");

    // --- 3. isinst: unrelated class ---
    Expect(icall_type_is_instance(&baseObj, &otherClass) == 0,
           "isinst unrelated class -> 0");
    Expect(icall_type_is_instance(&deepObj, &otherClass) == 0,
           "isinst deep vs unrelated -> 0");

    // --- 4. isinst: null ---
    Expect(icall_type_is_instance(nullptr, &baseClass) == 0,
           "isinst null object -> 0");
    Expect(icall_type_is_instance(&baseObj, nullptr) == 0,
           "isinst null klass -> 0");
    Expect(icall_type_is_instance(nullptr, nullptr) == 0,
           "isinst null/null -> 0");

    // --- 5. isinst: interfaces ---
    Expect(icall_type_is_instance(&implBaseObj, &ifaceClass) == 1,
           "isinst direct implementor -> 1");
    Expect(icall_type_is_instance(&derivedObj, &ifaceClass) == 1,
           "isinst interface inherited through base chain -> 1");
    Expect(icall_type_is_instance(&deepObj, &ifaceClass) == 1,
           "isinst interface through 3-level chain -> 1");
    Expect(icall_type_is_instance(&baseObj, &ifaceClass) == 0,
           "isinst non-implementor -> 0");
    Expect(icall_type_is_instance(&otherObj, &ifaceClass) == 0,
           "isinst unrelated vs interface -> 0");

    // --- 6. isinst: arrays ('['-prefix rule, M4 simplification) ---
    Il2CppObject* intArr = reinterpret_cast<Il2CppObject*>(
        il2cpp_array_new(nullptr, 4, false, 3));
    Expect(icall_type_is_instance(intArr, &int32ArrayClass) == 1,
           "isinst int32[] vs int32 array class -> 1");
    Expect(icall_type_is_instance(intArr, &doubleArrayClass) == 1,
           "isinst int32[] vs double array class -> 1 (M4 prefix-only rule)");
    Expect(icall_type_is_instance(intArr, &baseClass) == 0,
           "isinst array vs non-array class -> 0");
    Expect(icall_type_is_instance(intArr, &ifaceClass) == 0,
           "isinst array vs interface -> 0");
    Il2CppObject* strObj = reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new_from_cs("hello"));
    Expect(icall_type_is_instance(strObj, &int32ArrayClass) == 0,
           "isinst string vs array class -> 0");

    // --- 7. castclass ---
    Expect(icall_type_cast(&deepObj, &baseClass) == &deepObj,
           "castclass match returns the same pointer");
    Expect(icall_type_cast(&implBaseObj, &ifaceClass) == &implBaseObj,
           "castclass interface match returns the same pointer");
    Expect(icall_type_cast(&baseObj, &derivedClass) == nullptr,
           "castclass mismatch returns null");
    Expect(icall_type_cast(nullptr, &baseClass) == nullptr,
           "castclass null input returns null");
    Expect(icall_type_cast(&baseObj, nullptr) == nullptr,
           "castclass null klass returns null");

    // --- 8. equals ---
    Expect(icall_type_equals(&baseClass, &baseClass) == 1,
           "equals same descriptor -> 1");
    Expect(icall_type_equals(&deepClass, &deepClass) == 1,
           "equals other descriptor identity -> 1");
    {
        // Distinct descriptors, identical name -> same type by name.
        Il2CppClass aliasOfBase = {
            "T.Base", 8, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
        };
        Expect(icall_type_equals(&baseClass, &aliasOfBase) == 1,
               "equals distinct descriptors with same name -> 1");
        Expect(icall_type_equals(&baseClass, &otherClass) == 0,
               "equals different names -> 0");
    }
    Expect(icall_type_equals(nullptr, nullptr) == 1,
           "equals null/null -> 1 (identity fast path)");
    Expect(icall_type_equals(&baseClass, nullptr) == 0,
           "equals class vs null -> 0");
    Expect(icall_type_equals(nullptr, &baseClass) == 0,
           "equals null vs class -> 0");

    // --- 9. is_subclass_of ---
    Expect(icall_type_is_subclass_of(&deepClass, &baseClass) == 1,
           "subclass 3-level chain -> 1");
    Expect(icall_type_is_subclass_of(&deepClass, &derivedClass) == 1,
           "subclass mid-chain -> 1");
    Expect(icall_type_is_subclass_of(&derivedClass, &baseClass) == 1,
           "subclass 2-level chain -> 1");
    Expect(icall_type_is_subclass_of(&implBaseClass, &baseClass) == 1,
           "subclass 1-level chain -> 1");
    Expect(icall_type_is_subclass_of(&deepClass, &deepClass) == 0,
           "subclass of self -> 0 (IsSubclassOf excludes self)");
    Expect(icall_type_is_subclass_of(&baseClass, &deepClass) == 0,
           "subclass wrong direction -> 0");
    Expect(icall_type_is_subclass_of(&otherClass, &baseClass) == 0,
           "subclass unrelated -> 0");
    Expect(ThrowsDotNet([&] { icall_type_is_subclass_of(nullptr, &baseClass); },
                        "System.NullReferenceException,System.Exception"),
           "subclass null derived -> NullReferenceException (net48 null receiver)");
    Expect(ThrowsDotNet([&] { icall_type_is_subclass_of(&deepClass, nullptr); },
                        "System.ArgumentNullException,System.Exception"),
           "subclass null base -> ArgumentNullException (net48)");

    // --- 10. get_type_code (net48 empirical values, full 12-type sweep) ---
    Expect(icall_object_get_type_code(il2cpp_box_int32(nullptr, 42)) == 9,
           "type code boxed Int32 -> 9");
    Expect(icall_object_get_type_code(icall_box_bool(true)) == 3,
           "type code boxed Boolean -> 3");
    Expect(icall_object_get_type_code(icall_box_char('A')) == 4,
           "type code boxed Char -> 4");
    Expect(icall_object_get_type_code(icall_box_double(1.5)) == 14,
           "type code boxed Double -> 14 (net48: Single=13)");
    Expect(icall_object_get_type_code(icall_box_sbyte(-1)) == 5,
           "type code boxed SByte -> 5");
    Expect(icall_object_get_type_code(icall_box_byte(200)) == 6,
           "type code boxed Byte -> 6");
    Expect(icall_object_get_type_code(icall_box_short(-5)) == 7,
           "type code boxed Int16 -> 7");
    Expect(icall_object_get_type_code(icall_box_ushort(65535)) == 8,
           "type code boxed UInt16 -> 8");
    Expect(icall_object_get_type_code(icall_box_uint(-1)) == 10,
           "type code boxed UInt32 -> 10");
    Expect(icall_object_get_type_code(icall_box_long(1234567890123LL)) == 11,
           "type code boxed Int64 -> 11");
    Expect(icall_object_get_type_code(icall_box_ulong(0xFFFFFFFFFFFFFFFFULL)) == 12,
           "type code boxed UInt64 -> 12");
    Expect(icall_object_get_type_code(icall_box_float(1.5)) == 13,
           "type code boxed Single -> 13 (net48)");
    Expect(icall_object_get_type_code(reinterpret_cast<Il2CppObject*>(
               il2cpp_string_new_from_cs("hi"))) == 18,
           "type code String -> 18");

    // --- 10b. get_type_code edge VALUES (klass-name path, extreme payloads) ---
    Expect(icall_object_get_type_code(il2cpp_box_int32(nullptr, -2147483647 - 1)) == 9,
           "type code Int32.MinValue -> 9");
    Expect(icall_object_get_type_code(il2cpp_box_int32(nullptr, 0)) == 9,
           "type code Int32 zero -> 9");
    Expect(icall_object_get_type_code(icall_box_long(INT64_MAX)) == 11,
           "type code Int64.MaxValue -> 11");
    Expect(icall_object_get_type_code(icall_box_long(INT64_MIN)) == 11,
           "type code Int64.MinValue -> 11");
    Expect(icall_object_get_type_code(icall_box_ulong(0)) == 12,
           "type code UInt64 zero -> 12");
    Expect(icall_object_get_type_code(icall_box_uint(INT32_MIN)) == 10,
           "type code UInt32 full pattern -> 10");
    Expect(icall_object_get_type_code(icall_box_byte(0)) == 6,
           "type code Byte zero -> 6");
    Expect(icall_object_get_type_code(icall_box_byte(255)) == 6,
           "type code Byte 255 -> 6");
    Expect(icall_object_get_type_code(icall_box_sbyte(-128)) == 5,
           "type code SByte.MinValue -> 5");
    Expect(icall_object_get_type_code(icall_box_short(32767)) == 7,
           "type code Int16.MaxValue -> 7");
    Expect(icall_object_get_type_code(icall_box_ushort(0)) == 8,
           "type code UInt16 zero -> 8");
    Expect(icall_object_get_type_code(icall_box_float(3.402823466e+38)) == 13,
           "type code Single max -> 13");
    Expect(icall_object_get_type_code(icall_box_float(-0.0)) == 13,
           "type code Single -0.0 -> 13");
    Expect(icall_object_get_type_code(icall_box_double(INT64_MIN)) == 14,
           "type code Double extreme -> 14");

    Expect(icall_object_get_type_code(&implBaseObj) == 1,
           "type code user class -> 1 (Object fallback)");
    Expect(icall_object_get_type_code(intArr) == 1,
           "type code array -> 1 (Object fallback)");
    Expect(ThrowsDotNet([&] { icall_object_get_type_code(nullptr); },
                        "System.NullReferenceException,System.Exception"),
           "type code null -> NullReferenceException");

    // --- 11. get_type (name as managed string) ---
    {
        Il2CppObject* t = icall_object_get_type(il2cpp_box_int32(nullptr, 7));
        Expect(ManagedStringEqualsAscii(t, "System.Int32"),
               "get_type boxed Int32 -> \"System.Int32\"");
    }
    Expect(ManagedStringEqualsAscii(
               icall_object_get_type(reinterpret_cast<Il2CppObject*>(
                   il2cpp_string_new_from_cs("hi"))),
               "System.String"),
           "get_type string -> \"System.String\"");
    Expect(ManagedStringEqualsAscii(icall_object_get_type(&implBaseObj),
                                    "T.ImplBase"),
           "get_type user class -> \"T.ImplBase\"");
    Expect(ManagedStringEqualsAscii(icall_object_get_type(&deepObj),
                                    "T.Deep"),
           "get_type deep class -> \"T.Deep\"");
    Expect(ThrowsDotNet([&] { icall_object_get_type(nullptr); },
                        "System.NullReferenceException,System.Exception"),
           "get_type null -> NullReferenceException");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d typecheck assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime typecheck tests passed\n");
    return 0;
}
