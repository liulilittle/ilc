using System;
using System.Linq;

// P0-10: reference-element LINQ map/element operators over string[].
// The runtime entries under test are where_ref / select_ref_ref / first_ref /
// first_or_default_ref (linq_map.h). The callbacks are instance-bound
// delegates (the methodPtr ABI puts `this` in the first slot), which is the
// same single-cast delegate producer every int32 LINQ case already uses.
namespace Cases
{
    public sealed class RefOps
    {
        public bool IsLong(string s) { return s.Length > 2; }
        public string Bang(string s) { return s + "!"; }
    }

    public static class LoudP010Ref
    {
        public static void Main()
        {
            string[] words = { "a", "bb", "ccc", "dddd" };
            string[] empty = new string[0];
            RefOps ops = new RefOps();
            Func<string, bool> isLong = ops.IsLong;
            Func<string, string> bang = ops.Bang;

            string[] longWords = words.Where(isLong).ToArray();
            Console.WriteLine("[p0-10-where-len]=" + longWords.Length);
            Console.WriteLine("[p0-10-where-0]=" + longWords[0]);
            Console.WriteLine("[p0-10-where-1]=" + longWords[1]);
            Console.WriteLine("[p0-10-where-empty]=" + empty.Where(isLong).ToArray().Length);

            string[] banged = words.Select(bang).ToArray();
            Console.WriteLine("[p0-10-select-len]=" + banged.Length);
            Console.WriteLine("[p0-10-select-0]=" + banged[0]);
            Console.WriteLine("[p0-10-select-3]=" + banged[3]);

            string[] longBanged = words.Where(isLong).Select(bang).ToArray();
            Console.WriteLine("[p0-10-chain-len]=" + longBanged.Length);
            Console.WriteLine("[p0-10-chain-0]=" + longBanged[0]);
            Console.WriteLine("[p0-10-chain-1]=" + longBanged[1]);

            Console.WriteLine("[p0-10-first]=" + words.First());
            Console.WriteLine("[p0-10-first-empty-default]=" + (empty.FirstOrDefault() == null));
        }
    }
}

