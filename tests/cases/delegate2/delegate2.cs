//------------------------------------------------------------------------------
// delegate2.cs -- end-to-end reference case for multicast delegate semantics:
//   * single cast: Invoke, Target (null for static), Method.Name;
//   * += concatenates (no de-duplication: same method twice = length 3);
//   * invocation order = insertion order, return value = last invocation;
//   * -= removes the LAST occurrence of the method;
//   * GetInvocationList exposes the flattened chain in order;
//   * delegate equality is structural (same list => equal);
//   * Combine with null acts as identity;
//   * an exception thrown by one invocation stops the chain;
//   * unwinding all entries yields null.
//
// Runs on .NET Framework 4.8 (the reference runtime). Every line of output
// is deterministic.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class Delegate2 {
    private delegate int IntFn();
    private static int order = 0;
    private static int StepA() { order++; Console.WriteLine("A:" + order); return 1; }
    private static int StepB() { order++; Console.WriteLine("B:" + order); return 2; }
    private static int StepC() { order++; Console.WriteLine("C:" + order); return 3; }
    private static int Ten() { return 10; }
    private static int Twenty() { return 20; }
    private static int Thirty() { return 30; }
    private static int ThrowsEx() { throw new Exception("boom"); }

    public static void Main() {
        // --- single cast basics ---
        IntFn f = Ten;
        Console.WriteLine(f());                           // 10
        Console.WriteLine(f.GetInvocationList().Length);  // 1
        Console.WriteLine(f.Target == null);              // True (static method)
        Console.WriteLine(f.Method.Name);                 // Ten
        // --- += concatenation: last return wins ---
        f += Twenty;
        Console.WriteLine(f.GetInvocationList().Length);  // 2
        Console.WriteLine(f());                           // 20
        Console.WriteLine(((IntFn)f.GetInvocationList()[0])() + "," + ((IntFn)f.GetInvocationList()[1])()); // 10,20
        // --- no de-duplication: same method twice ---
        f += Ten;
        Console.WriteLine(f.GetInvocationList().Length);  // 3
        // --- -= removes the LAST occurrence ---
        f -= Ten;
        Console.WriteLine(f.GetInvocationList().Length);  // 2
        Console.WriteLine(((IntFn)f.GetInvocationList()[0])() + "," + ((IntFn)f.GetInvocationList()[1])()); // 10,20
        // --- invocation order (side effects) ---
        order = 0;
        IntFn g = StepA;
        g += StepB;
        g += StepC;
        int last = g();                                   // prints A:1 B:2 C:3
        Console.WriteLine("last=" + last);                // last=3
        Console.WriteLine("order=" + order);              // order=3
        // --- -= from the middle of the chain ---
        IntFn h = StepA;
        h += StepB;
        h += StepC;
        h -= StepB;
        Console.WriteLine(h.GetInvocationList().Length);  // 2
        order = 0;
        last = h();                                       // prints A:1 C:2
        Console.WriteLine("last=" + last);                // last=3
        Console.WriteLine("order=" + order);              // order=2
        // --- equality: structural (same flattened list) ---
        IntFn s = StepA;
        Console.WriteLine(s == StepA);                    // True
        IntFn ab1 = StepA; ab1 += StepB;
        IntFn ab2 = StepA; ab2 += StepB;
        Console.WriteLine(ab1 == ab2);                    // True
        IntFn ba = StepB; ba += StepA;
        Console.WriteLine(ab1 == ba);                     // False (order matters)
        Console.WriteLine(ab1.Equals(ab2));               // True
        // --- combine with null: identity ---
        IntFn n = null;
        IntFn ten = Ten;
        IntFn only = n + ten;
        Console.WriteLine(only == ten);                   // True
        Console.WriteLine((n + n) == null);               // True
        // --- exception in the middle stops the chain ---
        order = 0;
        IntFn ex = StepA;
        ex += ThrowsEx;
        ex += StepB;
        try { ex(); } catch (Exception e) {
            Console.WriteLine(e.GetType().Name);          // Exception
        }
        Console.WriteLine("order=" + order);              // order=1 (StepB skipped)
        // --- unwinding to null ---
        IntFn k = Ten; k += Twenty; k += Thirty;
        k -= Thirty; k -= Twenty; k -= Ten;
        Console.WriteLine(k == null);                     // True
    }
}}
