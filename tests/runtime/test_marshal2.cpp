//------------------------------------------------------------------------------
// test_marshal2.cpp -- Marshal primitive extension-2 unit tests (see
// il2cpp_marshal_ext2.h for the contract under test).
//
// Coverage (normal + edge + error paths):
//   1. size_of by net48 TypeCode: the documented sizes (Int32=9 -> 4,
//      Int64=11 -> 8, Double=14 -> 8, plus the full supported table
//      Boolean/Char/SByte/Byte/Int16/UInt16/UInt32/Single/UInt64/Decimal);
//      unsupported codes (Object=1, String=18, unknown) ->
//      NotSupportedException (no fixed native size, .NET semantics).
//   2. offset_of: packed-layout offsets for {int32,int64,double} (0/4/12)
//      and {bool,float,decimal} (0/1/5); fieldIndex == fieldCount and
//      negative fieldIndex -> ArgumentOutOfRangeException; negative
//      fieldCount -> ArgumentOutOfRangeException; empty layout has no field
//      0 -> ArgumentOutOfRangeException; null typeCodes (with fields) ->
//      ArgumentNullException; a prefix field with no fixed size ->
//      NotSupportedException. (The int64-overflow guard needs an >4GiB
//      table and is documented in the header, like batch 1's abort paths.)
//   3. last-error wrappers: set(0)/set(12345)/set(-1, DWORD 0xFFFFFFFF on
//      Windows) each round-trip through the immediate getter (no OS call
//      between set and get, so the read is deterministic).
//   4. string_to_hglobal_ansi: ASCII content + NUL terminator; empty string
//      yields a 1-byte buffer; null input -> nullptr (IntPtr.Zero). ASCII
//      only -- the M3 string model is ASCII (new_from_cs is an ASCII
//      literal helper; new_from_utf8 drops multi-byte sequences).
//   5. string_to_hglobal_uni: char16_t content + trailing NUL unit; null
//      input -> nullptr. Both buffers are released through
//      icall_marshal_free_hglobal (the documented pairing).
//
// Plain executable + minimal assertion harness (see test_marshalext.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_marshal_ext2.h"

#include "il2cpp_exception.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

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

// Run 'fn' and report whether it threw a DotNetException whose type chain
// exactly matches 'expectedType' (full chain -- see exceptionext.cpp).
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

// Expected type chains (must match exceptionext.cpp exactly).
const char* kArgumentNull =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,"
    "System.SystemException,System.Exception";
const char* kArgumentOutOfRange =
    "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,"
    "System.SystemException,System.Exception";
