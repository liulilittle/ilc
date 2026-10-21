using System;
using System.Linq;

//------------------------------------------------------------------------------
// loud-p0-06-ulong -- P0-06 slice: Aggregate TAccumulate = ulong.
//
// Before B4-1 the 0x0B (ulong) accumulator byte fell through to the empty
// "unsupported accumulator family" arm (LOUD). ulong is width-identical to
// long, so it rides the existing int64 fold (il2cpp_linq_aggregate_seed_i8 /
// ..._i8_rs_i8 for the result-selector form).
//
// Each fold result is consumed by a VALUE COMPARISON against the net48
// expectation and prints a literal verdict line. Box/ToString of a raw UInt64
// static value still trips UNRELATED transpiler gaps (the box fold emits an
// invalid static_cast; object formatting of UInt64 is not wired) -- both
// outside this batch -- while comparison is the semantics the fold itself must
// satisfy (bit pattern, wrap included).
//
// net48 oracle (source {1,2,3,4,5}):
//   [ul_seed]=15
//   [ul_empty]=10
//   [ul_wrap]=0                                        (ulong.MaxValue+1 wraps)
//   [ul_big]=9223372036854775795
//   [ul_rs]=30                                         (resultSelector runs once)
//------------------------------------------------------------------------------
namespace Cases
{
    public static class LoudP006Ulong
    {
        public static void Main()
        {
            int[] data = { 1, 2, 3, 4, 5 };

            ulong seed = data.Aggregate(0UL, (a, x) => a + (ulong)x);
            Console.WriteLine(seed == 15UL ? "[ul_seed]=15" : "[ul_seed]=BUG");

            ulong empty = new int[0].Aggregate(10UL, (a, x) => a + (ulong)x);
            Console.WriteLine(empty == 10UL ? "[ul_empty]=10" : "[ul_empty]=BUG");

            ulong wrap = new[] { 1 }.Aggregate(ulong.MaxValue, (a, x) => a + (ulong)x);
            Console.WriteLine(wrap == 0UL ? "[ul_wrap]=0" : "[ul_wrap]=BUG");

            ulong big = data.Aggregate(9223372036854775780UL, (a, x) => a + (ulong)x);
            Console.WriteLine(big == 9223372036854775795UL ? "[ul_big]=9223372036854775795" : "[ul_big]=BUG");

            ulong rs = data.Aggregate(0UL, (a, x) => a + (ulong)x, a => a * 2UL);
            Console.WriteLine(rs == 30UL ? "[ul_rs]=30" : "[ul_rs]=BUG");
        }
    }
}
