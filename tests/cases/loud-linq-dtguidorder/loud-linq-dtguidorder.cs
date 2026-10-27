//------------------------------------------------------------------------------
// loud-linq-dtguidorder.cs -- e2e for OrderBy / OrderByDescending with a
// DateTime / Guid KEY over the i4 / i8 / dec / ref sources (impl-linq3 B).
// Before this change every struct key LOUD-rejected ("int32/int64/double/
// float keys only"). The DateTime keys order by their ticks, the Guid keys by
// the net48 field-wise CompareTo contract (the GUID set below exercises the
// Data1/Data2/Data3/Data4 field ordering, not the string form). Covers
// ascending + descending, tie stability (equal keys keep source order),
// empty and single-element sources. Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqDtGuidOrder
    {
        // The transpiler's static storage has no 16-byte Guid slot, so the
        // Guid carriers come from locals (the loud-linq-grpguid convention).
        private static Guid[] GuidKeys()
        {
            Guid g1 = new Guid("0aa79e57-1c2b-4d3e-9f40-112233445566");
            Guid g2 = new Guid("0aa79e57-1c2b-4d3e-8f40-112233445566");
            Guid g3 = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
            Guid g4 = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
            return new Guid[] { g1, g2, g3, g4 };
        }

        public static void Main()
        {
            Guid[] G = GuidKeys();

            // int[] source, DateTime keys (with ties for stability). The keys
            // come from an explicitly-typed DateTime[] of inline ctors (the
            // loud-linq-grpdt convention).
            int[] ints = { 1, 2, 3, 4, 5 };
            DateTime[] dtKeys =
            {
                new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc),
                new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc),
                new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            };
            Console.WriteLine("i4dt asc: " + string.Join(",",
                ints.OrderBy(x => dtKeys[x - 1]).ToArray()));
            Console.WriteLine("i4dt desc: " + string.Join(",",
                ints.OrderByDescending(x => dtKeys[x - 1]).ToArray()));

            // int[] source, Guid keys (the field-wise contract decides).
            var guidKeys = new Guid[] { G[3], G[0], G[2], G[0], G[1] };
            Console.WriteLine("i4guid asc: " + string.Join(",",
                ints.OrderBy(x => guidKeys[x - 1]).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));
            Console.WriteLine("i4guid desc: " + string.Join(",",
                ints.OrderByDescending(x => guidKeys[x - 1]).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));

            // long[] source.
            long[] longs = { 10, 20, 30, 40 };
            DateTime[] dtKeys8 =
            {
                new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc),
                new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            };
            Console.WriteLine("i8dt asc: " + string.Join(",",
                longs.OrderBy(x => dtKeys8[(int)(x / 10) - 1]).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));
            var guidKeys8 = new Guid[] { G[1], G[3], G[2], G[0] };
            Console.WriteLine("i8guid desc: " + string.Join(",",
                longs.OrderByDescending(x => guidKeys8[(int)(x / 10) - 1]).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));

            // decimal[] source.
            decimal[] decs = { 1.5m, 2.5m, 3.5m, 4.5m };
            DateTime[] dtKeysD =
            {
                new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc),
                new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc),
            };
            Console.WriteLine("decdt asc: " + string.Join(",",
                decs.OrderBy(x => dtKeysD[(int)x - 1]).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));
            var guidKeysD = new Guid[] { G[2], G[0], G[3], G[1] };
            Console.WriteLine("decguid desc: " + string.Join(",",
                decs.OrderByDescending(x => guidKeysD[(int)x - 1]).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));

            // string[] (reference) source.
            string[] words = { "pear", "fig", "kiwi", "plum", "date" };
            DateTime[] dtKeysR =
            {
                new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc),
                new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc),
            };
            Console.WriteLine("refdt asc: " + string.Join(",",
                words.OrderBy(x => dtKeysR[Array.IndexOf(words, x)]).ToArray()));
            var guidKeysR = new Guid[] { G[0], G[1], G[2], G[3], G[0] };
            Console.WriteLine("refguid asc: " + string.Join(",",
                words.OrderBy(x => guidKeysR[Array.IndexOf(words, x)]).ToArray()));
            Console.WriteLine("refguid desc: " + string.Join(",",
                words.OrderByDescending(x => guidKeysR[Array.IndexOf(words, x)]).ToArray()));

            // Empty and single-element sources. The key selector builds the
            // DateTime inline (a captured DateTime local is declared as an
            // int32 closure slot -- a transpiler surface gap outside this
            // family).
            Console.WriteLine("empty: " + string.Join(",", new int[0].OrderBy(
                x => new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc)).ToArray()) +
                "|" + string.Join(",", new string[0].OrderBy(x => G[0]).ToArray()));
            Console.WriteLine("single: " + string.Join(",", new[] { 42 }.OrderByDescending(x => G[3]).ToArray()));

            Console.WriteLine("done");
        }
    }
}
