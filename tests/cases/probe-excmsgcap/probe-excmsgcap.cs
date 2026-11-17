//------------------------------------------------------------------------------
// probe-excmsgcap.cs -- a thrown exception's message must survive construction
// of MANY other exceptions.
//
// The runtime keeps an exception's message in a side table keyed by object
// identity. That table is a fixed array: when it fills, the capture is silently
// skipped, so an exception constructed after the limit reports the type-based
// default text instead of the message its constructor was given. net48 has no
// such limit -- the message lives on the instance.
//
// This probe builds more exceptions than the table can hold, keeps the FIRST
// one alive, and asks for its message at the end. Every line must match the
// net48 oracle byte for byte.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class ProbeExcMsgCap
    {
        public static void Main()
        {
            // The first exception is the one whose message must not be lost.
            Exception first = new InvalidOperationException("first-message");
            Console.WriteLine("first.early=" + first.Message);

            // Overflow the side table. The loop keeps every instance reachable
            // through an array so a collector cannot reclaim them and free a
            // slot -- the table is indexed by identity, not by liveness.
            Exception[] keep = new Exception[128];
            for (int i = 0; i < keep.Length; i++)
            {
                keep[i] = new InvalidOperationException("filler-" + i);
            }

            // The evidence: an exception constructed AFTER the table filled.
            Console.WriteLine("late.msg=" + keep[127].Message);

            // And the first one, which a lost entry would have degraded.
            Console.WriteLine("first.late=" + first.Message);
        }
    }
}
