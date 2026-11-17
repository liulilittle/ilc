//------------------------------------------------------------------------------
// probe-catchcarrier.cs -- a caught exception must stay an OBJECT when it
// crosses a method boundary.
//
// The runtime transports exceptions as a native carrier that holds the type
// name; the catch handler entry is materialised as that name interned as a
// managed string. Inline `ex.GetType()` is folded at the catch site, so it
// looks right, but as soon as the variable is passed to a method whose
// parameter is typed System.Exception, GetType() reads the real descriptor of
// the carrier -- a System.String -- and the exception's identity is lost.
//
// This is a common shape: a shared `Log(Exception ex)` helper. Every line
// below must match the net48 oracle byte for byte.
//------------------------------------------------------------------------------
using System;
using System.Threading;

namespace Cases
{
    public static class ProbeCatchCarrier
    {
        static string TypeViaParam(Exception ex)
        {
            return ex.GetType().FullName;
        }

        static string NameViaParam(Exception ex)
        {
            return ex.GetType().Name;
        }

        static string NullCheckViaParam(Exception ex)
        {
            return (ex == null) ? "<null>" : "not-null";
        }

        static string MessageViaParam(Exception ex)
        {
            return ex.Message;
        }

        public static void Main()
        {
            // 1. Caught by the base type and reported INLINE: the catch-site
            //    fold answers correctly today and must keep doing so.
            try
            {
                int[] a = new int[1];
                a[9] = 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("idx.inline=" + ex.GetType().FullName);
                Console.WriteLine("idx.param=" + TypeViaParam(ex));
                Console.WriteLine("idx.name=" + NameViaParam(ex));
                Console.WriteLine("idx.notnull=" + NullCheckViaParam(ex));
            }

            // 2. A runtime-raised exception whose type name only exists in the
            //    throw chain: ThreadStateException is NOT one of the runtime's
            //    builtin descriptors, so this also proves the fix is not a
            //    hardcoded five-type table.
            try
            {
                Thread t = new Thread(delegate() { });
                t.Start();
                t.SetApartmentState(ApartmentState.MTA);
            }
            catch (Exception ex)
            {
                Console.WriteLine("apa.inline=" + ex.GetType().FullName);
                Console.WriteLine("apa.param=" + TypeViaParam(ex));
                Console.WriteLine("apa.name=" + NameViaParam(ex));
            }

            // 3. A user-thrown exception: `new` allocates a real object, so the
            //    carrier must survive the same boundary.
            try
            {
                throw new InvalidOperationException("boom");
            }
            catch (Exception ex)
            {
                Console.WriteLine("user.inline=" + ex.GetType().FullName);
                Console.WriteLine("user.param=" + TypeViaParam(ex));
                Console.WriteLine("user.msg=" + MessageViaParam(ex));
            }
        }
    }
}
