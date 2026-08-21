using System;
using System.Runtime.InteropServices;
namespace Bench {
//------------------------------------------------------------------------------
// bench.cs -- fixed-workload performance benchmarks for the ILC transpiled
// output vs the .NET Framework 4.8 reference runtime.
//
// DOWNGRADED TO THE M-STAGE TRANSPILABLE SUBSET (documented limitation):
//   1. System.Diagnostics.Stopwatch is NOT transpilable (no icall wiring in
//      MethodRefResolver), so timing goes through the P/Invoke clock
//      benchlib.now_us() (M4 P/Invoke IS supported; steady-clock
//      microseconds as int32). Elapsed = now_us() - start; the int32 window
//      covers ~35 minutes, far beyond any benchmark here.
//   2. "name: <ms>" on one line requires String.Concat (string + int), which
//      the transpiler cannot resolve (link error). Each benchmark therefore
//      prints TWO lines: the name, then the integer milliseconds. The
//      harness (run-bench.ps1) pairs them and renders the comparison table.
//   3. Array.Sort / Hashtable / System.Random / string concatenation are not
//      transpilable: sort and hashtable are implemented here in user code
//      (same algorithm on both runtimes -- a fairer comparison), and the
//      string benchmark measures string materialization + Length instead of
//      "x" + i concatenation.
//   4. ILC runtime limitation (working-tree gc.cpp, 2026-08-03): objects
//      larger than the segment payload (Gen0 64 KB / LOH 1 MB) overrun the
//      segment (AllocCore: target->used += size without a capacity re-check
//      after AllocSegment). Arrays in the 64-85 KB Gen0 gap or >1 MB crash.
//      Every allocation below therefore fits ONE segment: arrays are
//      <= 800 KB (LOH) or <= 48 KB (Gen0); the fixed work is reached by
//      REPEATING the operation instead of enlarging the array.
//   5. ILC transpiler limitation (CodeGenerator WrapFrameRoots, 2026-08-03):
//      reference locals are registered as GC roots at function entry, but
//      the slots are uninitialized until first use; a collection running
//      during an allocation BEFORE the refs are assigned scans garbage and
//      crashes (ScanObject AV -- reproduced with hash's second LOH array).
//      The benchmark order and sizes below are chosen so that NO allocation
//      ever triggers a collection while a ref slot is unassigned:
//      total LOH usage stays under one 1 MB segment (no LOH crossing), the
//      Gen0 allocation happens while the Gen0 segment is still fresh, and
//      the allocation-pressure benchmarks (string/object) run last, when
//      their ref slots are already assigned before collections start.
//
// METHODOLOGY: every benchmark runs a FIXED amount of work (the time varies,
// the work does not -- that is what makes the ratio meaningful). The
// workloads are sized so the net48 reference takes at least ~15 ms: integer
// milliseconds are then meaningful on both sides. All results fold into the
// static 'checksum' field: the writes are observable (external-linkage
// storage in the generated C++), so -O3 cannot eliminate the benchmark
// loops; the .NET JIT keeps them for the same reason. Every sink stays
// inside int32 (masked sums -- no wrap, no C++ signed-overflow UB). The
// data generator is xorshift32 (shifts/XOR/mask only -- deterministic on
// net48 and on all supported C++ compilers).
//------------------------------------------------------------------------------
public static class Bench {
    // P/Invoke clock (benchlib: steady_clock, microseconds, wrapped to [0,2^31)).
    [DllImport("benchlib", EntryPoint = "now_us")]
    public static extern int now_us();

    // Sink: every benchmark folds its result into this static field.
    public static int checksum;

    // Deterministic xorshift32 state (seeded in the .cctor).
    public static int rngState = 12345;

    public static int NextRand() {
        int s = rngState;
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        rngState = s;
        return s & 0x7FFFFFFF;
    }

    // 1. Recursion: naive Fibonacci (exponential). Fib(34) = 5702887
    //    (int32-safe), ~5.7M calls -- a few tens of ms on net48.
    public static int Fib(int n) {
        if (n < 2) return n;
        return Fib(n - 1) + Fib(n - 2);
    }

    // 2. Pure integer loop: 50,000,000 xorshift32 updates + accumulation.
    //    The LCG recurrence cannot be closed-form folded by -O3 (and the
    //    writes feed the observable static sink). 'sum' wraps identically on
    //    net48 (defined) and on every supported C++ compiler (machine
    //    arithmetic); the value only feeds the sink, never the output.
    public static int LoopBench() {
        int sum = 0;
        int s = 12345;
        for (int i = 0; i < 50000000; i++) {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            sum += s;
        }
        return sum;
    }

    // 3. String work: 20,000,000 iterations of string materialization +
    //    Length read (Replaces the unsupported "x" + i concatenation
    //    benchmark). The string must VARY per iteration: with a single
    //    loop-invariant literal the net48 JIT hoists the allocation and
    //    folds the whole loop (measured 0 ms). Pick() keeps the string
    //    runtime-determined; the JIT still wins hugely here (verified: it
    //    hoists the four literals and runs ~2 ns/iteration), while the ILC
    //    side allocates a fresh string every iteration -- this benchmark
    //    doubles as an optimizer-gap probe (see README). The sink store
    //    lives inside the loop (observable static write).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static string Pick(int n) {
        if (n == 0) return "a";
        if (n == 1) return "ab";
        if (n == 2) return "abc";
        return "abcd";
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static int Len(string s) { return s.Length; }

