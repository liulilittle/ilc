//------------------------------------------------------------------------------
// delecb2.cs -- end-to-end reference case for DELEGATE DEEP-CHAIN forms:
// the transpiler-safe Func`1 fast path from delegateinst, extended with the
// CALLBACK-PARAMETER / CLOSURE shapes:
//   1. static-method delegate passed as a method parameter (higher-order Apply)
//   2. instance-method delegate passed as a parameter -- the bound target
//      travels through a call boundary and its side effect is visible at the
//      caller (delegateinst proved target binding; here it crosses a frame)
//   3. multicast chain passed as a parameter (all leaves run in order, the
//      last leaf's result wins)
//   4. delegate returned from a method -- closure shape: a lambda capturing a
//      method parameter (the transpiler has no closure-class model yet)
//   5. closure delegate mixed into a chain, invoked across a call boundary
//
// KNOWN TRANSPILER GAP (documented, same root as delegateinst.cs): StackFold's
// delegate newobj proxy extracts only the FuncPtrExpr and silently DROPS the
// instance target; closures (captured variables, compiler-generated classes)
// are not modeled at all. The case turns green once the transpiler passes the
// target through (2-arg emission) and emits closure classes.
//
// TRANSPILER-SAFE SUBSET: Func`1 delegates only (the icall_multicast_invoke_i4
// fast path: int32 return, <=4 int32 args); instance objects/fields/methods
// (propcase precedent); every output line is a bool comparison, so the
// reference output is fully deterministic and hand-verifiable.
//
// Runs on .NET Framework 4.8 (the reference runtime).
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

    public static class Delecb2
    {
        private static int Inc(int x) { return x + 1; }
        private static int Dbl(int x) { return x * 2; }

        // 1/2/3/5: a method that RECEIVES a delegate and invokes it
        private static int Apply(Func<int, int> f, int v) { return f(v); }

        // 4: a method that RETURNS a delegate -- closure over its parameter
        private static Func<int, int> MakeAdder(int n)
        {
            return x => x + n;
        }

        public static void Main()
        {
            // ===== 1. static-method delegate passed as a parameter =====
            Console.WriteLine("appinc=" + (Apply(Inc, 5) == 6));    // Inc(5) = 6
            Console.WriteLine("appdbl=" + (Apply(Dbl, 5) == 10));   // Dbl(5) = 10

            // ===== 2. instance-method delegate passed as a parameter =====
            Accum a = new Accum(10);
            Console.WriteLine("appinst=" + (Apply(a.Add, 5) == 15)); // a.Add(5): 10+5, reads a.Total
            Console.WriteLine("accstate=" + (a.Total == 15));        // side effect visible at caller

            // ===== 3. multicast chain passed as a parameter =====
            Func<int, int> chain = Inc;
            chain += Dbl;
            Console.WriteLine("chainlen=" + (chain.GetInvocationList().Length == 2));
            Console.WriteLine("chainarg=" + (Apply(chain, 5) == 12)); // Inc(5)=6, Dbl(6)=12; last wins

            // ===== 4. delegate returned from a method (closure over parameter) =====
            Func<int, int> add10 = MakeAdder(10);
            Console.WriteLine("adder=" + (add10(5) == 15));           // 5 + 10
            Func<int, int> add100 = MakeAdder(100);
            Console.WriteLine("adder100=" + (add100(5) == 105));      // 5 + 100
            Console.WriteLine("adderfresh=" + (!ReferenceEquals(add10, add100))); // distinct closure instances

            // ===== 5. closure delegate in a chain, invoked across a call boundary =====
            Func<int, int> mix = add10;
            mix += Inc;
            Console.WriteLine("mixlen=" + (mix.GetInvocationList().Length == 2));
            Console.WriteLine("mixarg=" + (Apply(mix, 5) == 16));    // add10(5)=15, Inc(15)=16
        }
    }
}
