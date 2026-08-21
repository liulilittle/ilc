//------------------------------------------------------------------------------
// test_typeext.cpp -- System.Type primitive unit tests.
//
// Coverage (normal + edge + error paths; see il2cpp_typeext.h for the
// conformance model -- base chain / interface table / '['-prefix array rule):
//   1. get_full_name -- full name as managed string; null -> ArgumentNull-
//      Exception.
//   2. is_assignable_from -- identity (incl. self -> 1, unlike IsSubclassOf);
//      base chain direction (derived assignable to base, NOT reverse);
//      interface: direct implementor -> 1, inherited through the base chain
//      -> 1, non-implementor -> 0; arrays: '['-prefix rule (M4); array vs
//      non-array -> 0; null RECEIVER (target) -> NullReferenceException
//      (net48 null Type); null ARGUMENT (source) -> 0 (net48
//      IsAssignableFrom(null) -> False, no throw).
//   3. get_typecode -- Int32 -> 9, Boolean -> 3, Char -> 4, Double -> 14
//      (net48 empirical; Single is 13), String -> 18, user class -> 1,
//      null -> 0 (TypeCode.Empty -- net48 Type.GetTypeCode(null)).
//   4. get_type_from_handle -- returns the type NAME as a managed string;
//      null -> nullptr (net48 Type.GetTypeFromHandle(default) -> null, the
//      default RuntimeTypeHandle is a null descriptor here -- no throw).
//   5. get_element_type -- array '['-prefix -> element type NAME as a managed
//      string; NON-array type -> nullptr with no throw; null receiver ->
//      NullReferenceException (all net48 probe-verified: typeof(int[]).
//      GetElementType() == typeof(int), typeof(int).GetElementType() == null,
//      ((Type)null).GetElementType() throws NRE; a nested '[' (jagged) element
//      is itself an array name -- "[[System.Int32" -> "[System.Int32").
//
// Plain executable + minimal assertion harness (see test_typecheck.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_typeext.h"

#include "il2cpp_exception.h"  // DotNetException
#include "il2cpp_metadata.h"
#include "il2cpp_string.h"     // il2cpp_string_new_from_cs / Il2CppString

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
// Test fixture: a 3-level user class hierarchy + an interface (same shape as
// test_typecheck.cpp) plus the descriptors needed for TypeCode / FullName /
// Object-assignability coverage.
//
//   System.Object  (root -- the base chain terminates here)
//   T.Base         (derives System.Object)
//   T.IFoo         (interface descriptor; 2 virtual slots, no impl table)
//   T.ImplBase     (derives T.Base; implements T.IFoo -- own interface map)
//   T.Derived      (derives T.ImplBase; NO own interface map -- the
//                    interface must be found through the base chain)
//   T.Deep         (derives T.Derived -- 3-level chain for walk tests)
//   T.Other        (unrelated class)
//   [System.Int32 / [System.Double (array-class descriptors)
//------------------------------------------------------------------------------

