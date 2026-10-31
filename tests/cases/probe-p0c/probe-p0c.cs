//------------------------------------------------------------------------------
// probe-p0c.cs -- mscorlib P0 batch C: the 10 non-COM gaps that are NOT
// varargs (docs/audit/icall-mscorlib-current.md section 3 P0 rows 2/3/4/11/
// 31-35/37). Every record is a fixed boolean/string/int derived from fixed
// inputs; the net48 oracle is produced by the same program (difftest).
//
// ROW MAP
//   #2  Debugger.IsLogging            [dbg.is_logging]
//   #3  Debugger.get_IsAttached       [dbg.is_attached]
//   #4  Environment.HasShutdownStarted[env.has_shutdown]
//   #11 ExtensibleClassFactory.RegisterObjectCreationCallback [ecf.*]
//   #31 RealProxy.GetProxiedType      [rp.proxied_type]
//   #32 RealProxy.GetStubData         [rp.stub_*]
//   #33 RealProxy.SetStubData         [rp.stub_*]
//   #34 RemotingServices.GetRealProxy [rs.real_*]
//   #35 RemotingServices.IsTransparentProxy [rs.is_tp*]
//   #37 ReaderWriterLock.AnyWritersSince [rwl.any_*]
//
// (#1 ArgIterator.GetRemainingCount is a VARARGS (__arglist) item: the
//  transpiler rejects the `arglist` IL opcode itself (StackFold.cs), so no
//  ArgIterator can even be constructed. It needs an independent varargs
//  calling-convention project -- see NOTES.md for the measured IL shape and
//  the effort estimate.)
//------------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;

namespace Cases
{
    // #31-#35: the smallest real proxy -- RealProxy is abstract with one
    // abstract member (Invoke), so a probe subclass must implement it.
    public class P0cProxy : RealProxy
    {
        public P0cProxy(Type classToProxy) : base(classToProxy) { }
        public override IMessage Invoke(IMessage msg) { return null; }
    }

    public static class ProbeP0c
    {
        // #2/#3 control: the OS answer for the SAME process. net48's
        // Debugger.IsAttached tracks the CLR managed debugger (measured false
        // under a native cdb debugger), so this record documents the platform
        // truth next to the managed property.
        [DllImport("kernel32.dll", EntryPoint = "IsDebuggerPresent")]
        private static extern bool NativeIsDebuggerPresent();

        private static IntPtr CreateInstance(IntPtr aggregator) { return IntPtr.Zero; }

        public static void Main()
        {
            // #2 / #3
            Console.WriteLine("[dbg.win32_present]=" + NativeIsDebuggerPresent());
            Console.WriteLine("[dbg.is_attached]=" + Debugger.IsAttached);
            Console.WriteLine("[dbg.is_logging]=" + Debugger.IsLogging());

            // #4
            Console.WriteLine("[env.has_shutdown]=" + Environment.HasShutdownStarted);

            // #11 (net48: the callback may only be registered from inside the
            // class constructor of a COM-creatable class -> a plain caller gets
            // InvalidOperationException; null is rejected first)
            try
            {
                ExtensibleClassFactory.RegisterObjectCreationCallback(new ObjectCreationDelegate(CreateInstance));
                Console.WriteLine("[ecf.callback]=ok");
            }
            catch (Exception e) { Console.WriteLine("[ecf.callback]=" + e.GetType().Name); }
            try
            {
                ExtensibleClassFactory.RegisterObjectCreationCallback(null);
                Console.WriteLine("[ecf.null]=ok");
            }
            catch (Exception e) { Console.WriteLine("[ecf.null]=" + e.GetType().Name); }

            // #31-#35
            P0cProxy p = new P0cProxy(typeof(MarshalByRefObject));
            Console.WriteLine("[rp.proxied_type]=" + p.GetProxiedType().Name);
            object tp = p.GetTransparentProxy();
            Console.WriteLine("[rp.tp_same]=" + ReferenceEquals(tp, p.GetTransparentProxy()));
            Console.WriteLine("[rs.is_tp]=" + RemotingServices.IsTransparentProxy(tp));
            Console.WriteLine("[rs.is_tp_real]=" + RemotingServices.IsTransparentProxy(p));
            Console.WriteLine("[rs.is_tp_plain]=" + RemotingServices.IsTransparentProxy(new object()));
            Console.WriteLine("[rs.is_tp_null]=" + RemotingServices.IsTransparentProxy(null));
            Console.WriteLine("[rs.real_same]=" + ReferenceEquals(RemotingServices.GetRealProxy(tp), p));
            Console.WriteLine("[rs.real_plain_null]=" + (RemotingServices.GetRealProxy(new object()) == null));
            Console.WriteLine("[rs.real_null_null]=" + (RemotingServices.GetRealProxy(null) == null));
            Console.WriteLine("[rp.stub_default_type]=" + RealProxy.GetStubData(p).GetType().Name);
            RealProxy.SetStubData(p, "stub42");
            Console.WriteLine("[rp.stub_roundtrip]=" + (string)RealProxy.GetStubData(p));
            try
            {
                RealProxy.SetStubData(p, null);
                Console.WriteLine("[rp.stub_setnull]=ok");
            }
            catch (Exception e) { Console.WriteLine("[rp.stub_setnull]=" + e.GetType().Name); }
            P0cProxy p2 = new P0cProxy(typeof(MarshalByRefObject));
            Console.WriteLine("[rp.stub_isolated]=" + (RealProxy.GetStubData(p2).GetType().Name));

            // #37
            ReaderWriterLock rwl = new ReaderWriterLock();
            int seq0 = rwl.WriterSeqNum;
            Console.WriteLine("[rwl.seq0]=" + seq0);
            Console.WriteLine("[rwl.any_before]=" + rwl.AnyWritersSince(seq0));
            Console.WriteLine("[rwl.held_r0]=" + rwl.IsReaderLockHeld);
            Console.WriteLine("[rwl.held_w0]=" + rwl.IsWriterLockHeld);
            rwl.AcquireReaderLock(-1);
            Console.WriteLine("[rwl.held_r1]=" + rwl.IsReaderLockHeld);
            Console.WriteLine("[rwl.any_reader]=" + rwl.AnyWritersSince(seq0));
            rwl.ReleaseReaderLock();
            Console.WriteLine("[rwl.held_r2]=" + rwl.IsReaderLockHeld);
            rwl.AcquireWriterLock(-1);
            Console.WriteLine("[rwl.held_w1]=" + rwl.IsWriterLockHeld);
            rwl.ReleaseWriterLock();
            Console.WriteLine("[rwl.held_w2]=" + rwl.IsWriterLockHeld);
            int seq1 = rwl.WriterSeqNum;
            Console.WriteLine("[rwl.seq_after_writer]=" + (seq1 > seq0));
            Console.WriteLine("[rwl.any_after]=" + rwl.AnyWritersSince(seq0));
            Console.WriteLine("[rwl.any_since_current]=" + rwl.AnyWritersSince(seq1));
            rwl.AcquireWriterLock(-1);
            rwl.ReleaseWriterLock();
            Console.WriteLine("[rwl.any_after2]=" + rwl.AnyWritersSince(seq1));
        }
    }
}
