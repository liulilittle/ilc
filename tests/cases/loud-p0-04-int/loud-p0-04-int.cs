using System;
using System.Linq;

//------------------------------------------------------------------------------
// loud-p0-04-int -- P0-04: GroupBy with NON-int32 integer KEY families.
//
// The runtime bucket key is an int32 slot (linq_map.h:249/400), so every
// integer key family the model carries in that slot must resolve to the same
// il2cpp_linq_group_by_full entry. Before B4-1 only 0x08 (int) keys resolved
// and every other integer key was LOUD ("int32 keys only").
//
// Covered keys: byte(0x05) sbyte(0x04) short(0x06) uint(0x09) long(0x0A)
// plus an int(0x08) control. Keys stay inside int32 so the key text matches
// net48 (the bucket is an int32 slot; bool/char keys are covered by the unit
// matrix, not here, because their TKey ToString differs from the int32
// carrier). The SOURCE stays int32[] -- a non-int32 source family is outside
// this batch (the group kernel indexes int32 elements).
//
// NOT in this e2e (unit-covered in MethodMapTests.B4, mapping-level): ushort
// (0x07) keys. The 0x07 resolution is wired, but an unrelated delegate gap
// breaks the value at runtime: the ushort-returning key-selector wrapper
// leaves the upper 16 bits of the return register undefined, so the int32
// bucket key reads garbage above the low half (observed 24464|0x729D0000).
// Also excluded: narrow TKey ToString (SByte/Int16/UInt16_ToString codegen
// gap) -- hence the explicit int/long casts below.
//
// net48 oracle (first-appearance bucket order, src {1,1,2,3}):
//   [gb_int]    key=1 n=3 / key=0 n=1
//   [gb_byte]   key=1 n=3 / key=0 n=1
//   [gb_sbyte]  key=-1 n=2 / key=0 n=1 / key=1 n=1
//   [gb_short]  key=5 n=2 / key=10 n=1 / key=15 n=1
//   [gb_uint]   key=1000 n=2 / key=2000 n=1 / key=3000 n=1
//   [gb_long]   key=-1 n=2 / key=-2 n=1 / key=-3 n=1
//------------------------------------------------------------------------------
namespace Cases
{
    public static class LoudP004Int
    {
        public static void Main()
        {
            int[] src = { 1, 1, 2, 3 };

            foreach (var g in src.GroupBy(x => x % 2))
                Console.WriteLine("[gb_int] key=" + g.Key + " n=" + g.Count());

            // The narrow TKey values print through an explicit int/long cast:
            // the bucket key is an int32 slot, and the narrow-type ToString
            // surfaces (SByte/Int16/UInt16_ToString) are a codegen gap outside
            // this batch -- the grouping itself must not depend on them.
            foreach (var g in src.GroupBy(x => (byte)(x % 2)))
                Console.WriteLine("[gb_byte] key=" + (int)g.Key + " n=" + g.Count());

            foreach (var g in src.GroupBy(x => (sbyte)(x - 2)))
                Console.WriteLine("[gb_sbyte] key=" + (int)g.Key + " n=" + g.Count());

            foreach (var g in src.GroupBy(x => (short)(x * 5)))
                Console.WriteLine("[gb_short] key=" + (int)g.Key + " n=" + g.Count());

            foreach (var g in src.GroupBy(x => (uint)(x * 1000)))
                Console.WriteLine("[gb_uint] key=" + (long)g.Key + " n=" + g.Count());

            foreach (var g in src.GroupBy(x => (long)(-x)))
                Console.WriteLine("[gb_long] key=" + g.Key + " n=" + g.Count());
        }
    }
}
