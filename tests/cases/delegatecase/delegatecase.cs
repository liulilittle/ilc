//------------------------------------------------------------------------------
// delegatecase.cs -- e2e reference case for the delegate extension domain
// (multicast combine/remove/invocation-list/equals/target/invoke).
//
// This program runs on .NET Framework 4.8 and its output is the REFERENCE for
// the runtime icalls implemented in src/runtime/src/delegateext.cpp:
//
//   icall_delegate_combine            -- Delegate.Combine (a + b)
//   icall_delegate_remove             -- Delegate.Remove  (a - b)
//   icall_delegate_get_invocation_list-- Delegate.GetInvocationList()
//   icall_delegate_equals             -- Delegate.Equals
//   icall_delegate_get_target         -- Delegate.Target
//   icall_multicast_invoke_i4         -- multicast invocation (last result wins)
//
// The probes below capture net48's observable semantics that are easy to get
// wrong from documentation alone:
//   * a + a on a single delegate (dedup or not?)
//   * ab + ab where ab is the SAME multicast object (identity shortcut?)
//   * removal of a multicast value removes a contiguous SEQUENCE
//   * Remove of a value that is absent returns the ORIGINAL object (identity)
//   * Delegate.Equals compares invocation LISTS (order matters), not object
//     identity
//
// Every line is deterministic (no addresses, no hash codes).
//------------------------------------------------------------------------------
using System;

namespace DelegateCase
{
    public static class Program
    {
        static int A(int x) { return x + 1; }
        static int B(int x) { return x * 10; }
        static int C(int x) { return x - 5; }
        static int D(int x) { return x + 100; }

        static void Line(string s) { Console.WriteLine(s); }

