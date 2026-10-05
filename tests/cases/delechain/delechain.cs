//------------------------------------------------------------------------------
// delechain.cs -- end-to-end reference case for delegate chains and the
// simple event pattern: single-cast delegates, multicast (+= chain, call
// order, -= removal), delegate equality, and a field-backed event.
//
// TRANSPILER-SAFE SUBSET (verified against the current transpiler):
//   * single-cast System.Func`N delegates bound to STATIC methods
//     (capture lambdas are rejected in phase 1; instance-method binding is
//     not supported either -- the runtime's il2cpp_delegate_new always
//     stamps target == nullptr);
//   * delegate Invoke (direct methodPtr dispatch);
//   * GetInvocationList().Length, Delegate.Target (via ReferenceEquals);
//   * instance Delegate.Equals (structural list equality);
//   * the custom event add/remove methods (Delegate.Combine/Remove icalls).
//
// KNOWN TRANSPILER GAPS (the case deliberately covers them; the reference
// run below documents the intended semantics). Observed on the 2026-08-03
// Release transpiler -- failure order:
//   1. LOADER: MethodSignatureCoder rejects generic-instantiation PARAMETER
//      types (signature byte 0x15 GenericInst) -> "signature parameter type
//      byte 0x15 not supported in M2". The custom event add/remove methods
//      take Func`N parameters, so this case fails to LOAD as a whole; the
//      transpiler-safe subset (single-cast machinery) was verified end-to-
//      end separately and matches the reference except for item 3.
//   2. EMISSION: CodeGenerator.GenericTypeSpecDefinitionName reads the
//      GenericInst signature without skipping the ELEMENT_TYPE_CLASS(0x12)
//      marker byte (StackFold.GenericDefinitionNameOf skips it correctly)
//      -> "Read out of bounds" in the TypeSpec table reader. Every delegate
//      += / -= (the castclass of the Combine/Remove result back to the
//      generic delegate) and any cast to a generic delegate hits this.
//   3. VIRTUAL DISPATCH: callvirt System.Object::Equals on a delegate
//      receiver resolves to icall_boxed_equals_ext2 (reference equality)
//      instead of dispatching to Delegate.Equals (structural list equality)
//      -- single-cast a.Equals(b) with two DIFFERENT objects wrapping the
//      same method returns False instead of True.
//   4. IsDelegateInvoke recognizes System.Func`N only -- custom (non-
//      generic) delegate Invoke and System.Action-based events fall through
//      to "callvirt owner X not in translated types".
//   5. delegate == / != (System.Delegate::op_Equality) is misclassified as
//      an instance method -> "pops this but the stack is empty" -- the case
//      uses instance .Equals and object.ReferenceEquals instead.
//   6. Other phase-1 restrictions applied here: no capture lambdas (rejected
//      by design), no instance-method binding (the runtime's
//      il2cpp_delegate_new always stamps target == nullptr), no
//      Delegate.Method (unwired), no "static field in string concat" (the
//      int.ToString() on the field address emits ldsflda, unsupported).
//
// Runs on .NET Framework 4.8 (the reference runtime). Every line of output
// is deterministic.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Delechain
    {
        // ---- pure handlers (silent) -------------------------------------
        private static int Inc(int x) { return x + 1; }
        private static int Dbl(int x) { return x * 2; }

        // ---- tracing handlers (call-order evidence) ---------------------
        private static int order = 0;
        private static int Trace1(int x) { order = order + 1; Console.WriteLine("t1@" + (order + 0)); return x + 1; }
        private static int Trace2(int x) { order = order + 1; Console.WriteLine("t2@" + (order + 0)); return x * 2; }
        private static int Trace3(int x) { order = order + 1; Console.WriteLine("t3@" + (order + 0)); return x + 3; }

        // ---- simple event pattern (field-backed, custom add/remove) -----
        private static Func<int, int> _changed;
        public static event Func<int, int> Changed
        {
            add { _changed = _changed + value; }
            remove { _changed = _changed - value; }
        }
        public static void Raise(int x)
        {
            if (ReferenceEquals(_changed, null)) { Console.WriteLine("raise=none"); return; }
            Console.WriteLine("raise=" + _changed(x));
        }

        public static void Main()
        {
            // ===== 1. single-cast delegate ===============================
            Func<int, int> f = Inc;
            Console.WriteLine("invoke=" + f(10));                            // invoke=11
            Console.WriteLine("len=" + f.GetInvocationList().Length);        // len=1
            Console.WriteLine("static=" + ReferenceEquals(f.Target, null));  // static=True
            f = Dbl;
            Console.WriteLine("rebind=" + f(10));                            // rebind=20

            // ===== 2. multicast: += chain, call order, -= removal ========
            order = 0;
            Func<int, int> t = Trace1;
            t += Trace2;
            t += Trace3;
            Console.WriteLine("mlen=" + t.GetInvocationList().Length);       // mlen=3
            // .NET multicast: every handler receives the ORIGINAL argument
            // (return values are not chained); the LAST handler's result wins.
            int tl = t(10);                       // t1@1 t2@2 t3@3; last = Trace3(10) = 13
            Console.WriteLine("tlast=" + tl);                                // tlast=13
            Console.WriteLine("torder=" + (order + 0));                      // torder=3

            t -= Trace2;                                                    // [Trace1, Trace3]
            Console.WriteLine("rmlen=" + t.GetInvocationList().Length);      // rmlen=2
            order = 0;
            tl = t(10);                            // t1@1 t3@2; last = Trace3(10) = 13
            Console.WriteLine("tlast=" + tl);                                // tlast=13
            Console.WriteLine("torder=" + (order + 0));                      // torder=2

            t -= Trace1;                                                    // [Trace3]
            t -= Trace3;                                                    // null
            Console.WriteLine("empty=" + ReferenceEquals(t, null));         // empty=True

            // ===== 3. delegate equality (structural) =====================
            Func<int, int> a = Inc;
            Func<int, int> b = Inc;
            Func<int, int> c = Dbl;
            Console.WriteLine("eqsame=" + a.Equals(b));                      // eqsame=True
            Console.WriteLine("eqdiff=" + a.Equals(c));                      // eqdiff=False
            Console.WriteLine("eqself=" + a.Equals(a));                      // eqself=True
            Console.WriteLine("eqnull=" + a.Equals(null));                   // eqnull=False
            Console.WriteLine("refdiff=" + ReferenceEquals(a, b));           // refdiff=False

            // ===== 4. event: subscribe, raise, unsubscribe ===============
            order = 0;
            Changed += Trace1;
            Changed += Trace2;
            Raise(100);                           // t1@1 t2@2 raise=200 (Trace2(100) last)
            Changed -= Trace1;
            Raise(100);                           // t2@3 raise=200 (order not reset here)
            Changed -= Trace2;
            Raise(100);                           // raise=none
        }
    }
}
