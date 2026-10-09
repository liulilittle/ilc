//------------------------------------------------------------------------------
// p-gn11mdimalloc.cs -- GN-11 probe: multidim ALLOCATION with a COMPLEX (array)
// element type. `new int[2,2][,]` / `new int[2,2][]` / `new object[2,2][,]`
// all lower to an ARRAY ctor whose DECLARING TypeSpec is
// `14 <element> <rank> <numSizes> ... <numLoBounds> ...`, where <element> is
// itself an ARRAY (0x14 multidim / 0x1D SZARRAY) -- a self-delimiting full
// type. At HEAD TryDecodeMultidimCtor reads the element byte then treats the
// element's FIRST byte as the rank (int[,][,] -> rank 8 instead of 2) and
// MdElemSuffix rejects the 0x14/0x1D element byte ("multidim array element
// byte 0x14 in M3 (allocation)"), so the whole Main is not transpiled.
//
// The fix fully consumes the element type before reading the outer rank; a
// reference element (any array/class) maps to the existing "object" slot.
//
// Only allocation + length probes are exercised (no element indexing).
//
// net48 is the reference baseline; every line must print byte-exact.
//------------------------------------------------------------------------------
using System;

namespace Cases {
public static class PGn11MdimAlloc {
    public static void Main() {
        // ---- 0. control: plain int[,] ----
        int[,] a0 = new int[2, 2];
        Console.WriteLine(a0.Length);            // 4
        Console.WriteLine(a0.Rank);              // 2
        Console.WriteLine(a0.GetLength(0));      // 2
        Console.WriteLine(a0.GetLength(1));      // 2
        Console.WriteLine(a0.GetUpperBound(0));  // 1

        // ---- 1. int[,][,] : rank-2 array of int[,] (multidim element) ----
        int[,][,] a1 = new int[2, 2][,];
        Console.WriteLine(a1.Length);            // 4
        Console.WriteLine(a1.Rank);              // 2
        Console.WriteLine(a1.GetLength(0));      // 2
        Console.WriteLine(a1.GetLength(1));      // 2
        Console.WriteLine(a1.GetUpperBound(1));  // 1

        // ---- 2. int[,][] : rank-2 array of int[] (SZARRAY element) ----
        int[,][] a2 = new int[2, 2][];
        Console.WriteLine(a2.Length);            // 4
        Console.WriteLine(a2.Rank);              // 2
        Console.WriteLine(a2.GetLength(0));      // 2
        Console.WriteLine(a2.GetLength(1));      // 2

        // ---- 3. object[,][,] : rank-2 array of object[,] ----
        object[,][,] a3 = new object[2, 2][,];
        Console.WriteLine(a3.Length);            // 4
        Console.WriteLine(a3.Rank);              // 2
        Console.WriteLine(a3.GetLength(0));      // 2
        Console.WriteLine(a3.GetUpperBound(0));  // 1
    }
}}