const char* kNotSupported =
    "System.NotSupportedException,System.NotSupportedException,System.SystemException,System.Exception";

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    // --- 1. size_of by net48 TypeCode ---
    Expect(il2cpp_marshal_size_of(9) == 4, "Int32 (9) is 4 bytes");
    Expect(il2cpp_marshal_size_of(11) == 8, "Int64 (11) is 8 bytes");
    Expect(il2cpp_marshal_size_of(14) == 8, "Double (14) is 8 bytes");
    Expect(il2cpp_marshal_size_of(13) == 4, "Single (13) is 4 bytes");
    Expect(il2cpp_marshal_size_of(3) == 1, "Boolean (3) is 1 byte");
    Expect(il2cpp_marshal_size_of(4) == 2, "Char (4) is 2 bytes");
    Expect(il2cpp_marshal_size_of(5) == 1, "SByte (5) is 1 byte");
    Expect(il2cpp_marshal_size_of(6) == 1, "Byte (6) is 1 byte");
    Expect(il2cpp_marshal_size_of(7) == 2, "Int16 (7) is 2 bytes");
    Expect(il2cpp_marshal_size_of(8) == 2, "UInt16 (8) is 2 bytes");
    Expect(il2cpp_marshal_size_of(10) == 4, "UInt32 (10) is 4 bytes");
    Expect(il2cpp_marshal_size_of(12) == 8, "UInt64 (12) is 8 bytes");
    Expect(il2cpp_marshal_size_of(15) == 16, "Decimal (15) is 16 bytes");
    Expect(ThrowsDotNet([] { (void)il2cpp_marshal_size_of(1); }, kNotSupported),
           "Object (1) -> NotSupportedException");
    Expect(ThrowsDotNet([] { (void)il2cpp_marshal_size_of(18); }, kNotSupported),
           "String (18) -> NotSupportedException");
    Expect(ThrowsDotNet([] { (void)il2cpp_marshal_size_of(16); }, kNotSupported),
           "DateTime (16) -> NotSupportedException");
    Expect(ThrowsDotNet([] { (void)il2cpp_marshal_size_of(99); }, kNotSupported),
           "unknown code -> NotSupportedException");

    // --- 2. offset_of (packed layout) ---
    int32_t mixedLayout[3] = { 9, 11, 14 }; // int32, int64, double
    Expect(il2cpp_marshal_offset_of(0, 3, mixedLayout) == 0, "offset of field 0 is 0");
    Expect(il2cpp_marshal_offset_of(1, 3, mixedLayout) == 4, "offset of field 1 is 4");
    Expect(il2cpp_marshal_offset_of(2, 3, mixedLayout) == 12, "offset of field 2 is 12");
    int32_t smallLayout[3] = { 3, 13, 15 }; // bool, float, decimal
    Expect(il2cpp_marshal_offset_of(1, 3, smallLayout) == 1, "bool+float: field 1 at 1");
    Expect(il2cpp_marshal_offset_of(2, 3, smallLayout) == 5, "decimal field at 5");
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_offset_of(3, 3, mixedLayout); }, kArgumentOutOfRange),
           "fieldIndex == fieldCount -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_offset_of(-1, 3, mixedLayout); }, kArgumentOutOfRange),
           "negative fieldIndex -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_offset_of(0, -1, mixedLayout); }, kArgumentOutOfRange),
           "negative fieldCount -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_offset_of(0, 0, nullptr); }, kArgumentOutOfRange),
           "empty layout has no field 0 -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_offset_of(1, 2, nullptr); }, kArgumentNull),
           "null typeCodes with fields -> ArgumentNullException");
    int32_t badLayout[2] = { 18, 9 }; // String field in the prefix of field 1
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_offset_of(1, 2, badLayout); }, kNotSupported),
           "prefix field without fixed size -> NotSupportedException");

    // --- 3. last-error round trips (set then immediate get: no OS call in
    // ---    between, so the read is deterministic on both platforms) ---
    il2cpp_marshal_set_last_win32_error(0);
    Expect(il2cpp_marshal_get_last_win32_error() == 0, "last error 0 round-trips");
    il2cpp_marshal_set_last_win32_error(12345);
    Expect(il2cpp_marshal_get_last_win32_error() == 12345, "last error 12345 round-trips");
    il2cpp_marshal_set_last_win32_error(-1); // DWORD 0xFFFFFFFF on Windows
    Expect(il2cpp_marshal_get_last_win32_error() == -1, "last error -1 round-trips");

    // --- 4. string_to_hglobal_ansi ---
    il2cpp::Il2CppString* hello = il2cpp::il2cpp_string_new_from_cs("hello");
    void* ansi = il2cpp_marshal_string_to_hglobal_ansi(hello);
    Expect(ansi != nullptr, "ansi marshal returns a buffer");
    const char* a = static_cast<const char*>(ansi);
    Expect(std::strcmp(a, "hello") == 0, "ansi content is the UTF-8 string + NUL");
    icall_marshal_free_hglobal(ansi);
    il2cpp::Il2CppString* empty = il2cpp::il2cpp_string_new_from_cs("");
    void* ansiEmpty = il2cpp_marshal_string_to_hglobal_ansi(empty);
    Expect(ansiEmpty != nullptr && static_cast<const char*>(ansiEmpty)[0] == '\0',
           "empty string -> 1-byte NUL-only buffer");
    icall_marshal_free_hglobal(ansiEmpty);
    Expect(il2cpp_marshal_string_to_hglobal_ansi(nullptr) == nullptr,
           "ansi(null) -> nullptr (IntPtr.Zero)");

    // --- 5. string_to_hglobal_uni ---
    void* uni = il2cpp_marshal_string_to_hglobal_uni(hello);
    Expect(uni != nullptr, "uni marshal returns a buffer");
    const char16_t* u = static_cast<const char16_t*>(uni);
    Expect(u[0] == u'h' && u[1] == u'e' && u[2] == u'l' && u[3] == u'l' &&
           u[4] == u'o' && u[5] == u'\0',
           "uni content is UTF-16 units + trailing NUL");
    icall_marshal_free_hglobal(uni);
    void* uniEmpty = il2cpp_marshal_string_to_hglobal_uni(empty);
    Expect(uniEmpty != nullptr && static_cast<const char16_t*>(uniEmpty)[0] == u'\0',
           "empty string -> NUL-only UTF-16 buffer");
    icall_marshal_free_hglobal(uniEmpty);
    Expect(il2cpp_marshal_string_to_hglobal_uni(nullptr) == nullptr,
           "uni(null) -> nullptr (IntPtr.Zero)");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d marshal2 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime marshal2 tests passed\n");
    return 0;
}
