// delegatecb.cs -- delegate -> C callback bridge (net48 reference run).
//
// The .NET reference model for passing a managed delegate to a native
// function: marshal the delegate to a native function pointer with
// Marshal.GetFunctionPointerForDelegate and hand the pointer over. The
// native side (mylib) receives the pointer and CALLS it.
//
// The delegate must stay ALIVE for the pointer to be valid (the runtime
// keeps it reachable; here the static field Keep roots the delegate across
// the GC.Collect below).
//
// Reference output (net48):
//   42
//   -9
//   21
//   300
//   7
//
// Transpiler note (M5.5, delegatecb domain): the M-stage transpiler does
// not yet wire delegate-typed parameters at P/Invoke call sites. Until the
// callback bridge (managed-frame stub on the native->managed edge) lands,
// the transpiler must LOUDLY REJECT such call sites instead of emitting
// garbage. The runtime side (icall_delegate_get_fn_ptr /
// icall_delegate_to_callback + the pinvokeext callback registry) is
// complete and unit-tested (test_delegate_cb.cpp); the C# reference run
// below documents the intended semantics. Transpiled validation is deferred
// to the M5.5 milestone.
//
// net48 deviation note: Marshal.GetFunctionPointerForDelegate on a
// MULTICAST delegate returns the LAST handler's pointer (net48 ground
// truth, measured on this machine -- a single raw pointer cannot
// faithfully represent an invocation list). This runtime LOUDLY REJECTS
// that marshaling (InvalidOperationException, see il2cpp_delegate_ext.h) --
// test code must not rely on the net48 multicast GetFunctionPointerForDelegate
// behavior. The supported multicast path is DIRECT INVOCATION
// (icall_multicast_invoke_i4): it calls every handler in order and returns
// the LAST result -- which for [Triple, Half] on 14 is exactly Half(14) = 7,
// the same value the net48 marshal quirk produces.
using System;
using System.Runtime.InteropServices;

namespace Cases { public static class DelegateCb {
    public delegate int Cb(int x);
    [DllImport("mylib", EntryPoint = "call_callback")]
    public static extern int call_callback(IntPtr fn, int x);

    public static int Triple(int x) { return x * 3; }
    public static int Half(int x) { return x / 2; }

    static Cb Keep;

    public static void Main() {
        Cb cb = Triple;
        IntPtr fp = Marshal.GetFunctionPointerForDelegate(cb);
        Console.WriteLine(call_callback(fp, 14));   // 42
        Console.WriteLine(call_callback(fp, -3));   // -9

        Cb cb2 = Half;
        IntPtr fp2 = Marshal.GetFunctionPointerForDelegate(cb2);
        Console.WriteLine(call_callback(fp2, 42));  // 21

        // Root the delegate so the pointer stays valid across a collection.
        Keep = cb;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(call_callback(fp, 100));  // 300

        // net48 multicast semantics: invoking a multicast delegate calls
        // every handler in order and returns the LAST result (Half(14) = 7,
        // not Triple(14) = 42). This runtime implements the same semantics
        // through icall_multicast_invoke_i4. (The net48 deviation -- the
        // runtime rejects multicast GetFunctionPointerForDelegate loudly --
        // is documented above; direct invocation is the supported path.)
        Cb multi = Triple;
        multi += Half;
        Console.WriteLine(multi(14)); // 42 (Triple) then 7 (Half); last = 7
    }
}}
