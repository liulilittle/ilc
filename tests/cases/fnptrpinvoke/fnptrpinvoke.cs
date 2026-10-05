// fnptrpinvoke.cs -- fnptr over the P/Invoke boundary (P-D3 P0/P1).
//
// P0 (DllImport fnptr RETURN): the native library hands back a function
// pointer (make_inc/make_add). The transpiler's extern "C" declaration must
// name void* (the pointer-wide fnptr carrier) -- before P-D3 the declaration
// collapsed to int32_t (silent x64 address truncation, P-DX P0 missing shape).
//
// P1 (DllImport fnptr PARAM): a fnptr VALUE (obtained from the DllImport
// return) is passed BACK into a native function that invokes it (apply2).
// Before P-D3 the F: param was LOUDLY rejected; P-D3 wires F:->void* (x64
// direct pass-through).
//
// Note (P-D3 probe): net48 C#9 cannot convert &managedMethod to
// delegate* unmanaged[Cdecl] (CS8786 -- the & operator is static-only and
// managed methods carry the managed calling convention), so the only legal
// net48 source shape for an unmanaged[Cdecl] VALUE is a DllImport return.
//
// Reference output (net48 oracle):
//   42    inc(41)            -- calli through the returned cdecl fnptr
//   42    add2(5, 37)        -- calli through the returned 2-arg cdecl fnptr
//   42    apply2(add2, 5, 37) -- fnptr VALUE passed as a DllImport param
using System;
using System.Runtime.InteropServices;

namespace Cases { public static class FnPtrPinvoke {
    [DllImport("mylib", EntryPoint = "make_inc")]
    static extern unsafe delegate* unmanaged[Cdecl]<int, int> make_inc();
    [DllImport("mylib", EntryPoint = "make_add")]
    static extern unsafe delegate* unmanaged[Cdecl]<int, int, int> make_add();
    [DllImport("mylib", EntryPoint = "apply2")]
    static extern unsafe int apply2(delegate* unmanaged[Cdecl]<int, int, int> cb, int a, int b);

    public static void Main() {
        unsafe {
            delegate* unmanaged[Cdecl]<int, int> inc = make_inc();
            Console.WriteLine(inc(41));                      // 42
            delegate* unmanaged[Cdecl]<int, int, int> add2 = make_add();
            Console.WriteLine(add2(5, 37));                  // 42
            Console.WriteLine(apply2(add2, 5, 37));          // 42
        }
    }
}}