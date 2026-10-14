//------------------------------------------------------------------------------
// test_pinvoke.cpp -- P/Invoke dynamic resolution unit tests.
//
// Coverage (success + caching paths):
//   1. il2cpp_pinvoke_resolve finds a well-known system library symbol:
//      Windows: kernel32!GetTickCount; POSIX: libc!strlen.
//   2. Resolution is CACHED (the second call returns the same pointer).
//   3. The resolved symbol is callable.
//
// The FAILURE paths (missing library / missing symbol) now throw managed
// DllNotFoundException / EntryPointNotFoundException carriers -- they are
// covered by test_pinvoke_fail.cpp (a direct missing-symbol call here would
// unwind the test main, not kill it).
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_pinvoke.h"
#include "il2cpp_pinvoke_bridge.h"

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

// GN-20.4 F1 probes: statement-position (void) and expression-position
// (int32_t) native callees, both exercising il2cpp_pinvoke_call.
void Gn20VoidStore(int32_t* out)
{
    if (out != nullptr)
    {
        *out = 0x5A5A;
    }
}

int32_t Gn20VoidStore2(int32_t* inout)
{
    if (inout != nullptr)
    {
        *inout += 1;
        return *inout;
    }
    return -1;
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

    // --- 4. GN-20.4 F1: the void-return bridge RETURNS to its caller ---
    // il2cpp_pinvoke_call<void> is the wrapper every STATEMENT-position
    // P/Invoke call compiles to ("IL call ... (P/Invoke SEH boundary)"). The
    // void arm of the try block must RETURN: falling out of the try reaches
    // the function's __builtin_unreachable() tail, which is undefined -- the
    // gdifont/gdidraw probes crashed 0xC0000005 at exactly such a call.
    int32_t voidProbe = 0;
    int32_t voidMarker = 0;
    il2cpp::runtime::il2cpp_pinvoke_call<void>(Gn20VoidStore, il2cpp::runtime::il2cpp_pinvoke_arg(&voidProbe));
    voidMarker = 1; // reached ONLY if the call above returned normally
    Expect(voidMarker == 1, "il2cpp_pinvoke_call<void> returns to the caller (no fall-through)");
    Expect(voidProbe == 0x5A5A, "il2cpp_pinvoke_call<void> forwarded the out argument");

    // The non-void arm still folds the native result through the bridge.
    int32_t retProbe = il2cpp::runtime::il2cpp_pinvoke_call<int32_t>(
        Gn20VoidStore2, il2cpp::runtime::il2cpp_pinvoke_arg(&voidProbe));
    Expect(retProbe == 0x5A5A + 1, "il2cpp_pinvoke_call<int32_t> still converts the native result");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d pinvoke assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime pinvoke tests passed\n");
    return 0;
}
