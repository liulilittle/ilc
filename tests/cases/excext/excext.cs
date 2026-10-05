using System;
using System.Collections.Generic;

namespace Cases
{
    // excext -- the common exception factories exercised end to end.
    // Every listed type derives from SystemException, so the shared Run()
    // catch shows both the exact type marker and the message; two extra
    // blocks prove base-class catching (ArgumentException catches
    // ArgumentNullException) and root catching (Exception catches all).
    public static class Excext
    {
        public static void Main()
        {
            Run(() => { throw new ArgumentNullException("param"); });
            Run(() => { throw new ArgumentException("bad argument"); });
            Run(() => { throw new ArgumentOutOfRangeException((string)null, "out of range"); });
            Run(() => { throw new InvalidOperationException("invalid op"); });
            Run(() => { throw new IndexOutOfRangeException("index out of range"); });
            Run(() => { throw new NullReferenceException("null reference"); });
            Run(() => { throw new DivideByZeroException("divide by zero"); });
            Run(() => { throw new OverflowException("overflow"); });
            Run(() => { throw new NotSupportedException("not supported"); });
            Run(() => { throw new KeyNotFoundException("key not found"); });
            Run(() => { throw new FormatException("bad format"); });

            // Base-class catch: ArgumentException catches ArgumentNullException.
            try { throw new ArgumentNullException("nested"); }
            catch (ArgumentException ex) { Console.WriteLine("base|" + ex.GetType().Name + "|" + ex.Message); }

            // Root catch: Exception catches everything.
            try { throw new FormatException("root"); }
            catch (Exception ex) { Console.WriteLine("root|" + ex.GetType().Name + "|" + ex.Message); }
        }

        static void Run(Action body)
        {
            try { body(); }
            catch (SystemException ex) { Console.WriteLine(ex.GetType().Name + "|" + ex.Message); }
        }
    }
}
