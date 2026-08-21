using System;
namespace Cases { public static class GcCase {
    public static void Main() {
        // Deterministic reference output (net48): raw heap sizes vary run to
        // run, so this case prints STABLE comparisons instead of byte values.
        //
        // PRIMING: the first GetTotalMemory check must run AFTER a managed
        // allocation. A net48 process enters Main with loader/JIT allocations
        // already on the heap, but the ILC runtime's heap is genuinely empty
        // at Main entry (runtime_init is a no-op) -- so a bare `> 0` at
        // process start prints False here and True on net48. Allocating the
        // probe first makes the comparison environment-independent: both
        // heaps are non-empty at the check.
        object o = new object();
        Console.WriteLine(GC.GetTotalMemory(false) > 0); // True on net48 and ILC (probe primed)
        Console.WriteLine(GC.GetGeneration(o));          // 0
        GC.Collect();                                    // full collection, all generations
        Console.WriteLine(GC.GetTotalMemory(false) > 0); // True on net48 and ILC
        Console.WriteLine(GC.GetGeneration(o) > 0);      // True (promoted by the full collect)
        GC.KeepAlive(o);                                 // no-op (roots o through the call)
        GC.SuppressFinalize(o);                          // no-op placeholder (no finalizer mechanism yet)
    }
}}
