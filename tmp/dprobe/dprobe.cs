using System;
namespace Cases { public static class DProbe {
    public delegate IntPtr WndProc(IntPtr h, int m, IntPtr w, IntPtr l);
    public static void Main() {
        WndProc p = H;
        IntPtr r = p(IntPtr.Zero, 1, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine(r == IntPtr.Zero);
    }
    static IntPtr H(IntPtr h, int m, IntPtr w, IntPtr l) { return IntPtr.Zero; }
}}
