//------------------------------------------------------------------------------
// linqcase.cs -- end-to-end reference case for a System.Linq.Enumerable chain
// over int[]: Where -> ToArray, Select -> ToArray, chained Where+Select,
// Count (plain + predicate + empty), Sum (plain + filtered + empty),
// Min/Max, First / FirstOrDefault (plain, predicate, no-match, empty),
// Take / Skip (slice semantics incl. the net48 negative-count no-throw quirk).
//
// DELEGATE CALLBACK POLICY -- NO LAMBDAS, BY DESIGN:
//   LINQ's Where/Select/First(pred) need a Func<int,bool>/Func<int,int>
//   callback. The transpiler does not support lambdas (anonymous methods not
//   wired), so every predicate is an INSTANCE METHOD bound to a delegate
//   (LinqPred.IsEven/IsBig/Double) -- the exact binding shape delegateinst.cs
//   already proves at runtime (il2cpp_delegate_new with target, instance-leaf
//   invocation). When the LINQ domain is wired, Enumerable receives a proper
//   instance-bound delegate and the invocation path is the already-proven one.
//
// EVERY line of output is a bool comparison (key=True/False), so the
// reference output is fully deterministic and hand-verifiable. Arrays are
// asserted element-by-element (no collection enumeration-order risk).
//
// WIRING STATUS (2026-08-05 transpiler): NOT WIRED -- no MethodMap.Linq.cs,
// no Enumerable icalls in the runtime. The transpiler is expected to abort at
// the first System.Linq call (fallback mangled-name method -> undefined
// symbol / aborted transpile, the standard gap shape). This case is created
// AHEAD of the LINQ wiring so the wiring batch gets a ready green gate;
// expected.txt is the net48 reference and documents the INTENDED semantics.
//
// Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    // instance-method predicate/mapper source: bound to Func delegates (no lambdas)
    public class LinqPred
    {
        public bool IsEven(int x) { return x % 2 == 0; }
        public bool IsBig(int x) { return x > 4; }
        public int Double(int x) { return x * 2; }
    }

    public static class Linqcase
    {
        public static void Main()
        {
            int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
            int[] small = { 1, 2 };
            int[] empty = new int[0];
            LinqPred p = new LinqPred();
            Func<int, bool> even = p.IsEven;
            Func<int, bool> big = p.IsBig;
            Func<int, int> dbl = p.Double;

            // ===== 1. Where (instance-method predicate) -> ToArray =====
            int[] evens = nums.Where(even).ToArray();          // {2,8,4,6}
            Console.WriteLine("where=" + (evens.Length == 4));
            Console.WriteLine("where0=" + (evens[0] == 2));
            Console.WriteLine("where1=" + (evens[1] == 8));
            Console.WriteLine("where2=" + (evens[2] == 4));
            Console.WriteLine("where3=" + (evens[3] == 6));

            // ===== 2. Select (instance-method mapper) -> ToArray =====
            int[] doubled = nums.Select(dbl).ToArray();
            Console.WriteLine("sel=" + (doubled.Length == 9));
            Console.WriteLine("sel0=" + (doubled[0] == 10));   // 5*2
            Console.WriteLine("sel1=" + (doubled[1] == 6));    // 3*2
            Console.WriteLine("sel8=" + (doubled[8] == 12));   // 6*2

            // ===== 3. chained Where + Select =====
            int[] bigDoubled = nums.Where(big).Select(dbl).ToArray(); // {10,18,14,16,12}
            Console.WriteLine("chain=" + (bigDoubled.Length == 5));
            Console.WriteLine("chain0=" + (bigDoubled[0] == 10));
            Console.WriteLine("chain4=" + (bigDoubled[4] == 12));

            // ===== 4. Count: plain + predicate + empty =====
            Console.WriteLine("count=" + (nums.Count() == 9));
            Console.WriteLine("countp=" + (nums.Count(even) == 4));
            Console.WriteLine("countempty=" + (empty.Count() == 0));

            // ===== 5. Sum: plain + filtered + empty =====
            Console.WriteLine("sum=" + (nums.Sum() == 45));
            Console.WriteLine("sumwhere=" + (nums.Where(even).Sum() == 20)); // 2+8+4+6
            Console.WriteLine("sumempty=" + (empty.Sum() == 0));

            // ===== 6. Min / Max =====
            Console.WriteLine("min=" + (nums.Min() == 1));
            Console.WriteLine("max=" + (nums.Max() == 9));

            // ===== 7. First / FirstOrDefault: plain + predicate =====
            Console.WriteLine("first=" + (nums.First() == 5));
            Console.WriteLine("firstp=" + (nums.First(even) == 2));
            Console.WriteLine("fod=" + (nums.FirstOrDefault() == 5));
            Console.WriteLine("fodp=" + (nums.FirstOrDefault(even) == 2));
            Console.WriteLine("fodnone=" + (small.FirstOrDefault(big) == 0)); // no match -> 0
            Console.WriteLine("fodempty=" + (empty.FirstOrDefault() == 0));   // empty -> 0
            bool firstThrown = false;
            try { int x = empty.First(); } catch (InvalidOperationException) { firstThrown = true; }
            Console.WriteLine("firstempty=" + firstThrown);   // empty First throws

            // ===== 8. Take / Skip: net48 slice semantics =====
            // net48 (probe-verified): a NEGATIVE count does NOT throw --
            // Take(-n) yields an empty result, Skip(-n) a full copy; count
            // > length clamps to the source.
            int[] take3 = nums.Take(3).ToArray();
            Console.WriteLine("take3=" + (take3.Length == 3 && take3[0] == 5 && take3[1] == 3 && take3[2] == 9));
            int[] take0 = nums.Take(0).ToArray();
            Console.WriteLine("take0=" + (take0.Length == 0));
            int[] take99 = nums.Take(99).ToArray();
            Console.WriteLine("take99=" + (take99.Length == 9 && take99[8] == 6));
            int[] takeneg = nums.Take(-1).ToArray();
            Console.WriteLine("takeneg=" + (takeneg.Length == 0));
            int[] skip3 = nums.Skip(3).ToArray();
            Console.WriteLine("skip3=" + (skip3.Length == 6 && skip3[0] == 1 && skip3[5] == 6));
            int[] skip0 = nums.Skip(0).ToArray();
            Console.WriteLine("skip0=" + (skip0.Length == 9));
            int[] skipneg = nums.Skip(-1).ToArray();
            Console.WriteLine("skipneg=" + (skipneg.Length == 9 && skipneg[0] == 5));
            int[] skip9 = nums.Skip(9).ToArray();
            Console.WriteLine("skip9=" + (skip9.Length == 0));
            int[] takeskip = nums.Take(6).Skip(2).ToArray();
            Console.WriteLine("takeskip=" + (takeskip.Length == 4 && takeskip[0] == 9 && takeskip[3] == 2));
            int[] skiptake = nums.Skip(2).Take(3).ToArray();
            Console.WriteLine("skiptake=" + (skiptake.Length == 3 && skiptake[0] == 9 && skiptake[2] == 7));
            int[] takeempty = empty.Take(3).ToArray();
            Console.WriteLine("takeempty=" + (takeempty.Length == 0));
            int[] wheretake = nums.Where(even).Take(3).ToArray();
            Console.WriteLine("wheretake=" + (wheretake.Length == 3 && wheretake[0] == 2 && wheretake[2] == 4));
        }
    }
}
