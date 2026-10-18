//------------------------------------------------------------------------------
// p-nb6-matches.cs -- nb6 OBJ-2 MatchCollection difftest probe
// (net48 oracle == transpiled runtime byte-exact).
//
// Pins the Regex.Matches(string, string[, RegexOptions]) static overloads and
// the MatchCollection object graph reusing the nb5 Match/Group/GroupCollection
// model: Count, the int indexer, and foreach enumeration (IEnumerator).
//------------------------------------------------------------------------------
using System;
using System.Text.RegularExpressions;

namespace Cases
{
    public static class PNb6Matches
    {
        static string B(bool b) { return b ? "True" : "False"; }

        public static void Main()
        {
            // Regex.Matches: 3 non-overlapping matches (leftmost, non-empty).
            MatchCollection rg = Regex.Matches("1 22 333", @"\d+");
            Console.WriteLine("[rg.matches.cnt]=" + rg.Count);
            Console.WriteLine("[rg.matches.0]=" + rg[0].Value);
            Console.WriteLine("[rg.matches.1]=" + rg[1].Value);
            Console.WriteLine("[rg.matches.2]=" + rg[2].Value);

            // MatchCollection reuses the Match/Group graph (groups per match).
            MatchCollection mc = Regex.Matches("a12b34c", @"(\d)(\d)");
            Console.WriteLine("[mc.count]=" + mc.Count);
            Console.WriteLine("[mc.0]=" + mc[0].Value + "|" + mc[0].Groups[1].Value
                + "|" + mc[0].Groups[2].Value + "|" + mc[0].Index);
            Console.WriteLine("[mc.1]=" + mc[1].Value + "|" + mc[1].Groups[1].Value
                + "|" + mc[1].Index);

            // foreach enumeration over the collection.
            int sum = 0;
            foreach (Match mm in mc)
            {
                sum += mm.Value.Length;
            }
            Console.WriteLine("[mc.iter.sum]=" + sum);

            // Empty Matches: a non-null collection with Count 0.
            MatchCollection emc = Regex.Matches("abc", @"\d");
            Console.WriteLine("[emc.count]=" + emc.Count);

            // 3-arg options overload.
            MatchCollection opt = Regex.Matches("A1b2", @"[a-z]\d", RegexOptions.IgnoreCase);
            Console.WriteLine("[mo.cnt]=" + opt.Count);
            Console.WriteLine("[mo.0]=" + opt[0].Value);

            // Out-of-range indexer throws ArgumentOutOfRangeException.
            try { Match oob = rg[99]; Console.WriteLine("[mc.oob]=" + oob.Value); }
            catch (Exception e) { Console.WriteLine("[mc.oob]=" + e.GetType().Name); }
            try { Match neg = rg[-1]; Console.WriteLine("[mc.neg]=" + neg.Value); }
            catch (Exception e) { Console.WriteLine("[mc.neg]=" + e.GetType().Name); }
        }
    }
}
