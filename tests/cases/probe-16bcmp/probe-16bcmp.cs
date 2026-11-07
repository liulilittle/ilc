// probe-16bcmp.cs -- b5-gaps §77: user IEqualityComparer over the 16-BYTE
// value-key / value-element families (System.Decimal / System.DateTime /
// System.TimeSpan / System.Guid / System.DateTimeOffset) across the
// set/sequence/contains ARRAY operators.
//
// Expected (net48 oracle): every comparer governs EQUALITY -- keys equal
// under the comparer merge (Distinct first-appearance, Union dedup, Intersect
// dedup, Except dedup, SequenceEqual pairwise) and Contains seeks by it. The
// comparers below are chosen so a silently-dropped comparer is observable
// (default equality would keep / reject differently). Results are pinned with
// Count (the comparer's merge effect on bucket/set SIZE) and Contains/SeqEq
// booleans -- the 16-byte element iteration surface is a separate wiring gap.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class Probe16bCmp
    {
        public static void Main()
        {
            // [a1] decimal[] Distinct: trunc-parity merges 1.5/3.5 and 2.5/4.5 -> 2
            decimal[] mxs = { 1.5m, 3.5m, 2.5m, 4.5m };
            Console.WriteLine("a1:" + mxs.Distinct(new ParityDecimalComparer()).Count());

            // [a2] decimal[] Except: drop the trunc-odd bucket -> {2.5} -> 1
            decimal[] mxs2 = { 1.5m, 3.5m, 2.5m, 4.5m };
            decimal[] msnd = { 3.5m };
            Console.WriteLine("a2:" + mxs2.Except(msnd, new ParityDecimalComparer()).Count());

            // [a5/a6] decimal[] SequenceEqual
            Console.WriteLine("a5:" + new[] { 1.5m, 2.5m }.SequenceEqual(new[] { 3.5m, 4.5m }, new ParityDecimalComparer()));
            Console.WriteLine("a6:" + new[] { 1.5m, 2.5m }.SequenceEqual(new[] { 3.5m, 6.0m }, new ParityDecimalComparer()));

            // [a7/a8] decimal[] Contains
            Console.WriteLine("a7:" + mxs.Contains(1.5m, new ParityDecimalComparer()));
            Console.WriteLine("a8:" + mxs.Contains(2.0m, new ParityDecimalComparer()));

            // [b1] DateTime[] Distinct: day-parity merges day1/day3, day2/day4 -> 2
            DateTime[] dts = {
                new DateTime(2020, 1, 1), new DateTime(2020, 1, 3),
                new DateTime(2020, 1, 2), new DateTime(2020, 1, 4) };
            Console.WriteLine("b1:" + dts.Distinct(new DayParityComparer()).Count());

            // [b2] DateTime[] Contains by day-parity (day7 odd -> in)
            Console.WriteLine("b2:" + dts.Contains(new DateTime(2020, 2, 7), new DayParityComparer()));

            // [b3] DateTime[] Except (even days removed -> {odd} -> 2), [b4] Union,
            // [b5] Intersect, [b6] SeqEq
            DateTime[] dt2 = { new DateTime(2020, 1, 2), new DateTime(2020, 3, 1) };
            Console.WriteLine("b3:" + dts.Except(dt2, new DayParityComparer()).Count());
            Console.WriteLine("b4:" + new[] { new DateTime(2020, 1, 1) }.Union(dt2, new DayParityComparer()).Count());
            Console.WriteLine("b5:" + new[] { new DateTime(2020, 1, 3) }.Intersect(dt2, new DayParityComparer()).Count());
            Console.WriteLine("b6:" + new[] { new DateTime(2020, 1, 1), new DateTime(2020, 1, 2) }
                .SequenceEqual(new[] { new DateTime(2020, 1, 3), new DateTime(2020, 1, 4) }, new DayParityComparer()));

            // [c1] TimeSpan[] Distinct: tick-parity merges 1s/3s -> {1s,2s} -> 2
            TimeSpan[] tss = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2) };
            Console.WriteLine("c1:" + tss.Distinct(new TickParityComparer()).Count());

            // [c2] TimeSpan[] Contains by tick-parity (5s odd -> in)
            Console.WriteLine("c2:" + tss.Contains(TimeSpan.FromSeconds(5), new TickParityComparer()));

            // [d1] Guid[] Distinct: last-byte-parity -> {01,06} -> 2
            Guid g1 = new Guid("00000000-0000-0000-0000-000000000001");
            Guid g2 = new Guid("00000000-0000-0000-0000-000000000003");
            Guid g3 = new Guid("00000000-0000-0000-0000-000000000006");
            Guid[] gs = { g1, g2, g3 };
            Console.WriteLine("d1:" + gs.Distinct(new GuidParityComparer()).Count());

            // [d2] Guid[] Contains by last-byte-parity (09 odd -> in)
            Guid g4 = new Guid("00000000-0000-0000-0000-000000000009");
            Console.WriteLine("d2:" + gs.Contains(g4, new GuidParityComparer()));

            // [e1] DateTimeOffset[] Distinct: hour-parity -> {1,2} -> 2
            DateTimeOffset[] dzs = {
                new DateTimeOffset(new DateTime(2020, 1, 1, 1, 0, 0), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2020, 1, 1, 3, 0, 0), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2020, 1, 1, 2, 0, 0), TimeSpan.Zero) };
            Console.WriteLine("e1:" + dzs.Distinct(new HourParityDtoComparer()).Count());

            // [e2] DateTimeOffset[] Contains by hour-parity (5 odd -> in)
            Console.WriteLine("e2:" + dzs.Contains(
                new DateTimeOffset(new DateTime(2020, 2, 1, 5, 0, 0), TimeSpan.Zero),
                new HourParityDtoComparer()));

            // [f1] decimal[] Distinct WITH GC.Collect inside the comparer
            // (rooting across the linear scan)
            decimal[] gcs = { 1.5m, 3.5m, 2.5m, 4.5m };
            Console.WriteLine("f1:" + gcs.Distinct(new GcParityDecimalComparer()).Count());

            Console.WriteLine("done");
        }
    }

    internal sealed class ParityDecimalComparer : IEqualityComparer<decimal>
    {
        public bool Equals(decimal a, decimal b) { return (decimal.ToInt32(decimal.Truncate(a)) & 1) == (decimal.ToInt32(decimal.Truncate(b)) & 1); }
        public int GetHashCode(decimal v) { return decimal.ToInt32(decimal.Truncate(v)) & 1; }
    }
    internal sealed class DayParityComparer : IEqualityComparer<DateTime>
    {
        public bool Equals(DateTime a, DateTime b) { return (a.Day & 1) == (b.Day & 1); }
        public int GetHashCode(DateTime v) { return v.Day & 1; }
    }
    internal sealed class TickParityComparer : IEqualityComparer<TimeSpan>
    {
        public bool Equals(TimeSpan a, TimeSpan b) { return (a.Ticks & 1) == (b.Ticks & 1); }
        public int GetHashCode(TimeSpan v) { return (int)(v.Ticks & 1); }
    }
    internal sealed class GuidParityComparer : IEqualityComparer<Guid>
    {
        public bool Equals(Guid a, Guid b) { return (a.ToByteArray()[15] & 1) == (b.ToByteArray()[15] & 1); }
        public int GetHashCode(Guid v) { return v.ToByteArray()[15] & 1; }
    }
    internal sealed class HourParityDtoComparer : IEqualityComparer<DateTimeOffset>
    {
        public bool Equals(DateTimeOffset a, DateTimeOffset b) { return (a.Hour & 1) == (b.Hour & 1); }
        public int GetHashCode(DateTimeOffset v) { return v.Hour & 1; }
    }
    internal sealed class GcParityDecimalComparer : IEqualityComparer<decimal>
    {
        public bool Equals(decimal a, decimal b)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return (decimal.ToInt32(decimal.Truncate(a)) & 1) == (decimal.ToInt32(decimal.Truncate(b)) & 1);
        }
        public int GetHashCode(decimal v) { return decimal.ToInt32(decimal.Truncate(v)) & 1; }
    }
}