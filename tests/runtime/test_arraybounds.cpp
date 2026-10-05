//------------------------------------------------------------------------------
// test_arraybounds.cpp -- bounds-checked element access throws catchable
// managed exceptions.
//
// P0 regression: il2cpp_array_get_elem_ptr out-of-range access used to
// std::abort() -- generated try/catch could not observe it. The failure
// path now throws a catchable DotNetException (.NET semantics):
//   out-of-range index -> IndexOutOfRangeException
//   null array          -> NullReferenceException
//
// Coverage (normal + edge + error paths):
//   1. In-range access (first / last element) returns the correct pointer
//      (the hot path is unchanged).
//   2. Negative index -> IndexOutOfRangeException, caught by C++ catch.
//   3. index == length (one past the end) -> IndexOutOfRangeException.
//   4. Large positive index -> IndexOutOfRangeException.
//   5. Empty array (length 0): index 0 -> IndexOutOfRangeException.
//   6. Null array -> NullReferenceException (not IndexOutOfRange).
//   7. catch(System.Exception) matches the thrown IndexOutOfRangeException
//      (base-class catch, .NET inheritance via il2cpp_exception_matches).
//   8. No false positives: an unrelated caught type never matches.
//   9. Default messages are the .NET standard texts (substring check).
//
// Plain executable + minimal assertion harness (see test_exceptionext.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_exception_ext.h"

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

// Snapshot of a caught exception: the type chain and the message are copied
// out of the exception at catch time (see test_exceptionext.cpp).
struct CaughtInfo
{
    bool   caught;
    char   type[160];
    char   msg[256];
};

template <typename ThrowFn>
CaughtInfo CatchIt(ThrowFn fn)
{
    CaughtInfo info = { false, "", "" };
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        info.caught = true;
#if defined(_MSC_VER)
        // C4996: MSVC deprecates strncpy -> strncpy_s (test_exceptionext.cpp
        // idiom); MinGW keeps the plain strncpy path.
        ::strncpy_s(info.type, sizeof(info.type), e.type_name(), sizeof(info.type) - 1);
        ::strncpy_s(info.msg, sizeof(info.msg), e.message_text(), sizeof(info.msg) - 1);
#else
        std::strncpy(info.type, e.type_name(), sizeof(info.type) - 1);
        std::strncpy(info.msg, e.message_text(), sizeof(info.msg) - 1);
#endif
    }
    return info;
}

// True when the caught chain matches the managed type (the exact conformance
// rule the generated catch clauses use).
bool IsType(const char* thrownChain, const char* managedType)
{
    return il2cpp::il2cpp_exception_matches(managedType, thrownChain);
}

} // namespace

int main()
{
    il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 4, false, 5);

    // --- 1. In-range access (fast path unchanged) ---
    char* p0 = il2cpp::il2cpp_array_get_elem_ptr(a, 0);
    char* p4 = il2cpp::il2cpp_array_get_elem_ptr(a, 4);
    Expect(p0 == a->data, "in-range index 0 returns the payload start");
    Expect(p4 == a->data + 4 * 4, "in-range last index returns the correct pointer");

    // --- 2. Negative index ---
    CaughtInfo neg = CatchIt([&] { il2cpp::il2cpp_array_get_elem_ptr(a, -1); });
    Expect(neg.caught, "negative index throws a catchable exception");
    Expect(IsType(neg.type, "System.IndexOutOfRangeException"),
           "negative index throws IndexOutOfRangeException");
    Expect(std::strstr(neg.msg, "outside the bounds") != nullptr,
           "negative index message is the .NET standard text");

    // --- 3. index == length (one past the end) ---
    CaughtInfo onePast = CatchIt([&] { il2cpp::il2cpp_array_get_elem_ptr(a, 5); });
    Expect(onePast.caught && IsType(onePast.type, "System.IndexOutOfRangeException"),
           "index == length throws IndexOutOfRangeException");

    // --- 4. Large positive index ---
    CaughtInfo big = CatchIt([&] { il2cpp::il2cpp_array_get_elem_ptr(a, 1000000); });
    Expect(big.caught && IsType(big.type, "System.IndexOutOfRangeException"),
           "large positive index throws IndexOutOfRangeException");

    // --- 5. Empty array: index 0 is already out of range ---
    il2cpp::Il2CppArray* z = il2cpp::il2cpp_array_new(nullptr, 4, false, 0);
    CaughtInfo empty = CatchIt([&] { il2cpp::il2cpp_array_get_elem_ptr(z, 0); });
    Expect(empty.caught && IsType(empty.type, "System.IndexOutOfRangeException"),
           "empty array index 0 throws IndexOutOfRangeException");

    // --- 6. Null array -> NullReferenceException ---
    CaughtInfo nullArr = CatchIt([&] { il2cpp::il2cpp_array_get_elem_ptr(nullptr, 0); });
    Expect(nullArr.caught, "null array throws a catchable exception");
    Expect(IsType(nullArr.type, "System.NullReferenceException"),
           "null array throws NullReferenceException");
    Expect(!IsType(nullArr.type, "System.IndexOutOfRangeException"),
           "null array is NOT an IndexOutOfRangeException");
    Expect(std::strstr(nullArr.msg, "not set to an instance") != nullptr,
           "null array message is the .NET standard text");

    // --- 7. Base-class catch: System.Exception matches the OOB throw ---
    CaughtInfo baseCatch = CatchIt([&] { il2cpp::il2cpp_array_get_elem_ptr(a, 9); });
    Expect(IsType(baseCatch.type, "System.Exception"),
           "catch(System.Exception) matches an out-of-range access");
    Expect(IsType(baseCatch.type, "System.SystemException"),
           "catch(System.SystemException) matches an out-of-range access");

    // --- 8. No false positives ---
    CaughtInfo oob = CatchIt([&] { il2cpp::il2cpp_array_get_elem_ptr(a, 9); });
    Expect(!IsType(oob.type, "System.ArgumentNullException"),
           "ArgumentNullException does not match an out-of-range access");
    Expect(!IsType(oob.type, "System.NotSupportedException"),
           "NotSupportedException does not match an out-of-range access");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d arraybounds assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime arraybounds tests passed\n");
    return 0;
}
