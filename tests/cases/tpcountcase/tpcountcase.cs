using System;
using System.Threading;

// tpcountcase.cs -- ThreadPool limit probe for net48 -> ILC byte-exact
// alignment. Covers the FULL public surface of System.Threading.ThreadPool
// limits available on a .NET Framework 4.8 build:
//
//   GetMinThreads(out worker, out io)
//   GetMaxThreads(out worker, out io)
//   GetAvailableThreads(out worker, out io)
//   Environment.ProcessorCount   (the machine the net48 pool sizes scale with)
//
// NOTES (evidence-based, measured on the oracle host):
//   * net48 ThreadPool has NO ThreadCount member (not even internal) -- that
//     member is net5+ surface only, so it cannot and must not appear in a
//     net48-targeted byte-exact probe (verified: reflection over the loaded
//     net48 ThreadPool type enumerates zero static properties).
//   * The probe is read-only: it queues NO work items, so net48
//     GetAvailableThreads reports the quiescent value (max - 0 in flight).
//   * The values scale with the machine's processor count (net48 default
//     min = #processors). Byte-exactness therefore requires the SAME host
//     for oracle and ILC -- which the harness guarantees by running both
//     sides on one machine.
namespace Cases { public static class TpCountCase {
    public static void Main()
    {
        int minW, minIO, maxW, maxIO, availW, availIO;
        ThreadPool.GetMinThreads(out minW, out minIO);
        ThreadPool.GetMaxThreads(out maxW, out maxIO);
        ThreadPool.GetAvailableThreads(out availW, out availIO);
        int pc = Environment.ProcessorCount;
        Console.WriteLine("minW: " + minW);
        Console.WriteLine("minIO: " + minIO);
        Console.WriteLine("maxW: " + maxW);
        Console.WriteLine("maxIO: " + maxIO);
        Console.WriteLine("availW: " + availW);
        Console.WriteLine("availIO: " + availIO);
        Console.WriteLine("processorCount: " + pc);
    }
}}