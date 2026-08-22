//------------------------------------------------------------------------------
// delhashprobe.cs -- net48 ground truth for delegateext BUG-2 / BUG-3.
//
// BUG-2: Delegate.Combine across ABI-incompatible delegate shapes.
//   * Combine(d1, d2) with DIFFERENT signature delegate types -> does net48
//     throw ArgumentException ("Cannot bind to the target method because its
//     signature is not compatible with that of the delegate type")?
//   * Combine via the STATIC Delegate.Combine(Delegate, Delegate) with two
//     DIFFERENT delegate types -> throws?
//   * Combine with two delegates of the SAME signature but DIFFERENT named
//     delegate types -> OK or throw?
//
// BUG-3: Delegate.GetHashCode semantics.
//   * Are two structurally-equal delegates (same method, same target) hash-equal?
//   * Is the hash derived from method+target (structural) rather than the
//     object address (non-deterministic across allocations)?
//   * Does a multicast chain's hash change after Combine? (net48 hashes the
//     flattened invocation list.)
//------------------------------------------------------------------------------
using System;

namespace DelHashProbe
{
    public static class Program
    {
        static int A(int x) { return x + 1; }
        static int B(int x) { return x * 10; }
        static string S(string s) { return s + "!"; }
        static void V(int x) { }

        static void Line(string s) { Console.WriteLine(s); }

        static void TryCombine(string desc, Delegate left, Delegate right)
        {
            try
            {
                Delegate result = Delegate.Combine(left, right);
                Line($"{desc}: OK len={(result == null ? 0 : result.GetInvocationList().Length)}");
            }
            catch (ArgumentException ex)
            {
                Line($"{desc}: ArgumentException {ex.Message}");
            }
            catch (Exception ex)
            {
                Line($"{desc}: {ex.GetType().Name} {ex.Message}");
            }
        }

        public static void Main()
        {
            // --- BUG-2 probes ---

            // 2a. Same named type, same signature: Func<int,int> + Func<int,int>.
            Func<int, int> fa = A, fb = B;
            TryCombine("same type Func<int,int>", fa, fb);

            // 2b. Static Delegate.Combine with two DIFFERENT delegate types
            // whose signatures are ALSO different: Func<int,int> + Func<string,string>.
            Func<string, string> fs = S;
            TryCombine("Delegate.Combine(Func<int,int>, Func<string,string>)", fa, fs);

            // 2c. Same SIGNATURE but DIFFERENT named delegate types: Func<int,int>
            // + a custom delegate int M(int).
            D2<int, int> custom = delegate (int x) { return x - 1; };
            TryCombine("Delegate.Combine(Func<int,int>, custom<int,int>)", fa, custom);

            // 2d. The '+' operator (compile-time type identical): Func<int,int> + Func<int,int>
            try
            {
                Func<int, int> sum = fa + fb;
                Line("op + same type: OK len=" + sum.GetInvocationList().Length);
            }
            catch (Exception ex)
            {
                Line("op + same type: " + ex.GetType().Name);
            }

            // 2e. Delegate.Combine(null, d) and (d, null) stay valid.
            TryCombine("Combine(null, fa)", null, fa);
            TryCombine("Combine(fa, null)", fa, null);

            // 2f. Action<int> (void return, different signature) + Func<int,int>.
            Action<int> act = V;
            TryCombine("Combine(Func<int,int>, Action<int>)", fa, act);

            // --- BUG-3 probes ---

            // 3a. Two distinct delegate objects over the SAME static method:
            // structural equality -> same hash?
            Func<int, int> g1 = A, g2 = A;
            Line("same-method hash equal: " + (g1.GetHashCode() == g2.GetHashCode()));
            Line("same-method hash g1=" + g1.GetHashCode() + " g2=" + g2.GetHashCode());

            // 3b. Two distinct delegate objects over DIFFERENT static methods.
            Line("diff-method hash differ: " + (fa.GetHashCode() != fb.GetHashCode()));

            // 3c. Instance-bound delegate: same method + same target across two
            // distinct delegate objects -> same hash? And a closure (compiler-
            // generated target) vs another closure instance -> differ?
            Counter c1 = new Counter(5);
            Counter c2 = new Counter(5);
            Func<int, int> h1 = c1.Increment, h2 = c2.Increment;
            Func<int, int> h3 = c1.Increment;
            Line("same target same method hash equal: " + (h1.GetHashCode() == h3.GetHashCode()));
            Line("diff target same method hash equal: " + (h1.GetHashCode() == h2.GetHashCode()));
            Line("h1=" + h1.GetHashCode() + " h2=" + h2.GetHashCode() + " h3=" + h3.GetHashCode());

            // 3d. Multicast chain: ab vs a NEW ab (distinct chain objects, same
            // list) -> structural equality -> same hash?
            Func<int, int> ab1 = fa + fb;
            Func<int, int> ab2 = fa + fb;
            Line("chains structural-equal: " + ab1.Equals(ab2));
            Line("chain hash equal: " + (ab1.GetHashCode() == ab2.GetHashCode()));
            Line("ab1=" + ab1.GetHashCode() + " ab2=" + ab2.GetHashCode());

            // 3e. A single leaf vs a 1-element chain: does the chain hash equal
            // the leaf's hash? (net48 hashes the list; [fa] == fa list-wise.)
            Func<int, int> one = (Func<int, int>)Delegate.Combine(fa, null);
            Line("1-elem chain equal: " + one.Equals(fa));
            Line("1-elem chain hash equal: " + (one.GetHashCode() == fa.GetHashCode()));

            // 3f. Same method+target delegate created in a DIFFERENT process
            // (run twice): deterministic across runs?
            Line("g1 stable: " + g1.GetHashCode());

            // 3g. DIFFERENT delegate TYPES: does the hash depend on the
            // delegate TYPE (Invoke method) rather than the bound method?
            Action<int> actG = V;
            Func<string, string> fsG = S;
            EventHandler ehG = delegate (object s, EventArgs e) { };
            D2<int, int> customG = delegate (int x) { return x - 1; };
            Line("Func<int,int>(A)   = " + g1.GetHashCode());
            Line("Action<int>(V)     = " + actG.GetHashCode());
            Line("Func<string,string>= " + fsG.GetHashCode());
            Line("EventHandler       = " + ehG.GetHashCode());
            Line("custom<int,int>    = " + customG.GetHashCode());
            Line("Func==custom: " + (g1.GetHashCode() == customG.GetHashCode()));
            Line("Func==Action: " + (g1.GetHashCode() == actG.GetHashCode()));
        }
    }

    public delegate int D2<T1, T2>(T1 x);

    public class Counter
    {
        public int Base;
        public Counter(int b) { Base = b; }
        public int Increment(int x) { return Base + x; }
    }
}