        public static void Main()
        {
            Func<int, int> a = A, b = B, c = C, d = D;

            // --- Single delegate basics ---
            Line("single invoke: " + a(1));                                  // 2
            Line("single list len: " + a.GetInvocationList().Length);        // 1
            Line("single list0==a: " + object.ReferenceEquals(a.GetInvocationList()[0], a));

            // --- Combine two singles ---
            Func<int, int> ab = a + b;
            Line("ab len: " + ab.GetInvocationList().Length);                // 2
            Line("ab invoke: " + ab(2));                                     // 20 (last result wins)
            Line("ab list0==a: " + object.ReferenceEquals(ab.GetInvocationList()[0], a));
            Line("ab list1==b: " + object.ReferenceEquals(ab.GetInvocationList()[1], b));

            // --- Append to a chain ---
            Func<int, int> abc = ab + c;
            Line("abc len: " + abc.GetInvocationList().Length);              // 3
            Line("abc invoke: " + abc(3));                                   // -2 (c last)

            // --- Remove middle ---
            Func<int, int> noB = abc - b;
            Line("abc-b len: " + noB.GetInvocationList().Length);            // 2
            Line("abc-b invoke: " + noB(4));                                 // -1 (c last)
            Line("abc-b list0==a: " + object.ReferenceEquals(noB.GetInvocationList()[0], a));
            Line("abc-b list1==c: " + object.ReferenceEquals(noB.GetInvocationList()[1], c));

            // --- Remove head ---
            Func<int, int> noA = abc - a;
            Line("abc-a len: " + noA.GetInvocationList().Length);            // 2
            Line("abc-a invoke: " + noA(1));                                 // -4 (c last)
            Line("abc-a list0==b: " + object.ReferenceEquals(noA.GetInvocationList()[0], b));

            // --- Remove tail ---
            Func<int, int> noC = abc - c;
            Line("abc-c len: " + noC.GetInvocationList().Length);            // 2
            Line("abc-c invoke: " + noC(5));                                 // 50 (b last)

            // --- Remove absent value returns the ORIGINAL object ---
            Func<int, int> notThere = abc - d;
            Line("abc-d same: " + object.ReferenceEquals(notThere, abc));    // True (identity)
            Line("abc-d invoke: " + notThere(6));                            // 1 (c last)

            // --- Remove from a single ---
            Line("a-a null: " + (a - a == null));                            // True
            Line("a-b same: " + object.ReferenceEquals(a - b, a));           // True (identity)

            // --- Remove single from 2-chain leaves the other single ---
            Func<int, int> abMinusA = ab - a;
            Line("ab-a len: " + abMinusA.GetInvocationList().Length);        // 1
            Line("ab-a list0==b: " + object.ReferenceEquals(abMinusA.GetInvocationList()[0], b));

            // --- Combine probes: does net48 dedup a + a? ---
            Func<int, int> aa = a + a;
            Line("a+a len: " + aa.GetInvocationList().Length);               // ??? (net48 ground truth)

            // --- Combine of the SAME multicast object ---
            Func<int, int> ab2 = ab + ab;
            Line("ab+ab len: " + ab2.GetInvocationList().Length);            // ??? (net48 ground truth)
            Line("ab+ab same: " + object.ReferenceEquals(ab2, ab));          // ??? (identity shortcut)

            // --- Remove of the whole sequence ---
            Line("ab-ab null: " + (ab - ab == null));                        // True

            // --- Remove FIRST occurrence in a chain with a duplicate ---
            Func<int, int> aba = (a + b) + a;
            Line("aba len: " + aba.GetInvocationList().Length);              // 3
            Func<int, int> abaMinusA = aba - a;
            Line("aba-a len: " + abaMinusA.GetInvocationList().Length);      // 2
            Line("aba-a list0==b: " + object.ReferenceEquals(abaMinusA.GetInvocationList()[0], b));
            Line("aba-a list1==a: " + object.ReferenceEquals(abaMinusA.GetInvocationList()[1], a));

            // --- Remove with a MULTICAST value (contiguous sequence match) ---
            Func<int, int> abcMinusAB = abc - ab;
            Line("abc-ab len: " + abcMinusAB.GetInvocationList().Length);    // 1
            Line("abc-ab list0==c: " + object.ReferenceEquals(abcMinusAB.GetInvocationList()[0], c));

            // --- Delegate.Equals ---
            Line("a.Equals(a): " + a.Equals(a));                             // True
            Line("a.Equals(b): " + a.Equals(b));                             // False
            Func<int, int> a2 = A;
            Line("a.Equals(a2): " + a.Equals(a2));                           // True (same method+target)
            Line("a.Equals(null): " + a.Equals(null));                       // False
            Line("ab.Equals(abc): " + ab.Equals(abc));                       // False (different lists)
            Func<int, int> ab3 = a + b;
            Line("ab.Equals(ab3): " + ab.Equals(ab3));                       // True (same list, new object)
            Line("ab.Equals(b+a): " + ab.Equals(b + a));                     // False (order matters)

            // --- Delegate.Target of a static-method delegate ---
            Line("a.Target null: " + (a.Target == null));                    // True

            // --- null combine handling ---
            Line("a+null same: " + object.ReferenceEquals(a + null, a));     // True
            Line("null+a same: " + object.ReferenceEquals(null + a, a));     // True

            // --- Chain built by appending a chain as the RIGHT operand ---
            Func<int, int> aBC = a + (b + c);
            Line("a(bc) len: " + aBC.GetInvocationList().Length);            // 3
            Line("a(bc) invoke: " + aBC(7));                                 // 2 (c last)
            Line("a(bc).Equals(abc): " + aBC.Equals(abc));                   // True (same list, different shape)

            // --- Single-value removal: LAST occurrence (chain with duplicate) ---
            Line("aba-a list0==a: " + object.ReferenceEquals(abaMinusA.GetInvocationList()[0], a));
            Line("aba-a list1==b: " + object.ReferenceEquals(abaMinusA.GetInvocationList()[1], b));

            // --- (a,a) - a leaves a single ---
            Func<int, int> aaMinusA = aa - a;
            Line("aa-a len: " + aaMinusA.GetInvocationList().Length);        // 1
            Line("aa-a list0==a: " + object.ReferenceEquals(aaMinusA.GetInvocationList()[0], a));

            // --- Sequence removal direction with duplicate occurrences ---
            Func<int, int> abcab = abc + ab;                                 // [a,b,c,a,b]
            Line("abcab len: " + abcab.GetInvocationList().Length);          // 5
            Func<int, int> abcabMinusAB = abcab - ab;
            Line("abcab-ab len: " + abcabMinusAB.GetInvocationList().Length); // ??? first->3, last->3
            Line("abcab-ab list0==a: " + object.ReferenceEquals(abcabMinusAB.GetInvocationList()[0], a));
            Line("abcab-ab list1==b: " + object.ReferenceEquals(abcabMinusAB.GetInvocationList()[1], b));
            Line("abcab-ab list2==c: " + object.ReferenceEquals(abcabMinusAB.GetInvocationList()[2], c));

            // --- Non-contiguous sequence value -> identity (no removal) ---
            Func<int, int> ac = a + c;
            Line("abc-ac same: " + object.ReferenceEquals(abc - ac, abc));   // True (not contiguous)
        }
    }
}
