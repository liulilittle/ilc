//------------------------------------------------------------------------------
// delegatechain.cs -- DELEGATE CHAIN DEEP layer 2 (batch C41). net48 oracle
// baseline. Extends delechain/multicast2/delecb2 over the transpiler-safe
// custom-delegate surface (ANY MulticastDelegate-derived type; the
// icall_multicast_invoke_i4 fast path -- int32 return, <=4 int32 args).
// Three deeper axes:
//
//   1. MULTICAST + MIDDLE REMOVAL (多播+移除中间): chains built by += and by
//      Delegate.Combine/Remove; removing the MIDDLE leaf of a 3-chain keeps
//      [first, last] with call order preserved; removing FIRST/LAST leaves;
//      removing an ABSENT leaf returns the SAME object (identity -- net48
//      Delegate.Remove never copies); removing the whole chain -> null;
//      Combine null rules (null+a == a == a+null; Remove(x,null) == x).
//   2. EVENT PATTERN (事件模式): a field-backed custom event with add/remove;
//      subscribe 3 -> raise (call order + last-wins) -> remove the MIDDLE
//      handler -> raise (order evidence) -> unsubscribe all -> "raise=none";
//      re-subscribe rebuilds the chain. An instance-bound leaf joins a chain.
//   3. EXCEPTION (异常): [A, THROW, B] aborts at the first throwing leaf (B
//      skipped, order probe); [THROW, A] runs nothing; [A, B, THROW] runs
//      both; the chain is IMMUTABLE after the throw (still 3 leaves); a
//      throws-once leaf re-invokes from the start; an EVENT handler that
//      throws aborts the remaining handlers and is caught at the raise site.
//
// MSVC C4716 workaround (multicast2 precedent): the throwing non-void leaf
// has a trailing `return 0;` (unreachable) so the generated C++ has a
// return on every path.
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

    public static class DelegateChain
    {
        public delegate int ChainFn(int x);

        private static int order = 0;
        private static bool onceFired = false;
        private static void ResetOrder() { order = 0; }

        private static int A(int x) { order++; Console.WriteLine("A:" + order); return x + 1; }
        private static int B(int x) { order++; Console.WriteLine("B:" + order); return x * 10; }
        private static int C(int x) { order++; Console.WriteLine("C:" + order); return x - 5; }
        private static int D(int x) { order++; Console.WriteLine("D:" + order); return x + 100; }
        private static int Thrower(int x) { if (x >= 0) throw new Exception("boom"); return 0; }
        private static int ThrowsOnce(int x)
        {
            if (!onceFired) { onceFired = true; throw new Exception("once"); }
            return 7;
        }

        // ---- event pattern (field-backed, custom add/remove) ----
        private static ChainFn _changed;
        public static event ChainFn Changed
        {
            add { _changed = _changed + value; }
            remove { _changed = _changed - value; }
        }
        private static int Raise(int x)
        {
            if (ReferenceEquals(_changed, null)) { Console.WriteLine("raise=none"); return -1; }
            return _changed(x);
        }

        public static void Main()
        {
            // ============ 1. multicast + middle removal ============
            ChainFn a = A, b = B, c = C, d = D;
            ChainFn abc = a + b + c;
            Console.WriteLine("abc len: " + (abc.GetInvocationList().Length == 3));
            ResetOrder();
            Console.WriteLine("abc last: " + (abc(1) == -4));   // A:1 B:2 C:3; C(1)=1-5=-4
            Console.WriteLine("abc order: " + (order == 3));

            ChainFn ac = abc - b;                              // remove MIDDLE leaf
            Console.WriteLine("ac len: " + (ac.GetInvocationList().Length == 2));
            ResetOrder();
            Console.WriteLine("ac last: " + (ac(1) == -4));    // A:1 C:2; last wins = C(1)=-4
            Console.WriteLine("ac order: " + (order == 2));
            Console.WriteLine("ac[0] is a: " + object.ReferenceEquals(ac.GetInvocationList()[0], a));
            Console.WriteLine("ac[1] is c: " + object.ReferenceEquals(ac.GetInvocationList()[1], c));

            ChainFn bc = abc - a;                              // remove FIRST leaf
            Console.WriteLine("bc len: " + (bc.GetInvocationList().Length == 2));
            ResetOrder();
            Console.WriteLine("bc last: " + (bc(1) == -4));    // B:1 C:2; last = C(1)=-4

            ChainFn ab = abc - c;                              // remove LAST leaf
            Console.WriteLine("ab len: " + (ab.GetInvocationList().Length == 2));
            ResetOrder();
            Console.WriteLine("ab last: " + (ab(1) == 10));    // A:1 B:2; last = B(1)=10

            // nested chain, remove a middle leaf across tree levels
            ChainFn nested = (a + b) + (c + d);
            Console.WriteLine("nested len: " + (nested.GetInvocationList().Length == 4));
            ChainFn nb = nested - b;
            Console.WriteLine("nb len: " + (nb.GetInvocationList().Length == 3));
            ResetOrder();
            nb(1);                                            // A:1 C:2 D:3
            Console.WriteLine("nb order: " + (order == 3));
            Console.WriteLine("nb[1] is c: " + object.ReferenceEquals(nb.GetInvocationList()[1], c));

            // absent removal: identity (no copy), chain unchanged
            ChainFn absent = abc - d;
            Console.WriteLine("absent identity: " + object.ReferenceEquals(absent, abc));
            Console.WriteLine("absent len: " + (absent.GetInvocationList().Length == 3));

            // remove the whole chain -> null; remove-all loop via chained ops
            Console.WriteLine("whole null: " + ((a + b + c) - (a + b + c) == null));
            Console.WriteLine("remove-all null: " + ((a + b) - a - b == null));
            Console.WriteLine("pair-b left: " + (((a + b) - b).GetInvocationList().Length == 1));

            // Delegate.Combine / Remove null rules
            Console.WriteLine("combine null-l: " + object.ReferenceEquals(Delegate.Combine(null, a), a));
            Console.WriteLine("combine null-r: " + object.ReferenceEquals(Delegate.Combine(a, null), a));
            Console.WriteLine("remove null: " + object.ReferenceEquals(Delegate.Remove(abc, null), abc));

            // instance-bound leaf joins a chain (delecb2 precedent)
            Accum acc = new Accum(100);
            ChainFn inst = acc.Add;
            Console.WriteLine("inst bind: " + (inst(5) == 105));
            Console.WriteLine("inst state: " + (acc.Total == 105));
            ChainFn mix = inst + A;
            ResetOrder();
            Console.WriteLine("mix last: " + (mix(1) == 2));   // acc.Add(1)=101, A(1)=2
            Console.WriteLine("mix order: " + (order == 1));

            // ============ 2. event pattern ============
            order = 0;
            Changed += A;
            Changed += B;
            Changed += C;
            ResetOrder();
            Console.WriteLine("raise 3 last: " + (Raise(1) == -4));   // A:1 B:2 C:3
            Console.WriteLine("raise 3 order: " + (order == 3));
            Changed -= B;                                        // remove MIDDLE handler
            ResetOrder();
            Console.WriteLine("raise 2 last: " + (Raise(1) == -4));   // A:1 C:2
            Console.WriteLine("raise 2 order: " + (order == 2));
            Changed -= A;
            Changed -= C;
            Console.WriteLine("raise none: " + (Raise(1) == -1));      // raise=none
            Changed += B;                                        // re-subscribe
            ResetOrder();
            Console.WriteLine("raise resub: " + (Raise(5) == 50));     // B(5)=50
            Changed -= B;

            // ============ 3. exception ============
            ChainFn t = A; t += Thrower; t += B;
            ResetOrder();
            try { t(1); } catch (Exception) { Console.WriteLine("mid threw"); }
            Console.WriteLine("mid order: " + (order == 1));           // B skipped
            ChainFn t2 = Thrower; t2 += A;
            ResetOrder();
            try { t2(1); } catch (Exception) { Console.WriteLine("first threw"); }
            Console.WriteLine("first order: " + (order == 0));
            ChainFn t3 = A; t3 += B; t3 += Thrower;
            ResetOrder();
            try { t3(1); } catch (Exception) { Console.WriteLine("last threw"); }
            Console.WriteLine("last order: " + (order == 2));
            Console.WriteLine("chain intact: " + (t.GetInvocationList().Length == 3));
            // throws-once leaf: the SAME chain re-invokes from the start
            ChainFn once = A; once += ThrowsOnce; once += B;
            ResetOrder();
            onceFired = false;
            try { once(1); } catch (Exception) { Console.WriteLine("once threw"); }
            Console.WriteLine("once order: " + (order == 1));
            Console.WriteLine("once last: " + (once(1) == 10));        // A:2 ThrowsOnce:7 B:3
            Console.WriteLine("once order2: " + (order == 3));
            // event handler throws -> aborts remaining, caught at raise site
            Changed += A;
            Changed += Thrower;
            Changed += C;
            ResetOrder();
            try { Raise(1); } catch (Exception) { Console.WriteLine("evt threw"); }
            Console.WriteLine("evt order: " + (order == 1));           // C skipped
        }
    }
}
