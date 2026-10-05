//------------------------------------------------------------------------------
// test_boxingext.cpp -- extended boxing/unboxing icall unit tests.
//
// Coverage (normal + edge + error paths):
//   1. bool round-trip: box true/false -> unbox returns the original value;
//      the payload byte pattern is int32 0/1 at offset 8 (layout check).
//   2. char round-trip: UTF-16 code units incl. boundary values 0x0000/0xFFFF.
//   3. double round-trip: typical value, negative zero, NaN, huge exponent.
//   4. int32 interop: il2cpp_box_int32 (boxing.cpp) -> icall_unbox_int32.
//   5. Layout compatibility with the Int32 box (see boxing.cpp): every box
//      class has instanceSize 16, payload at offset 8, refFieldBitmap 0, and
//      is registered under the primitive's full type name after first use.
//   6. Type mismatch -> InvalidCastException (every box-class pair direction);
//      catchability through il2cpp_exception_matches (exact + base type), the
//      generated catch-clause dispatch contract; boxing.cpp's legacy
//      il2cpp_unbox_int32 throws (never aborts) on wrong type and null.
//   7. Null unbox -> NullReferenceException (all four unbox icalls).
//   8. icall_boxed_equals: per-type value equality, NaN / -0.0 double
//      semantics, cross-type inequality, null handling, string reference
//      equality.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_boxing_ext.h"

#include "il2cpp_exception.h"
#include "il2cpp_runtime.h" // il2cpp_box_int32 / il2cpp_class_by_name
#include "il2cpp_string.h"  // il2cpp_string_new_from_cs

