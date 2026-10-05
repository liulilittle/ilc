//------------------------------------------------------------------------------
// eventdeep.cs -- EVENT DEEP probe (e2e batch C55, "事件深测: 事件模式/订阅/
// 多播组合 -- delegate 域后"). Builds on the delegate-domain surface
// (delegatechain C41: multicast + middle removal + event pattern; delegateops
// C49: Combine/Remove matrix + event subscription; mdievt C26: per-child
// instance events over a WinForms window) and goes DEEPER on the EVENT axis
// in a PURE-MANAGED model -- byte-exact vs the net48 oracle, dual-compiler
// (mingw + msvc).
//
// Every probe is a labeled bool / sequence string: a diff pinpoints the
// flipped line.
//
// Wired surface exercised (MethodMap.Delegate.cs + delegateext.cpp + the
// field-backed event pattern):
//   * event keyword with CUSTOM add/remove accessors over a delegate field
//     (the _changed/_tick pattern -- Combine/Remove + GetInvocationList on
//     the backing field);
//   * int Fn(int) delegates -> icall_multicast_invoke_i4 (the i4 fast path:
//     SUBSCRIBE A,B,C -> raise runs in order, LAST result wins);
//   * void VoidFn() delegates -> the same multicast invoke (mdievt
//     precedent: per-instance events with multiple void handlers);
//   * STATIC event + INSTANCE events on one class, per-instance isolation;
//   * SNAPSHOT REMOVE-DURING-RAISE: the field is read ONCE at raise start
//     (multicast delegates are immutable composition trees) -- a handler
//     that unsubscribes another does NOT stop it firing in the CURRENT
//     raise, but the NEXT raise skips it;
//   * CROSS-EVENT independence: two events, separate handler lists, raising
//     one never fires the other's handlers;
//   * INSTANCE-BOUND handler on the event (target state mutates per raise);
//   * handler that THROWS aborts the remaining handlers (caught at the
//     raise site).
//
// Determinism: pure managed, single-threaded, no timing. Expected values in
// trailing comments.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public class Accum
    {
        public int Total;
        public Accum(int t) { Total = t; }
        public int Add(int x) { Total = Total + x; return Total; }
    }

    public class TickSource
    {
        public delegate void VoidFn();
        private VoidFn _tick;
        public event VoidFn Tick
        {
            add { _tick = _tick + value; }
            remove { _tick = _tick - value; }
        }
        public int TickCount
        {
            get { return _tick == null ? 0 : _tick.GetInvocationList().Length; }
        }
        public void RaiseTick() { if (_tick != null) _tick(); }
    }

    public static class EventDeep
    {
        public delegate int Fn(int x);

        // ---- shared observable state ----
        private static int order = 0;
        private static string seq = "";
        private static void ResetAll() { order = 0; seq = ""; }

        // ---- handler leaves (int-returning) ----
        private static int A(int x) { order++; seq = seq + "A;"; return x + 1; }
        private static int B(int x) { order++; seq = seq + "B;"; return x * 10; }
        private static int C(int x) { order++; seq = seq + "C;"; return x - 5; }
        private static int D(int x) { order++; seq = seq + "D;"; return x + 100; }
        private static int Thrower(int x) { if (x >= 0) throw new Exception("boom"); return 0; }

        // ---- handler leaves (void) ----
        private static void S1() { seq = seq + "S1;"; }
        private static void S2() { seq = seq + "S2;"; }
        private static void S3() { seq = seq + "S3;"; }

        // ==================================================================
        // STATIC event (int-returning) -- field-backed custom add/remove.
        // ==================================================================
        private static Fn _changed;
        public static event Fn Changed
        {
            add { _changed = _changed + value; }
            remove { _changed = _changed - value; }
        }

        // Snapshot raise: read the field ONCE into a local, then invoke the
        // immutable composition tree (net48 field-like event semantics).
        private static int RaiseChanged(int x)
        {
            Fn v = _changed;
            if (v == null) return -1;
            return v(x);
        }

        private static int ChangedCount
        {
            get { return _changed == null ? 0 : _changed.GetInvocationList().Length; }
        }

        // ==================================================================
        // STATIC event #2 (Alt) -- CROSS-EVENT independence probe.
        // ==================================================================
        private static Fn _alt;
        public static event Fn Alt
        {
            add { _alt = _alt + value; }
            remove { _alt = _alt - value; }
        }
        private static int RaiseAlt(int x)
        {
            Fn v = _alt;
            if (v == null) return -1;
            return v(x);
        }
        private static int AltCount
        {
            get { return _alt == null ? 0 : _alt.GetInvocationList().Length; }
        }

        // a handler that REMOVES handler C from the static event during a
        // raise (snapshot probe)
        private static int RemoveC(int x) { Changed -= C; return 1; }

        public static void Main()
        {
            Fn a = A, b = B, c = C, d = D;

            // ============ 1. static event: subscribe 3 -> raise -> remove ============
            Changed += a; Changed += b; Changed += c;
            Console.WriteLine("s1_count=" + (ChangedCount == 3));
            ResetAll();
            Console.WriteLine("s1_last=" + (RaiseChanged(1) == -4));   // A,B,C; C(1)=-4
            Console.WriteLine("s1_seq=" + seq);                       // A;B;C;
            Console.WriteLine("s1_order=" + (order == 3));

            Changed -= b;                                              // remove MIDDLE
            ResetAll();
            Console.WriteLine("s1_rmid_last=" + (RaiseChanged(1) == -4)); // A,C
            Console.WriteLine("s1_rmid_seq=" + seq);                   // A;C;
            Console.WriteLine("s1_rmid_count=" + (ChangedCount == 2));

            Changed -= a; Changed -= c;
            Console.WriteLine("s1_empty=" + (RaiseChanged(1) == -1)); // none
            Console.WriteLine("s1_empty_count=" + (ChangedCount == 0));

            Changed += b;                                              // re-subscribe
            ResetAll();
            Console.WriteLine("s1_resub=" + (RaiseChanged(5) == 50)); // B(5)=50
            Console.WriteLine("s1_resub_seq=" + seq);                 // B;
            Changed -= b;

            // ============ 2. instance void event: per-instance isolation ============
            TickSource w1 = new TickSource();
            TickSource w2 = new TickSource();
            w1.Tick += S1; w1.Tick += S2;
            w2.Tick += S1;
            Console.WriteLine("s2_w1_count=" + (w1.TickCount == 2));
            Console.WriteLine("s2_w2_count=" + (w2.TickCount == 1));
            ResetAll();
            w1.RaiseTick();
            Console.WriteLine("s2_w1_seq=" + seq);                    // S1;S2;
            ResetAll();
            w2.RaiseTick();
            Console.WriteLine("s2_w2_seq=" + seq);                    // S1;
            w1.Tick -= S2;                                            // remove from w1 only
            Console.WriteLine("s2_w1_count2=" + (w1.TickCount == 1));
            Console.WriteLine("s2_w2_count2=" + (w2.TickCount == 1));
            ResetAll();
            w1.RaiseTick();
            Console.WriteLine("s2_w1_seq2=" + seq);                   // S1;
            w1.Tick -= S1; w2.Tick -= S1;
            Console.WriteLine("s2_w1_empty=" + (w1.TickCount == 0));
            Console.WriteLine("s2_w2_empty=" + (w2.TickCount == 0));

            // ============ 3. snapshot: remove-during-raise ============
            // [A,B,C,RemoveC]: the field is read once -> the CURRENT raise
            // still fires C (immutable tree); the NEXT raise skips it.
            Changed += a; Changed += b; Changed += c; Changed += RemoveC;
            ResetAll();
            Console.WriteLine("s3_last=" + (RaiseChanged(1) == 1));   // RemoveC returns 1 (last)
            Console.WriteLine("s3_seq=" + seq);                       // A;B;C;R;
            ResetAll();
            Console.WriteLine("s3_count=" + (ChangedCount == 3));     // C removed
            Console.WriteLine("s3_2nd_last=" + (RaiseChanged(1) == 1)); // A,B,RemoveC
            Console.WriteLine("s3_2nd_seq=" + seq);                   // A;B;R;
            Changed -= a; Changed -= b; Changed -= RemoveC;

            // ============ 4. cross-event independence (Changed vs Alt) ============
            Changed += a; Changed += b;
            Alt += c; Alt += d;
            Console.WriteLine("s4_changed_count=" + (ChangedCount == 2));
            Console.WriteLine("s4_alt_count=" + (AltCount == 2));
            ResetAll();
            Console.WriteLine("s4_changed_last=" + (RaiseChanged(1) == 10)); // B(1)=10
            Console.WriteLine("s4_changed_seq=" + seq);               // A;B;
            ResetAll();
            Console.WriteLine("s4_alt_last=" + (RaiseAlt(1) == 101)); // D(1)=101
            Console.WriteLine("s4_alt_seq=" + seq);                   // C;D;
            Changed -= a; Changed -= b; Alt -= c; Alt -= d;

            // ============ 5. instance-bound handler on the static event ============
            Accum acc = new Accum(100);
            Changed += acc.Add;
            ResetAll();
            Console.WriteLine("s5_inst_last=" + (RaiseChanged(5) == 105)); // acc.Add(5)=105
            Console.WriteLine("s5_inst_state=" + (acc.Total == 105));
            Console.WriteLine("s5_inst_seq=" + seq);                  // (acc.Add appends nothing)
            Changed -= acc.Add;
            Console.WriteLine("s5_inst_removed=" + (RaiseChanged(1) == -1));

            // instance-bound handler MIXED with a static leaf (order + last-wins)
            Changed += acc.Add;
            Changed += A;
            ResetAll();
            Console.WriteLine("s5_mix_last=" + (RaiseChanged(1) == 2));  // acc.Add(1)=101, A(1)=2
            Console.WriteLine("s5_mix_order=" + (order == 1));           // A ran once
            Console.WriteLine("s5_mix_state=" + (acc.Total == 106));     // 105 + 1
            Changed -= acc.Add; Changed -= A;

            // ============ 6. handler that throws aborts the rest ============
            Changed += a; Changed += Thrower; Changed += b;
            ResetAll();
            bool threw = false;
            try { RaiseChanged(1); } catch (Exception) { threw = true; }
            Console.WriteLine("s6_threw=" + threw);                   // True
            Console.WriteLine("s6_seq=" + seq);                       // A; (B skipped)
            Console.WriteLine("s6_count=" + (ChangedCount == 3));     // chain intact
            Changed -= a; Changed -= Thrower; Changed -= b;
            Console.WriteLine("s6_empty=" + (RaiseChanged(1) == -1));
        }
    }
}
