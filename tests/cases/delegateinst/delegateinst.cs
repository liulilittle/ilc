//------------------------------------------------------------------------------
// delegateinst.cs -- end-to-end reference case for INSTANCE-METHOD delegate
// binding: obj.Method bound to a delegate, invoke reads the bound instance's
// fields, multicast chains mixing instance + static leaves (call order and
// last-result semantics), -= removal of an instance leaf, Delegate.Target
// (the bound instance) and structural equality (same target + method).
//
// The RUNTIME already implements target binding:
//   * il2cpp_delegate_new(methodPtr, target)        [delegate.cpp]
//   * icall_multicast_invoke_i4 dispatches instance leaves as
//     (target, a0, ...) per leaf                    [delegateext.cpp:259]
//   * icall_delegate_get_target returns the leaf's instance target
//   * leaf equality = same methodPtr AND same target [delegateext.cpp:75]
//
// KNOWN TRANSPILER GAP (the case deliberately covers it; expected.txt is the
// net48 reference and documents the INTENDED semantics): StackFold's delegate
// newobj proxy (StackFold.cs ~:1704) extracts only the FuncPtrExpr and
// silently DROPS the instance target; CodeGenerator emits the single-arg
// il2cpp_delegate_new(&<method>) (CodeGenerator.cs ~:1333). An instance-bound
// delegate therefore arrives at the runtime with target == null, and the
// invoke side calls the generated instance method (ABI {ret} M(Il2CppObject*
// this, ...)) as if it were static -- wrong `this`, crash or wrong result.
// The case turns green once the transpiler passes the target through
// (2-arg emission, the documented M3.5 shape).
//
// TRANSPILER-SAFE SUBSET: Func`N delegates only (the icall_multicast_invoke_i4
// fast path: int32 return, <=4 int32 args); instance objects/fields/methods
// (propcase precedent); every output line is a bool comparison, so the
// reference output is fully deterministic and hand-verifiable.
//
// Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public class Counter
    {
        public int Value;
        public static int order = 0;         // shared by instance + static traces

        public Counter(int v) { Value = v; }

        // plain instance method: reads its own instance field
        public int Add(int x) { return Value + x; }

        // traced instance method: proves this leaf ran FIRST in a mixed chain
        public int TraceAdd(int x)
        {
            order = order + 1;
            Console.WriteLine("ti=" + (order == 1));
            return Value + x;
        }
    }

    public static class Delegateinst
    {
        private static int Inc(int x) { return x + 1; }

        // traced static method: proves this leaf ran SECOND in a mixed chain
        private static int TraceInc(int x)
        {
            Counter.order = Counter.order + 1;
            Console.WriteLine("ts=" + (Counter.order == 2));
            return x + 1;
        }

        public static void Main()
        {
            // ===== 1. instance binding: obj.Method -> invoke reads fields =====
            Counter c = new Counter(10);
            Func<int, int> f = c.Add;
            Console.WriteLine("invoke=" + (f(5) == 15));        // invoke=True  (10+5)
            Counter d = new Counter(20);
            Func<int, int> g = d.Add;
            Console.WriteLine("invoke2=" + (g(5) == 25));       // invoke2=True (20+5)

            // ===== 2. GetTarget: the bound instance (net48 returns object) ====
            Console.WriteLine("target=" + ReferenceEquals(f.Target, c)); // target=True
            Func<int, int> s = Inc;                             // static-method delegate
            Console.WriteLine("target0=" + (s.Target == null)); // target0=True
            Console.WriteLine("targetnull=" + (f.Target == null)); // targetnull=False

            // ===== 3. equality: same target + method =====
            Func<int, int> f2 = c.Add;
            Console.WriteLine("eqsame=" + f.Equals(f2));        // eqsame=True (same instance)
            Console.WriteLine("eqself=" + f.Equals(f));         // eqself=True
            Console.WriteLine("eqdiff=" + f.Equals(g));         // eqdiff=False (different instance)
            Console.WriteLine("eqstatic=" + f.Equals(s));       // eqstatic=False (instance vs static)
            Counter c2 = new Counter(10);                       // same Value, different instance
            Func<int, int> f3 = c2.Add;
            Console.WriteLine("eqval=" + f.Equals(f3));         // eqval=False (method same, target differs)

            // ===== 4. mixed multicast: instance + static, order + last =====
            Counter.order = 0;
            Func<int, int> m = c.TraceAdd;                      // instance leaf (c.Value = 10)
            m += TraceInc;                                      // static leaf
            Console.WriteLine("mlen=" + (m.GetInvocationList().Length == 2)); // mlen=True
            int r = m(10);  // ti=True (instance first), ts=True (static second); last = TraceInc(10) = 11
            Console.WriteLine("mlast=" + (r == 11));            // mlast=True

            // ===== 5. -= removes the instance leaf from a mixed chain =====
            Func<int, int> mix = c.TraceAdd;
            mix += Inc;                                         // untraced static leaf
            Console.WriteLine("mixlen=" + (mix.GetInvocationList().Length == 2)); // mixlen=True
            Counter.order = 0;
            r = mix(10);                                        // ti=True (instance first), then Inc; last = 11
            Console.WriteLine("mixlast=" + (r == 11));          // mixlast=True
            Console.WriteLine("mixorder=" + (Counter.order == 1)); // mixorder=True (only ti ran)
            Func<int, int> instDel = c.TraceAdd;
            mix -= instDel;                                     // [c.TraceAdd, Inc] - [c.TraceAdd] = [Inc]
            Console.WriteLine("mixrm=" + (mix.GetInvocationList().Length == 1)); // mixrm=True
            r = mix(10);                                        // Inc alone
            Console.WriteLine("mixrmlast=" + (r == 11));        // mixrmlast=True

            // ===== 6. -= from a pure-instance chain; surviving leaf still
            //          reads ITS OWN instance field =====
            Func<int, int> p = c.Add;                           // c.Value = 10
            p += g;                                             // g = d.Add (d.Value = 20)
            Console.WriteLine("plen=" + (p.GetInvocationList().Length == 2)); // plen=True
            r = p(5);                                           // last = d.Add(5) = 25
            Console.WriteLine("plast=" + (r == 25));            // plast=True
            p -= f;                                             // remove c.Add -> [d.Add]
            Console.WriteLine("prm=" + (p.GetInvocationList().Length == 1)); // prm=True
            r = p(5);                                           // remaining leaf still reads d.Value
            Console.WriteLine("prem=" + (r == 25));             // prem=True
        }
    }
}
