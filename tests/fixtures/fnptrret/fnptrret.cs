// fnptrret.cs -- unit-test fixture for the P-D3 P0 DllImport fnptr RETURN fix.
//
// P0 (P-DX P0 missing shape): a DllImport whose return is a function pointer
// (delegate* unmanaged[Cdecl]) fell through PinvokeReturnTypeCpp to "int32_t"
// (the extern "C" declaration truncated the x64 address) AND the call result
// temp collapsed to int32_t (UserCallResultKind null -> int32 slot). Both
// halves of the silent truncation are exercised by `delegate* p = make_inc();
// p(41)` -- the pointer returned by the native library must cross the P/Invoke
// boundary at full width.
//
// The fixture's transpile must SUCCEED and emit `extern "C" void* make_inc(void)`
// plus a void* result temp and the correct calli through it.
//
// NOTE: lives in tests/fixtures (unit-only; never an e2e case -- no mylib here).
using System;
using System.Runtime.InteropServices;
namespace Cases { public static class FnPtrRet {
    [DllImport("mylib", EntryPoint = "make_inc")]
    public static extern unsafe delegate* unmanaged[Cdecl]<int, int> make_inc();
    public static void Main() {
        unsafe {
            delegate* unmanaged[Cdecl]<int, int> p = make_inc();
            Console.WriteLine(p(41));
        }
    }
}}