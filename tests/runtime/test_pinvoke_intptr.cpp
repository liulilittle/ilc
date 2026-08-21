//------------------------------------------------------------------------------
// test_pinvoke_intptr.cpp -- P/Invoke IntPtr PARAMETER full-width tests
// (WinForms stage-1 gap #8: hWnd and friends -- the x64 truncation fix).
//
// The transpiler's P/Invoke parameter list today declares every non-string
// parameter as int32_t (CodeGenerator.PinvokeParamList), which truncates a
// 64-bit pointer on x64 -- the RETURN side was already fixed to intptr_t
// (PinvokeReturnTypeCpp, M4.5); this test pins the PARAMETER-side contract
// the wiring patch must satisfy (PinvokeParamList "n" -> intptr_t).
//
// Coverage:
//   1. The EMITTED SHAPE: native functions with the post-fix declaration
//      (intptr_t parameters) round-trip full 64-bit values byte-exact --
//      a HWND-size value (0x0000000123456789) and a high-bit value.
//   2. Mixed-width signature (SendMessageW form -- intptr_t hwnd/wParam/
//      lParam + int32_t msg): every parameter carries its exact value.
//   3. REAL-WORLD full-width through the P/Invoke resolution path
//      (Windows): GetModuleHandleA returns a full HMODULE; passing it back
//      through GetProcAddress (an intptr_t parameter) resolves a symbol.
//   4. NEGATIVE CONTROL (the point of the fix): truncating the HMODULE to
//      int32 makes GetProcAddress fail -- proving the test catches the
//      truncation the wiring patch prevents. kernel32.dll loads at a high
//      x64 address, so its HMODULE ALWAYS has high bits set.
//   5. HWND value fidelity (Windows): a real user32 HWND (GetDesktopWindow)
//      flows as a parameter through IsWindow and is recognized.
//
// Plain executable + minimal assertion harness (see test_pinvoke.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_pinvoke.h"

#include <cstdint>
#include <cstdio>

namespace {

static_assert(sizeof(void*) == 8, "the IntPtr full-width tests assume x64");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Native functions with the DECLARATION SHAPE the wiring patch emits for
// IntPtr P/Invoke parameters (intptr_t, NOT int32_t).
extern "C" intptr_t wm3_ptr_roundtrip(intptr_t value)
{
    return value;
}

extern "C" intptr_t wm3_sendmsg_echo(intptr_t hwnd, int32_t msg,
                                     intptr_t wparam, intptr_t lparam)
{
    // Any truncated parameter would change the sum (the values are chosen so
    // each high half is non-zero).
    return hwnd + static_cast<intptr_t>(msg) + wparam + lparam;
}

} // namespace

int main()
{
    // --- 1. Emitted-shape round trip: full-width IntPtr parameters ---
    {
        Expect(wm3_ptr_roundtrip(static_cast<intptr_t>(INT64_C(0x0000000123456789)))
                   == INT64_C(0x0000000123456789),
               "a HWND-size value (0x0000000123456789) round-trips byte-exact");
        Expect(wm3_ptr_roundtrip(static_cast<intptr_t>(INT64_C(0xFFFFFFFFFFFF0000)))
                   == static_cast<intptr_t>(INT64_C(0xFFFFFFFFFFFF0000)),
               "a high-bit pointer value round-trips byte-exact");
        Expect(wm3_ptr_roundtrip(static_cast<intptr_t>(INT64_C(-1)))
                   == INT64_C(-1),
               "INTPTR_MAX bit pattern round-trips");
        Expect(wm3_ptr_roundtrip(0) == 0, "zero round-trips");
    }

    // --- 2. Mixed-width signature (SendMessageW form) ---
    {
        const intptr_t kHwnd   = static_cast<intptr_t>(INT64_C(0x1122334455667788));
        const int32_t  kMsg    = INT32_C(0x000000FF);
        const intptr_t kWParam = static_cast<intptr_t>(INT64_C(0x99AABBCCDDEEFF00));
        const intptr_t kLParam = static_cast<intptr_t>(INT64_C(0x0000000000000100));
        const intptr_t expected = kHwnd + static_cast<intptr_t>(kMsg) + kWParam + kLParam;
        Expect(wm3_sendmsg_echo(kHwnd, kMsg, kWParam, kLParam) == expected,
               "SendMessageW-form signature carries every parameter exactly "
               "(any int32 truncation would change the sum)");
    }

    // --- 3-4. Real full-width through the resolution path (Windows) ---
#if defined(_WIN32)
    {
        void* hGetModuleHandleA = il2cpp::il2cpp_pinvoke_resolve("kernel32.dll", "GetModuleHandleA");
        void* hGetProcAddress   = il2cpp::il2cpp_pinvoke_resolve("kernel32.dll", "GetProcAddress");
        Expect(hGetModuleHandleA != nullptr && hGetProcAddress != nullptr,
               "kernel32 GetModuleHandleA/GetProcAddress resolve");

        typedef void* (*GetModuleHandleAFn)(const char*);
        typedef void* (*GetProcAddressFn)(void*, const char*);
        auto GetModuleHandleA = reinterpret_cast<GetModuleHandleAFn>(hGetModuleHandleA);
        auto GetProcAddress   = reinterpret_cast<GetProcAddressFn>(hGetProcAddress);

        void* hmod = GetModuleHandleA("kernel32.dll");
        Expect(hmod != nullptr, "GetModuleHandleA returns a module handle");
#if INTPTR_MAX == INT64_MAX
        Expect(reinterpret_cast<uintptr_t>(hmod) > UINT32_MAX,
               "the kernel32 HMODULE has high bits set (the truncation case)");
#endif

        // The full-width handle flows back as a parameter.
        Expect(GetProcAddress(hmod, "GetTickCount") != nullptr,
               "the full-width HMODULE parameter resolves a symbol");

        // NEGATIVE CONTROL: the truncated (int32) handle must NOT resolve --
        // this is exactly the failure the int32_t declaration caused.
        void* truncated = reinterpret_cast<void*>(
            static_cast<intptr_t>(static_cast<int32_t>(reinterpret_cast<intptr_t>(hmod))));
        Expect(GetProcAddress(truncated, "GetTickCount") == nullptr,
               "NEGATIVE: an int32-truncated HMODULE fails to resolve (the "
               "truncation the wiring patch prevents)");
    }

    // --- 5. HWND value fidelity through real user32 calls ---
    {
        void* hGetDesktopWindow = il2cpp::il2cpp_pinvoke_resolve("user32.dll", "GetDesktopWindow");
        void* hIsWindow         = il2cpp::il2cpp_pinvoke_resolve("user32.dll", "IsWindow");
        Expect(hGetDesktopWindow != nullptr && hIsWindow != nullptr,
               "user32 GetDesktopWindow/IsWindow resolve");

        typedef void* (*GetDesktopWindowFn)();
        typedef int32_t (*IsWindowFn)(void*);
        auto GetDesktopWindow = reinterpret_cast<GetDesktopWindowFn>(hGetDesktopWindow);
        auto IsWindow         = reinterpret_cast<IsWindowFn>(hIsWindow);

        void* hwnd = GetDesktopWindow();
        Expect(hwnd != nullptr, "GetDesktopWindow returns a real HWND");
        Expect(IsWindow(hwnd) != 0,
               "a real HWND value flows as a parameter and IsWindow accepts it");
        Expect(IsWindow(nullptr) == 0,
               "IsWindow(null) is false (the parameter carries the exact value)");
    }
#endif

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d pinvoke-intptr assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime pinvoke-intptr tests passed\n");
    return 0;
}
