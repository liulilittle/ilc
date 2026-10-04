// delegategdfp.cs -- GetDelegateForFunctionPointer<T> (P-D2 #3 e2e): wrap a
// native function pointer into a delegate of type T and invoke it (net48: the
// wrapped delegate's Invoke calls the native function DIRECTLY -- the methodPtr
// IS the wrapped code address). The transpiler emits the runtime icall
// il2cpp_delegate_from_fn_ptr(ptr, typeId), stamping T's SigHash typeId so the
// wrapped delegate Combine/Equals type-checks against normally-created
// delegates of T. A null pointer raises ArgumentNullException.
//
// Reference output (net48):
//   42
//   21
//   15
//   null
//
// net48 semantics the case pins:
//   * direct native-call invoke: t(14) -> triple_fn(14) = 42.
//   * the wrapped delegate CAN be combined (same typeId): u = t + t invokes
//     triple_fn(5)=15 for EACH leaf (same argument, invocation order, LAST
//     result wins) -> 15.
//   * null pointer -> ArgumentNullException (Parameter name: ptr), caught.
using System;
using System.Runtime.InteropServices;
namespace Cases { public static class DelegateGdfp {
    public delegate int Trafo(int x);

    [DllImport("mylib", EntryPoint = "triple_fn")]
    public static extern int triple_fn(int x);
    [DllImport("mylib", EntryPoint = "triple_addr")]
    public static extern IntPtr triple_addr();

    public static void Main() {
        IntPtr p = triple_addr();
        Trafo t = Marshal.GetDelegateForFunctionPointer<Trafo>(p);
        Console.WriteLine(t(14));   // 42 -- direct native call
        Console.WriteLine(t(7));    // 21

        Trafo u = t;
        u += t;                     // same-type comb-ine (typeId matches)
        Console.WriteLine(u(5));    // 15 (triple_fn(5)=15 per leaf; last wins)

        try
        {
            Marshal.GetDelegateForFunctionPointer<Trafo>(IntPtr.Zero);
            Console.WriteLine("no-throw");
        }
        catch (ArgumentNullException)
        {
            Console.WriteLine("null");
        }
    }
}}