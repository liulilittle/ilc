//------------------------------------------------------------------------------
// p-nb6-named.cs -- nb6 OBJ-3 named-group difftest probe
// (net48 oracle == transpiled runtime byte-exact).
//
// Pins the .NET named-group numbering rule (unnamed groups numbered first,
// left-to-right; named groups numbered AFTER them, left-to-right), the
// Group.Name property, GroupCollection[string] name lookup (missing name ->
// empty group, never a throw), the ${name} / ${n} replacement tokens, and the
// parse error for an empty group name.
//
// OUT OF SCOPE (documented GAP, see report): net48 accepts all-digit names
// ((?<1>...) = explicit group number) and duplicate names ((?<x>a)(?<x>b), which
// net48 MERGES onto one group number); this runtime rejects both as
// ArgumentException (a duplicate name is a parse-time error -- verified by the
// runtime test_regex_match, not here, because net48 accepts it and would not be
// byte-exact). A "${<digits>}" group number that overflows int32 throws
// ArgumentException in both net48 and this runtime (D4, pinned below).
//------------------------------------------------------------------------------
using System;
using System.Text.RegularExpressions;

namespace Cases
{
    public static class PNb6Named
    {
        static string B(bool b) { return b ? "True" : "False"; }

        public static void Main()
        {
            // Named group sandwiched between unnamed groups: the two unnamed
            // groups keep numbers 1/2 (left-to-right), the named group is
            // numbered 3 (after all unnamed).
            Match mid = Regex.Match("abc", @"(a)(?<mid>b)(c)");
            Console.WriteLine("[mid.gc]=" + mid.Groups.Count);
            Console.WriteLine("[mid.i1]=" + mid.Groups[1].Name + "|" + mid.Groups[1].Value);
            Console.WriteLine("[mid.i2]=" + mid.Groups[2].Name + "|" + mid.Groups[2].Value);
            Console.WriteLine("[mid.i3]=" + mid.Groups[3].Name + "|" + mid.Groups[3].Value);
            Console.WriteLine("[mid.bymid]=" + mid.Groups["mid"].Index + "|" + mid.Groups["mid"].Value);
            Console.WriteLine("[mid.bynum]=" + B(mid.Groups["1"].Success) + "|" + mid.Groups["1"].Value);
            Console.WriteLine("[mid.nope]=" + B(mid.Groups["nope"].Success) + "|" + mid.Groups["nope"].Value.Length);

            // All-named pattern: numbers 1..N in left-to-right order.
            Match nm = Regex.Match("key=42", @"(?<name>\w+)=(?<val>\d+)");
            Console.WriteLine("[nm.gcount]=" + nm.Groups.Count);
            Console.WriteLine("[nm.g1]=" + nm.Groups[1].Name + "|" + nm.Groups[1].Value);
            Console.WriteLine("[nm.g2]=" + nm.Groups[2].Name + "|" + nm.Groups[2].Value);
            Console.WriteLine("[nm.by]=" + nm.Groups["name"].Value + "|" + nm.Groups["val"].Value);
            Console.WriteLine("[nm.byNum]=" + B(nm.Groups["1"].Success));
            Console.WriteLine("[nm.nope]=" + B(nm.Groups["nope"].Success));

            // ${name} replacement (and numeric ${n} / unknown literal).
            Console.WriteLine("[nr.repl]=" + Regex.Replace("key=42",
                @"(?<name>\w+)=(?<val>\d+)", "${val}=${name}"));
            Console.WriteLine("[nr.repl2]=" + Regex.Replace("ab",
                @"(?<x>a)(b)", "${x}-$2-${nope}"));
            Console.WriteLine("[nr.brace]=" + Regex.Replace("ab",
                @"(a)(b)", "${1}${0}"));

            // Repeated named group keeps its last capture.
            Match rep = Regex.Match("abab", @"(?<x>ab)+");
            Console.WriteLine("[rep.x]=" + rep.Groups["x"].Value + "|" + rep.Groups["x"].Index);

            // Non-participating named group.
            Match np = Regex.Match("b", @"(?<x>a)?(b)");
            Console.WriteLine("[np.x]=" + B(np.Groups["x"].Success) + "|" + np.Groups["x"].Value.Length);

            // Empty group name -> net48 ArgumentException at parse time.
            try { Regex.Match("a", @"(?<>a)"); Console.WriteLine("[bad2]=ok"); }
            catch (Exception e) { Console.WriteLine("[bad2]=" + e.GetType().Name); }

            // D4: a "${<digits>}" group number that overflows int32 throws
            // ArgumentException (net48 parses the replacement eagerly).
            try { Console.WriteLine("[bad3]=" + Regex.Replace("ab", @"(a)(b)", "${99999999999999999999}")); }
            catch (Exception e) { Console.WriteLine("[bad3]=" + e.GetType().Name); }
        }
    }
}
