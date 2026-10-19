using System;
using System.Linq;
using System.Text;

//------------------------------------------------------------------------------
// p-nb9-fix-selidx -- nb9 fix batch (M19): Select result-family dispatch.
//
// Before this fix `Select` was dispatched on the Func generic-arity count only,
// so `Select((x,i) => ...)` went to the int32 kernel with the 1-arg callback
// ABI (the probe crashed with 0xC0000005 before the fix). The mapping now
// dispatches on TResult (genArgs[1]) and on the selector arity, emitting
//   select_i4_i4 / select_i4_i8 / select_i4_d / select_i4_ref
//   select_idx_i4_i4 / select_idx_i4_i8 / select_idx_i4_d / select_idx_i4_ref
// (see the transpile log: the pre-fix build emitted select_idx_i4_i4 for the
// (long) and (string) selectors as well).
//
// net48 oracle (host .NET Framework 4.x, source {1,2,3}):
//   [idx_i4]  =10,21,32
//   [noidx_i4]=10,20,30
//
// NOTE: only the int32 result family is consumable end to end today. The
// long/double/reference families are emitted correctly but their CONSUMERS are
// still unwired (ToArray<int64>/<double> is rejected by the stage-1 element
// gate, and foreach over a Select result crashes) -- both pre-existing and
// outside this batch's item list. See REPORT-NB9-FIX.md (M19).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixSelidx
    {
        public static void Main()
        {
            int[] data = { 1, 2, 3 };
            var sb = new StringBuilder();

            int[] idx = data.Select((x, i) => x * 10 + i).ToArray();
            for (int i = 0; i < idx.Length; i++) sb.Append(idx[i] + ",");
            Console.WriteLine("[idx_i4]=" + sb.ToString());

            sb = new StringBuilder();
            int[] plain = data.Select(x => x * 10).ToArray();
            for (int i = 0; i < plain.Length; i++) sb.Append(plain[i] + ",");
            Console.WriteLine("[noidx_i4]=" + sb.ToString());
        }
    }
}
