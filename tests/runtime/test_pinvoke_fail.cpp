//------------------------------------------------------------------------------
// test_pinvoke_fail.cpp -- P/Invoke dynamic resolution FAILURE paths.
//
// Coverage (P-D4 failed-resolution semantics):
//   1. A MISSING LIBRARY throws the managed DllNotFoundException carrier
//      (the old behavior aborted the process -- letting this test run at all
//      UNDER the old runtime is the RED state: the abort kills the suite).
//   2. A MISSING SYMBOL (library present, entry absent) throws the managed
//      EntryPointNotFoundException carrier.
//   3. Null dll/entry STILL ABORTS (an internal/programming error -- net48
//      has no corresponding scenario; the abort is intentional).
//
// The thrown carrier is a C++ DotNetException whose type-name CHAIN must
// contain the managed full name (il2cpp_exception_matches semantics -- the
// same matcher generated catch clauses use).
//------------------------------------------------------------------------------

#include "il2cpp_pinvoke.h"
#include "il2cpp_exception.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

#if defined(_WIN32)
    #define TEST_LIB "kernel32.dll"
#else
    #define TEST_LIB "libc.so.6"
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

// Catch a DotNetException thrown by resolve and verify its type chain.
bool CatchResolveException(const char* lib, const char* entry, const char* expectedType, const char* what)
{
    try
    {
        il2cpp::il2cpp_pinvoke_resolve(lib, entry);
        std::printf("FAIL: %s (no exception thrown)\n", what);
        ++g_failures;
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        if (!il2cpp::il2cpp_exception_matches(expectedType, e.type_name()))
        {
            std::printf("FAIL: %s (type '%s' not in chain '%s')\n", what, expectedType, e.type_name());
            ++g_failures;
            return false;
        }
        return true;
    }
    catch (...)
    {
        std::printf("FAIL: %s (non-managed exception thrown)\n", what);
        ++g_failures;
        return false;
    }
}

} // namespace

int main()
{
    // --- 1. Missing library -> DllNotFoundException chain ---
    // A library name that no project ever ships (definitely_missing_d4) so
    // LoadLibrary/dlopen cannot succeed on any host.
    //
    // NOTE: the runtime CACHES the failed library handle (GetLibraryHandle
    // stores the null HANDLE), so a second resolve of the same library must
    // throw again without re-loading -- exercised below.
    Expect(CatchResolveException("definitely_missing_d4", "entry_x",
                                 "System.DllNotFoundException",
                                 "missing library -> DllNotFoundException"),
           "missing library throws a DllNotFoundException carrier");

    // --- 2. Missing symbol -> EntryPointNotFoundException chain ---
    // TEST_LIB is loaded successfully (a well-known system library), but the
    // entry does not exist in it.
    Expect(CatchResolveException(TEST_LIB, "no_such_entry_d4",
                                 "System.EntryPointNotFoundException",
                                 "missing symbol -> EntryPointNotFoundException"),
           "missing symbol throws an EntryPointNotFoundException carrier");

    // --- 3. Cached failure: the same missing library throws again ---
    Expect(CatchResolveException("definitely_missing_d4", "entry_x",
                                 "System.DllNotFoundException",
                                 "cached missing library throws again"),
           "a cached missing library still throws (failed handle cached)");

    if (g_failures != 0)
    {
        std::printf("%d pinvoke-fail assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime pinvoke-fail tests passed\n");
    return 0;
}