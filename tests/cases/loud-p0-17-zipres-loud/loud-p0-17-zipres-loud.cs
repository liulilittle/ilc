using System;
using System.Collections.Generic;
using System.Linq;

//------------------------------------------------------------------------------
// zipres -- the narrow (1/2-byte) Zip RESULT cell ABI.
//
// Before this batch the wide/narrow zip surface wrote only a 4- or 8-byte
// result cell, so a `byte` TResult (a 1-byte cell) had no ABI: the transpiler
// rejected the shape at transpile time (this case used to be a LOUD contract).
//
// This batch adds the narrow result cells (sbyte/byte/bool -> 1 byte,
// short/ushort/char -> 2 bytes), so the case is now a real byte-exact
// assertion: the Zip result is a genuine byte[] whose cells hold the truncated
// sum. The full mixed reference-source surface is covered by tests/cases/
// zip-ref.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class LoudP017ZipResLoud
    {
        public static void Main()
        {
            List<byte> bl = new List<byte> { 1, 200 };
            byte[] summed = bl.Zip(bl, (a, b) => (byte)(a + b)).ToArray();
            Console.WriteLine("[byte-res]=" + summed.Length + ":" + summed[0] + "," + summed[1]);

            byte[] b2 = new byte[] { 3, 4, 5 };
            byte[] b3 = new byte[] { 10, 20 };
            byte[] r2 = b2.Zip(b3, (a, b) => (byte)(a + b)).ToArray();
            Console.WriteLine("[byte-arr]=" + r2.Length + ":" + r2[0] + "," + r2[1]);
        }
    }
}
