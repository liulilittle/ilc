// probe-groupbykey.cs -- b5-gaps 17.2 (3): the GroupBy KEY-FAMILY residue.
//
// Baseline LOUDs covered here:
//   * GroupBy<int32,TimeSpan>  -> "a value-type (System.TimeSpan) key has no
//                                 runtime key carrier";
//   * GroupBy<int64,DateTime>  -> "the stage-1 runtime LINQ surface is int32
//                                 (0x08) element-family";
//   * a USER-ENUM key          -> "a value-type (Cases.Color) key has no
//                                 runtime key carrier".
//
// Semantics locked against the net48 oracle (probe p1/eq.cs):
//   * a TimeSpan key buckets by TICKS (a 1.5 s span built from milliseconds
//     and from ticks is ONE bucket);
//   * a user enum key buckets by its underlying int32 value;
//   * DateTime/DateTimeOffset keys bucket by the INSTANT (a DateTimeOffset at
//     +02:00 and the same UTC instant at +00:00 are ONE bucket), and get_Key
//     reports the FIRST key of the bucket (the offset round-trips);
//   * long / byte / double SOURCES bucket exactly like an int32 source
//     (first-appearance order; a key-only bucket holds the source elements);
//   * a REFERENCE key + result selector receives the REAL key object (not the
//     int32 carrier).
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public enum Color { Red = 1, Green = 2, Blue = 3 }

    public static class ProbeGroupByKey
    {
        public static void Main()
        {
            // ===== 1. TimeSpan KEY over an int32 source =====
            int[] xs = { 0, 1, 2, 0, 1, 2 };
            foreach (var g in xs.GroupBy(x => TimeSpan.FromSeconds(x)))
                Console.WriteLine("[ts] key=" + g.Key.Ticks + " n=" + g.Count());
            // The same span built two ways is the SAME bucket (ticks equality).
            int[] ys = { 1, 1, 2 };
            foreach (var g in ys.GroupBy(x => x == 1
                         ? TimeSpan.FromMilliseconds(1500)
                         : TimeSpan.FromTicks(15000000)))
                Console.WriteLine("[ts2] key=" + g.Key.Ticks + " n=" + g.Count());

            // ===== 2. USER-ENUM key over an int32 source =====
            int[] es = { 1, 2, 3, 1, 2, 1 };
            foreach (var g in es.GroupBy(x => (Color)x))
                Console.WriteLine("[enum] key=" + (int)g.Key + " n=" + g.Count());

            // ===== 3. long SOURCE (int64 cells) =====
            long[] ls = { 5L, 3L, 5L, 9L };
            foreach (var g in ls.GroupBy(x => x % 3))
            {
                Console.WriteLine("[i8] key=" + g.Key + " n=" + g.Count());
                foreach (long e in g) Console.WriteLine("[i8] e=" + e);
            }

            // ===== 4. long source, DateTime key =====
            long[] ds = { 0L, 1L, 2L, 3L };
            foreach (var g in ds.GroupBy(x => new DateTime(2020, 1, 1).AddDays(x % 2)))
                Console.WriteLine("[i8dt] key=" + g.Key.Day + " n=" + g.Count());

            // ===== 5. byte SOURCE (1-byte cells, widened into the int32 slot) =====
            byte[] bs = { 7, 8, 7, 9 };
            foreach (var g in bs.GroupBy(x => x % 2))
            {
                Console.WriteLine("[narrow] key=" + g.Key + " n=" + g.Count());
                foreach (byte e in g) Console.WriteLine("[narrow] e=" + e);
            }

            // ===== 6. double SOURCE (8-byte cells) =====
            double[] vs = { 1.5, 2.5, 1.5 };
            foreach (var g in vs.GroupBy(x => x > 2.0 ? 1 : 0))
            {
                Console.WriteLine("[d] key=" + g.Key + " n=" + g.Count());
                foreach (double e in g) Console.WriteLine("[d] e=" + e);
            }

            // ===== 7. DateTimeOffset key: instant equality, first-key offset =====
            int[] zs = { 0, 1, 2 };
            foreach (var g in zs.GroupBy(x => x == 2
                         ? new DateTimeOffset(new DateTime(2020, 1, 1), TimeSpan.FromHours(2))
                         : new DateTimeOffset(new DateTime(2020, 1, 1), TimeSpan.Zero)))
                Console.WriteLine("[dto] key=" + g.Key.Offset.Ticks + " n=" + g.Count());

            // ===== 8. REFERENCE key + result selector (the key object, not the
            // int32 carrier) =====
            string[] ss = { "a", "bb", "a" };
            foreach (string s in ss.GroupBy(x => x, (k, g) => k))
                Console.WriteLine("[refrs] " + s);

            // ===== 9. decimal KEY compares by VALUE, not by the 16-byte image
            // (1.0m / 1.00m / 1.000m are ONE bucket; the raw-image compare kept
            // them apart because the scale bits differ). =====
            decimal[] dk = { 1.0m, 1.00m, 1.000m, 2m };
            foreach (var g in dk.GroupBy(x => x))
                Console.WriteLine("[deckey] n=" + g.Count());

            // ===== 10. DateTime KEY compares TICKS only -- the Kind is NOT part
            // of the key (Utc / Local / Unspecified with the same ticks are ONE
            // bucket; the old keyHi=kind compare split them). =====
            long ticks = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            int[] kinds = { 0, 1, 2 };
            foreach (var g in kinds.GroupBy(k => new DateTime(ticks, (DateTimeKind)k)))
                Console.WriteLine("[dtkey] key=" + g.Key.Ticks + " n=" + g.Count());

            Console.WriteLine("done");
        }
    }
}
