// wf1probe.cs -- minimal WIRED-subset P/Invoke probe: only the marshal
// surface the transpiler already supports (int32 + IntPtr RETURN + UTF-8
// strings). Used to verify the kernel32/user32 CMake shims + full pipeline
// (transpile -> cmake -> build -> run) work, isolating the winforms1
// blockers to the NOT-yet-wired marshal (IntPtr params, user struct fields,
// UTF-16 string args).
using System;
using System.Runtime.InteropServices;
namespace Cases { public static class Wf1Probe {
    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string name);
    [DllImport("user32", EntryPoint = "GetSystemMetrics")]
    public static extern int GetSystemMetrics(int index);
    public static void Main() {
        IntPtr h = GetModuleHandleW(null);
        Console.WriteLine("module_nonzero=" + (h != IntPtr.Zero));
        Console.WriteLine("sm_cxscreen=" + (GetSystemMetrics(0) > 0));
        Console.WriteLine("sm_cyscreen=" + (GetSystemMetrics(1) > 0));
    }
}}
