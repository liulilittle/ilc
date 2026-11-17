//------------------------------------------------------------------------------
// probe-half-nonp0.cs -- the 17 non-P0 half-wired mscorlib icalls
// (docs/audit/icall-mscorlib-current.md section 3.8, the "half" rows outside
// P0). Each of those runtime entries is a no-op / constant stub; the question
// this probe answers is whether the CONSTANT they return is the one net48
// answers, not whether the stub looks reasonable.
//
// ROW MAP (17 rows, of which 7 have a public BCL entry point)
//   [serv.is_server]   GC.IsServerGC           <- get_IsServerGC      (half)
//   [ac.overrides]     AppContext override blob<- GetAppContextOverridesInternalCall
//   [ac.getvalue]      AppContext.GetData      <- GetValueInternalCall
//   [ac.tryswitch]     AppContext.TryGetSwitch <- IsEnabledInternalCall
//   [thr.apartment]    Thread.GetApartmentState<- GetApartmentStateNative
//   [thr.setapa]       Thread.SetApartmentState<- SetApartmentStateNative
//   [thr.current]      Thread.CurrentThread    <- InternalGetCurrentThread
//
// The other 10 (Delegate.FindMethodHandle, Exception.PrepareForForeignExceptionRaise,
// Exception.SaveStackTracesFromDeepCopy, Thread.{Get,Set,Clear}AbortReason,
// Thread.GetContextInternal, Thread.InternalCrossContextCallback,
// Thread.{Set,Restore}AppDomainStack) are CoreLib-internal and have no public
// call path a user assembly can write; they are recorded here as documented
// non-targets rather than probed.
//
// Every value printed below must match the net48 oracle byte for byte -- that is
// the whole assertion (difftest does the comparison).
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Runtime;
using System.Threading;

namespace Cases
{
    public static class ProbeHalfNonP0
    {
        public static void Main()
        {
            // [serv.is_server] GCSettings.IsServerGC reads the same predicate as
            // GC.IsServerGC. The runtime models a workstation collector (stub
            // returns 0), so this must be False on both sides.
            Console.WriteLine("serv.is_server=" + GCSettings.IsServerGC);

            // [ac.overrides] No App.config exists for an AOT image, so the raw
            // <AppContextSwitchOverrides> blob is null. GetData on that key
            // therefore answers null -- the same as net48 would for a lookup that
            // matches nothing.
            object overrides = AppContext.GetData("APP_CONTEXT_SWITCH_OVERRIDES");
            Console.WriteLine("ac.overrides_null=" + (overrides == null));

            // [ac.getvalue] An unknown switch: net48 answers null, the stub
            // answers null. A MISSING switch must not be reported as true.
            object missing = AppContext.GetData("ILC_PROBE_NO_SUCH_SWITCH_9F3A");
            Console.WriteLine("ac.getvalue_null=" + (missing == null));

            // [ac.tryswitch] TryGetSwitch on an unknown name is False and leaves
            // the out parameter at its default.
            bool isEnabled = true;
            bool found = AppContext.TryGetSwitch("ILC_PROBE_NO_SUCH_SWITCH_9F3A", out isEnabled);
            Console.WriteLine("ac.tryswitch_found=" + found);
            Console.WriteLine("ac.tryswitch_value=" + isEnabled);

            // [thr.apartment] ApartmentState is STA=0, MTA=1, Unknown=2. net48
            // answers Unknown for a thread that has not started, and the real
            // apartment once it runs. This runtime never initialises a COM
            // apartment, so a started thread must read MTA.
            Thread t = new Thread(delegate() { });
            Console.WriteLine("thr.apartment_unstarted=" + (int)t.GetApartmentState());
            Console.WriteLine("thr.apartment_current=" + (int)Thread.CurrentThread.GetApartmentState());

            // The enum NAME resolves through the KnownBclEnumNames carrier, so a
            // parameter-typed call also compiles (this is the shape that used to
            // declare the mangled System_Threading_ApartmentState).
            Thread t3 = new Thread(delegate() { });
            bool setOk;
            try
            {
                t3.SetApartmentState(ApartmentState.MTA);
                setOk = true;
            }
            catch (Exception ex)
            {
                setOk = false;
                Console.WriteLine("thr.setapa_ex=" + ex.GetType().Name);
            }
            Console.WriteLine("thr.setapa_ok=" + setOk);
            Console.WriteLine("thr.apartment_after=" + (int)t3.GetApartmentState());

            // [thr.setapa] DEFERRED: Thread.SetApartmentState has no MethodMap arm
            // (only TrySetApartmentState does), so any call to it makes the whole
            // method untranspilable. Measured -- see
            // docs/audit/half-wired-nonp0.md section 3. Probed in a separate case
            // once the arm exists.

            // [thr.current] Thread.CurrentThread is the same object on repeated
            // reads and has a positive managed id.
            Thread cur1 = Thread.CurrentThread;
            Thread cur2 = Thread.CurrentThread;
            Console.WriteLine("thr.current_same=" + ReferenceEquals(cur1, cur2));
            Console.WriteLine("thr.current_id_pos=" + (cur1.ManagedThreadId > 0));
        }
    }
}
