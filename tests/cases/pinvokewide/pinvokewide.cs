// pinvokewide.cs -- DllImport marshalling matrix for the FULL-WIDTH types
// (int64/double/float/uint64 params AND returns).
//
// P-D1 audit (P-D1-AUDIT-1.md §4 / P-D1-AUDIT-2.md §4): the P/Invoke type
// matrix silently collapsed int64/double/float P/Invoke params and returns
// to int32_t -- the extern "C" declaration declared `int32_t`, so a long
// param was truncated at the call site and a double return was read from
// EAX (garbage). This case pins the CORRECT full-width declarations and the
// end-to-end numeric behavior against the net48 oracle.
//
// Reference output (net48):
//   4000000001        (add_longs:   2000000000 + 2000000001)
//   2.75              (add_doubles: 0.5 + 2.25)
//   3                 (scale_float: 1.5f * 2f)
//   9000000000        (sum_ulongs:  4000000000 + 5000000000)
using System;
using System.Runtime.InteropServices;
namespace Cases { public static class PinvokeWide {
    [DllImport("mylib", EntryPoint = "add_longs")]
    public static extern long add_longs(long a, long b);
    [DllImport("mylib", EntryPoint = "add_doubles")]
    public static extern double add_doubles(double a, double b);
    [DllImport("mylib", EntryPoint = "scale_float")]
    public static extern float scale_float(float a);
    [DllImport("mylib", EntryPoint = "sum_ulongs")]
    public static extern ulong sum_ulongs(ulong a, ulong b);
    public static void Main() {
        Console.WriteLine(add_longs(2000000000L, 2000000001L));
        Console.WriteLine(add_doubles(0.5, 2.25));
        Console.WriteLine(scale_float(1.5f));
        Console.WriteLine(sum_ulongs(4000000000UL, 5000000000UL));
    }
}}
