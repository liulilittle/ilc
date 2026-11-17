//------------------------------------------------------------------------------
// probe-apartment-fail.cs -- the REFUSAL half of the Thread apartment surface.
//
// probe-half-nonp0 covers the accepting half (unstarted thread, MTA request).
// This case covers what net48 does when the request must be refused, which is
// where the two entry points differ:
//
//   TrySetApartmentState  is NOT a swallowing variant of SetApartmentState:
//                         on a started thread net48 THROWS, it does not
//                         answer false.
//   SetApartmentState     throws the same ThreadStateException.
//   invalid enum value    is an ArgumentOutOfRangeException, not a state error.
//
// SCOPE LIMIT: the STA-accepting paths are NOT asserted here. On this oracle
// (net48, no STA message pump) accepting an STA request leaves the process
// unable to print an exception ("Exception.ToString() failed"), so the oracle
// for those paths is not trustworthy. They are covered separately.
//
// Every line printed here must match the net48 oracle byte for byte.
//------------------------------------------------------------------------------
using System;
using System.Threading;

namespace Cases
{
    public static class ProbeApartmentFail
    {
        // Type only, read INLINE at the catch site. net48 cannot format these
        // exceptions, so Message and ToString must never be touched. The read
        // must stay inline: this runtime's catch variable is a type-name
        // carrier, and handing it to a helper whose parameter is typed
        // System.Exception degrades GetType() to System.String (a separate
        // known defect -- docs/audit/half-wired-nonp0.md).
        public static void Main()
        {
            // 1. TrySetApartmentState on a STARTED thread. net48 throws here --
            //    a Try* method that throws is surprising, but it is what the
            //    reference runtime does, so it is the contract.
            Thread started = new Thread(delegate() { });
            started.Start();
            try
            {
                bool r = started.TrySetApartmentState(ApartmentState.MTA);
                Console.WriteLine("f.try_started=returned|" + r);
            }
            catch (Exception ex)
            {
                Console.WriteLine("f.try_started=threw|" + ex.GetType().FullName);
            }
            started.Join();

            // 2. SetApartmentState on a STARTED thread: ThreadStateException.
            Thread started2 = new Thread(delegate() { });
            started2.Start();
            try
            {
                started2.SetApartmentState(ApartmentState.MTA);
                Console.WriteLine("f.set_started=<no throw>");
            }
            catch (Exception ex)
            {
                Console.WriteLine("f.set_started=threw|" + ex.GetType().FullName);
            }
            started2.Join();

            // 3. SetApartmentState on a TERMINATED thread is refused the same
            //    way: the thread can no longer change apartment.
            Thread dead = new Thread(delegate() { });
            dead.Start();
            dead.Join();
            try
            {
                dead.SetApartmentState(ApartmentState.MTA);
                Console.WriteLine("f.set_dead=<no throw>");
            }
            catch (Exception ex)
            {
                Console.WriteLine("f.set_dead=threw|" + ex.GetType().FullName);
            }

            // 4. An out-of-range value is an ARGUMENT error even on an unstarted
            //    thread, where every valid request would be accepted.
            Thread bogus = new Thread(delegate() { });
            try
            {
                bogus.SetApartmentState((ApartmentState)99);
                Console.WriteLine("f.set_bogus=<no throw>");
            }
            catch (Exception ex)
            {
                Console.WriteLine("f.set_bogus=threw|" + ex.GetType().FullName);
            }

            // 5. The Try form reports the same invalid value the same way.
            Thread bogus2 = new Thread(delegate() { });
            try
            {
                bool r = bogus2.TrySetApartmentState((ApartmentState)99);
                Console.WriteLine("f.try_bogus=returned|" + r);
            }
            catch (Exception ex)
            {
                Console.WriteLine("f.try_bogus=threw|" + ex.GetType().FullName);
            }
        }
    }
}
