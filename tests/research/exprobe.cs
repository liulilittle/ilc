// exprobe.cs — exception family (throw rethrow / catch filters / finally determinism) net48 evidence.
// Key points: throw; keeps the original exception and stack / throw ex; resets the stack; the catch-when filter is evaluated before the handler,
// a false filter does not enter the handler, an exception thrown inside the filter propagates the filter's exception; finally is deterministic on all paths.
using System;
using System.Runtime.CompilerServices;

class ExProbe
{
    static void P(string s) => Console.WriteLine(s);
    static void Pb(string label, bool v) => P(label + " = " + (v ? 1 : 0));

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void InnerThrow() { throw new InvalidOperationException("orig"); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void RethrowOrig() { try { InnerThrow(); } catch (Exception e) { throw; } }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void RethrowNew() { try { InnerThrow(); } catch (Exception e) { throw e; } }

    static bool Filter(Exception e) { P("  filter: " + e.Message); return e.Message == "boom"; }
    static bool FilterFalse(Exception e) { P("  filter(false)"); return false; }

    static void Main()
    {
        // ---- catch type matching: most-derived type selected before base ----
        P("-- multiple catch order --");
        try { throw new InvalidOperationException("x"); }
        catch (InvalidOperationException) { P("catch: InvalidOperationException"); }
        catch (Exception) { P("catch: Exception"); }
        try { throw new Exception("y"); }
        catch (InvalidOperationException) { P("catch: InvalidOperationException"); }
        catch (Exception) { P("catch: Exception"); }

        // ---- throw; vs throw ex;: stack trace ----
        P("-- rethrow --");
        try { RethrowOrig(); }
        catch (Exception e)
        {
            Pb("throw; preserves the original stack (includes InnerThrow)", e.StackTrace.Contains("InnerThrow"));
            Pb("throw; message preserved", e.Message == "orig");
        }
        try { RethrowNew(); }
        catch (Exception e)
        {
            Pb("throw ex; resets the stack (no InnerThrow)", !e.StackTrace.Contains("InnerThrow"));
            Pb("throw ex; message preserved", e.Message == "orig");
        }

        // ---- throwing a new exception inside catch: InnerException chain ----
        P("-- throw new exception wrapping --");
        try
        {
            try { InnerThrow(); }
            catch (Exception e) { throw new ArgumentException("wrapped", e); }
        }
        catch (Exception e)
        {
            P("outer: " + e.GetType().Name);
            P("InnerException: " + (e.InnerException != null ? e.InnerException.GetType().Name : "<null>"));
        }

        // ---- catch-when filter: true -> handler; false -> not entered (propagates outward) ----
        P("-- catch filter --");
        try
        {
            P("  try");
            throw new Exception("boom");
        }
        catch (Exception e) when (Filter(e)) { P("  handler: caught"); }
        finally { P("  finally"); }
        try
        {
            try { throw new Exception("nope"); }
            catch (Exception e) when (FilterFalse(e)) { P("  inner-handler"); }
        }
        catch (Exception e) { P("outer-handler: " + e.Message); }

        // ---- exception thrown inside filter: propagates the filter's exception (original replaced) ----
        P("-- filter throws --");
        try
        {
            try { throw new Exception("original"); }
            catch (Exception e) when (ThrowingFilter(e)) { P("  inner-handler"); }
        }
        catch (Exception e) { P("outer: " + e.GetType().Name + " / " + e.Message); }

        // ---- finally determinism: runs on normal / exception / return paths ----
        P("-- finally all paths --");
        TryFinally(0);    // normal
        try { TryFinally(1); } catch (Exception e) { P("  caught: " + e.GetType().Name); }
        P("finally+return value: " + FinallyReturn());

        // ---- nested try/catch/finally order: try -> catch -> finally ----
        P("-- try/catch/finally order --");
        try
        {
            P("  try");
            throw new Exception("z");
        }
        catch (Exception) { P("  catch"); }
        finally { P("  finally"); }
    }

    static bool ThrowingFilter(Exception e)
    {
        P("  filter throws");
        throw new ArgumentNullException("filter-arg");
    }

    static void TryFinally(int mode)
    {
        try
        {
            if (mode == 1) throw new Exception("boom");
            P("  try: ok");
        }
        finally { P("  finally: ran"); }
    }

    static int FinallyReturn()
    {
        try { return 42; }
        finally { P("  finally: ran before return"); }
    }
}
