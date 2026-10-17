//------------------------------------------------------------------------------
// p-nb-tim.cs -- batch nb1 TIM-OVL difftest probe (net48 oracle == transpiled
// runtime byte-exact).
//
// Covers the System.Threading.Timer overload surface:
//   * ctor (TimerCallback, object, int, int)         -- wired (batch 158).
//   * ctor (TimerCallback, object, long, long)       -- was count-blind int32
//     narrowing.
//   * ctor (TimerCallback, object, TimeSpan, TimeSpan) -- the C0 compile
//     break (cannot convert Il2CppTimeSpan to int32_t) that this batch
//     eliminates.
//   * ctor (TimerCallback, object, uint, uint)       -- count-blind uint32
//     bit-pattern.
//   * Change(int,int) / Change(long,long) /
//     Change(TimeSpan,TimeSpan) / Change(uint,uint).
//
// The probe is TIMING-INSENSITIVE: every timer is created DISARMED
// (dueTime = -1 / Timeout.InfiniteTimeSpan / uint.MaxValue) and only the
// Change(...) RETURN VALUE (bool, deterministic) and Dispose no-throw are
// recorded -- the ntim_oracle line 1-8 shape ([tim.X.change]=True +
// [tim.X.dispose]=True). A one-shot Change(1, -1) fires once; the assert is
// >= 1 (the real-timer thread may also have counted, never less than one).
//
// The oracle evidence (ntim_oracle.txt): all eight records True -- the
// runtime must converge on the same bool surface without a build failure.
//------------------------------------------------------------------------------
using System;
using System.Threading;

namespace Cases
{
    public static class PNbTim
    {
        static int g_fires;
        static void Cb(object state) { g_fires++; }

        public static void Main()
        {
            // int overload (already wired -- regression guard).
            g_fires = 0;
            Timer t1 = new Timer(Cb, null, -1, -1);
            try
            {
                Console.WriteLine("[tim.int.change]=" + t1.Change(1, -1));
                Thread.Sleep(40);
                Console.WriteLine("[tim.int.fired]=" + (g_fires >= 1));
            }
            finally { t1.Dispose(); Console.WriteLine("[tim.int.dispose]=True"); }

            // long overload.
            g_fires = 0;
            Timer t2 = new Timer(Cb, null, -1L, -1L);
            try
            {
                Console.WriteLine("[tim.long.change]=" + t2.Change(1L, -1L));
                Thread.Sleep(40);
                Console.WriteLine("[tim.long.fired]=" + (g_fires >= 1));
            }
            finally { t2.Dispose(); Console.WriteLine("[tim.long.dispose]=True"); }

            // TimeSpan overload (the C0 compile break).
            g_fires = 0;
            Timer t3 = new Timer(Cb, null, TimeSpan.FromMilliseconds(-1), TimeSpan.FromMilliseconds(-1));
            try
            {
                Console.WriteLine("[tim.ts.change]=" + t3.Change(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(-1)));
                Thread.Sleep(40);
                Console.WriteLine("[tim.ts.fired]=" + (g_fires >= 1));
            }
            finally { t3.Dispose(); Console.WriteLine("[tim.ts.dispose]=True"); }

            // uint overload (count-blind uint32 bit-pattern previously).
            g_fires = 0;
            Timer t4 = new Timer(Cb, null, uint.MaxValue, uint.MaxValue);
            try
            {
                Console.WriteLine("[tim.uint.change]=" + t4.Change(1u, uint.MaxValue));
                Thread.Sleep(40);
                Console.WriteLine("[tim.uint.fired]=" + (g_fires >= 1));
            }
            finally { t4.Dispose(); Console.WriteLine("[tim.uint.dispose]=True"); }
        }
    }
}