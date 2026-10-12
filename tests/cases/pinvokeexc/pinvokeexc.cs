using System;
using System.Runtime.InteropServices;

namespace Cases { public static class PinvokeExc {
    // GN-17 probe A: a native library throwing a C++ std::exception must
    // surface as System.Runtime.InteropServices.SEHException at the managed
    // boundary (net48 semantics -- the process does NOT abort, the managed
    // catch runs). ALIVE after the catch proves the process survived (the
    // pre-GN-17 behavior was std::terminate/abort).
    [DllImport("mylib", EntryPoint = "native_throw_cpp")]
    public static extern void native_throw_cpp();

    public static void Main() {
        try {
            native_throw_cpp();
            Console.WriteLine("NO_THROW");
        } catch (Exception e) {
            Console.WriteLine("CAUGHT|" + e.GetType().FullName);
        }
        Console.WriteLine("ALIVE");
    }
}}