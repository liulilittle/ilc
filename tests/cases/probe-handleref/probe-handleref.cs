//------------------------------------------------------------------------------
// probe-handleref.cs -- System.Runtime.InteropServices.HandleRef P/Invoke
// decay probe (net48 oracle == transpiled runtime, byte-exact stdout).
//
// MEASURED net48 (.NET Framework 4.8, x64) contract, pinned by this probe:
//   * A HandleRef P/Invoke parameter reaches native code as its .Handle value
//     (a native int), never as a pointer to the struct. [real.setevent]
//   * new HandleRef(null, IntPtr.Zero) is legal: the null wrapper does not
//     throw and the zero handle is passed through unchanged -- SetEvent(NULL)
//     fails with ERROR_INVALID_HANDLE (6). [zero.ret]=False [zero.lasterror]=6
//   * The .Wrapper object stays alive for the whole call. A wrapper that is
//     only referenced by a HandleRef rvalue survives a GC triggered from a
//     native callback inside the call (finalizer counter unchanged), and it
//     becomes collectable once the HandleRef value is released.
//   * Mixed arity F(HandleRef, int, HandleRef) (the ImageList_Add shape)
//     passes both handles and the scalar positionally.
//
// The native side is tests/cases/probe-handleref/probehandleref/ (built as a
// static library and linked into the generated exe).
//
// Determinism: no raw pointer/handle values are printed; every record is a
// fixed boolean/int derived from fixed inputs.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class ProbeHandleRef
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true)]
        public static extern IntPtr CreateEventW(IntPtr attrs, bool manualReset, bool initialState, string name);

        [DllImport("kernel32.dll", EntryPoint = "SetEvent", SetLastError = true)]
        public static extern bool SetEvent(HandleRef hEvent);

        [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("probehandleref", EntryPoint = "probe_mixed")]
        public static extern int ProbeMixed(HandleRef first, int tag, HandleRef second);

        [DllImport("probehandleref", EntryPoint = "probe_keepalive")]
        public static extern int ProbeKeepAlive(HandleRef handle, IntPtr callback);

        // Native callback shape (see the note in Main: net48 rejects Func<int>).
        public delegate int CollectorFn();

        // Finalizable wrappers for the keep-alive probe. SEPARATE types keep
        // the during-call counter independent of the release-phase counter.
        public sealed class DuringWrapper
        {
            public static int Count;

            ~DuringWrapper()
            {
                Count++;
            }
        }


        // Native callback body: force a full collection while the native call
        // is on the stack. If the HandleRef wrapper were not kept alive, its
        // finalizer would run here and the return value would be non-zero.
        private static int CollectInCallback()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return DuringWrapper.Count;
        }

        public static void Main()
        {
            DuringWrapper.Count = 0;


            IntPtr evt = CreateEventW(IntPtr.Zero, false, false, null);
            Console.WriteLine("[evt.nonzero]=" + ((long)evt != 0));

            // [real] a real handle passed through a HandleRef takes effect.
            Console.WriteLine("[real.setevent]=" + SetEvent(new HandleRef(null, evt)));

            // [zero] a null wrapper is legal and the zero handle is passed
            // through unchanged: SetEvent(NULL) fails with error 6.
            Console.WriteLine("[zero.ret]=" + SetEvent(new HandleRef(null, IntPtr.Zero)));
            Console.WriteLine("[zero.lasterror]=" + Marshal.GetLastWin32Error());

            // [mixed] F(HandleRef, int, HandleRef).
            Console.WriteLine("[mixed.same]=" + ProbeMixed(new HandleRef(null, evt), 7, new HandleRef(null, evt)));
            Console.WriteLine("[mixed.tag]=" + ProbeMixed(new HandleRef(null, evt), 8, new HandleRef(null, evt)));

            // [ka] keep-alive: the FINALIZABLE wrapper is only referenced by the
            // HandleRef rvalue passed below; the native callback runs a full GC
            // inside the call. net48 keeps the wrapper alive for the call.
            // NOTE (net48 measured): Marshal.GetFunctionPointerForDelegate rejects
            // System.Func`1 (ArgumentException "the delegate type is not a generic
            // type definition"); a custom delegate type marshals fine.
            CollectorFn collector = CollectInCallback;
            IntPtr cb = Marshal.GetFunctionPointerForDelegate(collector);
            int during = ProbeKeepAlive(new HandleRef(new DuringWrapper(), evt), cb);
            Console.WriteLine("[ka.during_call_finalized]=" + during);
            GC.KeepAlive(collector);

            // [member] HandleRef.Handle / HandleRef.Wrapper members.
            HandleRef probe = new HandleRef(null, evt);
            Console.WriteLine("[member.handle_roundtrip]=" + (probe.Handle == evt));
            Console.WriteLine("[member.wrapper_null]=" + (probe.Wrapper == null));

            Console.WriteLine("[cleanup]=" + CloseHandle(evt));
        }
    }
}
