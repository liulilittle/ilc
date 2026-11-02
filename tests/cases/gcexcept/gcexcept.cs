//------------------------------------------------------------------------------
// gcexcept.cs -- the GC icall ArgumentNullException CATCHABILITY contract.
//
// net48 contract: GC.GetGeneration(null), GC.SuppressFinalize(null) and
// GC.ReRegisterForFinalize(null) throw System.ArgumentNullException, and that
// exception is catchable as ArgumentException / SystemException / Exception
// (the normal .NET hierarchy). The runtime models catch matching by walking a
// comma-separated TYPE CHAIN carried by the thrown name (il2cpp_exception_matches,
// exception.cpp), so an icall that throws the BARE name
// "System.ArgumentNullException" cannot be caught by `catch (Exception)` and
// escapes as "Unhandled managed exception" (exit 1) instead of running the
// handler -- a real behavioural divergence from net48, measured:
//   net48     : caught-base:ArgumentNullException / caught-arg:ArgumentNullException
//   before fix: Unhandled managed exception: obj (exit 1)
//
// The records below pin the whole hierarchy at every catch width, so a
// regression to a bare name turns the base-class rows red.
//------------------------------------------------------------------------------
using System;

public static class P
{
    private static void Try(string id, Action a)
    {
        try { a(); Console.WriteLine(id + "=no-throw"); }
        catch (ArgumentNullException) { Console.WriteLine(id + "=ArgumentNullException"); }
        catch (Exception e) { Console.WriteLine(id + "=other:" + e.GetType().Name); }
    }

    private static void TryBase(string id, Action a)
    {
        try { a(); Console.WriteLine(id + "=no-throw"); }
        catch (Exception e) { Console.WriteLine(id + "=base:" + e.GetType().Name); }
    }

    private static void TryArg(string id, Action a)
    {
        try { a(); Console.WriteLine(id + "=no-throw"); }
        catch (ArgumentException e) { Console.WriteLine(id + "=arg:" + e.GetType().Name); }
    }

    private static void TrySystem(string id, Action a)
    {
        try { a(); Console.WriteLine(id + "=no-throw"); }
        catch (SystemException e) { Console.WriteLine(id + "=sys:" + e.GetType().Name); }
    }

    public static void Main()
    {
        // SuppressFinalize(null): exact, ArgumentException, SystemException, Exception
        Try("suppress.exact", () => GC.SuppressFinalize(null));
        TryArg("suppress.arg", () => GC.SuppressFinalize(null));
        TrySystem("suppress.sys", () => GC.SuppressFinalize(null));
        TryBase("suppress.base", () => GC.SuppressFinalize(null));

        // ReRegisterForFinalize(null)
        Try("rereg.exact", () => GC.ReRegisterForFinalize(null));
        TryArg("rereg.arg", () => GC.ReRegisterForFinalize(null));
        TrySystem("rereg.sys", () => GC.ReRegisterForFinalize(null));
        TryBase("rereg.base", () => GC.ReRegisterForFinalize(null));

        // NOTE (measured): GC.GetGeneration(null) is NOT an ArgumentNullException
        // on net48 -- it dereferences and throws NullReferenceException
        // ("gen.exact=other:NullReferenceException"). That is a DIFFERENT
        // divergence (our gcapi throws ArgumentNullException there); it is not
        // pinned here because the reference runtime faults while printing the
        // unhandled NRE (exit 0xC0000005), so the oracle output is unstable.

        // The non-null paths must still work (no throw, real value).
        object o = new object();
        Console.WriteLine("ok.gen=" + GC.GetGeneration(o));
        GC.SuppressFinalize(o);
        Console.WriteLine("ok.suppress=1");
        GC.ReRegisterForFinalize(o);
        Console.WriteLine("ok.rereg=1");
    }
}
