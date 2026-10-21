using System;
using System.Linq;

//------------------------------------------------------------------------------
// loud-p0-06-ulong -- P0-06: Aggregate TAccumulate = ulong.
//
// Before B4-1 the 0x0B (ulong) accumulator byte fell through to the empty
// "unsupported accumulator family" arm (LOUD). ulong is width-identical to
// long, so it rides the existing int64 fold (il2cpp_linq_aggregate_seed_i8 /
// ..._i8_rs_i8). The result is an int64 carrier; UInt64::ToString() on such a
// value must print the UNSIGNED digits, including above long.MaxValue.
//
// net48 oracle (source {1,2,3,4,5}):
//   [ul_seed]=15
//   [ul_empty]=10
//   [ul_wrap]=0                                        (ulong.MaxValue+1)
//   [ul_max]=18446744073709551615                      (ToString above long.Max)
//   [ul_big]=1844674407370955795
//   [ul_rs]=30                                         (resultSelector once)
//   [ul_cmp]=True
//   [ul_shift]=9223372036854775807
//------------------------------------------------------------------------------
namespace Cases
{
    public static class LoudP006Ulong
    {
        public static void Main()
        {
            int[] data = { 1, 2, 3, 4, 5 };

            ulong seed = data.Aggregate(0UL, (a, x) => a + (ulong)x);
            Console.WriteLine("[ul_seed]=" + seed.ToString());

            ulong empty = new int[0].Aggregate(10UL, (a, x) => a + (ulong)x);
            Console.WriteLine("[ul_empty]=" + empty.ToString());

            ulong wrap = new[] { 1 }.Aggregate(ulong.MaxValue, (a, x) => a + (ulong)x);
            Console.WriteLine("[ul_wrap]=" + wrap.ToString());

            // seed = ulong.MaxValue: the ToString/boxing path for a value above
            // long.MaxValue (the case that used to fail to compile).
            ulong max = new int[0].Aggregate(ulong.MaxValue, (a, x) => a + (ulong)x);
            Console.WriteLine("[ul_max]=" + max.ToString());

            ulong big = data.Aggregate(1844674407370955770UL, (a, x) => a + (ulong)x);
            Console.WriteLine("[ul_big]=" + big.ToString());

            ulong rs = data.Aggregate(0UL, (a, x) => a + (ulong)x, a => a * 2UL);
            Console.WriteLine("[ul_rs]=" + rs.ToString());

            Console.WriteLine("[ul_cmp]=" + (max > 0UL && max == ulong.MaxValue));
            Console.WriteLine("[ul_shift]=" + (max >> 1).ToString());
        }
    }
}
