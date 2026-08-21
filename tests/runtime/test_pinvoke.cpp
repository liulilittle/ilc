//------------------------------------------------------------------------------
// test_pinvoke.cpp -- P/Invoke dynamic resolution unit tests.
//
// Coverage (normal + error paths):
//   1. il2cpp_pinvoke_resolve finds a well-known system library symbol:
//      Windows: kernel32!GetTickCount; POSIX: libc!strlen.
//   2. Resolution is CACHED (the second call returns the same pointer).
//   3. A missing symbol aborts loudly (EntryPointNotFound semantics) --
//      verified via the e2e path (a direct call would kill the test).
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_pinvoke.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

#if defined(_WIN32)
    #define TEST_LIB "kernel32.dll"
    #define TEST_SYM "GetTickCount"
    using TestFn = uint32_t (*)();
#else
    #define TEST_LIB "libc.so.6"
    #define TEST_SYM "strlen"
    using TestFn = size_t (*)(const char*);
#endif

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

} // namespace

int main()
{
    // --- 1. Resolution finds a real system symbol ---
    void* sym1 = il2cpp::il2cpp_pinvoke_resolve(TEST_LIB, TEST_SYM);
    Expect(sym1 != nullptr, "system library symbol resolves to a non-null pointer");

    // --- 2. Caching: the second resolution returns the same pointer ---
    void* sym2 = il2cpp::il2cpp_pinvoke_resolve(TEST_LIB, TEST_SYM);
    Expect(sym1 == sym2, "P/Invoke resolution is cached (same pointer on repeat)");

    // --- 3. The resolved symbol is callable (round-trip sanity) ---
#if !defined(_WIN32)
    // POSIX: call strlen through the resolved pointer.
    auto fn = reinterpret_cast<TestFn>(sym1);
    Expect(fn("hello") == 5, "resolved libc strlen callable via the pointer");
#else
    // Windows: call GetTickCount through the resolved pointer.
    auto fn = reinterpret_cast<TestFn>(sym1);
    Expect(fn() > 0, "resolved kernel32 GetTickCount callable via the pointer");
#endif

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d pinvoke assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime pinvoke tests passed\n");
    return 0;
}
