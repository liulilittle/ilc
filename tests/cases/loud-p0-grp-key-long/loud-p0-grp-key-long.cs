using System;
using System.Linq;

//------------------------------------------------------------------------------
// loud-p0-grp-key-long -- p0grp companion: GroupBy with a key family that has
// NO runtime carrier (long / ulong / decimal), and GroupBy over a reference
// source.
//
// These shapes are TRANSPILE-TIME LOUD (NotSupportedException naming the real
// reason), never a silent wrong bucket:
//
//   * long/ulong keys: the bucket key is an int32 slot; a 64-bit key would be
//     truncated and collapse distinct net48 buckets.
//   * decimal keys: the bucket has no 16-byte key slot and the grouping key is
//     never boxed (only ELEMENT families are).
//   * a decimal[] source with an ELEMENT SELECTOR: the element selector
//     callback would need another by-value struct ABI.
//   * a reference (string[]) source: the grouping kernel reads int32 or
//     16-byte decimal cells only.
//
// Each statement is individually reachable: the transpiler aborts the whole
// method on the FIRST one, so this case exists purely to pin the message of
// that first rejection (see scripts/sweep-exclude.txt -- this case is a LOUD
// contract and is verified by scripts/loud-gate.ps1).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class LoudP0GrpKeyLong
    {
        public static void Main()
        {
            int[] xs = { 1, 2, 3 };

            // long key: no int64 key carrier.
            foreach (var g in xs.GroupBy(x => (long)x))
                Console.WriteLine("[gb_long] key=" + g.Key + " n=" + g.Count());
        }
    }
}
