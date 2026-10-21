using System;
using System.Collections.Generic;
using System.Linq;

// P0-11: Enumerable.Any() (no predicate) reads only the array header, so it
// works for every element family. Any(predicate)/Count(predicate) must read
// the element at its REAL width (int64/double/float and the narrow
// byte/sbyte/short/ushort/char/bool families) and widen to the predicate's
// int32/int64/double slot -- never read a 1/2/8-byte cell as int32.
// List<T> is a list OBJECT, not an array: Any/Count/Where/Select/First/
// FirstOrDefault/All over List<int> and List<string> must go through the
// list carriers, never an Il2CppArray* cast.
//
// Callbacks are INSTANCE-BOUND delegates (the transpiler has no lambdas); the
// runtime's per-binding dispatch puts `this` in the first slot.
namespace Cases
{
    public sealed class AnyProbe
    {
        public int Value;
    }

    // Counts its invocations so the case can prove All SHORT-CIRCUITS (net48
    // stops at the first false) and does NOT call the predicate twice.
    public sealed class CallCounter
    {
        public int Calls;
        public bool Gt1(int x) { Calls++; return x > 1; }
        public bool AlwaysTrue(int x) { Calls++; return true; }
    }

    public sealed class AnyPreds
    {
        public bool IntGt2(int x) { return x > 2; }
        public bool IntGt1(int x) { return x > 1; }
        public bool LongGt2(long x) { return x > 2; }
        public bool DoubleGt2(double x) { return x > 2.0; }
        public bool FloatGt2(float x) { return x > 2.0f; }
        public bool ByteLt3(byte x) { return x < 3; }
        public bool SByteLt0(sbyte x) { return x < 0; }
        public bool ShortLt3(short x) { return x < 3; }
        public bool UShortGt60000(ushort x) { return x > 60000; }
        public bool CharLtC(char c) { return c < 'c'; }
        public bool BoolTrue(bool b) { return b; }
        public bool UIntGt4000000000(uint x) { return x > 4000000000u; }
        public bool StrLong(string s) { return s.Length > 2; }
        public string Bang(string s) { return s + "!"; }
        public int Double2(int x) { return x * 2; }
        public bool ULongGt2(ulong x) { return x > 2UL; }
        public bool ByteGt199(byte x) { return x > 199; }
    }

