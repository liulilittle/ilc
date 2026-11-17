//------------------------------------------------------------------------------
// probe-excparamname.cs -- an exception's ParamName must belong to THAT
// exception, and a catch's ParamName must not leak across unrelated throws.
//
// ParamName is a property of one instance. The runtime serves it from a
// per-object side-table entry, but a receiver with no entry falls back to the
// thread-local snapshot of the MOST RECENT throw -- so a caught exception that
// was raised without a parameter name answers the previous throw's name, and a
// constructed exception read after an unrelated throw does the same.
//
// Every line must match the net48 oracle byte for byte.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class ProbeExcParamName
    {
        static string ParamViaParam(Exception ex)
        {
            return (ex as ArgumentException) == null
                ? "<not-an-arg-exception>"
                : ((ArgumentException)ex).ParamName;
        }

        public static void Main()
        {
            // 1. A constructed ArgumentException keeps the name its ctor was
            //    given, even after other exceptions are thrown.
            ArgumentException named = new ArgumentException("has a name", "firstParam");
            Console.WriteLine("a.early=" + named.ParamName);

            // 2. An unrelated throw that carries a DIFFERENT parameter name.
            try
            {
                int[] a = new int[1];
                a[9] = 1;
            }
            catch (IndexOutOfRangeException)
            {
            }

            // 3. The first exception must still answer its own name.
            Console.WriteLine("a.late=" + named.ParamName);

            // 4. An exception constructed WITHOUT a parameter name answers null,
            //    not whatever the last throw happened to carry.
            ArgumentException bare = new ArgumentException("no name here");
            Console.WriteLine("b.null=" + (bare.ParamName == null ? "<null>" : bare.ParamName));

            // 5. Two live exceptions must not interfere.
            ArgumentNullException p1 = new ArgumentNullException("alpha");
            ArgumentNullException p2 = new ArgumentNullException("beta");
            Console.WriteLine("c.p1=" + p1.ParamName);
            Console.WriteLine("c.p2=" + p2.ParamName);
            Console.WriteLine("c.p1again=" + p1.ParamName);

            // 6. A CAUGHT exception must carry the ParamName of the throw that
            //    raised it -- never a name left over from an earlier throw.
            //    Two different throws, each read through a helper (so the read
            //    happens after the exception crossed a method boundary).
            try
            {
                throw new ArgumentNullException("fromFirst");
            }
            catch (Exception ex)
            {
                Console.WriteLine("d.first=" + ParamViaParam(ex));
                Console.WriteLine("d.firstInline=" + ((ArgumentException)ex).ParamName);
            }

            try
            {
                throw new ArgumentOutOfRangeException("fromSecond");
            }
            catch (Exception ex)
            {
                Console.WriteLine("d.second=" + ParamViaParam(ex));
                Console.WriteLine("d.secondInline=" + ((ArgumentException)ex).ParamName);
            }
        }
    }
}
