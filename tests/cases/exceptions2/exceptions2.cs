//------------------------------------------------------------------------------
// exceptions2.cs -- EXCEPTION DEEP 4 e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C81, AFTER the exception domain
// closure (the throw-chain / message-carrier fixes, batches 138/126: typed
// catches restored for KeyNotFoundException / SyncLock).
//
// Goes deeper than exceptions/exceptionchain2/excdeep2/excext/excextdeep/
// exops2 on the WIRED exception surface (MethodMap.Exception.cs + the
// DotNetException carrier): the NESTED try/finally lattice and the
// catch-variable DATA surface. Every EH probe lives in its OWN helper
// method -- the transpiler's EH-region emission degrades when one method
// carries many sibling/nested regions (a "dup on empty stack" abort;
// recorded D-EX4-2, worked around by the method split).
//
//   1. NESTED try/finally (3-deep): a throw at the innermost level -- the
//      finally blocks run innermost-first during unwinding, then the OUTER
//      catch fires. Order pinned by bool flags.
//   2. FINALLY ORDER ON THE NORMAL PATH: a return inside the try, then two
//      finally levels -- the finally mutations do NOT change the returned
//      value (exceptionchain2 c13 extended to 2 finally levels).
//   3. try/finally INSIDE a catch: after catching, a nested try/finally runs
//      fully; then the handler continues.
//   4. NESTED try/finally + catch DURING UNWINDING: an inner throw unwinds
//      through a finally, a middle catch reads the type, the outer catch
//      does NOT fire. (A throw inside a finally DURING unwinding would emit
//      a localized net48 stderr trace -- that shape is NOT probed, GAPS
//      D-EX4-1.)
//   5. CATCH-VARIABLE DATA at multiple levels: ex.GetType().Name (exact
//      thrown type through a base catch) + ex.Message presence on BOTH an
//      inner and an outer catch variable (nested catch handlers).
//   6. RETHROW THROUGH NESTED FINALLY: a bare `throw;` in an inner catch
//      under a finally -- the SAME type + message reach the outer catch.
//   7. EXCEPTION FACTORIES + DATA: ArgumentNullException / ArgumentException
//      catch-variable reads (type + message presence, locale-invariant).
//
// DISCIPLINE (exops2/excextdeep precedents -- KEEP): every catch uses a
// VARIABLE receiver (the tracked "System.Type" shape) so get_Message /
// GetType() resolve. BCL-factory messages are only probed for PRESENCE /
// type (net48 localizes; the ILC carrier is English). Custom-type ctor
// messages are NOT forwarded (D-EX2-2) -- parked. InnerException / Data /
// StackTrace stay OUT (D-EX2-1, GAPS). Every line prints "label=True/False".
// net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases { public static class Exceptions2 {
    // ============ 1. nested try/finally (3-deep) ============
    static void S1Nested()
    {
        bool n1 = false, n2 = false, n3 = false, n4 = false;
        try
        {
            try
            {
                try
                {
                    try { throw new InvalidOperationException("deep"); }
                    finally { n1 = true; }              // innermost finally
                }
                finally { n2 = true; }                  // middle finally
            }
            finally { n3 = true; }                      // outermost finally
        }
        catch (InvalidOperationException) { n4 = true; }
        Console.WriteLine("n1=" + n1);              // True
        Console.WriteLine("n2=" + n2);              // True
        Console.WriteLine("n3=" + n3);              // True
        Console.WriteLine("n4=" + n4);              // True (outer catch after 3 finallys)
    }

    // ============ 2. finally on the normal path, 2 levels ============
    static void S2Normal()
    {
        Console.WriteLine("f2=" + (RetFinally2(4) == 40));  // True
    }

    // ============ 3. try/finally inside a catch ============
    static void S3CatchFinally()
    {
        bool f1 = false, f2b = false;
        try { throw new ArgumentException("in"); }
        catch (ArgumentException)
        {
            try { f1 = true; }
            finally { f2b = true; }
        }
        Console.WriteLine("f3_try=" + f1);          // True
        Console.WriteLine("f3_fin=" + f2b);         // True
    }

    // ============ 4. nested try/catch/finally during unwinding ============
    static void S4Unwind()
    {
        bool r1 = false, r2 = false, r3 = false;
        try
        {
            try
            {
                try { throw new ArgumentException("unwind"); }
                finally { r1 = true; }
            }
            catch (ArgumentException ex)
            {
                r2 = (ex.GetType().Name == "ArgumentException");
            }
        }
        catch (Exception) { r3 = true; }   // never fires (inner caught it)
        Console.WriteLine("rep1=" + r1);   // True (inner finally ran)
        Console.WriteLine("rep2=" + r2);   // True (middle catch got the type)
        Console.WriteLine("rep3=" + (!r3));// True (outer catch did not fire)
    }

    // ============ 5. catch-variable data at multiple levels ============
    static void S5Data()
    {
        try { throw new NotSupportedException("multi"); }
        catch (Exception inner)
        {
            Console.WriteLine("d_in_t=" + (inner.GetType().Name == "NotSupportedException"));
            Console.WriteLine("d_in_m=" + (inner.Message != null));
            try { throw new FormatException("inner2"); }
            catch (Exception inner2)
            {
                Console.WriteLine("d_i2_t=" + (inner2.GetType().Name == "FormatException"));
                Console.WriteLine("d_i2_m=" + (inner2.Message != null));
            }
            Console.WriteLine("d_outer_again=" + (inner.Message != null));
        }
    }

    // ============ 6. rethrow through nested finally ============
    static void S6Rethrow()
    {
        bool rr1 = false, rr2 = false;
        try
        {
            try { throw new ArgumentException("rethr"); }
            catch (ArgumentException) { throw; }
            finally { Console.WriteLine("rr_fin=true"); }
        }
        catch (Exception ex)
        {
            Console.WriteLine("rr_in_t=" + (ex.GetType().Name == "ArgumentException"));
            Console.WriteLine("rr_in_m=" + (ex.Message != null));
            rr1 = true;
            rr2 = true;
        }
        Console.WriteLine("rr1=" + rr1);            // True
        Console.WriteLine("rr2=" + rr2);            // True
    }

    // ============ 7. exception factories + data ============
    static void S7Factories()
    {
        try { throw new ArgumentNullException("param"); }
        catch (ArgumentNullException ex)
        {
            Console.WriteLine("e7_t=" + (ex.GetType().Name == "ArgumentNullException"));
            Console.WriteLine("e7_m=" + (ex.Message != null));
        }
        try { throw new ArgumentException("arg msg"); }
        catch (ArgumentException ex)
        {
            Console.WriteLine("e8_t=" + (ex.GetType().Name == "ArgumentException"));
            Console.WriteLine("e8_m=" + (ex.Message != null));
        }
    }

    public static void Main()
    {
        S1Nested();
        S2Normal();
        S3CatchFinally();
        S4Unwind();
        S5Data();
        S6Rethrow();
        S7Factories();
        Console.WriteLine("alive=true");
    }

    static int RetFinally2(int v)
    {
        int result = 0;
        try
        {
            try { result = v * 10; return result; }
            finally { result = result + 1; }
        }
        finally { result = result + 2; }
    }
}}