#include <cmath>
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

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    // --- 1. bool round-trip + canonical int32 0/1 payload ---
    Il2CppObject* bTrue = icall_box_bool(true);
    Il2CppObject* bFalse = icall_box_bool(false);
    Expect(bTrue != nullptr && bFalse != nullptr, "box bool allocates managed objects");
    Expect(icall_unbox_bool(bTrue), "boxed true unboxes to true");
    Expect(!icall_unbox_bool(bFalse), "boxed false unboxes to false");
    Expect(*reinterpret_cast<int32_t*>(reinterpret_cast<char*>(bTrue) + 8) == 1,
           "boxed true payload is int32 1 at offset 8");
    Expect(*reinterpret_cast<int32_t*>(reinterpret_cast<char*>(bFalse) + 8) == 0,
           "boxed false payload is int32 0 at offset 8");

    // --- 2. char round-trip (UTF-16 code units, incl. boundaries) ---
    Expect(icall_unbox_char(icall_box_char('A')) == 65, "boxed 'A' unboxes to UTF-16 65");
    Expect(icall_unbox_char(icall_box_char(0)) == 0, "boxed U+0000 round-trips");
    Expect(icall_unbox_char(icall_box_char(0xFFFF)) == 0xFFFF, "boxed U+FFFF round-trips");

    // --- 3. double round-trip (incl. edge values) ---
    const double nan = std::numeric_limits<double>::quiet_NaN();
    Expect(icall_unbox_double(icall_box_double(3.14)) == 3.14, "boxed 3.14 round-trips");
    Expect(icall_unbox_double(icall_box_double(-0.0)) == 0.0, "boxed -0.0 round-trips (value-wise)");
    Expect(std::signbit(icall_unbox_double(icall_box_double(-0.0))), "boxed -0.0 preserves its sign bit");
    Expect(std::isnan(icall_unbox_double(icall_box_double(nan))), "boxed NaN round-trips as NaN");
    Expect(icall_unbox_double(icall_box_double(1e300)) == 1e300, "boxed 1e300 round-trips");

    // --- 4. int32 interop with boxing.cpp ---
    Il2CppObject* i42 = il2cpp_box_int32(nullptr, 42);
    Expect(icall_unbox_int32(i42) == 42, "il2cpp_box_int32 object unboxes via icall_unbox_int32");
    Expect(icall_unbox_int32(il2cpp_box_int32(nullptr, -7)) == -7, "negative int32 round-trips via icall");

    // --- 5. layout compatibility with the Int32 box (boxing.cpp contract) ---
    Il2CppClass* int32Klass = i42->klass;
    Expect(int32Klass != nullptr && std::strcmp(int32Klass->name, "System.Int32") == 0,
           "int32 box class identity is System.Int32");
    Expect(int32Klass->instanceSize == 16 && int32Klass->refFieldBitmap == 0u,
           "int32 box layout: 16 bytes, no reference fields");
    Expect(bTrue->klass->instanceSize == 16 && bTrue->klass->refFieldBitmap == 0u,
           "bool box layout: 16 bytes, no reference fields");
    Il2CppObject* ch = icall_box_char('A');
    Expect(ch->klass->instanceSize == 16 && ch->klass->refFieldBitmap == 0u,
           "char box layout: 16 bytes, no reference fields");
    Il2CppObject* db = icall_box_double(1.5);
    Expect(db->klass->instanceSize == 16 && db->klass->refFieldBitmap == 0u,
           "double box layout: 16 bytes, no reference fields");
    Expect(std::strcmp(bTrue->klass->name, "System.Boolean") == 0, "bool box class identity is System.Boolean");
    Expect(std::strcmp(ch->klass->name, "System.Char") == 0, "char box class identity is System.Char");
    Expect(std::strcmp(db->klass->name, "System.Double") == 0, "double box class identity is System.Double");
    // Registration visibility: each box class resolves by name after first use.
    Expect(il2cpp_class_by_name("System.Boolean") != nullptr, "System.Boolean registered after first box");
    Expect(il2cpp_class_by_name("System.Char") != nullptr, "System.Char registered after first box");
    Expect(il2cpp_class_by_name("System.Double") != nullptr, "System.Double registered after first box");

    // --- 5b. CANONICAL DESCRIPTORS + interface tables (boxdeep contract): the
    // box classes MUST be the metadata.cpp builtin descriptors
    // (il2cpp_metadata.h: the extern System_Int32_class & co. -- generated
    // isinst/castclass compares against THOSE pointers), and each carries its
    // .NET interface table so `boxed is IConvertible/IComparable/IFormattable`
    // holds. A file-local duplicate descriptor would make isinst miss. ---
    Expect(i42->klass == &System_Int32_class,
           "boxed int32's klass IS the builtin System_Int32_class descriptor");
    Expect(bTrue->klass == &System_Boolean_class,
           "boxed bool's klass IS the builtin System_Boolean_class descriptor");
    Expect(ch->klass == &System_Char_class,
           "boxed char's klass IS the builtin System_Char_class descriptor");
    Expect(db->klass == &System_Double_class,
           "boxed double's klass IS the builtin System_Double_class descriptor");
    Expect(int32Klass == &System_Int32_class && il2cpp_is_instance(i42, &System_Int32_class),
           "il2cpp_is_instance(boxed int32, System_Int32) matches the canonical descriptor");
    // Numeric boxes (Int32/Double): IConvertible + IComparable + IFormattable.
    Expect(i42->klass->interfaces != nullptr && i42->klass->interfaceCount == 3,
           "boxed int32 carries its 3-interface table (IConvertible/IComparable/IFormattable)");
    Expect(il2cpp_is_instance(i42, &System_IConvertible_class),
           "boxed int32 IS IConvertible (isinst hit)");
    Expect(il2cpp_is_instance(i42, &System_IComparable_class),
           "boxed int32 IS IComparable (isinst hit)");
    Expect(il2cpp_is_instance(i42, &System_IFormattable_class),
           "boxed int32 IS IFormattable (isinst hit)");
    Expect(db->klass->interfaces != nullptr && db->klass->interfaceCount == 3,
           "boxed double carries its 3-interface table");
    Expect(il2cpp_is_instance(db, &System_IConvertible_class),
           "boxed double IS IConvertible (isinst hit)");
    // Boolean/Char: IConvertible + IComparable only (no IFormattable -- .NET).
    Expect(bTrue->klass->interfaces != nullptr && bTrue->klass->interfaceCount == 2,
           "boxed bool carries its 2-interface table");
    Expect(il2cpp_is_instance(bTrue, &System_IComparable_class),
           "boxed bool IS IComparable (isinst hit)");
    Expect(!il2cpp_is_instance(bTrue, &System_IFormattable_class),
           "boxed bool is NOT IFormattable (no interface-table entry)");
    Expect(ch->klass->interfaceCount == 2 && il2cpp_is_instance(ch, &System_IConvertible_class),
           "boxed char IS IConvertible via its 2-interface table");

    // --- 6. type mismatch -> InvalidCastException (box-class pair directions) ---
    Expect(ThrowsDotNet([&] { icall_unbox_bool(i42); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed int32 as bool throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_char(i42); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed int32 as char throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_double(bTrue); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed bool as double throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_bool(ch); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed char as bool throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_int32(db); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed double as int32 throws InvalidCastException");
    Expect(ThrowsDotNet([&] { icall_unbox_double(ch); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "unboxing a boxed char as double throws InvalidCastException");

    // --- 6b. CATCHABILITY: the thrown chain must match the generated
    // catch-clause dispatch (il2cpp_exception_matches) -- exact type AND
    // base type. This is the contract the transpiler's try/catch emission
    // (il2cpp_exception_matches("<CaughtType>", e.type_name())) relies on;
    // a chain without the duplicated exact type would be RETHROWN and
    // reported as an unhandled exception at runtime. ---
    try
    {
        icall_unbox_char(i42);
        Expect(false, "wrong-type unbox throws (control)");
    }
    catch (const il2cpp::DotNetException& e)
    {
        Expect(il2cpp::il2cpp_exception_matches("System.InvalidCastException", e.type_name()),
               "catch(InvalidCastException) matches a wrong-type unbox (exact type)");
        Expect(il2cpp::il2cpp_exception_matches("System.Exception", e.type_name()),
               "catch(Exception) matches a wrong-type unbox (base type)");
        Expect(!il2cpp::il2cpp_exception_matches("System.NullReferenceException", e.type_name()),
               "unrelated caught type does not match a wrong-type unbox");
    }
    try
    {
        icall_unbox_double(bTrue);
        Expect(false, "wrong-type double unbox throws (control)");
    }
    catch (const il2cpp::DotNetException& e)
    {
        Expect(il2cpp::il2cpp_exception_matches("System.InvalidCastException", e.type_name()),
               "catch(InvalidCastException) matches a wrong-type double unbox");
        Expect(il2cpp::il2cpp_exception_matches("System.SystemException", e.type_name()),
               "catch(SystemException) matches a wrong-type double unbox (base type)");
    }

    // --- 6c. boxing.cpp legacy helper: il2cpp_unbox_int32 throws (not
    // aborts) on null / wrong type -- the boxext case's (int)o unbox of a
    // boxed bool depends on the InvalidCastException being catchable. ---
    Expect(ThrowsDotNet([&] { il2cpp_unbox_int32(bTrue); }, "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception"),
           "il2cpp_unbox_int32 on a boxed bool throws InvalidCastException (no abort)");
    try
    {
        il2cpp_unbox_int32(bTrue);
        Expect(false, "il2cpp_unbox_int32 wrong-type unbox throws (control)");
    }
    catch (const il2cpp::DotNetException& e)
    {
        Expect(il2cpp::il2cpp_exception_matches("System.InvalidCastException", e.type_name()),
               "catch(InvalidCastException) matches il2cpp_unbox_int32 wrong-type unbox");
    }
    Expect(ThrowsDotNet([&] { il2cpp_unbox_int32(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "il2cpp_unbox_int32 on null throws NullReferenceException (no abort)");
    try
    {
        il2cpp_unbox_int32(nullptr);
        Expect(false, "il2cpp_unbox_int32 null unbox throws (control)");
    }
    catch (const il2cpp::DotNetException& e)
    {
        Expect(il2cpp::il2cpp_exception_matches("System.NullReferenceException", e.type_name()),
               "catch(NullReferenceException) matches il2cpp_unbox_int32 null unbox");
    }

    // --- 7. null unbox -> NullReferenceException (all four icalls) ---
    Expect(ThrowsDotNet([&] { icall_unbox_int32(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "unbox of null as int32 throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_unbox_bool(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "unbox of null as bool throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_unbox_char(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "unbox of null as char throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_unbox_double(nullptr); }, "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"),
           "unbox of null as double throws NullReferenceException");

    // --- 8. icall_boxed_equals ---
    // bool / char / int32: payload equality per type.
    Expect(icall_boxed_equals(icall_box_bool(true), icall_box_bool(true)), "boxed true equals boxed true");
    Expect(!icall_boxed_equals(icall_box_bool(true), icall_box_bool(false)), "boxed true != boxed false");
    Expect(icall_boxed_equals(icall_box_char('A'), icall_box_char('A')), "boxed 'A' equals boxed 'A'");
    Expect(!icall_boxed_equals(icall_box_char('A'), icall_box_char('B')), "boxed 'A' != boxed 'B'");
    Expect(icall_boxed_equals(il2cpp_box_int32(nullptr, 42), il2cpp_box_int32(nullptr, 42)),
           "boxed 42 equals boxed 42");
    Expect(!icall_boxed_equals(il2cpp_box_int32(nullptr, 42), il2cpp_box_int32(nullptr, 43)),
           "boxed 42 != boxed 43");
    // double: value equality with .NET Double.Equals edge semantics.
    Expect(icall_boxed_equals(icall_box_double(3.14), icall_box_double(3.14)), "boxed 3.14 equals boxed 3.14");
    Expect(!icall_boxed_equals(icall_box_double(3.14), icall_box_double(3.15)), "boxed 3.14 != boxed 3.15");
    Expect(icall_boxed_equals(icall_box_double(nan), icall_box_double(nan)), "NaN equals NaN (Double.Equals)");
    Expect(icall_boxed_equals(icall_box_double(-0.0), icall_box_double(0.0)), "-0.0 equals +0.0 (Double.Equals)");
    // Cross-type: same numeric value, different box class -> unequal.
    Expect(!icall_boxed_equals(il2cpp_box_int32(nullptr, 5), icall_box_double(5.0)),
           "boxed int 5 != boxed double 5.0 (cross-type)");
    Expect(!icall_boxed_equals(icall_box_char('A'), il2cpp_box_int32(nullptr, 65)),
           "boxed char 'A' != boxed int 65 (cross-type)");
    // Null handling.
    Expect(icall_boxed_equals(nullptr, nullptr), "null equals null");
    Expect(!icall_boxed_equals(nullptr, icall_box_bool(true)), "null != boxed value");
    Expect(!icall_boxed_equals(icall_box_bool(true), nullptr), "boxed value != null");
    // Strings: reference equality only (distinct instances are unequal even
    // with identical content; a string compared to itself is equal). The
    // string's object header is its first member (offset 0), so the
    // reinterpret_cast is the established string->object up-cast pattern.
    Il2CppString* s1 = il2cpp_string_new_from_cs("hello");
    Il2CppString* s2 = il2cpp_string_new_from_cs("hello");
    Il2CppObject* s1o = reinterpret_cast<Il2CppObject*>(s1);
    Il2CppObject* s2o = reinterpret_cast<Il2CppObject*>(s2);
    Expect(icall_boxed_equals(s1o, s1o), "same string reference equals itself");
    Expect(!icall_boxed_equals(s1o, s2o), "distinct strings with equal content are unequal (reference equality)");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d boxing-ext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime boxing-ext tests passed\n");
    return 0;
}
