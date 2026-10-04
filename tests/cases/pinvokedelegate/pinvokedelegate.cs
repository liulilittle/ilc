// pinvokedelegate.cs -- delegate-typed DllImport param -> native callback
// bridge (P-D2 #4 e2e). The managed side passes a DELEGATE directly as a
// DllImport parameter (no explicit GetFunctionPointerForDelegate); the
// transpiler declares the native fn-pointer carrier (intptr_t) and the call
// site wraps the delegate in the per-signature GfpS dispatcher.
//
// Reference output (net48):
//   42
//   -9
//   21
//   105
//   42
//   108
//   150
//
// net48 semantics the case pins:
//   * STATIC single-cast delegate param -> the raw methodPtr (Triple/Half
//     are static functions) -- byte-identical on both runtimes.
//   * INSTANCE-bound delegate param -> the runtime marshals a native stub
//     that forwards to the bound target; the transpiled GfpS returns a
//     per-process-stable THUNK (slot thunk + strong GC root) that forwards
//     methodPtr(target, args) -- same observable result.
//   * 2-arg delegate -> a second GfpS signature module (int32_t(int32_t,
//     int32_t)).
//   * ROOTING -- the instance delegate stays alive across a full collection
//     (the GfpS strong root keeps it), so the native-held pointer stays valid
//     (delegatecb contract). The static delegates are function addresses and
//     never die.
// The multicast-marshal net48 quirk is NOT exercised (the GfpS dispatcher
// LOUDLY rejects a multicast delegate -- documented deviation, direct
// invocation is the supported path).
using System;
using System.Runtime.InteropServices;
namespace Cases { public static class PinvokeDelegate {
    public delegate int IntFn(int x);
    public delegate int IntFn2(int a, int b);

    [DllImport("mylib", EntryPoint = "call_with_delegate")]
    public static extern int call_with_delegate(IntFn cb, int x);
    [DllImport("mylib", EntryPoint = "call_with_delegate2")]
    public static extern int call_with_delegate2(IntFn2 cb, int a, int b);

    public static int Triple(int x) { return x * 3; }
    public static int Half(int x) { return x / 2; }
    public static int Sum2(int a, int b) { return a + b; }

    public class Counter {
        public int value;
        public int Inc(int x) { return x + value; }
    }

    static IntFn Keep;

    public static void Main() {
        IntFn cb = Triple;
        Console.WriteLine(call_with_delegate(cb, 14));       // 42 (static)
        Console.WriteLine(call_with_delegate(cb, -3));       // -9 (static)
        IntFn half = Half;
        Console.WriteLine(call_with_delegate(half, 42));     // 21 (static)

        Counter c = new Counter();
        c.value = 100;
        IntFn icb = c.Inc;
        Console.WriteLine(call_with_delegate(icb, 5));       // 105 (instance)

        IntFn2 cb2 = Sum2;
        Console.WriteLine(call_with_delegate2(cb2, 20, 22)); // 42 (2-arg)

        Keep = icb;                                          // root across GC
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(call_with_delegate(icb, 8));       // 108 (rooted)
        Console.WriteLine(call_with_delegate(cb, 50));       // 150
    }
}}
