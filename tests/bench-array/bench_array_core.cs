using System;
using System.Runtime.InteropServices;
namespace BenchArr {
//------------------------------------------------------------------------------
// bench_array_core.cs -- the TRANSPILABLE SUBSET of bench_array.cs.
//
// The full program (bench_array.cs) cannot be linked for the ILC side yet:
// System.String::get_Chars (str_chars), String.Concat (str_concat) and
// Array.Copy (arr_copy) have no runtime/transpiler wiring, so the generated
// C++ fails to link (undefined symbols). The three benchmarks below use only
// wired constructs (newarr/ldelem/stelem on int[], il2cpp_string_get_length,
// P/Invoke clock, int32 arithmetic) -- the exact shapes that already run in
// the bench suite's ArrayBench/StringBench.
//
// KEEP THIS FILE IN SYNC WITH bench_array.cs: the three methods are copied
// verbatim (same names, same workloads, same output lines), so the harness
// can merge the net48 reference of the FULL program with the ILC numbers of
// this CORE program into one comparison table.
//------------------------------------------------------------------------------
public static class BenchArr {
    // P/Invoke clock (benchlib: steady_clock, microseconds, wrapped to [0,2^31)).
    [DllImport("benchlib", EntryPoint = "now_us")]
    public static extern int now_us();

    // Sink: every benchmark folds its result into this static field.
    public static int checksum;

    // 1. Array sum: 40,000,000 element reads. int[100000] (400 KB, LOH) is
    //    filled once, then summed 400 times. Masked accumulation: values are
    //    i & 0x1F (0..31), 40M x 31 = 1.24e9 < 2^31 -- no wrap.
    //    NoInlining: identical standalone code in both programs (see bench_array.cs).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static int ArrSum() {
        int[] a = new int[100000];
        for (int i = 0; i < 100000; i++) a[i] = i;
        int sum = 0;
        for (int r = 0; r < 400; r++) {
            for (int i = 0; i < 100000; i++) sum += a[i] & 0x1F;
        }
        return sum;
    }

    // 2. Array write: 100,000,000 element writes (fill 100,000 elements,
    //    1,000 rounds). The per-round sink read (a[r], r < 100) keeps the
    //    writes observable on both runtimes. NoInlining (see bench_array.cs).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static int ArrWrite() {
        int[] a = new int[100000];
        int sum = 0;
        for (int r = 0; r < 1000; r++) {
            for (int i = 0; i < 100000; i++) a[i] = i;
            sum += a[r] & 0x1F;
        }
        return sum;
    }

    // 5. String length: 20,000,000 s.Length reads. Len() is NoInlining on
    //    BOTH sides, so the call cannot be hoisted or folded away by the
    //    net48 JIT; the transpiled side is inspected in the generated C++.
    //    Length is 256, masked to 0 -- the sink is constant, but the call
    //    itself is observable (external function, not foldable).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static int Len(string s) { return s.Length; }

    public static int StrLen() {
        string s = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                 + "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                 + "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                 + "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        int sum = 0;
        for (int i = 0; i < 20000000; i++) sum += Len(s) & 0xFF;
        return sum;
    }

    public static void Main() {
        int t0, t1;

        t0 = now_us();
        checksum += ArrSum();
        t1 = now_us();
        Console.WriteLine("arr_sum");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += ArrWrite();
        t1 = now_us();
        Console.WriteLine("arr_write");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += StrLen();
        t1 = now_us();
        Console.WriteLine("str_len");
        Console.WriteLine((t1 - t0) / 1000);
    }
}
}

