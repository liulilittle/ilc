using System;
namespace Cases { public static class Exdeep {
    // Deep exception-semantics e2e case (net48 reference run).
    // Material for the transpiler's H8/H9 exception-chain verification:
    //  - typed throws must be catchable by their own type AND by base types
    //  - nested try/catch unwinding order (finallys innermost-first)
    //  - finally execution order on all three paths (normal / throw / catch-rethrow)
    //  - bare `throw;` must preserve the ORIGINAL type (not the catch type)
    //  - runtime fault exceptions: DivideByZero / Overflow (INT_MIN/-1) /
    //    NullReference / IndexOutOfRange / throw-null
    // Every output line is labeled for diffing (expected.txt); no random or
    // time-dependent output.

    // NOTE on Exception.Message / InnerException: this case intentionally does
    // NOT read ex.Message or ex.InnerException. The M-stage transpiler maps
    // neither get_Message nor get_InnerException (MethodRefResolver covers only
    // Console Write/WriteLine, ctor no-ops and a few icalls), so reading them
    // would generate an unresolved symbol. The message string still flows into
    // the throw carrier via the mapped `new XException("...")` ctor; the type
    // chain {Type},{Type},{Base} is exercised by the catch matching below.

    class MyException : Exception {
        public MyException() { }
        public MyException(string message) : base(message) { }
    }
    class MyException2 : MyException {
        public MyException2() { }
    }

