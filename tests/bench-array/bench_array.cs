using System;
using System.Runtime.InteropServices;
namespace BenchArr {
//------------------------------------------------------------------------------
// bench_array.cs -- array/string hot-path benchmarks for the ILC transpiled
// output vs the .NET Framework 4.8 reference runtime.
//
// This is the FULL benchmark program (all six benchmarks). It deliberately
// lives in tests/bench-array/ (NOT tests/bench/): the bench suite's csproj
// globs every *.cs under tests/bench/ recursively, so an extra source file
// there would break bench.csproj (duplicate Main). Same conventions as
// tests/bench/bench.cs -- see that file's header for the documented M-stage
// subset and runtime limitations:
//
//   1. Timing goes through the P/Invoke clock benchlib.now_us() (steady
//      clock, microseconds, int32); no Stopwatch (not transpilable).
//   2. "name: <ms>" on one line needs String.Concat, which is not wired --
//      each benchmark prints TWO lines (name, then integer ms); the harness
//      pairs them.
//   3. Every allocation stays inside ONE GC segment (Gen0 64 KB payload /
//      LOH 1 MB payload; arrays in the 64-85 KB Gen0 gap crash). Total LOH
//      stays under one 1 MB segment: arr_sum 400 KB + arr_write 400 KB +
//      arr_copy 2 x 96 KB = 992 KB. Workloads are reached by REPEATING the
//      operation (fixed work, varying time -- what makes ratios meaningful).
//   4. Reference locals are GC-rooted at frame entry with uninitialized
//      slots (CodeGenerator WrapFrameRoots limitation): no collection may
//      run while a ref slot is unassigned. The benchmark order keeps
//      allocation-pressure (str_concat) last, mirroring bench.cs.
//
// BENCHMARKS (fixed work + observable checksum sink; masked sums stay inside
// int32 -- no wrap, no C++ signed-overflow UB). Workloads are sized so the
// net48 reference takes at least ~15 ms (integer milliseconds are then
// meaningful on both sides):
//
//   arr_sum    40,000,000 int[] element READS -- the
//              il2cpp_array_get_elem_ptr hot path (audit gap #1).
//   arr_write  100,000,000 int[] element WRITES -- the stelem hot path.
//   arr_copy   20,000 x Array.Copy(int[],0,int[],0,24000) = 480M element
//              copies (~1.9 GB). TRANSPILABLE since batch 159 (Array.Copy
//              wired in earlier batches -- measured on both sides, ~1.08x).
//   str_chars  40,000,128 string[i] char reads (get_Chars). TRANSPILABLE
//              since batch 159 (get_Chars wired -- ~14x vs net48).
//   str_len    20,000,000 s.Length reads via a NoInlining wrapper (the JIT
//              cannot hoist a NoInlining call; the transpiled side keeps the
//              call too). PROPERTY-FOLDING VERIFICATION POINT: the generated
//              C++ for this loop is inspected -- a folded Length compiles to
//              a direct field read, an unfolded one to
//              il2cpp_string_get_length(...) per iteration.
//   str_concat 500,000 x "v" + i concatenations. TRANSPILABLE since batch
//              159 (String.Concat wired in earlier batches -- ~1.29x vs
//              net48, the documented 0.81-0.85 gap is closed).
//
// The transpilable subset of this file (arr_sum / arr_write / str_len) is
// reproduced verbatim in bench_array_core.cs -- the harness transpiles the
// CORE program for the ILC side (apples-to-apples for the shared three) and
// the FULL program for both sides since batch 159 (arr_copy / str_chars /
// str_concat measured from the FULL program). Keep the three methods in
// sync.
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
    //    NoInlining: keeps the JIT from inlining the benchmark into Main --
    //    identical standalone code in both programs (the same method
    //    compiled into the FULL and CORE assemblies must measure identically;
    //    without it the JIT context differs and arr_sum varied 2x between
    //    the two programs).
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
    //    writes observable on both runtimes. NoInlining -- same rationale
    //    as ArrSum.
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

    // 3. Array copy: 20,000 x Array.Copy(src, 0, dst, 0, 24000) =
    //    480,000,000 element copies (~1.9 GB memcpy). Two int[24000]
    //    (96 KB each, LOH -- above the 85 KB threshold, below the 1 MB
    //    payload). net48-only (Array.Copy unwired).
    public static int ArrCopy() {
        int[] src = new int[24000];
        int[] dst = new int[24000];
        for (int i = 0; i < 24000; i++) src[i] = i;
        for (int r = 0; r < 20000; r++) Array.Copy(src, 0, dst, 0, 24000);
        return dst[0] + dst[23999];
    }

    // 4. String chars: 40,000,128 reads of s[i] (256-char string, 156,252
    //    rounds x 256). Values are '0'-'9'/'a'-'f' (& 0x1F yields <= 25),
    //    masked sum 4e7 x 25 < 2^31 -- no wrap. net48-only (get_Chars
    //    unwired).
    public static int StrChars() {
        string s = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                 + "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                 + "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                 + "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        int sum = 0;
        for (int r = 0; r < 156252; r++) {
            for (int i = 0; i < 256; i++) sum += s[i] & 0x1F;
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

    // 6. String concatenation: 500,000 x "v" + (i & 7) (box + Concat).
    //    500,000 tiny string allocations. net48-only (String.Concat unwired).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static int StrConcat() {
        int sum = 0;
        for (int i = 0; i < 500000; i++) {
            string t = "v" + (i & 7);
            sum += t.Length;
        }
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
        checksum += ArrCopy();
        t1 = now_us();
        Console.WriteLine("arr_copy");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += StrChars();
        t1 = now_us();
        Console.WriteLine("str_chars");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += StrLen();
        t1 = now_us();
        Console.WriteLine("str_len");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += StrConcat();
        t1 = now_us();
        Console.WriteLine("str_concat");
        Console.WriteLine((t1 - t0) / 1000);
    }
}
}
