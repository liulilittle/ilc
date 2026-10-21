using System;
using System.Linq;

// P0-10: reference-element LINQ operators over string[]/class[].
// Runtime entries: where_ref / select_ref_ref / first_ref /
// first_or_default_ref / any_ref / count_ref / all_ref (linq.h, linq_map.h).
// Callbacks are instance-bound delegates (the methodPtr ABI puts `this` in the
// first slot) -- the same single-cast delegate producer every int32 LINQ case
// uses.
//
// Boundary coverage (net48 oracle): First on an empty source THROWS; a null
// element is a legitimate value (First returns null, FirstOrDefault must not
// throw); a null predicate/selector/source is ArgumentNullException; the
// Where/Select results are fresh arrays independent of the source.
namespace Cases
{
    public sealed class RefOps
    {
        public bool IsLong(string s) { return s.Length > 2; }
        public bool NonEmpty(string s) { return s.Length > 0; }
        public string Bang(string s) { return s + "!"; }
        public bool IsLongWidget(Widget w) { return w.V > 1; }
        public Widget Wrap(Widget w) { return new Widget(w.V * 10); }
    }

    public sealed class Widget
    {
        public int V;
        public Widget(int v) { V = v; }
    }

    public static class LoudP010Ref
    {
        static bool ThrowsFirstOnEmpty()
        {
            try
            {
                new string[0].First();
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        static bool ThrowsArgNullWherePred()
        {
            try
            {
                new string[] { "a" }.Where((Func<string, bool>)null);
                return false;
            }
            catch (ArgumentNullException)
            {
                return true;
            }
        }

        static bool ThrowsArgNullWhereSource()
        {
            try
            {
                string[] nullSource = null;
                nullSource.Where(new RefOps().IsLong);
                return false;
            }
            catch (ArgumentNullException)
            {
                return true;
            }
        }

        static bool ThrowsArgNullSelectSel()
        {
            try
            {
                new string[] { "a" }.Select((Func<string, string>)null);
                return false;
            }
            catch (ArgumentNullException)
            {
                return true;
            }
        }

        static bool ThrowsFirstOnEmptyLast()
        {
            try
            {
                new string[0].Last();
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        static bool ThrowsArgNullAnyPred()
        {
            try
            {
                new string[] { "a" }.Any((Func<string, bool>)null);
                return false;
            }
            catch (ArgumentNullException)
            {
                return true;
            }
        }

        public static void Main()
        {
            string[] words = { "a", "bb", "ccc", "dddd" };
            string[] empty = new string[0];
            string[] nullElem = { null };
            RefOps ops = new RefOps();
            Func<string, bool> isLong = ops.IsLong;
            Func<string, bool> nonEmpty = ops.NonEmpty;
            Func<string, string> bang = ops.Bang;

            // ===== Where =====
            string[] longWords = words.Where(isLong).ToArray();
            Console.WriteLine("[p0-10-where-len]=" + longWords.Length);
            Console.WriteLine("[p0-10-where-0]=" + longWords[0]);
            Console.WriteLine("[p0-10-where-1]=" + longWords[1]);
            Console.WriteLine("[p0-10-where-empty]=" + empty.Where(isLong).ToArray().Length);

            // Result/source independence: mutating the result never touches the source.
            string[] filtered = words.Where(nonEmpty).ToArray();
            filtered[0] = "MUTATED";
            Console.WriteLine("[p0-10-where-independent]=" + (words[0] == "a" && filtered[0] == "MUTATED"));

            // ===== Select =====
            string[] banged = words.Select(bang).ToArray();
            Console.WriteLine("[p0-10-select-len]=" + banged.Length);
            Console.WriteLine("[p0-10-select-0]=" + banged[0]);
            Console.WriteLine("[p0-10-select-3]=" + banged[3]);

            string[] mapped = words.Select(bang).ToArray();
            mapped[3] = "MUTATED";
            Console.WriteLine("[p0-10-select-independent]=" + (words[3] == "dddd" && mapped[3] == "MUTATED"));

            // ===== chain =====
            string[] longBanged = words.Where(isLong).Select(bang).ToArray();
            Console.WriteLine("[p0-10-chain-len]=" + longBanged.Length);
            Console.WriteLine("[p0-10-chain-0]=" + longBanged[0]);
            Console.WriteLine("[p0-10-chain-1]=" + longBanged[1]);

            // ===== First / FirstOrDefault =====
            Console.WriteLine("[p0-10-first]=" + words.First());
            Console.WriteLine("[p0-10-first-empty-throws]=" + ThrowsFirstOnEmpty());
            Console.WriteLine("[p0-10-first-null-elem]=" + (nullElem.First() == null));
            Console.WriteLine("[p0-10-fod-empty-null]=" + (empty.FirstOrDefault() == null));
            Console.WriteLine("[p0-10-fod-null-elem]=" + (nullElem.FirstOrDefault() == null));
            Console.WriteLine("[p0-10-fod-nonempty]=" + words.FirstOrDefault());

            // ===== Any / Count / All over a reference source =====
            Console.WriteLine("[p0-10-any]=" + words.Any());
            Console.WriteLine("[p0-10-any-empty]=" + empty.Any());
            Console.WriteLine("[p0-10-anyp]=" + words.Any(isLong));
            Console.WriteLine("[p0-10-anyp-empty]=" + empty.Any(isLong));
            Console.WriteLine("[p0-10-count]=" + words.Count());
            Console.WriteLine("[p0-10-countp]=" + words.Count(isLong));
            Console.WriteLine("[p0-10-countp-empty]=" + empty.Count(isLong));
            Console.WriteLine("[p0-10-all]=" + words.All(nonEmpty));
            Console.WriteLine("[p0-10-all-empty]=" + empty.All(isLong));

            // ===== Last / LastOrDefault over a reference source =====
            // (linq.h last_ref / last_or_default_ref: the kernels exist and are
            // wired; the empty-source contract is the same as First's.)
            Console.WriteLine("[p0-10-last]=" + words.Last());
            Console.WriteLine("[p0-10-last-empty-throws]=" + ThrowsFirstOnEmptyLast());
            Console.WriteLine("[p0-10-last-null-elem]=" + (nullElem.Last() == null));
            Console.WriteLine("[p0-10-lastod-empty-null]=" + (empty.LastOrDefault() == null));
            Console.WriteLine("[p0-10-lastod-null-elem]=" + (nullElem.LastOrDefault() == null));
            Console.WriteLine("[p0-10-lastod-nonempty]=" + words.LastOrDefault());


            // ===== exception contracts =====
            Console.WriteLine("[p0-10-where-null-pred-ane]=" + ThrowsArgNullWherePred());
            Console.WriteLine("[p0-10-where-null-src-ane]=" + ThrowsArgNullWhereSource());
            Console.WriteLine("[p0-10-select-null-sel-ane]=" + ThrowsArgNullSelectSel());
            Console.WriteLine("[p0-10-any-null-pred-ane]=" + ThrowsArgNullAnyPred());

            // ===== a user class array (non-string reference element) =====
            Widget[] widgets = { new Widget(1), new Widget(2), new Widget(3) };
            Func<Widget, bool> isLongWidget = ops.IsLongWidget;
            Func<Widget, Widget> wrap = ops.Wrap;
            Console.WriteLine("[p0-10-class-where]=" + widgets.Where(isLongWidget).ToArray().Length);
            Console.WriteLine("[p0-10-class-select]=" + widgets.Select(wrap).ToArray()[2].V);
            Console.WriteLine("[p0-10-class-first]=" + widgets.First().V);
            Console.WriteLine("[p0-10-class-fod-null]=" + (new Widget[0].FirstOrDefault() == null));
            Console.WriteLine("[p0-10-class-last]=" + widgets.Last().V);
            Console.WriteLine("[p0-10-class-lastod-null]=" + (new Widget[0].LastOrDefault() == null));
        }
    }
}
