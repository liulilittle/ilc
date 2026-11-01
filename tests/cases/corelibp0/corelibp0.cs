using System;
using System.Runtime.CompilerServices;
using System.Threading;

// corelibp0 -- the CoreLib P0 members that exist in BOTH shipped
// System.Private.CoreLib 8.0.31 and 9.0.20 (docs/audit/corelib-p0-reachability.md).
//
// Every member exercised here is a CoreLib-INTERNAL QCall/FCall; each is
// reached through its public managed wrapper, which is the only shape user IL
// can bind. The assertions state the contract the SHIPPED runtime was measured
// to satisfy (probe: .opencode/tmp/impl-corelib-p0/probe2, net8.0 oracle), so
// the oracle exe and this runtime must produce identical bytes.
//
// Self-asserting booleans, invariant text only: every emitted value is either
// True/False or an exception's Message, so there is no culture or address
// dependence in the output.
public static class Program
{
    static int s_failures;

    static void Check(bool cond, string what)
    {
        Console.WriteLine((cond ? "True" : "False") + " " + what);
        if (!cond) s_failures++;
    }

    static string Throw(Action a)
    {
        try
        {
            a();
            return "no-throw";
        }
        catch (Exception e)
        {
            return e.GetType().Name + ": " + e.Message;
        }
    }

    public static int Main()
    {
        // --- GC.GetTotalPauseDuration (FCall _GetTotalPauseDuration, ticks) ---
        // The wrapper is new TimeSpan(_GetTotalPauseDuration()): a non-negative
        // 100ns tick count. Not pinned to a number (a collection may have run
        // during startup); the CONTRACT is what is asserted.
        TimeSpan pause = GC.GetTotalPauseDuration();
        Check(pause >= TimeSpan.Zero, "gc pause non-negative");
        Check(pause.Ticks >= 0, "gc pause ticks non-negative");

        // --- GC.EndNoGCRegion (QCall GCInterface_EndNoGCRegion) ---
        // No NoGC region is in progress, so the internal status is
        // NotInProgress and the wrapper raises InvalidOperationException with
        // this exact text (measured on the shipped runtime).
        string end = Throw(() => GC.EndNoGCRegion());
        Check(end == "InvalidOperationException: NoGCRegion mode must be set.", "gc EndNoGCRegion outside region");

        // --- GC.RegisterNoGCRegionCallback (QCall GCInterface_EnableNoGCRegionCallback) ---
        // Same region check, same message -- the region test precedes any use
        // of the work item.
        string regcb = Throw(() => GC.RegisterNoGCRegionCallback(1000, () => { }));
        Check(regcb == "InvalidOperationException: NoGCRegion mode must be set.", "gc RegisterNoGCRegionCallback outside region");

        // --- GC.RefreshMemoryLimit (QCall GCInterface_RefreshMemoryLimit) ---
        // Re-reads the machine/container limits; a fixed-budget collector
        // returns without changing anything.
        string refresh = Throw(() => GC.RefreshMemoryLimit());
        Check(refresh == "no-throw", "gc RefreshMemoryLimit");

        // --- GC.WaitForPendingFinalizers (QCall GCInterface_WaitForPendingFinalizers) ---
        string wpf = Throw(() => GC.WaitForPendingFinalizers());
        Check(wpf == "no-throw", "gc WaitForPendingFinalizers");

        // --- GC memory pressure + full-GC notification (FCall/QCall pair) ---
        string amp = Throw(() =>
        {
            GC.AddMemoryPressure(4096);
            GC.RemoveMemoryPressure(4096);
            GC.RegisterForFullGCNotification(10, 10);
            GC.CancelFullGCNotification();
        });
        Check(amp == "no-throw", "gc pressure + full-GC notification");

        // --- GC._CollectionCount (FCall, behind CollectionCount) ---
        // Monotonic counter, defined for 0..MaxGeneration.
        Check(GC.CollectionCount(0) >= 0, "gc CollectionCount(0) non-negative");
        Check(GC.CollectionCount(GC.MaxGeneration) >= 0, "gc CollectionCount(max) non-negative");

        // --- GC._Collect (QCall GCInterface_Collect, behind Collect) ---
        string collect = Throw(() => GC.Collect());
        Check(collect == "no-throw", "gc Collect");

        // --- RuntimeHelpers.TryEnsureSufficientExecutionStack (FCall) ---
        // The non-throwing twin of EnsureSufficientExecutionStack: plenty of
        // stack here, so both answer "sufficient".
        Check(RuntimeHelpers.TryEnsureSufficientExecutionStack(), "rh TryEnsureSufficientExecutionStack true at shallow depth");
        string ensure = Throw(() => RuntimeHelpers.EnsureSufficientExecutionStack());
        Check(ensure == "no-throw", "rh EnsureSufficientExecutionStack at shallow depth");
        Check(RuntimeHelpers.TryEnsureSufficientExecutionStack() == true, "rh TryEnsure after throw variant");

        // --- RuntimeHelpers.AllocateTypeAssociatedMemory (QCall RuntimeTypeHandle_AllocateTypeAssociatedMemory) ---
        // Type-associated memory is process-lifetime here (an AOT image never
        // unloads), so a usable block always comes back for a positive size.
        IntPtr block = RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(Program), 128);
        Check(block != IntPtr.Zero, "rh AllocateTypeAssociatedMemory non-null");
        IntPtr block2 = RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(int), 8);
        Check(block2 != IntPtr.Zero, "rh AllocateTypeAssociatedMemory second block");
        Check(block != block2, "rh AllocateTypeAssociatedMemory distinct blocks");

        // --- Interlocked.MemoryBarrierProcessWide (QCall Interlocked_MemoryBarrierProcessWide) ---
        string mbpw = Throw(() =>
        {
            Interlocked.MemoryBarrierProcessWide();
            Interlocked.MemoryBarrier();
        });
        Check(mbpw == "no-throw", "interlocked process-wide barrier");

        // --- Thread surface reachable from the QCall bodies -------------------
        // Thread.YieldInternal (QCall ThreadNative_YieldThread) backs the public
        // bool Thread.Yield(); Thread.GetCurrentOSThreadId backs the internal
        // id. Assert only the ORDERING-free facts -- "did another thread run"
        // is a genuine Windows/POSIX difference, not part of the contract.
        string yield = Throw(() => Thread.Yield());
        Check(yield == "no-throw", "thread Yield returns without throwing");
        Check(Thread.CurrentThread.ManagedThreadId != 0, "thread ManagedThreadId non-zero after Yield");

        // --- Math.ModF (FCall ModF -> icall_math_modf_d) ----------------------
        // ModF is CoreLib-internal (there is no public Math.ModF), so it is
        // pinned by the resolver unit tests rather than from user IL. The
        // identity it implements -- fractional part is value minus the
        // truncated integral part -- is asserted here through the public
        // neighbours, so the case also covers the arithmetic the mapping
        // feeds (std::modf writes intPart through the pointer and returns the
        // fraction).
        Check(Math.Truncate(-3.7) == -3.0, "math Truncate negative");
        Check(3.7 - Math.Truncate(3.7) - 0.7 < 1e-12 && 3.7 - Math.Truncate(3.7) - 0.7 > -1e-12,
            "math fraction identity ModF implements");

        Console.WriteLine("failures=" + s_failures);
        return s_failures == 0 ? 0 : 1;
    }
}
