using System;
using System.Collections.Generic;
using System.Linq;

// LOUD P0-17: a Zip TResult family with a 1- or 2-byte cell (sbyte/byte/short/
// ushort) -- and equally bool/char -- must be rejected at TRANSPILE time.
//
// The wide/narrow zip surface writes a 4- or 8-byte result cell (int32/uint32/
// int64/double/float, e.g. the float case rides the 8-byte double carrier).
// A `byte` result (TResult == System.Byte) has a 1-byte cell, so there is no
// ABI to write it: before the R5 fix the transpiler emitted resKind 0 and the
// runtime wrote an int32 into a cell the caller reads as one byte -- a silent
// wrong value (and an out-of-bounds write for the last element). It is now a
// NotSupportedException at transpile time (see docs/audit/b5-gaps.md section 4).
//
// This case is EXPECTED TO FAIL TO TRANSPILE; the harness records the LOUD
// message as the evidence.
namespace Cases
{
    public static class LoudP017ZipResLoud
    {
        public static void Main()
        {
            List<byte> bl = new List<byte> { 1, 200 };
            byte[] summed = bl.Zip(bl, (a, b) => (byte)(a + b)).ToArray();
            Console.WriteLine("[byte-res]=" + summed.Length);
        }
    }
}
