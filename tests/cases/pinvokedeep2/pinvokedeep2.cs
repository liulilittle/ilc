//------------------------------------------------------------------------------
// pinvokedeep2.cs -- P/Invoke DEEP e2e 2: callback + struct-layout MARSHALING
// COMBINATIONS (net48 oracle, byte-for-byte). Created in e2e batch C59
// (2026-08-12), pinvoke domain deep. delegatecb (M5.5) pinned the
// delegate -> native callback bridge (GetFunctionPointerForDelegate ->
// icall_delegate_get_fn_ptr); marshaldeep (C45) pinned the SizeOf/OffsetOf
// registered-struct family (D-MAR-1). THIS case combines the two surfaces
// and adds the NATIVE LAYOUT AUTHORITY cross-check:
//
//   1. NATIVE STRUCT LAYOUT (mylib's own C++ structs) vs the MANAGED
//      layout registry: sizeof/offsetof computed inside the native library
//      must equal Marshal.SizeOf / Marshal.OffsetOf on the registered
//      System.Drawing family (Point=8 x/y@0/4, Rectangle=16 x/y/width/height
//      @0/4/8/12). A third native struct (int+long+short) pins the x64
//      packing rules the two sides must agree on.
//   2. CALLBACK ARITY -- a 1-arg and a 2-arg native callback function; the
//      managed delegate is marshaled with GetFunctionPointerForDelegate and
//      called from native.
//   3. CALLBACK SEQUENCES -- the native side invokes the managed callback
//      MULTIPLE times per call (call_callback_twice sums two invocations).
//   4. CALLBACK + STRUCT COMBINED -- a managed struct-layout VALUE
//      (Marshal.OffsetOf<Point>("y") = 4) is passed into native, which
//      routes it through the callback: fn(off) + 1. This proves the
//      managed layout registry and the callback bridge interoperate.
//   5. CALLBACK ROOTING -- the delegate stays rooted (static field) across a
//      full collection; the marshaled pointer stays valid (delegatecb
//      contract: the runtime keeps the delegate reachable via the static
//      root, so the methodPtr outlives the collection).
//
// DETERMINISM: the native layout constants (sizeof/offsetof) are identical
// on net48 (mylib.dll built by g++) and the transpiled builds (mylib static,
// g++/msvc) -- the same x64 Windows ABI. All assertions are bools; the only
// cross-boundary values are the int32 offsets and callback results.
//------------------------------------------------------------------------------
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Cases { public static class PinvokeDeep2 {
    // ---- native struct layout probes (mylib, C++ sizeof/offsetof) ----
    [DllImport("mylib", EntryPoint = "native_point_size")]
    public static extern int native_point_size();
    [DllImport("mylib", EntryPoint = "native_rect_size")]
    public static extern int native_rect_size();
    [DllImport("mylib", EntryPoint = "native_mixed_size")]
    public static extern int native_mixed_size();
    [DllImport("mylib", EntryPoint = "native_point_off_y")]
    public static extern int native_point_off_y();
    [DllImport("mylib", EntryPoint = "native_rect_off_height")]
    public static extern int native_rect_off_height();
    [DllImport("mylib", EntryPoint = "native_mixed_off_b")]
    public static extern int native_mixed_off_b();

    // ---- native callback entry points ----
    [DllImport("mylib", EntryPoint = "call_callback")]
    public static extern int call_callback(IntPtr fn, int x);
    [DllImport("mylib", EntryPoint = "call_callback_twice")]
    public static extern int call_callback_twice(IntPtr fn, int a, int b);
    [DllImport("mylib", EntryPoint = "call_callback2")]
    public static extern int call_callback2(IntPtr fn, int a, int b);
    [DllImport("mylib", EntryPoint = "struct_field_callback")]
    public static extern int struct_field_callback(IntPtr fn, int off);

    public delegate int Cb(int x);
    public delegate int Cb2(int a, int b);

    public static int Triple(int x) { return x * 3; }
    public static int Half(int x) { return x / 2; }
    public static int Sum2(int a, int b) { return a + b; }
    public static int Diff2(int a, int b) { return a - b; }

    static Cb Keep;

    public static void Main()
    {
        // ===== 1. native struct layout vs the managed layout registry =====
        Console.WriteLine("pt_size=" + (native_point_size() == Marshal.SizeOf<Point>()));          // True (8)
        Console.WriteLine("rt_size=" + (native_rect_size() == Marshal.SizeOf<Rectangle>()));       // True (16)
        Console.WriteLine("pt_off_y=" + (native_point_off_y() ==
                                          Marshal.OffsetOf<Point>("y").ToInt32()));               // True (4)
        Console.WriteLine("rt_off_h=" + (native_rect_off_height() ==
                                          Marshal.OffsetOf<Rectangle>("height").ToInt32()));       // True (12)
        Console.WriteLine("mixed_size=" + (native_mixed_size() == 24));                            // True (x64 int+pad+long+short)
        Console.WriteLine("mixed_off_b=" + (native_mixed_off_b() == 8));                           // True (long aligned to 8)

        // ===== 2. callback arity: 1-arg and 2-arg native callers =====
        Cb cb = Triple;
        IntPtr fp = Marshal.GetFunctionPointerForDelegate(cb);
        Console.WriteLine("cb_triple=" + (call_callback(fp, 14) == 42));   // True
        Console.WriteLine("cb_triple_neg=" + (call_callback(fp, -3) == -9)); // True
        Cb2 cb2 = Sum2;
        IntPtr fp2 = Marshal.GetFunctionPointerForDelegate(cb2);
        Console.WriteLine("cb2_sum=" + (call_callback2(fp2, 20, 22) == 42)); // True
        Cb2 cb3 = Diff2;
        IntPtr fp3 = Marshal.GetFunctionPointerForDelegate(cb3);
        Console.WriteLine("cb2_diff=" + (call_callback2(fp3, 50, 8) == 42)); // True

        // ===== 3. callback invoked MULTIPLE times per native call =====
        Cb half = Half;
        IntPtr fh = Marshal.GetFunctionPointerForDelegate(half);
        Console.WriteLine("cb_twice=" + (call_callback_twice(fh, 10, 20) == 15)); // True (5 + 10)
        Console.WriteLine("cb_twice_mix=" + (call_callback_twice(fh, 3, 7) == 4)); // True (3/2=1 + 7/2=3)

        // ===== 4. CALLBACK + STRUCT COMBINED: managed layout value through the
        // callback bridge =====
        int offY = Marshal.OffsetOf<Point>("y").ToInt32();   // 4
        int offH = Marshal.OffsetOf<Rectangle>("height").ToInt32(); // 12
        Console.WriteLine("combo_y=" + (struct_field_callback(fh, offY) == 3));  // True (Half(4)=2, +1)
        Console.WriteLine("combo_h=" + (struct_field_callback(fh, offH) == 7));  // True (Half(12)=6, +1)

        // ===== 5. callback rooting across a collection =====
        Keep = cb;                       // root the ORIGINAL delegate (not the local)
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine("rooted_alive=" + (call_callback(fp, 100) == 300)); // True (Triple(100))
    }
}}
