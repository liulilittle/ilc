//------------------------------------------------------------------------------
// p-nb4-gc.cs -- nb4 GC-SPOT difftest probe (net48 oracle == transpiled
// runtime byte-exact).
//
// Pins the two GC notification icalls (RegisterForFullGCNotification /
// CancelFullGCNotification -- the runtime entries already exist in gc.cpp,
// only the MethodMap arms were missing) and the Volatile family
// (Thread.VolatileRead/VolatileWrite + System.Threading.Volatile.Read/Write).
//
// Determinism rules: only booleans, fixed strings and primitive read-backs
// are printed -- no wall-clock magnitudes, no exception text, no addresses.
// The probe is single-threaded, so the fence atomics are plain transactions.
//------------------------------------------------------------------------------
using System;
using System.Threading;

namespace Cases
{
    public static class PNb4Gc
    {
        public static void Main()
        {
            // GC notification registration / cancellation (both return bool
            // in net48 metadata but the probes discard it). The thresholds
            // must be 1..99 (net48 ArgumentOutOfRangeException otherwise).
            GC.RegisterForFullGCNotification(10, 10);
            Console.WriteLine("[gc.reg]=registered");
            GC.CancelFullGCNotification();
            Console.WriteLine("[gc.cancel]=called");

            // Volume 1 -- Thread.VolatileRead/VolatileWrite (int + long).
            int x = 0;
            Thread.VolatileWrite(ref x, 1);
            long y = 41;
            Thread.VolatileWrite(ref y, 42);
            Console.WriteLine("[thr.vr]=" + Thread.VolatileRead(ref x)
                              + "|" + Thread.VolatileRead(ref y));

            // Volume 2 -- System.Threading.Volatile.Read/Write (int).
            int a = 3;
            int b = 0;
            Console.WriteLine("[vol.rw]=" + Volatile.Read(ref a) + "|");
            Volatile.Write(ref b, 99);
            Console.WriteLine(Volatile.Read(ref b));
        }
    }
}