il2cpp::Il2CppClass objectClass = {
    "System.Object", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppClass baseClass = {
    "T.Base", 16, 0u, 0, &objectClass, nullptr, 0, nullptr, 0,
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
    "T.Other", 16, 0u, 0, &objectClass, nullptr, 0, nullptr, 0,
};

// Array-class descriptors for the '['-prefix rule (mirror array.cpp names).
il2cpp::Il2CppClass int32ArrayClass = {
    "[System.Int32", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppClass doubleArrayClass = {
    "[System.Double", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppClass stringArrayClass = {
    "[System.String", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
// A jagged array descriptor: the element of int[][] is int[] -- the nested
// '[' encodes that ("[[System.Int32" -> element "[System.Int32").
il2cpp::Il2CppClass jaggedIntArrayClass = {
    "[[System.Int32", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

// Primitive descriptors for TypeCode mapping (names mirror typecheck.cpp's
// icall_object_get_type_code table).
il2cpp::Il2CppClass int32Class = {"System.Int32", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0};
il2cpp::Il2CppClass boolClass = {"System.Boolean", 1, 0u, 0, nullptr, nullptr, 0, nullptr, 0};
il2cpp::Il2CppClass charClass = {"System.Char", 2, 0u, 0, nullptr, nullptr, 0, nullptr, 0};
il2cpp::Il2CppClass doubleClass = {"System.Double", 8, 0u, 0, nullptr, nullptr, 0, nullptr, 0};
il2cpp::Il2CppClass stringClass = {"System.String", 16, 0u, 0, &objectClass, nullptr, 0, nullptr, 0};

// Compare a managed string with an ASCII literal (the name strings are ASCII
// by construction).
bool ManagedStringEqualsAscii(il2cpp::Il2CppString* s, const char* ascii)
{
    const size_t len = std::strlen(ascii);
    if (s == nullptr || static_cast<size_t>(s->length) != len)
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

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    // --- 1. get_full_name ---
    Expect(ManagedStringEqualsAscii(il2cpp_type_get_full_name(&baseClass), "T.Base"),
           "full_name base -> \"T.Base\"");
    Expect(ManagedStringEqualsAscii(il2cpp_type_get_full_name(&deepClass), "T.Deep"),
           "full_name deep -> \"T.Deep\"");
    Expect(ManagedStringEqualsAscii(il2cpp_type_get_full_name(&int32Class), "System.Int32"),
           "full_name int32 -> \"System.Int32\"");
    Expect(ManagedStringEqualsAscii(il2cpp_type_get_full_name(&int32ArrayClass),
                                    "[System.Int32"),
           "full_name array class -> \"[System.Int32\"");
    Expect(ThrowsDotNet([&] { il2cpp_type_get_full_name(nullptr); },
                        "System.ArgumentNullException,System.Exception"),
           "full_name null -> ArgumentNullException");

    // --- 2. is_assignable_from ---
    // 2a. identity (incl. self, unlike IsSubclassOf)
    Expect(il2cpp_type_is_assignable_from(&baseClass, &baseClass) == 1,
           "assignable identity -> 1");
    Expect(il2cpp_type_is_assignable_from(&deepClass, &deepClass) == 1,
           "assignable self -> 1 (a type is assignable to itself)");
    // 2b. base chain direction
    Expect(il2cpp_type_is_assignable_from(&baseClass, &deepClass) == 1,
           "assignable 3-level chain (base from deep) -> 1");
    Expect(il2cpp_type_is_assignable_from(&derivedClass, &deepClass) == 1,
           "assignable mid-chain (derived from deep) -> 1");
    Expect(il2cpp_type_is_assignable_from(&deepClass, &baseClass) == 0,
           "assignable wrong direction (deep from base) -> 0");
    Expect(il2cpp_type_is_assignable_from(&objectClass, &baseClass) == 1,
           "assignable to System.Object (root chain) -> 1");
    Expect(il2cpp_type_is_assignable_from(&baseClass, &objectClass) == 0,
           "assignable object from base -> 0 (direction)");
    Expect(il2cpp_type_is_assignable_from(&baseClass, &otherClass) == 0,
           "assignable unrelated classes -> 0");
    // 2c. interfaces
    Expect(il2cpp_type_is_assignable_from(&ifaceClass, &implBaseClass) == 1,
           "assignable direct implementor -> 1");
    Expect(il2cpp_type_is_assignable_from(&ifaceClass, &deepClass) == 1,
           "assignable interface through base chain -> 1");
    Expect(il2cpp_type_is_assignable_from(&ifaceClass, &ifaceClass) == 1,
           "assignable interface to itself -> 1 (identity)");
    Expect(il2cpp_type_is_assignable_from(&ifaceClass, &otherClass) == 0,
           "assignable non-implementor -> 0");
    Expect(il2cpp_type_is_assignable_from(&implBaseClass, &ifaceClass) == 0,
           "assignable iface to impl -> 0 (interface is not a class)");
    // 2d. arrays ('['-prefix rule, M4 simplification)
    Expect(il2cpp_type_is_assignable_from(&int32ArrayClass, &doubleArrayClass) == 1,
           "assignable int32[] from double[] -> 1 (M4 prefix-only rule)");
    Expect(il2cpp_type_is_assignable_from(&int32ArrayClass, &baseClass) == 0,
           "assignable array from class -> 0");
    Expect(il2cpp_type_is_assignable_from(&baseClass, &int32ArrayClass) == 0,
           "assignable class from array -> 0");
    Expect(il2cpp_type_is_assignable_from(&objectClass, &int32ArrayClass) == 0,
           "assignable object from array -> 0 (M4: arrays have no base chain)");
    // 2e. null args (net48 asymmetry, probe-verified): a null RECEIVER
    // (target) throws NullReferenceException; a null ARGUMENT (source)
    // returns false -- typeof(int).IsAssignableFrom(null) -> False, no throw.
    Expect(ThrowsDotNet([&] { il2cpp_type_is_assignable_from(nullptr, &baseClass); },
                        "System.NullReferenceException,System.Exception"),
           "assignable null target -> NullReferenceException (net48 null receiver)");
    Expect(il2cpp_type_is_assignable_from(&baseClass, nullptr) == 0,
           "assignable null source -> 0 (net48 IsAssignableFrom(null) -> False)");
    Expect(ThrowsDotNet([&] { il2cpp_type_is_assignable_from(nullptr, nullptr); },
                        "System.NullReferenceException,System.Exception"),
           "assignable null/null -> NullReferenceException (net48 null receiver)");

    // --- 3. get_typecode (net48 empirical values) ---
    Expect(il2cpp_type_get_typecode(&int32Class) == 9,
           "typecode Int32 -> 9");
    Expect(il2cpp_type_get_typecode(&boolClass) == 3,
           "typecode Boolean -> 3");
    Expect(il2cpp_type_get_typecode(&charClass) == 4,
           "typecode Char -> 4");
    Expect(il2cpp_type_get_typecode(&doubleClass) == 14,
           "typecode Double -> 14 (net48: Single=13)");
    Expect(il2cpp_type_get_typecode(&stringClass) == 18,
           "typecode String -> 18");
    Expect(il2cpp_type_get_typecode(&baseClass) == 1,
           "typecode user class -> 1 (Object fallback)");
    Expect(il2cpp_type_get_typecode(&int32ArrayClass) == 1,
           "typecode array -> 1 (Object fallback)");
    Expect(il2cpp_type_get_typecode(nullptr) == 0,
           "typecode null -> 0 (TypeCode.Empty, net48 Type.GetTypeCode(null))");

    // --- 4. get_type_from_handle (simplified Type = name string) ---
    Expect(ManagedStringEqualsAscii(
               reinterpret_cast<Il2CppString*>(
                   il2cpp_type_get_type_from_handle(&baseClass)),
               "T.Base"),
           "get_type_from_handle base -> \"T.Base\"");
    Expect(ManagedStringEqualsAscii(
               reinterpret_cast<Il2CppString*>(
                   il2cpp_type_get_type_from_handle(&stringClass)),
               "System.String"),
           "get_type_from_handle string -> \"System.String\"");
    Expect(ManagedStringEqualsAscii(
               reinterpret_cast<Il2CppString*>(
                   il2cpp_type_get_type_from_handle(&doubleArrayClass)),
               "[System.Double"),
           "get_type_from_handle array -> \"[System.Double\"");
    // net48: Type.GetTypeFromHandle(default(RuntimeTypeHandle)) -> null, NO
    // throw (a null descriptor IS the default handle in this runtime -- the
    // handle VALUE is the descriptor address).
    Expect(il2cpp_type_get_type_from_handle(nullptr) == nullptr,
           "get_type_from_handle null -> nullptr (net48 GetTypeFromHandle(default) -> null)");

    // --- 5. get_element_type (net48 probe-verified) ---
    // Array '['-prefix -> element type NAME as a managed string.
    Expect(ManagedStringEqualsAscii(
               reinterpret_cast<Il2CppString*>(
                   il2cpp_type_get_element_type(&int32ArrayClass)),
               "System.Int32"),
           "get_element_type int32[] -> \"System.Int32\" (net48 typeof(int[]).GetElementType())");
    Expect(ManagedStringEqualsAscii(
               reinterpret_cast<Il2CppString*>(
                   il2cpp_type_get_element_type(&doubleArrayClass)),
               "System.Double"),
           "get_element_type double[] -> \"System.Double\" (net48 typeof(double[]).GetElementType())");
    Expect(ManagedStringEqualsAscii(
               reinterpret_cast<Il2CppString*>(
                   il2cpp_type_get_element_type(&stringArrayClass)),
               "System.String"),
           "get_element_type string[] -> \"System.String\" (net48 typeof(string[]).GetElementType())");
    // NON-array type -> nullptr, NO throw (net48 typeof(int).GetElementType()
    // -> null).
    Expect(il2cpp_type_get_element_type(&baseClass) == nullptr,
           "get_element_type non-array class -> nullptr (net48 typeof(X).GetElementType() -> null)");
    Expect(il2cpp_type_get_element_type(&stringClass) == nullptr,
           "get_element_type non-array string -> nullptr (net48 typeof(string).GetElementType() -> null)");
    Expect(il2cpp_type_get_element_type(&ifaceClass) == nullptr,
           "get_element_type interface -> nullptr (net48 typeof(interface).GetElementType() -> null)");
    // Nested '[' (jagged) element: the element of int[][] is int[] -- the
    // stripped name is itself an array class name.
    Expect(ManagedStringEqualsAscii(
               reinterpret_cast<Il2CppString*>(
                   il2cpp_type_get_element_type(&jaggedIntArrayClass)),
               "[System.Int32"),
           "get_element_type int[][] -> \"[System.Int32\" (net48 typeof(int[][]).GetElementType() == int[])");
    // Null receiver -> NullReferenceException (net48 ((Type)null).GetElementType()).
    Expect(ThrowsDotNet([&] { il2cpp_type_get_element_type(nullptr); },
                        "System.NullReferenceException,System.Exception"),
           "get_element_type null receiver -> NullReferenceException (net48)");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d typeext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime typeext tests passed\n");
    return 0;
}
