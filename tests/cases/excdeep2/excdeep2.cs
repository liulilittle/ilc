//------------------------------------------------------------------------------
// excdeep2.cs -- EXCEPTION DEEP 2 e2e case (net48 oracle, byte-for-byte).
// Created in e2e batch C38, AFTER the C4716 trailing-return fix (batch 93,
// commit 68645c9): a throw-only NON-VOID method (IL ends on `throw`/rethrow
// -- no `ret` falls through) previously emitted a C++ function whose end was
// reachable without a return -> MSVC C4716 build failure. The emitter now
// appends the return type's zero value (`return 0;` / `return nullptr;`) on
// the throw-tail fall-through. excdeep (C7) covered typed throws / nesting /
// finally / rethrow; excdeep2 drives the THROW-ONLY NON-VOID surface through
// every call shape:
//
//   1. METHOD THROW -- throw-only non-void methods (int / string / user
//      class return) called DIRECTLY and caught by exact type.
//   2. CHAIN THROW -- a non-void method whose body returns a throw-only
//      method's (never-produced) result: the exception propagates through
//      the caller frame to the outer catch (2-deep and 3-deep chains).
//   3. DELEGATE THROW -- a Func<int>/Func<string> bound to a throw-only
//      non-void method; invoking the delegate throws (the delegate methodPtr
//      is the same generated function).
//   4. LINQ THROW -- a predicate used by Enumerable.Count / Where that
//      THROWS mid-walk: the DotNetException propagates out of the LINQ
//      icall frame (linq_map.cpp has no catch) to the generated catch.
//   5. AFTER-CATCH liveness -- execution continues after every catch; the
//      final line asserts all handlers fired.
//
// RECORDED, NOT PROBED: `catch` without a variable never reads ex.Message
// (get_Message is still unwired); the Exception OBJECT shape (catch (Exception
// ex) { ex.Something }) stays out of scope. Every probe catches the EXACT
// thrown type (a chain whose first segment is the thrown type matches).
//
// Every line prints "label=True/False". net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public class Payload
    {
        public int V;
    }

    public static class Excdeep2
    {
        // ---- throw-only NON-void methods (the C4716 shape) ----
        static int ThrowInt() { throw new InvalidOperationException("int"); }
        static string ThrowStr() { throw new ArgumentException("str"); }
        static Payload ThrowObj() { throw new NotSupportedException("obj"); }

        // ---- chains: non-void body returns a throw-only method's result ----
        static int Chain1() { return ThrowInt(); }
        static string Chain2() { return ThrowStr(); }
        static int Chain3() { return Chain1(); }

        // ---- LINQ predicate that throws at one element ----
        static bool Boom(int x) { if (x == 5) throw new InvalidOperationException("boom"); return true; }

        public static void Main()
        {
            // ===== 1. method throw (direct call to throw-only non-void) =====
            bool m1 = false;
            try { int r = ThrowInt(); } catch (InvalidOperationException) { m1 = true; }
            Console.WriteLine("m1=" + m1);

            bool m2 = false;
            try { string s = ThrowStr(); } catch (ArgumentException) { m2 = true; }
            Console.WriteLine("m2=" + m2);

            bool m3 = false;
            try { Payload p = ThrowObj(); } catch (NotSupportedException) { m3 = true; }
            Console.WriteLine("m3=" + m3);

            // ===== 2. chain throw (propagation through non-void frames) =====
            bool c1 = false;
            try { int r = Chain1(); } catch (InvalidOperationException) { c1 = true; }
            Console.WriteLine("c1=" + c1);

            bool c2 = false;
            try { string s = Chain2(); } catch (ArgumentException) { c2 = true; }
            Console.WriteLine("c2=" + c2);

            bool c3 = false;
            try { int r = Chain3(); } catch (InvalidOperationException) { c3 = true; }
            Console.WriteLine("c3=" + c3);

            // ===== 3. delegate throw (Func bound to throw-only) =====
            Func<int> f1 = ThrowInt;
            bool d1 = false;
            try { int r = f1(); } catch (InvalidOperationException) { d1 = true; }
            Console.WriteLine("d1=" + d1);

            Func<string> f2 = ThrowStr;
            bool d2 = false;
            try { string s = f2(); } catch (ArgumentException) { d2 = true; }
            Console.WriteLine("d2=" + d2);

            // ===== 4. LINQ throw (predicate throws mid-walk) =====
            int[] nums = { 1, 2, 3, 4, 5, 6, 7 };
            Func<int, bool> boom = Boom;
            bool l1 = false;
            try { int n = nums.Count(boom); } catch (InvalidOperationException) { l1 = true; }
            Console.WriteLine("l1=" + l1);

            bool l2 = false;
            try { int[] arr = nums.Where(boom).ToArray(); } catch (InvalidOperationException) { l2 = true; }
            Console.WriteLine("l2=" + l2);

            // ===== 5. after-catch liveness =====
            Console.WriteLine("alive=" + (m1 && m2 && m3 && c1 && c2 && c3 && d1 && d2 && l1 && l2));
        }
    }
}
