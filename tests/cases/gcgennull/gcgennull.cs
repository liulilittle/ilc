//------------------------------------------------------------------------------
// gcgennull.cs -- GC.GetGeneration(null) throws NullReferenceException.
//
// MEASURED on net48: GetGeneration(null) throws NullReferenceException, NOT
// ArgumentNullException -- the BCL implementation dereferences the object rather
// than null-checking it, unlike GC.SuppressFinalize / GC.ReRegisterForFinalize
// which DO throw ArgumentNullException (pinned by tests/cases/gcexcept).
//
// Pinning the wrong type is observable in both directions: a
// catch (NullReferenceException) that should run does not, and a
// catch (ArgumentNullException) that should NOT run does. Both are recorded
// below, so either regression turns a row red.
//
// The exception is caught by its own exact type so the reference runtime never
// prints an unhandled exception (that path faults the net48 CLR itself, which
// would make the oracle unstable).
//------------------------------------------------------------------------------
using System;

public static class P
{
    private static void Probe(string id)
    {
        try { GC.GetGeneration(null); Console.WriteLine(id + "=no-throw"); }
        catch (NullReferenceException) { Console.WriteLine(id + "=NullReferenceException"); }
        catch (ArgumentNullException) { Console.WriteLine(id + "=ArgumentNullException"); }
        catch (Exception e) { Console.WriteLine(id + "=other:" + e.GetType().Name); }
    }

    public static void Main()
    {
        Probe("gen.null");

        // The base-class widths must work too (NullReferenceException derives
        // from SystemException -> Exception).
        try { GC.GetGeneration(null); Console.WriteLine("gen.base=no-throw"); }
        catch (SystemException e) { Console.WriteLine("gen.base=" + e.GetType().Name); }

        // Must NOT be catchable as ArgumentException (it is not one).
        try { GC.GetGeneration(null); Console.WriteLine("gen.arg=no-throw"); }
        catch (ArgumentException) { Console.WriteLine("gen.arg=ArgumentException"); }
        catch (Exception e) { Console.WriteLine("gen.arg=" + e.GetType().Name); }

        // The non-null path still returns a real generation.
        object o = new object();
        Console.WriteLine("gen.ok=" + GC.GetGeneration(o));
    }
}