    public static void Main() {
        // f1: nested try/catch; the inner handler catches -> outer never runs.
        try {
            try { throw new InvalidOperationException("inner"); }
            catch (InvalidOperationException) { Console.WriteLine("f1: inner"); }
        } catch (Exception) { Console.WriteLine("f1: outer"); }

        // f2: nested; inner handler does NOT match -> outer catches a different type.
        try {
            try { throw new ArgumentException("inner"); }
            catch (InvalidOperationException) { Console.WriteLine("f2: wrong"); }
        } catch (ArgumentException) { Console.WriteLine("f2: outer"); }

        // f3: nested with no matching inner handler -> outer BASE catch of a
        // user-defined type.
        try {
            try { throw new MyException("deep"); }
            catch (DivideByZeroException) { Console.WriteLine("f3: wrong"); }
        } catch (Exception) { Console.WriteLine("f3: outer base"); }

        // f4: finally on the normal path runs in order.
        try { Console.WriteLine("f4: try"); }
        finally { Console.WriteLine("f4: finally"); }
        Console.WriteLine("f4: after");

        // f5: finally runs during unwinding (before the outer catch).
        try {
            try { Console.WriteLine("f5: try"); throw new Exception("boom"); }
            finally { Console.WriteLine("f5: finally"); }
        } catch (Exception) { Console.WriteLine("f5: catch"); }

        // f6: catch rethrows -> finally runs again -> outer catch sees the
        // original exception type.
        try {
            try {
                Console.WriteLine("f6: try");
                throw new InvalidOperationException("rethrow");
            } catch (Exception) {
                Console.WriteLine("f6: catch");
                throw;
            } finally {
                Console.WriteLine("f6: finally");
            }
        } catch (InvalidOperationException) { Console.WriteLine("f6: outer ioe"); }

        // f7: bare rethrow preserves the ORIGINAL type, not the catch type.
        try {
            try { throw new MyException("orig"); }
            catch (Exception) { throw; }
        } catch (MyException) { Console.WriteLine("f7: exact type preserved"); }

        // f8: finally runs before the method returns; the return VALUE is the
        // one evaluated in the try body (40); the finally mutation (+1) is not
        // part of the returned value.
        int r8 = RetFinally(4);
        Console.WriteLine("f8: ret");
        Console.WriteLine(r8);

        // f9: an exception thrown IN finally replaces the in-flight exception.
        try {
            try { throw new InvalidOperationException("original"); }
            finally { throw new ArgumentException("from finally"); }
        } catch (ArgumentException) { Console.WriteLine("f9: finally throw wins"); }
        catch (InvalidOperationException) { Console.WriteLine("f9: original"); }

        // f10: a user exception derived from a user exception is caught by the
        // intermediate base type.
        try { throw new MyException2(); }
        catch (MyException) { Console.WriteLine("f10: base catches derived"); }

        // f10b: exact user type catch.
        try { throw new MyException("exact"); }
        catch (MyException) { Console.WriteLine("f10b: exact user type"); }

        // f11: user exception caught by System.Exception (base chain).
        try { throw new MyException("base"); }
        catch (Exception) { Console.WriteLine("f11: user type via Exception"); }

        // f12: DivideByZeroException caught by its exact type (operands must be
        // variables: `x / 0` with a constant zero is a compile-time error).
        int x = 1, y = 0;
        try { int z = x / y; Console.WriteLine(z); }
        catch (DivideByZeroException) { Console.WriteLine("f12: divzero"); }

        // f13: INT_MIN / -1 is an OVERFLOW, not a divide-by-zero (audit H9).
        int mn = int.MinValue;
        try { int z = mn / -1; Console.WriteLine(z); }
        catch (OverflowException) { Console.WriteLine("f13: overflow"); }

        // f14: null dereference (Length) -> NullReferenceException.
        string ns = null;
        try { int len = ns.Length; Console.WriteLine(len); }
        catch (NullReferenceException) { Console.WriteLine("f14: nre"); }

        // f14b: null receiver method call -> NullReferenceException.
        object no = null;
        try { string t = no.ToString(); Console.WriteLine(t); }
        catch (NullReferenceException) { Console.WriteLine("f14b: nre call"); }

        // f15: array index out of range (positive).
        int[] arr = new int[3];
        try { int v = arr[5]; Console.WriteLine(v); }
        catch (IndexOutOfRangeException) { Console.WriteLine("f15: index +"); }

        // f15b: array index out of range (negative; a constant -1 would emit
        // CS0251, so the index goes through a variable).
        int neg = -1;
        try { int v = arr[neg]; Console.WriteLine(v); }
        catch (IndexOutOfRangeException) { Console.WriteLine("f15b: index -"); }

        // f16: `throw null` -> NullReferenceException (net48 empirical).
        try { throw null; }
        catch (NullReferenceException) { Console.WriteLine("f16: throw null -> nre"); }

        // f17: multiple catch clauses; the FIRST matching one wins.
        x = 1; y = 0;
        try { int z = x / y; Console.WriteLine(z); }
        catch (DivideByZeroException) { Console.WriteLine("f17: first"); }
        catch (Exception) { Console.WriteLine("f17: second"); }

        // f18: multiple clauses; the second one matches (sibling types do not
        // match each other).
        try { throw new InvalidOperationException("multi"); }
        catch (ArgumentException) { Console.WriteLine("f18: arg"); }
        catch (InvalidOperationException) { Console.WriteLine("f18: ioe"); }
        catch (Exception) { Console.WriteLine("f18: base"); }

        // f19: an empty catch swallows the exception; execution continues.
        try { throw new Exception("swallow"); }
        catch { }
        Console.WriteLine("f19: alive");

        // f20: deep nesting unwinds finallys innermost-first, then the outer
        // catch of the user type.
        try {
            try {
                try { throw new MyException("deep"); }
                finally { Console.WriteLine("f20: inner finally"); }
            } finally { Console.WriteLine("f20: middle finally"); }
        } catch (MyException) { Console.WriteLine("f20: catch"); }

        // f21: rethrow of a derived user type through a base catch; the exact
        // type survives and is caught by the intermediate base.
        try {
            try { throw new MyException2(); }
            catch (Exception) { throw; }
        } catch (MyException) { Console.WriteLine("f21: derived preserved"); }

        // f23: return inside catch; finally still runs first.
        int r23 = CatchRet(4);
        Console.WriteLine("f23: catchret");
        Console.WriteLine(r23);
    }

    static int RetFinally(int v) {
        int result = 0;
        try { result = v * 10; return result; }
        finally { result = result + 1; }
    }

    static int CatchRet(int v) {
        try { throw new Exception("x"); }
        catch { return v + 100; }
        finally { Console.WriteLine("f23: catchret finally"); }
    }
}}
