// Minimal reproduction of the catch-entry + isinst shape.
// Variant A: `as` is the ONLY use of the caught variable.
// Variant B: `is` is the ONLY use.
using System;

namespace Cases
{
    public static class ProbeCatchF6a
    {
        static string AsOnly()
        {
            try { throw new InvalidOperationException("a"); }
            catch (Exception ex)
            {
                Exception r = ex as Exception;
                return (r == null) ? "null" : "nonnull";
            }
        }

        static string IsOnly()
        {
            try { throw new InvalidOperationException("b"); }
            catch (Exception ex)
            {
                return (ex is Exception) ? "yes" : "no";
            }
        }

        public static void Main()
        {
            Console.WriteLine("f6a.as_only=" + AsOnly());
            Console.WriteLine("f6a.is_only=" + IsOnly());
        }
    }
}
