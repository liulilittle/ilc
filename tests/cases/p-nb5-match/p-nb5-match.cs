//------------------------------------------------------------------------------
// p-nb5-match.cs -- nb5 OBJ-1 Match object model difftest probe
// (net48 oracle == transpiled runtime byte-exact).
//
// Pins the minimum Match object-graph closure replacing the old string[]
// result shape: Regex.Match -> Match{Success,Index,Length,Value,ToString} +
// Groups.Count + Groups[int] -> Group{Value,Index,Length,Success,Name}.
//
// Records mirror the batch2 oracle subset (.opencode/tmp/next-batch2/
// batch2_oracle.txt): positive match (named/unnamed numbering deferred to
// OBJ-3), no-match Match (Groups.Count == 1 with a non-participating group 0),
// and a non-participating capture group.
//------------------------------------------------------------------------------
using System;
using System.Text.RegularExpressions;

namespace Cases
{
    public static class PNb5Match
    {
        static string B(bool b) { return b ? "True" : "False"; }

        public static void Main()
        {
            Match m = Regex.Match("a12b34", @"(\d)(\d)");
            Console.WriteLine("[m.success]=" + B(m.Success));
            Console.WriteLine("[m.index]=" + m.Index);
            Console.WriteLine("[m.length]=" + m.Length);
            Console.WriteLine("[m.value]=" + m.Value);
            Console.WriteLine("[m.tostring]=" + m.ToString());
            Console.WriteLine("[m.gcount]=" + m.Groups.Count);
            Console.WriteLine("[m.g0]=" + m.Groups[0].Value + "|" + m.Groups[0].Index + "|"
                + m.Groups[0].Length + "|" + B(m.Groups[0].Success) + "|" + m.Groups[0].Name);
            Console.WriteLine("[m.g1]=" + m.Groups[1].Value + "|" + m.Groups[1].Index + "|"
                + m.Groups[1].Length + "|" + B(m.Groups[1].Success) + "|" + m.Groups[1].Name);
            Console.WriteLine("[m.g2]=" + m.Groups[2].Value + "|" + m.Groups[2].Index + "|"
                + m.Groups[2].Length + "|" + B(m.Groups[2].Success) + "|" + m.Groups[2].Name);

            Match n = Regex.Match("abc", @"\d");
            Console.WriteLine("[n.success]=" + B(n.Success) + "|" + n.Index + "|" + n.Length
                + "|len" + n.Value.Length + "|gc" + n.Groups.Count + "|g0" + B(n.Groups[0].Success)
                + "|g0n" + n.Groups[0].Name + "|g0vlen" + n.Groups[0].Value.Length);

            Match p = Regex.Match("ab", @"(a)(b)");
            Console.WriteLine("[p.g1]=" + B(p.Groups[1].Success) + "|vlen" + p.Groups[1].Value.Length
                + "|" + p.Groups[1].Index + "|" + p.Groups[1].Length + "|g2=" + p.Groups[2].Value);

            Match q = Regex.Match("b", @"(a)?(b)");
            Console.WriteLine("[q.g1]=" + B(q.Groups[1].Success) + "|" + q.Groups[1].Index + "|"
                + q.Groups[1].Length + "|vlen" + q.Groups[1].Value.Length + "|g2=" + q.Groups[2].Value
                + "|gc=" + q.Groups.Count);
        }
    }
}
