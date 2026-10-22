using System;
using System.Collections.Generic;
using System.Linq;

// B5-R3: the WIDE (long/double/float) and NARROW (byte/sbyte/short/ushort/
// char/bool) element families through Where/Select/First/FirstOrDefault/Last/
// LastOrDefault, plus the List<T> materializers for the wide families.
//
// Two defects this case is designed to catch:
//   * a materialized wide array that LOSES its elemKind (array.cpp sets 0 for
//     an untyped allocation), so an 8-byte cell is ambiguous and string.Join
//     prints the raw IEEE754 bit pattern instead of the value;
//   * the wide/narrow Where/Select/First/Last entries being declared but never
//     defined (link-time "undefined reference").
//
// Callbacks are INSTANCE-BOUND delegates (the transpiler has no lambdas).
namespace Cases
{
    public sealed class WidePreds
    {
        public bool LongGt1(long x) { return x > 1; }
        public bool DoubleGt2(double x) { return x > 2.0; }
        public bool FloatGt1(float x) { return x > 1.0f; }
        public bool ByteGt1(byte x) { return x > 1; }
        public bool SByteLt0(sbyte x) { return x < 0; }
        public bool ShortGt0(short x) { return x > 0; }
        public bool UShortGt1(ushort x) { return x > 1; }
        public bool CharGeB(char c) { return c >= 'b'; }
        public bool BoolTrue(bool b) { return b; }
        public long Twice(long x) { return x * 2; }
        public double Half(double x) { return x / 2.0; }
        public float ToFloat(float x) { return x + 1.0f; }
        public int ToInt(long x) { return (int)(x % 100); }
    }

    public static class LoudP013Wide
    {
        static string Join(string[] a) { return string.Join(",", a); }
        static string JoinL(long[] a) { return string.Join(",", a); }
        static string JoinD(double[] a) { return string.Join(",", a); }
        static string JoinF(float[] a) { return string.Join(",", a); }

        static bool ThrowsFirstEmptyLong()
        {
            try { new long[0].First(); return false; }
            catch (InvalidOperationException) { return true; }
        }

        static bool ThrowsLastEmptyDouble()
        {
            try { new double[0].Last(); return false; }
            catch (InvalidOperationException) { return true; }
        }

