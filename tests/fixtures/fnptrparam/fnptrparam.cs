// fnptrparam.cs -- unit-test fixture for the P-D3 P1 fnptr DllImport PARAM fix.
//
// P1 (P-DX P0 LOUD reject -> P-D3 wire): a DllImport param of fnptr type
// (delegate* unmanaged[Cdecl]) was LOUDLY rejected ("no marshalling bridge
// exists -- P-D3 wires fnptr"). P-D3 wires F:->void* (x64 direct
// pass-through; the fnptr value is already the model's void* "p" slot).
//
// The fixture's transpile must SUCCEED and emit `extern "C" int32_t
// apply_cb(void*, int32_t)` (void* for the fnptr param).
//
// NOTE: lives in tests/fixtures (unit-only; never an e2e case -- no mylib here).
using System;
using System.Runtime.InteropServices;
namespace Cases { public static class FnPtrParam {
    [DllImport("mylib", EntryPoint = "apply_cb")]
    public static extern unsafe int apply_cb(delegate* unmanaged[Cdecl]<int, int> cb, int x);
    public static void Main() {
        Console.WriteLine("ok");
    }
}}