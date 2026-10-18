//------------------------------------------------------------------------------
// p-nb8-regex2.cs -- nb8 REGEX-GAP difftest probe (net48 oracle == transpiled
// runtime byte-exact).
//
// Pins the two parser extensions nb6 left as GAP:
//   * single-quote group aliases "(?'name'...)" (identical semantics to
//     "(?<name>...)", mixed with unnamed groups numbered by the .NET rule).
//   * all-digit group names "(?<1>...)" = an explicit .NET group NUMBER,
//     including the net48 collision rule (an explicit number may share a slot
//     with an unnamed group; the last capture wins) and Groups.Count.
//
// OUT OF SCOPE (documented GAP): a numbering HOLE ("(?<2>a)" alone pads an
// empty group 1) is refused loudly -- the .NET sparse-capture machinery is a
// larger follow-up; duplicate real names remain loudly rejected (net48 merges).
//------------------------------------------------------------------------------
using System;
using System.Text.RegularExpressions;

namespace Cases
{
    public static class PNb8Regex2
    {
        static string B(bool b) { return b ? "True" : "False"; }

        static string Grp(Match m, int i)
        {
            Group g = m.Groups[i];
            return g.Name + "|" + g.Value;
        }

        static string ByName(Match m, string n)
        {
            Group g = m.Groups[n];
            return B(g.Success) + "|" + g.Value;
        }

        public static void Main()
        {
            // --- single-quote aliases ------------------------------------------
            Match sq = Regex.Match("key=42", @"(?'name'\w+)=(?'val'\d+)");
            Console.WriteLine("[sq.count]=" + sq.Groups.Count);
            Console.WriteLine("[sq.name]=" + ByName(sq, "name"));
            Console.WriteLine("[sq.val]=" + ByName(sq, "val"));
            Console.WriteLine("[sq.byName]=" + sq.Groups["name"].Name + "|" + sq.Groups["name"].Value);
            Console.WriteLine("[sq.repl]=" + Regex.Replace("key=42", @"(?'name'\w+)=(?'val'\d+)", "${val}=${name}"));

            // single-quote mixed with unnamed groups: unnamed numbered first.
            Match sqm = Regex.Match("abc", @"(a)(?'mid'b)(c)");
            Console.WriteLine("[sqm.count]=" + sqm.Groups.Count);
            Console.WriteLine("[sqm.i1]=" + Grp(sqm, 1));
            Console.WriteLine("[sqm.i2]=" + Grp(sqm, 2));
            Console.WriteLine("[sqm.i3]=" + Grp(sqm, 3));
            Console.WriteLine("[sqm.bymid]=" + ByName(sqm, "mid"));

            // --- all-digit group names (explicit numbers) ----------------------
            Match num = Regex.Match("ab", @"(?<1>a)(?<2>b)");
            Console.WriteLine("[num.count]=" + num.Groups.Count);
            Console.WriteLine("[num.i1]=" + Grp(num, 1));
            Console.WriteLine("[num.i2]=" + Grp(num, 2));
            Console.WriteLine("[num.by1]=" + ByName(num, "1"));
            Console.WriteLine("[num.by2]=" + ByName(num, "2"));

            // explicit number colliding with an unnamed group (last capture wins).
            Match mix = Regex.Match("abc", @"(a)(?<2>b)(c)");
            Console.WriteLine("[mix.count]=" + mix.Groups.Count);
            Console.WriteLine("[mix.i1]=" + Grp(mix, 1));
            Console.WriteLine("[mix.i2]=" + Grp(mix, 2));
            Console.WriteLine("[mix.i3]=" + Grp(mix, 3));
            Console.WriteLine("[mix.by2]=" + ByName(mix, "2"));

            // ${n} replacement using an explicit numeric group name.
            Console.WriteLine("[num.repl]=" + Regex.Replace("ab", @"(?<1>a)(?<2>b)", "${2}${1}"));

            // duplicate explicit numbers collapse onto one slot.
            Match d = Regex.Match("aa", @"(?<1>a)(?<1>a)");
            Console.WriteLine("[dup.num]=" + d.Groups.Count + "|" + Grp(d, 1));

            // GAP#5: a bare "$<digits>" scans the whole run as one int32.
            Console.WriteLine("[r.absent10]=" + Regex.Replace("a1", @"(\d)", "$10"));
            Console.WriteLine("[r.absent99]=" + Regex.Replace("ab", @"(a)(b)", "$99"));
            Console.WriteLine("[r.lead0]=" + Regex.Replace("a1", @"(\d)", "$01"));
            try { Console.WriteLine("[r.overflow]=" + Regex.Replace("a1", @"(\d)", "$9999999999")); }
            catch (Exception e) { Console.WriteLine("[r.overflow]=" + e.GetType().Name); }
            try { Console.WriteLine("[r.overflow.nomatch]=" + Regex.Replace("x", @"q", "$9999999999")); }
            catch (Exception e) { Console.WriteLine("[r.overflow.nomatch]=" + e.GetType().Name); }

            Console.WriteLine("[done]=1");
        }
    }
}
