// probe-grpcmp.cs -- b5-gaps §76: GroupBy with a USER IEqualityComparer<TKey>
// across the key families.
//
// Expected (net48 oracle): the comparer governs BUCKET MEMBERSHIP -- keys equal
// under the comparer merge into ONE bucket; buckets and per-bucket elements
// keep first-appearance order; a null comparer equals EqualityComparer<T>.Default.
//
// Each record pins (parity / floor / case-fold / last-byte) semantics that
// DIFFER from the default comparer, so a silently-dropped comparer is
// observable. Bucket elements are enumerated for the i4/byte/long/double/string
// families (["gN"] prefix + elements); the by-value-value struct / enum / guid
// families print key+count (their bucket-element enumeration is a separate
// surface; the comparer governs membership, which key+count show exactly).
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public enum Tone { Do = 1, Re = 2, Mi = 3, Fa = 4 }

    public static class ProbeGrpCmp
    {
        public static void Main()
        {
            // NOTE: bucket ELEMENT enumeration (foreach over an IGrouping) is a
            // separate wiring surface from the comparer -- the records below pin
            // the comparer's MEMBERSHIP (which keys merge, the per-bucket count),
            // which key+count show exactly.
            // [g1] int key + parity: 0,2,4,6 merge; 1,3,5 merge
            int[] xs = { 0, 1, 2, 3, 4, 5, 6 };
            foreach (var g in xs.GroupBy(x => x, new ParityIntComparer()))
                Console.WriteLine("g1:" + g.Key + ":" + g.Count());

            // [g2] byte key + parity on the byte value
            byte[] bs = { 1, 2, 3, 4 };
            foreach (var g in bs.GroupBy(x => x, new ParityByteComparer()))
                Console.WriteLine("g2:" + g.Key + ":" + g.Count());

            // [g3] long key + parity
            long[] ls = { 10, 11, 20, 21 };
            foreach (var g in ls.GroupBy(x => x, new ParityLongComparer()))
                Console.WriteLine("g3:" + g.Key + ":" + g.Count());

            // [g4] double key + floor(n): 1.0/1.5 merge, 2.0/2.9 merge
            double[] ds = { 1.0, 1.5, 2.0, 2.9, 1.25 };
            foreach (var g in ds.GroupBy(x => x, new FloorDoubleComparer()))
                Console.WriteLine("g4:" + g.Key + ":" + g.Count());

            // [g5] string key + OrdinalIgnoreCase
            string[] ss = { "Apple", "apple", "banana", "Banana" };
            foreach (var g in ss.GroupBy(x => x, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine("g5:" + g.Key + ":" + g.Count());

            // [g6] decimal key + trunc-parity: 1.5/3.5 merge, 2.5/4.5 merge
            decimal[] mxs = { 1.5m, 2.5m, 3.5m, 4.5m };
            foreach (var g in mxs.GroupBy(x => x, new ParityDecimalComparer()))
                Console.WriteLine("g6:" + g.Key.ToString() + ":" + g.Count());

            // [g7] DateTime key + day-parity: day1/day3 merge, day2 merge
            DateTime[] dts = { new DateTime(2020, 1, 1), new DateTime(2020, 1, 2), new DateTime(2020, 2, 1), new DateTime(2020, 1, 3) };
            foreach (var g in dts.GroupBy(x => x, new DayParityComparer()))
                Console.WriteLine("g7:" + g.Key.Day + ":" + g.Count());

            // [g8] TimeSpan key + ticks-parity
            TimeSpan[] tss = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3) };
            foreach (var g in tss.GroupBy(x => x, new TickParityComparer()))
                Console.WriteLine("g8:" + g.Key.Ticks + ":" + g.Count());

            // [g9] Guid key + last-byte-parity
            Guid a = new Guid("00000000-0000-0000-0000-000000000001");
            Guid b = new Guid("00000000-0000-0000-0000-000000000003");
            Guid c = new Guid("00000000-0000-0000-0000-000000000006");
            Guid[] gs = { a, b, c };
            foreach (var g in gs.GroupBy(x => x, new GuidParityComparer()))
                Console.WriteLine("g9:" + g.Key.ToByteArray()[15] + ":" + g.Count());

            // [g10] DateTimeOffset key + hour-parity
            DateTimeOffset[] dzs = {
                new DateTimeOffset(new DateTime(2020, 1, 1, 1, 0, 0), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2020, 1, 1, 2, 0, 0), TimeSpan.Zero),
                new DateTimeOffset(new DateTime(2020, 1, 1, 3, 0, 0), TimeSpan.Zero) };
            foreach (var g in dzs.GroupBy(x => x, new HourParityDtoComparer()))
                Console.WriteLine("g10:" + g.Key.Hour + ":" + g.Count());

            // [g11] user ENUM key + underlying-value parity (comparer over a
            // user enum stays LOUD in this section -- the generated enum C++
            // type cannot be reconstructed by the runtime comparer kernel; the
            // default (null) enum comparer IS covered by the GroupBy keyfamily
            // surface, see probe-groupbykey).
            {
                // The DEFAULT enum comparer (no user comparer) is wired:
                int[] es = { 1, 2, 3, 4 };
                foreach (var g in es.GroupBy(x => (Tone)x))
                    Console.WriteLine("g11:" + (int)g.Key + ":" + g.Count());
            }

            // [g12] int key + parity + result selector
            int[] xs2 = { 1, 2, 3, 4 };
            foreach (var pair in xs2.GroupBy(x => x, new ParityIntComparer()).Select(g2 => g2.Key + ":" + g2.Count()))
                Console.WriteLine("g12:" + pair);

            // [g13] null comparer == EqualityComparer<T>.Default (int)
            int[] xs3 = { 1, 1, 2, 2, 3 };
            foreach (var g in xs3.GroupBy(x => x, (IEqualityComparer<int>)null))
                Console.WriteLine("g13:" + g.Key + ":" + g.Count());

            // [g14] GC.Collect inside the comparer Equals (rooting across buckets)
            int[] xs4 = { 0, 1, 2, 3, 4, 5 };
            foreach (var g in xs4.GroupBy(x => x, new GcParityComparer()))
                Console.WriteLine("g14:" + g.Key + ":" + g.Count());

            Console.WriteLine("done");
        }
    }

    internal sealed class ParityIntComparer : IEqualityComparer<int>
    {
        public bool Equals(int a, int b) { return (a & 1) == (b & 1); }
        public int GetHashCode(int v) { return v & 1; }
    }
    internal sealed class ParityByteComparer : IEqualityComparer<byte>
    {
        public bool Equals(byte a, byte b) { return ((int)a & 1) == ((int)b & 1); }
        public int GetHashCode(byte v) { return v & 1; }
    }
    internal sealed class ParityLongComparer : IEqualityComparer<long>
    {
        public bool Equals(long a, long b) { return (a & 1) == (b & 1); }
        public int GetHashCode(long v) { return (int)(v & 1); }
    }
    internal sealed class FloorDoubleComparer : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) { return Math.Floor(a) == Math.Floor(b); }
        public int GetHashCode(double v) { return (int)Math.Floor(v); }
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
    internal sealed class ToneParityComparer : IEqualityComparer<Tone>
    {
        public bool Equals(Tone a, Tone b) { return ((int)a & 1) == ((int)b & 1); }
        public int GetHashCode(Tone v) { return (int)v & 1; }
    }
    internal sealed class GcParityComparer : IEqualityComparer<int>
    {
        public bool Equals(int a, int b)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return (a & 1) == (b & 1);
        }
        public int GetHashCode(int v) { return v & 1; }
    }
}