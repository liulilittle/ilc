using System;
using System.Collections.Generic;
using System.Linq;

//------------------------------------------------------------------------------
// zip-ref -- Enumerable.Zip over a REFERENCE-element source, and the narrow
// (1/2-byte) result cell ABI.
//
// Before this batch the zip surface was value-cell only:
//   * a reference-element source was LOUD-rejected at transpile time (the old
//     reference branch called a value-result thunk through the
//     Il2CppObject* ABI and stored EAX as a GC pointer -- a measured
//     0xC0000005);
//   * a 1/2-byte integral / bool / char TResult had no cell ABI, so it was
//     rejected at transpile time.
//
// This batch adds the mixed entry (a reference side reads Il2CppObject*; the
// selector is called through its STATICALLY DECLARED ABI) and the narrow /
// reference result cells:
//   * reference result  -> a reference array;
//   * sbyte/byte/bool   -> a 1-byte cell;
//   * short/ushort/char -> a 2-byte cell.
//
// Every value is deterministic; the pairing stops at the SHORTER source.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class ZipRef
    {
        public static void Main()
        {
            string[] s1 = { "a", "bb", "ccc" };
            string[] s2 = { "x", "yy", "zzz" };
            int[] i1 = { 1, 2, 3 };
            int[] n1 = { 1, 200, 3 };

            // ===== reference source -> reference result =====
            foreach (var r in s1.Zip(s2, (a, b) => a + b))
                Console.WriteLine("[refref] " + r);

            // ===== reference source -> value result =====
            foreach (var r in s1.Zip(s2, (a, b) => a.Length + b.Length))
                Console.WriteLine("[refval] " + r);

            // ===== value source -> reference source, value result =====
            foreach (var r in i1.Zip(s2, (a, b) => b.Length * 10 + a))
                Console.WriteLine("[valref] " + r);

            // ===== narrow result cells =====
            byte[] bytes = n1.Zip(n1, (a, b) => (byte)(a + b)).ToArray();
            Console.WriteLine("[byte] " + bytes.Length + ":" + bytes[0] + "," + bytes[1] + "," + bytes[2]);

            short[] shorts = n1.Zip(n1, (a, b) => (short)(a * 100 + b)).ToArray();
            Console.WriteLine("[short] " + shorts[0] + "," + shorts[1] + "," + shorts[2]);

            char[] chars = n1.Zip(n1, (a, b) => (char)(64 + a + b)).ToArray();
            Console.WriteLine("[char] " + (int)chars[0] + "," + (int)chars[1] + "," + (int)chars[2]);

            // ===== List narrow result (the old loud-p0-17 shape) =====
            List<byte> bl = new List<byte> { 1, 200 };
            byte[] summed = bl.Zip(bl, (a, b) => (byte)(a + b)).ToArray();
            Console.WriteLine("[listbyte] " + summed.Length + ":" + summed[0] + "," + summed[1]);

            // ===== reference-element List source -> reference result =====
            List<string> sl = new List<string> { "p", "q" };
            foreach (var r in sl.Zip(sl, (a, b) => a + b))
                Console.WriteLine("[listref] " + r);
        }
    }
}
