using System;
namespace Cases { public static class Exceptionchain2 {
    // exceptionchain2: deep exception-chain e2e (net48 reference run).
    // throw-chain-fix verification: EVERY output line is a bool (True/False),
    // so the expected.txt diff is a pure boolean matrix. The line order is
    // the only label -- each WriteLine below documents which probe it is.
    //
    // Probes:
    //  c1  DivideByZeroException  -- runtime fault, exact-type catch hits
    //  c2  OverflowException      -- INT_MIN / -1, exact-type catch hits
    //  c3  ArgumentException      -- explicit throw, exact-type catch hits
    //  c4  InvalidCastException   -- failed castclass, exact-type catch hits
    //  c5  NullReferenceException -- null dereference, exact-type catch hits
    //  c6  `throw null` -> NRE    -- throw-chain fix verification
    //  c7  base catch             -- catch (Exception) hits a specific type
    //  c8  sibling clause skipped -- first clause does NOT match (exdeep f18
    //      probe), the second clause hits -- EH sibling catch
    //  c9  finally order, normal path: try -> finally -> after (3 bools)
    //  c10 finally during unwind: finally before the outer catch (2 bools)
    //  c11 inner catch rethrows -> outer catch catches
    //  c12 rethrow preserves the ORIGINAL exact type
    //  c13 return value survives the finally mutation (40, not 41)
    //  c14 wrong exact type does NOT bleed -- outer base catch only

    public static void Main() {
        // c1: DivideByZeroException -- exact-type catch hits (x/y with y=0:
        // a constant zero divisor would be a compile-time error).
        int x = 1, y = 0;
        bool c1 = false;
        try { int z = x / y; Console.WriteLine(z); }
        catch (DivideByZeroException) { c1 = true; }
        Console.WriteLine(c1);                        // c1 True

        // c2: OverflowException -- INT_MIN / -1 is an overflow, not divzero.
        int mn = int.MinValue;
        bool c2 = false;
        try { int z = mn / -1; Console.WriteLine(z); }
        catch (OverflowException) { c2 = true; }
        Console.WriteLine(c2);                        // c2 True

        // c3: ArgumentException -- explicit throw, exact-type catch hits.
        bool c3 = false;
        try { throw new ArgumentException("arg"); }
        catch (ArgumentException) { c3 = true; }
        Console.WriteLine(c3);                        // c3 True

        // c4: InvalidCastException -- failed castclass (boxed int -> string).
        object o = 123;
        bool c4 = false;
        try { string s = (string)o; Console.WriteLine(s); }
        catch (InvalidCastException) { c4 = true; }
        Console.WriteLine(c4);                        // c4 True

        // c5: NullReferenceException -- null dereference.
        string ns = null;
        bool c5 = false;
        try { int len = ns.Length; Console.WriteLine(len); }
        catch (NullReferenceException) { c5 = true; }
        Console.WriteLine(c5);                        // c5 True

        // c6: `throw null` -> NullReferenceException (throw-chain fix: a null
        // throw carrier must surface as NRE, not crash the runtime).
        bool c6 = false;
        try { throw null; }
        catch (NullReferenceException) { c6 = true; }
        Console.WriteLine(c6);                        // c6 True

        // c7: base-class catch -- catch (Exception) hits a specific type.
        bool c7 = false;
        try { throw new ArgumentException("base"); }
        catch (Exception) { c7 = true; }
        Console.WriteLine(c7);                        // c7 True

        // c8: sibling clause skipped -- the thrown type matches ONLY the
        // second clause (exdeep f18 probe: sibling types do not match each
        // other; an unconditional first-clause match would print False).
        bool c8 = false;
        try { throw new InvalidOperationException("sib"); }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { c8 = true; }
        Console.WriteLine(c8);                        // c8 True

        // c9: finally execution order on the normal path: try -> finally ->
        // after (three bools, printed in order).
        bool c9t = false, c9f = false, c9a = false;
        try { c9t = true; } finally { c9f = true; }
        c9a = true;
        Console.WriteLine(c9t);                       // c9 try
        Console.WriteLine(c9f);                       // c9 finally
        Console.WriteLine(c9a);                       // c9 after

        // c10: finally runs during unwinding -- before the outer catch.
        bool c10f = false, c10c = false;
        try {
            try { throw new Exception("boom"); }
            finally { c10f = true; }
        } catch (Exception) { c10c = true; }
        Console.WriteLine(c10f);                      // c10 finally
        Console.WriteLine(c10c);                      // c10 catch

        // c11: inner catch rethrows -> the outer catch catches.
        bool c11 = false;
        try {
            try { throw new ArgumentException("rethrow"); }
            catch (ArgumentException) { throw; }
        } catch (Exception) { c11 = true; }
        Console.WriteLine(c11);                       // c11 True

        // c12: bare rethrow preserves the ORIGINAL exact type (not the catch
        // clause's type) -- the outer exact-type catch must hit.
        bool c12 = false;
        try {
            try { throw new InvalidOperationException("orig"); }
            catch (Exception) { throw; }
        } catch (InvalidOperationException) { c12 = true; }
        Console.WriteLine(c12);                       // c12 True

        // c13: the finally mutation does not change the returned value
        // (40 from the try body, not 41).
        int r13 = RetFinally(4);
        Console.WriteLine(r13 == 40);                 // c13 True

        // c14: wrong exact type does NOT bleed -- the inner catch clause is a
        // DIFFERENT type (miss), so only the outer base catch runs. If the
        // chain matched the first clause blindly, c14 stays False.
        bool c14 = false;
        try {
            try { throw new ArgumentException("miss"); }
            catch (InvalidOperationException) { }
        } catch (Exception) { c14 = true; }
        Console.WriteLine(c14);                       // c14 True
    }

    static int RetFinally(int v) {
        int result = 0;
        try { result = v * 10; return result; }
        finally { result = result + 1; }
    }
}}
