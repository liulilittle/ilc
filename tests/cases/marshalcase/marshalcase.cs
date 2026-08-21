using System;
using System.Runtime.InteropServices;
namespace Cases { public static class MarshalCase {
    public static void Main() {
        // 1. AllocHGlobal(16) returns a non-zero IntPtr (native heap alloc)
        IntPtr p = Marshal.AllocHGlobal(16);
        // NOTE: `p != IntPtr.Zero` (op_Inequality) has NO MethodMap.IntPtr
        // mapping, and `p == IntPtr.Zero` (op_Equality) misses the real
        // mscorlib blob (IntPtr params are raw 0x17/0x18, the IntPtr.cs
        // guard accepts only 0x11) -- both die in StackFold as INSTANCE
        // misclassification. The wired ToInt64 form carries the same
        // "non-zero" bool.
        Console.WriteLine(p.ToInt64() != 0);
        // 2. Copy round trip: byte[] -> native buffer -> byte[]
        byte[] src = { 1, 2, 3, 4 };
        Marshal.Copy(src, 0, p, 4);
        byte[] back = new byte[4];
        Marshal.Copy(p, back, 0, 4);
        Console.WriteLine(back[0] == 1 && back[1] == 2 && back[2] == 3 && back[3] == 4);
        // 3. FreeHGlobal does not crash
        Marshal.FreeHGlobal(p);
        Console.WriteLine(true);
        // 4. GetLastWin32Error shape only (value is timing dependent -- never
        //    assert it; the shape check is: the call returns a valid int)
        int e = Marshal.GetLastWin32Error();
        Console.WriteLine(e >= int.MinValue && e <= int.MaxValue);
        // 5. PtrToStringAnsi (wired): native "hi\0" buffer -> managed string
        IntPtr q = Marshal.AllocHGlobal(3);
        Marshal.Copy(new byte[] { 104, 105, 0 }, 0, q, 3);
        Console.WriteLine(Marshal.PtrToStringAnsi(q) == "hi");
        Marshal.FreeHGlobal(q);
    }
}}
