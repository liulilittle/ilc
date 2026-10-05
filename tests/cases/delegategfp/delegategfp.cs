// delegategfp.cs -- GetFunctionPointerForDelegate on INSTANCE-bound delegates
// (P-D2 #2 e2e): the per-signature GfpS dispatcher marshals an instance-bound
// delegate to a per-process-stable THUNK (a strong-GC-root slot that forwards
// methodPtr(target, args) from the native side) -- the WinProc/WndProc shape
// the corpus (mdievt/menuevt/nestedpanel) needs. Static single-cast delegates
// keep returning the raw methodPtr (the net48 byte contract).
//
// Reference output (net48):
//   105
//   108
//   42
//   21
//
// net48 semantics the case pins:
//   * INSTANCE single-cast delegate -> a native stub forwards to the bound
//     target (Counter.Inc(x) + value); the transpiled GfpS thunk does the
//     same via methodPtr(target, x). Same observable result.
//   * ROOTING -- the instance delegate must stay reachable for the native-held
//     pointer to stay valid across a full collection. The GfpS module holds
//     its slot as a PERMANENT STRONG ROOT (il2cpp_gc_add_static_root), so the
//     marshaled pointer outlives any collect -- net48 reachability equivalent.
//   * STATIC single-cast -> raw methodPtr (Triple/Half are static functions),
//     byte-identical to the old icall_delegate_get_fn_ptr path.
// The multicast-marshal net48 quirk (last-handler pointer) is NOT exercised
// (the GfpS dispatcher LOUDLY rejects a multicast delegate -- documented
// deviation; direct invocation is the supported path, delegatecb pins it).
using System;
using System.Runtime.InteropServices;
namespace Cases { public static class DelegateGfp {
    public delegate int Cb(int x);

    [DllImport("mylib", EntryPoint = "call_callback")]
    public static extern int call_callback(IntPtr fn, int x);

    public static int Triple(int x) { return x * 3; }
    public static int Half(int x) { return x / 2; }

    public class Counter {
        public int value;
        public int Inc(int x) { return x + value; }
    }

    static Cb Keep;
    static IntPtr KeepPtr;
    static Counter KeepTarget;

    public static void Main() {
        Counter c = new Counter();
        c.value = 100;
        Cb cb = c.Inc;
        IntPtr fp = Marshal.GetFunctionPointerForDelegate(cb);   // instance -> GfpS thunk
        Console.WriteLine(call_callback(fp, 5));                   // 105 (Inc(5)+100)

        // root the delegate (and the object) across a full collection; the
        // GfpS strong root alone would keep the delegate alive -- the static
        // field root also protects the Counter instance itself.
        Keep = cb;
        KeepPtr = fp;
        KeepTarget = c;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(call_callback(fp, 8));                   // 108 (Inc(8)+100)

        Cb scb = Triple;
        IntPtr s1 = Marshal.GetFunctionPointerForDelegate(scb);    // static -> raw methodPtr
        Console.WriteLine(call_callback(s1, 14));                  // 42 (Triple(14))
        Cb scb2 = Half;
        IntPtr s2 = Marshal.GetFunctionPointerForDelegate(scb2);
        Console.WriteLine(call_callback(s2, 42));                  // 21 (Half(42))
    }
}}