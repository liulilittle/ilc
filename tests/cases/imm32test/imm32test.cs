using System;
using System.Runtime.InteropServices;
internal static class P {
    [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(HandleRef h);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(HandleRef h, HandleRef ctx);
    [DllImport("imm32.dll")] static extern IntPtr ImmAssociateContext(HandleRef h, HandleRef ctx);
    [DllImport("imm32.dll")] static extern bool ImmGetConversionStatus(HandleRef ctx, ref int conv, ref int sentence);
    [DllImport("imm32.dll")] static extern bool ImmSetConversionStatus(HandleRef ctx, int conv, int sentence);
    [DllImport("imm32.dll")] static extern bool ImmGetOpenStatus(HandleRef ctx);
    [DllImport("imm32.dll")] static extern IntPtr ImmCreateContext();
    [DllImport("imm32.dll")] static extern bool ImmDestroyContext(HandleRef ctx);
    public static void Main() {
        IntPtr ctx = ImmCreateContext();
        Console.WriteLine("[ctx.isnull]=" + (ctx == IntPtr.Zero));
        Console.WriteLine("[get.null]=" + (ImmGetContext(new HandleRef(null, IntPtr.Zero)) == IntPtr.Zero));
        Console.WriteLine("[release.zero]=" + ImmReleaseContext(new HandleRef(null, IntPtr.Zero), new HandleRef(null, IntPtr.Zero)));
        int conv = -1, sent = -1;
        Console.WriteLine("[conv.zero]=" + ImmGetConversionStatus(new HandleRef(null, IntPtr.Zero), ref conv, ref sent));
        Console.WriteLine("[conv.vals]=" + conv + "," + sent);
        Console.WriteLine("[set.zero]=" + ImmSetConversionStatus(new HandleRef(null, IntPtr.Zero), 0, 0));
        Console.WriteLine("[open.zero]=" + ImmGetOpenStatus(new HandleRef(null, IntPtr.Zero)));
        Console.WriteLine("[destroy]=" + (ctx != IntPtr.Zero ? ImmDestroyContext(new HandleRef(null, ctx)) : false));
    }
}