        public static void Main()
        {
            WidePreds p = new WidePreds();
            Func<long, bool> longGt1 = p.LongGt1;
            Func<double, bool> doubleGt2 = p.DoubleGt2;
            Func<float, bool> floatGt1 = p.FloatGt1;
            Func<byte, bool> byteGt1 = p.ByteGt1;
            Func<sbyte, bool> sbyteLt0 = p.SByteLt0;
            Func<short, bool> shortGt0 = p.ShortGt0;
            Func<ushort, bool> ushortGt1 = p.UShortGt1;
            Func<char, bool> charGeB = p.CharGeB;
            Func<bool, bool> boolTrue = p.BoolTrue;

            long[] longs = { 3L, 1L, 4294967297L };
            double[] doubles = { 1.5, 2.5, 3.5 };
            float[] floats = { 1.5f, 2.5f, 3.5f };

            // ===== Where over the wide families =====
            Console.WriteLine("[w-long-where]=" + JoinL(longs.Where(longGt1).ToArray()));
            Console.WriteLine("[w-double-where]=" + JoinD(doubles.Where(doubleGt2).ToArray()));
            Console.WriteLine("[w-float-where]=" + JoinF(floats.Where(floatGt1).ToArray()));
            Console.WriteLine("[w-long-where-none]=" + longs.Where(x => false).ToArray().Length);
            Console.WriteLine("[w-double-where-all]=" + doubles.Where(x => true).ToArray().Length);

            // ===== Where over the narrow families =====
            // NOTE: the narrow families are asserted with EXPLICIT per-element
            // casts rather than string.Join. string.Join(Array) formats a value
            // array by elemKind/elemSize (stringext2.cpp ValueArrayElement),
            // whose table has no signed/unsigned-narrow or bool entry -- a
            // PRE-EXISTING shared-formatter limitation registered in
            // b5-gaps.md, unrelated to LINQ. The LINQ contract under test is
            // the FILTERING plus the value, which the casts verify exactly.
            byte[] bytes = { 1, 2, 200 };
            byte[] byteKept = bytes.Where(byteGt1).ToArray();
            Console.WriteLine("[w-byte-where]=" + byteKept.Length + ":" + (int)byteKept[0] + "," + (int)byteKept[1]);
            sbyte[] sbytes = { -1, 0, 127 };
            sbyte[] sbyteKept = sbytes.Where(sbyteLt0).ToArray();
            Console.WriteLine("[w-sbyte-where]=" + sbyteKept.Length + ":" + (int)sbyteKept[0]);
            short[] shorts = { -1, 0, 32767 };
            short[] shortKept = shorts.Where(shortGt0).ToArray();
            Console.WriteLine("[w-short-where]=" + shortKept.Length + ":" + (int)shortKept[0]);
            ushort[] ushorts = { 0, 65535 };
            ushort[] ushortKept = ushorts.Where(ushortGt1).ToArray();
            Console.WriteLine("[w-ushort-where]=" + ushortKept.Length + ":" + (int)ushortKept[0]);
            char[] chars = { 'a', 'b', 'z' };
            Console.WriteLine("[w-char-where]=" + new string(chars.Where(charGeB).ToArray()));
            bool[] bools = { false, true };
            bool[] boolKept = bools.Where(boolTrue).ToArray();
            Console.WriteLine("[w-bool-where]=" + boolKept.Length + ":" + boolKept[0]);

            // ===== Select over the wide families =====
            Console.WriteLine("[w-long-select]=" + JoinL(longs.Select(p.Twice).ToArray()));
            Console.WriteLine("[w-double-select]=" + JoinD(doubles.Select(p.Half).ToArray()));
            Console.WriteLine("[w-float-select]=" + JoinF(floats.Select(p.ToFloat).ToArray()));
            Console.WriteLine("[w-long-select-int]=" + string.Join(",", longs.Select(p.ToInt)));

            // ===== First / FirstOrDefault / Last / LastOrDefault =====
            Console.WriteLine("[w-long-first]=" + longs.First());
            Console.WriteLine("[w-long-first-default-empty]=" + new long[0].FirstOrDefault());
            Console.WriteLine("[w-double-first]=" + doubles.First());
            Console.WriteLine("[w-double-first-default-empty]=" + new double[0].FirstOrDefault());
            Console.WriteLine("[w-long-last]=" + longs.Last());
            Console.WriteLine("[w-double-last]=" + doubles.Last());
            Console.WriteLine("[w-float-last]=" + floats.Last());
            Console.WriteLine("[w-long-last-default-empty]=" + new long[0].LastOrDefault());
            Console.WriteLine("[w-double-last-default-empty]=" + new double[0].LastOrDefault());
            Console.WriteLine("[w-long-first-empty-throws]=" + ThrowsFirstEmptyLong());
            Console.WriteLine("[w-double-last-empty-throws]=" + ThrowsLastEmptyDouble());

            // ===== List<wide>.Except / Distinct / Concat / Zip / Take / Skip /
            //       Reverse / ToArray -- every materializer must keep elemKind,
            //       otherwise the values print as raw bit patterns. =====
            List<double> dl = new List<double> { 1.5, 2.5 };
            Console.WriteLine("[w-list-double-except]=" + JoinD(dl.Except(new double[] { }).ToArray()));
            Console.WriteLine("[w-list-double-distinct]=" + JoinD(dl.Distinct().ToArray()));
            Console.WriteLine("[w-list-double-toarray]=" + JoinD(dl.ToArray()));
            Console.WriteLine("[w-list-double-concat]=" +
                JoinD(dl.Concat(new List<double> { 3.5 }).ToArray()));
            Console.WriteLine("[w-list-double-take]=" + JoinD(dl.Take(1).ToArray()));
            Console.WriteLine("[w-list-double-skip]=" + JoinD(dl.Skip(1).ToArray()));
            Console.WriteLine("[w-list-double-reverse]=" + JoinD(Enumerable.Reverse(dl).ToArray()));

            List<float> fl = new List<float> { 1.5f, 2.5f };
            Console.WriteLine("[w-list-float-except]=" + JoinF(fl.Except(new float[] { }).ToArray()));
            Console.WriteLine("[w-list-float-distinct]=" + JoinF(fl.Distinct().ToArray()));
            Console.WriteLine("[w-list-float-concat]=" +
                JoinF(fl.Concat(new List<float> { 3.5f }).ToArray()));
            Console.WriteLine("[w-list-float-reverse]=" + JoinF(Enumerable.Reverse(fl).ToArray()));

            List<long> ll = new List<long> { 3000000000L, 2L };
            Console.WriteLine("[w-list-long-except]=" + JoinL(ll.Except(new long[] { }).ToArray()));
            Console.WriteLine("[w-list-long-distinct]=" + JoinL(ll.Distinct().ToArray()));
            Console.WriteLine("[w-list-long-take]=" + JoinL(ll.Take(1).ToArray()));
            Console.WriteLine("[w-list-long-skip]=" + JoinL(ll.Skip(1).ToArray()));
            Console.WriteLine("[w-list-long-reverse]=" + JoinL(Enumerable.Reverse(ll).ToArray()));

            // Zip over wide lists (result is a wide family).
            Console.WriteLine("[w-list-double-zip]=" +
                JoinD(dl.Zip(new List<double> { 10.0, 20.0 }, (a, b) => a + b).ToArray()));

            // ===== List<int> aggregates unaffected by the elemKind change =====
            List<int> il = new List<int> { 1, 2, 3, 4 };
            Console.WriteLine("[w-list-int-sum]=" + il.Sum());
            Console.WriteLine("[w-list-int-min]=" + il.Min());
            Console.WriteLine("[w-list-int-max]=" + il.Max());
            Console.WriteLine("[w-list-int-average]=" + il.Average());
            Console.WriteLine("[w-list-int-toarray]=" + string.Join(",", il.ToArray()));
            Console.WriteLine("[w-list-int-except]=" +
                string.Join(",", il.Except(new List<int> { 2 }).ToArray()));
        }
    }
}
