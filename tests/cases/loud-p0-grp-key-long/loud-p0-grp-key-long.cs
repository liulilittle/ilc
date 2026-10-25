using System;
using System.Linq;

//------------------------------------------------------------------------------
// p0grp key-carrier companion -- GroupBy over a key family whose bucket key is
// wider than the int32 slot.
//
// The stage-1 grouping surface used to be an int32-slot key, so a long / ulong /
// decimal / DateTime / Guid key was LOUD-rejected at transpile time (the int32
// bucket would truncate a 64-bit key, or reinterpret a 16-byte struct). This
// batch adds a 16-byte key carrier to the grouping bucket, so each key family
// buckets by its REAL value:
//
//   * long / ulong   -- the 64-bit value (ulong rides the same bit pattern);
//   * decimal        -- the raw 16 bytes (sign / scale / mantissa preserved);
//   * DateTime       -- ticks + kind (the struct's trailing padding is not part
//                       of the key);
//   * Guid           -- the raw 16 bytes.
//
// Every statement is deterministic: the keys are distinct constants and the
// source order is preserved (first-appearance bucket order, net48 Lookup).
//
// The result-selector overload GroupBy(keySelector, resultSelector) is wired
// for the int64 key too: the leaf receives the key in its REAL 64-bit slot and
// the bucket's element array second.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class LoudP0GrpKeyLong
    {
        public static void Main()
        {
            int[] xs = { 1, 2, 3, 2, 1, 3 };

            // ===== long key =====
            foreach (var g in xs.GroupBy(x => (long)x))
                Console.WriteLine("[long] key=" + g.Key + " n=" + g.Count());

            // A value above int32.MaxValue: an int32 bucket would collapse these
            // into one truncated key; the 64-bit carrier keeps them apart.
            foreach (var g in xs.GroupBy(x => 3000000000L + x))
                Console.WriteLine("[longbig] key=" + g.Key + " n=" + g.Count());

            // ===== ulong key (bit pattern) =====
            foreach (var g in xs.GroupBy(x => (ulong)(uint)x))
                Console.WriteLine("[ulong] key=" + g.Key + " n=" + g.Count());

            // ===== decimal key (16-byte struct carrier) =====
            foreach (var g in xs.GroupBy(x => (decimal)x))
                Console.WriteLine("[dec] key=" + g.Key + " n=" + g.Count());

            // ===== DateTime key (ticks + kind carrier) =====
            foreach (var g in xs.GroupBy(x => new DateTime(2020, 1, 1).AddDays(x)))
                Console.WriteLine("[dt] key=" + g.Key.ToString("yyyy-MM-dd") + " n=" + g.Count());

            // ===== Guid key (16-byte carrier) =====
            foreach (var g in xs.GroupBy(x => new Guid("00000000-0000-0000-0000-00000000000" + x)))
                Console.WriteLine("[guid] key=" + g.Key + " n=" + g.Count());

            // ===== result selector over a long key =====
            // The leaf receives the key in its REAL 64-bit slot and the bucket's
            // element array second; the result is a string (reference family).
            foreach (var r in xs.GroupBy(x => (long)x, (k, e) => k + ":" + e.Count()))
                Console.WriteLine("[longres] " + r);
        }
    }
}
