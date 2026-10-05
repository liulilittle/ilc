//------------------------------------------------------------------------------
// delegops.cs -- end-to-end reference case for DEEP System.Delegate
// operations (net48 oracle). Extends delegate2 (multicast basics, batch 22b
// unlocked get_Method wiring) and delegateinst (instance binding, batch 21)
// with a combined operations matrix:
//
//   * INSTANCE binding: obj.Method bound to Func<int,int>, invoke reads the
//     bound instance's fields; Target == the bound instance; two instances
//     of the same method are NOT equal (different targets);
//   * MULTICAST combine/remove: instance + static chains, + operator, -=
//     removes the LAST occurrence, GetInvocationList().Length, invocation
//     order (last result wins);
//   * STATIC binding: static-method delegate, Target == null, invoke is the
//     method result;
//   * GENERIC delegates: System.Func<int,int> and System.Action<int>
//     (invoked with an argument; multicast Action chain runs in order);
//   * GetMethodInfo: f.Method.Name -- RECORDED GAP (see below);
//   * EQUALITY: same target+method ==, different target !=, multicast list
//     equality (same list, new object ==), order matters (!=).
//
// RECORDED GAPS (exercised with try/catch so the case still runs; the
// divergence IS the recorded defect -- batch rule: record, do NOT fix):
//   * Delegate.Method.Name: the transpiler emits icall_delegate_get_method_name
//     but NEVER emits il2cpp_delegate_register_method calls in generated
//     startup code, so the runtime name registry is EMPTY -> every lookup
//     throws InvalidOperationException (net48 returns the SHORT method
//     name). The probes print "IOE" on the runtime path vs the real name
//     on net48 -- the diff pinpoints the gap.
//   * NOT EXERCISED (build breakers / no runtime surface): non-generic
//     System.Action (castclass needs System_Action_class descriptor, which
//     does not exist -- Action<int> uses the generic identity cast and is
//     safe); try { delegateInvoke(); } with a DISCARDED result (DCE drops
//     the invoke -> the EH dance emits a catch without a try -- transpiler
//     EH defect, delegate2.cs:85 hit it at HEAD).
//
// WIRED surface (MethodMap.Delegate.cs -> delegateext.cpp): Combine/Remove/
// op_Addition/op_Subtraction/op_Equality/op_Inequality/GetInvocationList/
// get_Target/get_Method/Equals; icall_multicast_invoke_i4 for int32-return
// delegates.
//
// Every line is deterministic; net48 is the reference baseline
// (run-tests.ps1 diffs byte-for-byte). Lines that diverge expose runtime
// defects (recorded, NOT fixed).
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Delegops {
    public class Counter {
        public int Value;
        public Counter(int v) { Value = v; }
        public int Add(int x) { return Value + x; }
        public int Mul(int x) { return Value * x; }
    }

    private static int Stat(int x) { return x + 100; }
    private static int order = 0;

    public static void Main() {
        // ============ 1. instance binding ============
        Counter c = new Counter(10);
        Func<int, int> inst = c.Add;
        Console.WriteLine("i0=" + (inst(5) == 15));      // 10 + 5
        Console.WriteLine("i1=" + (inst(1) == 11));      // 10 + 1
        Console.WriteLine("i2=" + ((inst.Target == null) == false)); // instance target != null
        Console.WriteLine("i3=" + ReferenceEquals(inst.Target, c)); // target == the instance
        Counter c2 = new Counter(20);
        Func<int, int> other = c2.Add;
        Console.WriteLine("i4=" + (other(5) == 25));     // 20 + 5
        Console.WriteLine("i5=" + inst.Equals(other));   // different target -> False
        Console.WriteLine("i6=" + inst.Equals((Func<int, int>)c.Add));   // same target+method -> True
        Console.WriteLine("i7=" + inst.Equals(inst));    // self -> True

        // ============ 2. static binding ============
        Func<int, int> st = Stat;
        Console.WriteLine("s0=" + (st.Target == null));  // static -> null target
        Console.WriteLine("s1=" + (st(1) == 101));       // 1 + 100
        Console.WriteLine("s2=" + inst.Equals(st));      // instance vs static -> False

        // ============ 3. multicast combine/remove ============
        order = 0;
        Func<int, int> chain = inst + st;                // [c.Add, Stat]
        Console.WriteLine("m0=" + (chain.GetInvocationList().Length == 2));
        Console.WriteLine("m1=" + (chain(5) == 105));    // Stat last: 5+100
        chain += st;                                     // [c.Add, Stat, Stat]
        Console.WriteLine("m2=" + (chain.GetInvocationList().Length == 3));
        Console.WriteLine("m3=" + (chain(1) == 101));    // Stat last
        chain -= st;                                     // remove LAST Stat -> [c.Add, Stat]
        Console.WriteLine("m4=" + (chain.GetInvocationList().Length == 2));
        Console.WriteLine("m5=" + (chain(5) == 105));
        Func<int, int> chain2 = inst + st;
        Console.WriteLine("m6=" + chain.Equals(chain2)); // same list (new object) -> True
        Console.WriteLine("m7=" + chain.Equals(st + inst)); // order matters -> False
        Func<int, int> rmAll = inst + st;
        rmAll -= inst;
        rmAll -= st;
        Console.WriteLine("m8=" + (rmAll == null));      // unwound -> null

        // ============ 4. generic delegates Func/Action ============
        Action<int> act = (x) => { order = order + x; };
        Console.WriteLine("g0=" + (act.GetInvocationList().Length == 1));
        act(3);
        Console.WriteLine("g1=" + (order == 3));
        Action<int> act2 = (x) => { order = order + x * 10; };
        act += act2;
        Console.WriteLine("g2=" + (act.GetInvocationList().Length == 2));
        act(2);
        Console.WriteLine("g3=" + (order == 25));        // 3 + 2 + 20
        Func<int, int> mul = c.Mul;
        Console.WriteLine("g4=" + (mul(3) == 30));       // 10 * 3
        Console.WriteLine("g5=" + inst.Equals(mul));     // same target, diff method -> False

        // ============ 5. GetMethodInfo: RECORDED GAP (try/catch) ============
        string nm = "?";
        string nme = "none";
        try { nm = inst.Method.Name; } catch (Exception e) { nme = e.GetType().Name; }
        Console.WriteLine("n0=" + (nme == "none" ? nm : "IOE"));
        string sn = "?";
        string sne = "none";
        try { sn = st.Method.Name; } catch (Exception e) { sne = e.GetType().Name; }
        Console.WriteLine("n1=" + (sne == "none" ? sn : "IOE"));
    }
}}