    public static class LoudP011Any
    {
        public static void Main()
        {
            int[] ints = { 1, 2 };
            int[] emptyInts = new int[0];
            string[] strs = { "a" };
            string[] emptyStrs = new string[0];
            object[] objs = { new object() };
            AnyProbe[] probes = { new AnyProbe() };
            AnyProbe[] emptyProbes = new AnyProbe[0];

            Console.WriteLine("[p0-11-int]=" + ints.Any());
            Console.WriteLine("[p0-11-int-empty]=" + emptyInts.Any());
            Console.WriteLine("[p0-11-str]=" + strs.Any());
            Console.WriteLine("[p0-11-str-empty]=" + emptyStrs.Any());
            Console.WriteLine("[p0-11-obj]=" + objs.Any());
            Console.WriteLine("[p0-11-class]=" + probes.Any());
            Console.WriteLine("[p0-11-class-empty]=" + emptyProbes.Any());

            AnyPreds p = new AnyPreds();

            // int32 (the pre-existing family) -- regression guard.
            Func<int, bool> intGt2 = p.IntGt2;
            Func<int, bool> intGt1 = p.IntGt1;
            Console.WriteLine("[p0-11-anyp-int]=" + ints.Any(intGt2));
            Console.WriteLine("[p0-11-countp-int]=" + ints.Count(intGt1));
            Console.WriteLine("[p0-11-anyp-int-empty]=" + emptyInts.Any(intGt2));
            Console.WriteLine("[p0-11-countp-int-empty]=" + emptyInts.Count(intGt2));

            // int64 / uint64
            long[] longs = { 1L, 4294967297L };
            Func<long, bool> longGt2 = p.LongGt2;
            Console.WriteLine("[p0-11-anyp-long]=" + longs.Any(longGt2));
            Console.WriteLine("[p0-11-countp-long]=" + longs.Count(longGt2));
            ulong[] ulongs = { 1UL, 4294967297UL };
            Func<ulong, bool> ulongGt2 = p.ULongGt2;
            Console.WriteLine("[p0-11-anyp-ulong]=" + ulongs.Any(ulongGt2));

            // double / float
            double[] doubles = { 1.0, 3.5 };
            Func<double, bool> doubleGt2 = p.DoubleGt2;
            Console.WriteLine("[p0-11-anyp-double]=" + doubles.Any(doubleGt2));
            Console.WriteLine("[p0-11-countp-double]=" + doubles.Count(doubleGt2));
            float[] floats = { 1.0f, 3.5f };
            Func<float, bool> floatGt2 = p.FloatGt2;
            Console.WriteLine("[p0-11-anyp-float]=" + floats.Any(floatGt2));
            Console.WriteLine("[p0-11-countp-float]=" + floats.Count(floatGt2));

            // narrow integer families (1/2-byte cells widened to the int32 ABI)
            byte[] bytes = { 1, 2, 200 };
            Func<byte, bool> byteLt3 = p.ByteLt3;
            Console.WriteLine("[p0-11-anyp-byte]=" + bytes.Any(byteLt3));
            Console.WriteLine("[p0-11-countp-byte]=" + bytes.Count(byteLt3));
            Func<byte, bool> byteGt199 = p.ByteGt199;
            Console.WriteLine("[p0-11-anyp-byte-200]=" + bytes.Any(byteGt199));
            sbyte[] sbytes = { -1, 0, 127 };
            Func<sbyte, bool> sbyteLt0 = p.SByteLt0;
            Console.WriteLine("[p0-11-anyp-sbyte]=" + sbytes.Any(sbyteLt0));
            Console.WriteLine("[p0-11-countp-sbyte]=" + sbytes.Count(sbyteLt0));
            short[] shorts = { -1, 0, 32767 };
            Func<short, bool> shortLt3 = p.ShortLt3;
            Console.WriteLine("[p0-11-anyp-short]=" + shorts.Any(shortLt3));
            Console.WriteLine("[p0-11-countp-short]=" + shorts.Count(shortLt3));
            ushort[] ushorts = { 0, 65535 };
            Func<ushort, bool> ushortGt = p.UShortGt60000;
            Console.WriteLine("[p0-11-anyp-ushort]=" + ushorts.Any(ushortGt));
            Console.WriteLine("[p0-11-countp-ushort]=" + ushorts.Count(ushortGt));
            char[] chars = { 'a', 'b', 'z' };
            Func<char, bool> charLtC = p.CharLtC;
            Console.WriteLine("[p0-11-anyp-char]=" + chars.Any(charLtC));
            Console.WriteLine("[p0-11-countp-char]=" + chars.Count(charLtC));
            bool[] bools = { false, true };
            Func<bool, bool> boolTrue = p.BoolTrue;
            Console.WriteLine("[p0-11-anyp-bool]=" + bools.Any(boolTrue));
            Console.WriteLine("[p0-11-countp-bool]=" + bools.Count(boolTrue));
            uint[] uints = { 1u, 4000000001u };
            Func<uint, bool> uintGt = p.UIntGt4000000000;
            Console.WriteLine("[p0-11-anyp-uint]=" + uints.Any(uintGt));
            Console.WriteLine("[p0-11-countp-uint]=" + uints.Count(uintGt));

            // All over the same families (vacuous truth + short circuit).
            Console.WriteLine("[p0-11-all-int]=" + ints.All(intGt1));
            Console.WriteLine("[p0-11-all-long]=" + longs.All(longGt2));
            Console.WriteLine("[p0-11-all-byte]=" + bytes.All(byteLt3));
            Console.WriteLine("[p0-11-all-empty]=" + emptyInts.All(intGt2));

            // ===== List<T> carriers (list OBJECTS, never arrays) =====
            List<int> li = new List<int> { 1, 2, 3 };
            List<int> liEmpty = new List<int>();
            Console.WriteLine("[p0-11-list-any]=" + li.Any());
            Console.WriteLine("[p0-11-list-any-empty]=" + liEmpty.Any());
            Console.WriteLine("[p0-11-list-count]=" + li.Count());
            Console.WriteLine("[p0-11-list-count-empty]=" + liEmpty.Count());
            Console.WriteLine("[p0-11-list-anyp]=" + li.Any(intGt2));
            Console.WriteLine("[p0-11-list-countp]=" + li.Count(intGt1));
            Console.WriteLine("[p0-11-list-all]=" + li.All(intGt1));
            Console.WriteLine("[p0-11-list-all-empty]=" + liEmpty.All(intGt2));
            Console.WriteLine("[p0-11-list-where]=" + li.Where(intGt1).ToArray().Length);
            Console.WriteLine("[p0-11-list-where-0]=" + li.Where(intGt1).ToArray()[0]);
            Func<int, int> dbl = p.Double2;
            Console.WriteLine("[p0-11-list-select]=" + li.Select(dbl).ToArray()[2]);
            Console.WriteLine("[p0-11-list-first]=" + li.First());
            Console.WriteLine("[p0-11-list-first-default]=" + liEmpty.FirstOrDefault());

            List<string> ls = new List<string> { "a", "bbb", "cccc" };
            List<string> lsEmpty = new List<string>();
            Func<string, bool> strLong = p.StrLong;
            Func<string, string> bang = p.Bang;
            Console.WriteLine("[p0-11-liststr-any]=" + ls.Any());
            Console.WriteLine("[p0-11-liststr-any-empty]=" + lsEmpty.Any());
            Console.WriteLine("[p0-11-liststr-count]=" + ls.Count());
            Console.WriteLine("[p0-11-liststr-anyp]=" + ls.Any(strLong));
            Console.WriteLine("[p0-11-liststr-countp]=" + ls.Count(strLong));
            Console.WriteLine("[p0-11-liststr-all]=" + ls.All(strLong));
            Console.WriteLine("[p0-11-liststr-all-empty]=" + lsEmpty.All(strLong));
            string[] lw = ls.Where(strLong).ToArray();
            Console.WriteLine("[p0-11-liststr-where]=" + lw.Length + ":" + lw[0]);
            string[] lb = ls.Select(bang).ToArray();
            Console.WriteLine("[p0-11-liststr-select]=" + lb.Length + ":" + lb[0]);
            Console.WriteLine("[p0-11-liststr-first]=" + ls.First());
            Console.WriteLine("[p0-11-liststr-first-default]=" + (lsEmpty.FirstOrDefault() == null));

            // ===== All: vacuous truth + short circuit (every family) =====
            // net48 All stops at the FIRST false and returns; the predicate
            // must be called exactly once there (never twice, never for the
            // remaining elements).
            CallCounter cc = new CallCounter();
            Func<int, bool> ccGt1 = cc.Gt1;
            Console.WriteLine("[p0-11-all-shortcircuit-result]=" + ints.All(ccGt1));
            Console.WriteLine("[p0-11-all-shortcircuit-calls]=" + cc.Calls);

            CallCounter cc2 = new CallCounter();
            Func<int, bool> ccTrue = cc2.AlwaysTrue;
            Console.WriteLine("[p0-11-all-fullscan-result]=" + ints.All(ccTrue));
            Console.WriteLine("[p0-11-all-fullscan-calls]=" + cc2.Calls);

            Console.WriteLine("[p0-11-all-vacuous-int]=" + emptyInts.All(intGt2));
            Console.WriteLine("[p0-11-all-vacuous-long]=" + new long[0].All(longGt2));
            Console.WriteLine("[p0-11-all-vacuous-double]=" + new double[0].All(doubleGt2));
            Console.WriteLine("[p0-11-all-vacuous-byte]=" + new byte[0].All(byteLt3));
            Console.WriteLine("[p0-11-all-vacuous-bool]=" + new bool[0].All(boolTrue));
            Console.WriteLine("[p0-11-all-vacuous-str]=" + emptyStrs.All(strLong));

            // ===== Any(predicate) over EMPTY sources of every family =====
            Console.WriteLine("[p0-11-anyp-empty-long]=" + new long[0].Any(longGt2));
            Console.WriteLine("[p0-11-anyp-empty-double]=" + new double[0].Any(doubleGt2));
            Console.WriteLine("[p0-11-anyp-empty-byte]=" + new byte[0].Any(byteLt3));
            Console.WriteLine("[p0-11-anyp-empty-char]=" + new char[0].Any(charLtC));
            Console.WriteLine("[p0-11-anyp-empty-bool]=" + new bool[0].Any(boolTrue));

            // ===== List<T>: All short circuit + vacuous, Any/Count empty =====
            CallCounter lc = new CallCounter();
            Func<int, bool> lcGt1 = lc.Gt1;
            Console.WriteLine("[p0-11-list-all-shortcircuit-result]=" + li.All(lcGt1));
            Console.WriteLine("[p0-11-list-all-shortcircuit-calls]=" + lc.Calls);
            Console.WriteLine("[p0-11-list-all-vacuous]=" + liEmpty.All(intGt2));
            Console.WriteLine("[p0-11-liststr-anyp-empty]=" + lsEmpty.Any(strLong));
            Console.WriteLine("[p0-11-liststr-countp-empty]=" + lsEmpty.Count(strLong));
        }
    }
}
