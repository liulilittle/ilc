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
//
// SCOPE: every overload net48's Thread.VolatileRead/VolatileWrite actually
// declares -- 13 each (byte/sbyte/short/ushort/int/uint/long/ulong/float/
// double/intptr/uintptr/object). bool/char/string are NOT overloads there
// (bool and char fold into the byte overload, string into object), which is
// exactly why they are absent from Volume 1. Volatile.Read/Write additionally
// carry a generic T overload; the reference case exercises the concrete ones.
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

            // Volume 3 -- Thread.VolatileRead/VolatileWrite over every other
            // scalar width net48 declares. Each row writes a sentinel and
            // reads it back, so a wrong-width dispatch cannot hide: the value
            // must survive the round-trip unchanged.
            byte vb = 7; Thread.VolatileWrite(ref vb, (byte)9);
            Console.WriteLine("[b]=" + Thread.VolatileRead(ref vb));

            sbyte vsb = -7; Thread.VolatileWrite(ref vsb, (sbyte)-9);
            Console.WriteLine("[sb]=" + Thread.VolatileRead(ref vsb));

            short vsh = -300; Thread.VolatileWrite(ref vsh, (short)456);
            Console.WriteLine("[s]=" + Thread.VolatileRead(ref vsh));

            ushort vus = 300; Thread.VolatileWrite(ref vus, (ushort)456);
            Console.WriteLine("[us]=" + Thread.VolatileRead(ref vus));

            uint vui = 3000000000u; Thread.VolatileWrite(ref vui, 4000000000u);
            Console.WriteLine("[ui]=" + Thread.VolatileRead(ref vui));

            ulong vul = 9000000000000000000ul;
            Thread.VolatileWrite(ref vul, 8000000000000000000ul);
            Console.WriteLine("[ul]=" + Thread.VolatileRead(ref vul));

            float vf = 1.5f; Thread.VolatileWrite(ref vf, 2.5f);
            Console.WriteLine("[f]=" + Thread.VolatileRead(ref vf));

            double vd = 1.5; Thread.VolatileWrite(ref vd, 2.5);
            Console.WriteLine("[d]=" + Thread.VolatileRead(ref vd));

            IntPtr vip = (IntPtr)1; Thread.VolatileWrite(ref vip, (IntPtr)2);
            Console.WriteLine("[ip]=" + Thread.VolatileRead(ref vip));

            UIntPtr vup = (UIntPtr)1; Thread.VolatileWrite(ref vup, (UIntPtr)2);
            Console.WriteLine("[up]=" + Thread.VolatileRead(ref vup));

            object vo = "p"; Thread.VolatileWrite(ref vo, "q");
            Console.WriteLine("[ref]=" + (string)Thread.VolatileRead(ref vo));

            // Volume 4 -- the static Volatile class over the same widths.
            byte sb2 = 1; Volatile.Write(ref sb2, (byte)2);
            Console.WriteLine("[vol.b]=" + Volatile.Read(ref sb2));

            sbyte ssb2 = -1; Volatile.Write(ref ssb2, (sbyte)-2);
            Console.WriteLine("[vol.sb]=" + Volatile.Read(ref ssb2));

            short ssh2 = 1; Volatile.Write(ref ssh2, (short)2);
            Console.WriteLine("[vol.s]=" + Volatile.Read(ref ssh2));

            ushort sus2 = 1; Volatile.Write(ref sus2, (ushort)2);
            Console.WriteLine("[vol.us]=" + Volatile.Read(ref sus2));

            uint sui2 = 1; Volatile.Write(ref sui2, 2);
            Console.WriteLine("[vol.ui]=" + Volatile.Read(ref sui2));

            ulong sul2 = 1; Volatile.Write(ref sul2, 2);
            Console.WriteLine("[vol.ul]=" + Volatile.Read(ref sul2));

            float sf2 = 1.5f; Volatile.Write(ref sf2, 2.5f);
            Console.WriteLine("[vol.f]=" + Volatile.Read(ref sf2));

            double sd2 = 1.5; Volatile.Write(ref sd2, 2.5);
            Console.WriteLine("[vol.d]=" + Volatile.Read(ref sd2));

            IntPtr sip2 = (IntPtr)1; Volatile.Write(ref sip2, (IntPtr)2);
            Console.WriteLine("[vol.ip]=" + Volatile.Read(ref sip2));

            UIntPtr sup2 = (UIntPtr)1; Volatile.Write(ref sup2, (UIntPtr)2);
            Console.WriteLine("[vol.up]=" + Volatile.Read(ref sup2));

            object so2 = "p"; Volatile.Write(ref so2, "q");
            Console.WriteLine("[vol.ref]=" + (string)Volatile.Read(ref so2));
        }
    }
}