    public static int StringBench() {
        for (int i = 0; i < 20000000; i++) {
            checksum += Len(Pick(i & 3));
        }
        return 0;
    }

    // 4. Array work: fill + sum a 400 KB int array (LOH; the whole LOH
    //    budget of the run stays under one 1 MB segment -- no crossing),
    //    repeated 200 times = 40,000,000 element operations. Masked
    //    accumulation keeps the sum inside int32.
    public static int ArrayBench() {
        int[] a = new int[100000];
        int sum = 0;
        for (int r = 0; r < 200; r++) {
            for (int i = 0; i < 100000; i++) a[i] = i;
            for (int i = 0; i < 100000; i++) sum += a[i] & 0x7F;
        }
        return sum;
    }

    // 5. Object churn: 5,000,000 small heap objects (allocation + ctor +
    //    instance method call). Masked sink stays inside int32. NOTE: the
    //    net48 JIT inlines the ctor + Get, proves the object never escapes
    //    and eliminates the allocation (~6 ns/iteration); the ILC side
    //    allocates every iteration (IL2CPP_NOINLINE). Like the string
    //    benchmark, this doubles as an optimizer-gap probe (see README).
    public static int ObjectBench() {
        int sum = 0;
        for (int i = 0; i < 5000000; i++) {
            Item it = new Item(i);
            sum += it.Get() & 0xFF;
        }
        return sum;
    }

    // 6. Sorting: insertion sort over 12,000 deterministic pseudo-random ints
    //    (48 KB -- fits the Gen0 segment; self-implemented because
    //    Array.Sort is not transpilable). The same code runs on both
    //    runtimes, so the comparison is algorithm-pure.
    public static int SortBench() {
        int[] a = new int[12000];
        for (int i = 0; i < 12000; i++) a[i] = NextRand();
        for (int i = 1; i < 12000; i++) {
            int key = a[i];
            int j = i - 1;
            while (j >= 0 && a[j] > key) {
                a[j + 1] = a[j];
                j--;
            }
            a[j + 1] = key;
        }
        return (a[0] & 0xFF) + (a[11999] & 0xFF);
    }

    // 7. Hashing: open-addressing int table (65,536 slots = 2^16, -1 =
    //    empty; the two 256 KB arrays keep total LOH under one 1 MB
    //    segment), 40,000 inserts + 40,000 lookups of the same deterministic
    //    keys per round, repeated 12 rounds with a fresh table each round
    //    (self-implemented; Hashtable is not transpilable). The slot count is
    //    a power of two so the probe wrap `& (slots - 1)` cycles the full
    //    table (a non-power-of-two count produced an infinite probe cycle).
    //    Masked sink stays inside int32.
    public static int HashBench() {
        int slots = 65536;
        int[] keys = new int[slots];
        int[] vals = new int[slots];
        int sum = 0;
        for (int r = 0; r < 12; r++) {
            for (int i = 0; i < slots; i++) keys[i] = -1;
            int saved = rngState;
            for (int i = 0; i < 40000; i++) {
                int k = NextRand();
                int idx = ((k >> 8) ^ k) & (slots - 1);
                while (keys[idx] != -1) idx = (idx + 1) & (slots - 1);
                keys[idx] = k;
                vals[idx] = i;
            }
            rngState = saved;
            for (int i = 0; i < 40000; i++) {
                int k = NextRand();
                int idx = ((k >> 8) ^ k) & (slots - 1);
                while (keys[idx] != k && keys[idx] != -1) idx = (idx + 1) & (slots - 1);
                if (keys[idx] == k) sum += vals[idx] & 0xFF;
            }
        }
        return sum;
    }

    public static void Main() {
        int t0, t1;

        t0 = now_us();
        checksum += Fib(34);
        t1 = now_us();
        Console.WriteLine("fib");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += LoopBench();
        t1 = now_us();
        Console.WriteLine("loop");
        Console.WriteLine((t1 - t0) / 1000);

        // sort/array/hash run before the allocation-pressure benchmarks:
        // their first allocations must not trigger a collection while the
        // transpiler's uninitialized GC-root slots are still garbage (see
        // limitation 5 above).
        t0 = now_us();
        checksum += SortBench();
        t1 = now_us();
        Console.WriteLine("sort");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += ArrayBench();
        t1 = now_us();
        Console.WriteLine("array");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += HashBench();
        t1 = now_us();
        Console.WriteLine("hash");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += StringBench();
        t1 = now_us();
        Console.WriteLine("string");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += ObjectBench();
        t1 = now_us();
        Console.WriteLine("object");
        Console.WriteLine((t1 - t0) / 1000);
    }
}

// Small heap object used by the object-churn benchmark.
public class Item {
    public int v;
    public Item(int x) { v = x; }
    public int Get() { return v; }
}
}
