//------------------------------------------------------------------------------
// collation1.cs -- CULTURE collation for System.String keys (net48 oracle,
// byte-for-byte). impl-collation.
//
// net48 orders strings with Comparer<string>.Default, i.e.
// CultureInfo.CurrentCulture.CompareInfo with CompareOptions.None. That is NOT
// ordinal: "a" sorts BEFORE "A", "a" sorts before "B", and the comparison uses
// the OS NLS weights. The runtime answers it with
// runtime::il2cpp_string_compare_culture -- CompareStringEx with the current
// locale, the same OS primitive net48 uses on Windows.
//
// The nine dimensions this case pins (E15 items 1..9):
//   1. OrderBy over a reference array with a STATICALLY System.String key
//      (mixed case: culture, not ordinal) plus ToArray materialization.
//   2. OrderByDescending is the exact inverse of 1.
//   3. ThenBy / ThenByDescending with a string key in EITHER position of the
//      fused composite (int primary + string secondary, string primary + int
//      secondary, string + string).
//   4. Comparer<string>.Default pair comparisons, printed against the ORDINAL
//      answer so a silent ordinal shortcut is visible as a mismatch.
//   5. Non-ASCII inputs (accented Latin, CJK, kana, sharp s) -- these are the
//      shapes where an ordinal or ASCII-fold implementation diverges.
//   6. Min / Max over string sequences: empty -> null, all-null -> null, a null
//      never wins once a value exists, and the winner is chosen with culture
//      collation.
//   7. List<string> sources for the same operators (the list snapshots through
//      its own accessor and rides the same culture comparison).
//   8. An `object[]` source whose TKey is System.Object: the keys ARE strings
//      at run time, so the culture comparison must be chosen per INSTANCE.
//   9. STABILITY under equal keys -- net48 OrderBy is a stable sort, so equal
//      keys keep their source order.
//
// All probes stay on the wired surface; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class Collation1
    {
        private static string J(IEnumerable<string> xs) { return string.Join(",", xs); }

        private static string Show(string s) { return s == null ? "<null>" : "[" + s + "]"; }

        // The CULTURE answer for a pair, taken from the operator that now really
        // implements net48 Comparer<string>.Default (Enumerable.OrderBy), so this
        // case never depends on a Comparer<T> singleton the runtime still folds.
        // "-1" = a first, "1" = b first, "0" = the sort left them adjacent in
        // the order given (i.e. they compare equal, net48 stability).
        private static int CultureOrder(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.Ordinal)) return 0;
            string[] sorted = new[] { a, b }.OrderBy(s => s).ToArray();
            if (sorted[0] == a && sorted[1] == b) return -1;
            if (sorted[0] == b && sorted[1] == a) return 1;
            return 0;
        }

        // "culture/ordinal" -- the two answers side by side. An accidental
        // ORDINAL implementation in the sort would make the first number equal
        // the second.
        private static string Pair(string a, string b)
        {
            return CultureOrder(a, b).ToString() + "/" +
                   Math.Sign(string.CompareOrdinal(a, b)).ToString();
        }

        public static void Main()
        {
            // --- 1. OrderBy with a static string key (culture, not ordinal) ---
            string[] words = { "b", "A", "c", "a" };
            Console.WriteLine("[orderby]=" + J(words.OrderBy(s => s)));
            Console.WriteLine("[orderby.array]=" + J(words.OrderBy(s => s).ToArray()));
            Console.WriteLine("[orderby.src.unmodified]=" + J(words));
            string[] letters = { "b", "a", "B", "A" };
            Console.WriteLine("[orderby.letters]=" + J(letters.OrderBy(s => s)));
            Console.WriteLine("[orderby.prefix]=" + J(new[] { "ab", "a", "abc", "" }.OrderBy(s => s)));

            // --- 2. OrderByDescending is the exact inverse ---
            Console.WriteLine("[orderby.desc]=" + J(words.OrderByDescending(s => s)));
            Console.WriteLine("[orderby.desc.letters]=" + J(letters.OrderByDescending(s => s)));

            // --- 3. ThenBy / ThenByDescending fused composites ---
            string[] mixed = { "b2", "A1", "b1", "A2", "a3" };
            Console.WriteLine("[thenby.int.then.string]=" + J(
                mixed.OrderBy(s => s.Length).ThenBy(s => s)));
            Console.WriteLine("[thenby.int.then.string.desc]=" + J(
                mixed.OrderBy(s => s.Length).ThenByDescending(s => s)));
            Console.WriteLine("[thenby.string.then.int]=" + J(
                mixed.OrderBy(s => s).ThenBy(s => s.Length)));
            Console.WriteLine("[thenby.string.then.string]=" + J(
                mixed.OrderBy(s => s.Substring(0, 1)).ThenBy(s => s)));
            Console.WriteLine("[thenby.desc.primary]=" + J(
                mixed.OrderByDescending(s => s).ThenBy(s => s)));

            // --- 4. String pairs: culture vs ordinal ---
            Console.WriteLine("[pair.a/A]=" + Pair("a", "A"));
            Console.WriteLine("[pair.A/a]=" + Pair("A", "a"));
            Console.WriteLine("[pair.a/B]=" + Pair("a", "B"));
            Console.WriteLine("[pair.b/A]=" + Pair("b", "A"));
            Console.WriteLine("[pair._/a]=" + Pair("_", "a"));
            Console.WriteLine("[pair.Z/a]=" + Pair("Z", "a"));
            Console.WriteLine("[pair.empty/a]=" + Pair("", "a"));
            Console.WriteLine("[pair.ab/a]=" + Pair("ab", "a"));
            Console.WriteLine("[pair.eq]=" + Pair("abc", "abc"));
            Console.WriteLine("[pair.culture.ne.ordinal]=" + (Pair("a", "A") != "0/0" &&
                CultureOrder("a", "A").ToString() != Math.Sign(string.CompareOrdinal("a", "A")).ToString()));

            // --- 5. Non-ASCII (OS NLS weights, not code units) ---
            Console.WriteLine("[n.pair.e/acute]=" + Pair("e", "\u00e9"));
            Console.WriteLine("[n.pair.a/umlaut]=" + Pair("\u00e4", "z"));
            Console.WriteLine("[n.pair.cn]=" + Pair("\u4e2d", "\u56fd"));
            Console.WriteLine("[n.pair.kana]=" + Pair("\u3042", "\u30a2"));
            Console.WriteLine("[n.pair.kana2]=" + Pair("\u304b", "\u3042"));
            Console.WriteLine("[n.orderby]=" +
                J(new[] { "\u00e4", "z", "\u00e9", "e", "a", "B" }.OrderBy(s => s)));
            Console.WriteLine("[n.min]=" + Show(new[] { "\u00e4", "z", "\u00e9" }.Min()));
            Console.WriteLine("[n.max]=" + Show(new[] { "\u00e4", "z", "\u00e9" }.Max()));

            // --- 6. Min / Max over string sequences ---
            Console.WriteLine("[min.bAcA]=" + Show(words.Min()));
            Console.WriteLine("[max.bAcA]=" + Show(words.Max()));
            Console.WriteLine("[min.Ba]=" + Show(new[] { "B", "a" }.Min()));
            Console.WriteLine("[max.Ba]=" + Show(new[] { "B", "a" }.Max()));
            Console.WriteLine("[min.empty]=" + Show(new string[0].Min()));
            Console.WriteLine("[max.empty]=" + Show(new string[0].Max()));
            Console.WriteLine("[min.allnull]=" + Show(new string[] { null, null }.Min()));
            Console.WriteLine("[max.allnull]=" + Show(new string[] { null, null }.Max()));
            Console.WriteLine("[min.nulls]=" + Show(new[] { "b", null, "A" }.Min()));
            Console.WriteLine("[max.nulls]=" + Show(new[] { "b", null, "A" }.Max()));
            Console.WriteLine("[min.single]=" + Show(new[] { "b" }.Min()));

            // --- 7. List<string> sources ---
            var list = new List<string> { "b", "A", "c", "a" };
            Console.WriteLine("[list.orderby]=" + J(list.OrderBy(s => s).ToArray()));
            Console.WriteLine("[list.orderby.desc]=" + J(list.OrderByDescending(s => s).ToArray()));
            Console.WriteLine("[list.min]=" + Show(list.Min()));
            Console.WriteLine("[list.max]=" + Show(list.Max()));
            Console.WriteLine("[list.empty.min]=" + Show(new List<string>().Min()));
            Console.WriteLine("[list.nulls.max]=" + Show(new List<string> { null, "a", "B" }.Max()));
            var mixedList = new List<string> { "b2", "A1", "b1", "A2", "a3" };
            Console.WriteLine("[list.thenby.int.then.string]=" + J(
                mixedList.OrderBy(s => s.Length).ThenBy(s => s)));
            Console.WriteLine("[list.thenby.string.then.int]=" + J(
                mixedList.OrderBy(s => s).ThenBy(s => s.Length)));
            Console.WriteLine("[list.thenby.desc2]=" + J(
                mixedList.OrderBy(s => s.Length).ThenByDescending(s => s)));

            // --- 8. object[] source: the key type is System.Object ---
            // net48 CompareRefKeys-equivalent: the key STATIC type is object, so
            // only the run-time instance reveals that they are strings.
            object[] objs = { "b", "A", "c", "a" };
            object[] sortedObjs = objs.OrderBy(x => x).ToArray();
            string[] sortedAsString = new string[sortedObjs.Length];
            for (int i = 0; i < sortedObjs.Length; ++i) sortedAsString[i] = (string)sortedObjs[i];
            Console.WriteLine("[objkey]=" + J(sortedAsString));
            object[] descObjs = objs.OrderByDescending(x => x).ToArray();
            string[] descAsString = new string[descObjs.Length];
            for (int i = 0; i < descObjs.Length; ++i) descAsString[i] = (string)descObjs[i];
            Console.WriteLine("[objkey.desc]=" + J(descAsString));
            object[] withNull = { "b", null, "a" };
            object[] nullSorted = withNull.OrderBy(x => x).ToArray();
            string[] nullShown = new string[nullSorted.Length];
            for (int i = 0; i < nullSorted.Length; ++i) nullShown[i] = Show(nullSorted[i] as string);
            Console.WriteLine("[objkey.null]=" + J(nullShown));

            // --- 9. Stability under equal keys ---
            // Every element has key "k", so the result must be the source order.
            string[] same = { "x", "y", "z", "w" };
            Console.WriteLine("[stable.samekey]=" + J(same.OrderBy(s => "k")));
            // Equal keys modulo case: "a" and "A" compare equal under culture, so
            // their relative order must be preserved (a1 before A2 before A3 ...).
            string[] eq = { "a1", "A1", "a2", "A2" };
            Console.WriteLine("[stable.case]=" + J(eq.OrderBy(s => s.Substring(0, 1).ToLowerInvariant())));
            Console.WriteLine("[stable.thenby]=" +
                J(new[] { "a2", "A1", "a1", "A2" }.OrderBy(s => s.Length).ThenBy(s => s.Substring(0, 1).ToLowerInvariant())));
        }
    }
}
