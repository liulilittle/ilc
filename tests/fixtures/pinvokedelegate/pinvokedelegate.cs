// pinvokedelegate.cs -- unit-test fixture for the P-D1 P0 delegate-typed
// DllImport parameter LOUD rejection.
//
// P-D1 audit (P-D1-AUDIT-1.md §4 / P-D1-AUDIT-2.md §4): a delegate-typed
// DllImport parameter encoded "r:<DelegateFullName>" fell through the
// P/Invoke marshalling matrix to int32_t -- the extern "C" declaration
// declared `int32_t` for what is really an Il2CppObject*, and the
// il2cpp_pinvoke_arg bridge reinterpreted the pointer into an int32_t
// (silent compile + runtime crash). delegatecb.cs:19-27 claims the
// transpiler "must LOUDLY REJECT such call sites" but no rejection code
// existed. This fixture must make CodeGenerator.Generate throw
// NotSupportedException.
//
// NOTE: this fixture lives in tests/fixtures (NOT tests/cases) -- its
// transpile is expected to FAIL, so it must never enter the e2e case pool.
// Main never calls the rejected import (the declaration alone must trigger
// the rejection during decl emission).
using System;
using System.Runtime.InteropServices;
namespace Cases { public static class PinvokeDelegate {
    public delegate int IntFn(int x);
    [DllImport("mylib", EntryPoint = "call_with_delegate")]
    public static extern int call_with_delegate(IntFn cb, int x);
    public static void Main() {
        Console.WriteLine("ok");
    }
}}
