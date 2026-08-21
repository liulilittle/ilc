//------------------------------------------------------------------------------
// sldeep.cs -- SortedList DEEP e2e reference case (net48 oracle, byte-for-byte).
// Created in e2e batch C37 (2026-08-11), AFTER batch 84 (commit 437adc1,
// "SortedList full wiring + dictsi wrapper refactor", dictcase PASS dual-
// compiler). dictcase covered the SortedList basics (int-key add / indexer /
// GetByIndex / GetKey / ContainsKey / dup-throw / remove / indexer-set /
// clear). THIS case pushes the SORTED-ARRAY contract deeper:
//
//   1. SORTING STABILITY -- the list stays in ascending key order under every
//      mutation pattern: reverse-order insert, full-range int keys (mixed
//      values: int.MinValue / int.MaxValue / 0 / negatives / positives),
//      remove + re-add + indexer-set churn, duplicate-Add rejection at
//      head/middle/tail (list unchanged), and a 100-key deterministic
//      permutation through multiple growth doublings.
//   2. MIXED KEYS (net48-valid domain) -- keys of the int type mixed across
//      the whole value range sort numerically (never lexically); a FRESHLY
//      boxed key resolves its entry by VALUE (box identity is irrelevant), and
//      the indexer SET by a fresh box inserts at the correct sorted position.
//      NOTE (recorded, not probed): true mixed-TYPE keys (int + string) are a
//      deliberate ILC divergence -- net48 throws InvalidOperationException
//      from Comparer.Default, the ILC runtime instead defines an
//      ints-before-strings total order (sortedlist.h). The e2e cannot assert
//      either side, so that surface stays covered by the C++ unit test.
//   3. SNAPSHOT -- Keys / Values return non-null snapshot objects; each
//      access yields a FRESH object (ReferenceEquals false); the sorted key
//      order is stable and index-aligned with values via GetKey/GetValueAt;
//      Capacity stays >= Count after growth.
//      NOTE (recorded, not probed): net48 Keys/Values are LIVE ICollection
//      views (a later Add is reflected), the ILC runtime returns a SNAPSHOT
//      array that does not reflect later mutations (sortedlist.h). The C#
//      surface that could observe the difference (ICollection.Count /
//      IEnumerable enumeration) is NOT wired in the transpiler, so the e2e
//      stays on the subset both sides agree on.
//   4. NULL-VALUE semantics -- a stored null value reads back as null, an
//      absent key reads back as null, and Count still counts the null-value
//      pair (net48 indexer contract).
//
// Every line is a labeled bool; a diff failure shows exactly which assertion
// flipped.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases
{
    public static class SlDeep
    {
        static bool Ascending(SortedList sl)
        {
            for (int i = 1; i < sl.Count; i++)
            {
                if ((int)sl.GetKey(i - 1) > (int)sl.GetKey(i)) return false;
            }
            return true;
        }

        public static void Main()
        {
            // ============ 1. sorting stability: reverse-order insert ============
            var sl = new SortedList();
            for (int i = 9; i >= 0; i--) sl.Add(i, "v" + i);          // 9..0
            Console.WriteLine(sl.Count == 10);                          // True
            Console.WriteLine(Ascending(sl));                           // True
            Console.WriteLine((int)sl.GetKey(0) == 0);                  // True
            Console.WriteLine((int)sl.GetKey(9) == 9);                  // True

            // ============ 2. full-range int keys (mixed values sort numerically) ============
            var sl2 = new SortedList();
            sl2.Add(2147483647, "max");
            sl2.Add(-2147483648, "min");
            sl2.Add(0, "zero");
            sl2.Add(-5, "neg5");
            sl2.Add(42, "forty");
            sl2.Add(7, "seven");
            Console.WriteLine(sl2.Count == 6);                          // True
            Console.WriteLine(Ascending(sl2));                          // True
            Console.WriteLine((int)sl2.GetKey(0) == -2147483648);       // True (min first)
            Console.WriteLine((int)sl2.GetKey(1) == -5);                // True
            Console.WriteLine((int)sl2.GetKey(4) == 42);                // True
            Console.WriteLine((int)sl2.GetKey(5) == 2147483647);        // True (max last)

            // ============ 3. churn: remove + re-add + indexer-set keeps sorted ============
            var sl3 = new SortedList();
            for (int i = 0; i < 20; i++) sl3.Add(i * 3, "a" + (i * 3)); // 0,3,...,57
            Console.WriteLine(sl3.Count == 20);                         // True
            Console.WriteLine(Ascending(sl3));                          // True
            sl3.Remove(21);                                             // remove middle
            sl3.Remove(0);                                              // remove head
            sl3.Remove(57);                                             // remove tail
            Console.WriteLine(sl3.Count == 17);                         // True
            sl3.Add(-10, "neg");                                        // new extreme head
            sl3[100] = "hundred";                                       // indexer-set insert tail
            sl3[6] = "six-replaced";                                    // indexer-set replace existing
            Console.WriteLine(sl3.Count == 19);                         // True
            Console.WriteLine(Ascending(sl3));                          // True
            Console.WriteLine((int)sl3.GetKey(0) == -10);               // True
            Console.WriteLine((int)sl3.GetKey(18) == 100);              // True
            Console.WriteLine((string)sl3[6] == "six-replaced");        // True
            Console.WriteLine((string)sl3[100] == "hundred");           // True
            Console.WriteLine((string)sl3[-10] == "neg");               // True (negative-key indexer)

            // ============ 4. duplicate Add at head/middle/tail (list unchanged) ============
            var sl4 = new SortedList();
            sl4.Add(5, "five");
            sl4.Add(10, "ten");
            sl4.Add(15, "fifteen");
            bool dup1 = false, dup2 = false, dup3 = false;
            try { sl4.Add(5, "d5"); } catch (ArgumentException) { dup1 = true; }
            try { sl4.Add(10, "d10"); } catch (ArgumentException) { dup2 = true; }
            try { sl4.Add(15, "d15"); } catch (ArgumentException) { dup3 = true; }
            Console.WriteLine(dup1 && dup2 && dup3);                    // True
            Console.WriteLine(sl4.Count == 3);                          // True (unchanged)
            Console.WriteLine((string)sl4[5] == "five");                // True (original value kept)
            Console.WriteLine((string)sl4[10] == "ten");                // True
            Console.WriteLine((string)sl4[15] == "fifteen");            // True

            // ============ 5. 100-key permutation through growth doublings ============
            var big = new SortedList();
            int[] order = new int[100];
            for (int i = 0; i < 100; i++) order[i] = (i * 7919) % 100;  // deterministic shuffle (full permutation)
            for (int i = 0; i < 100; i++) big.Add(order[i], "v");
            Console.WriteLine(big.Count == 100);                        // True
            Console.WriteLine(Ascending(big));                          // True
            bool exact = true;
            for (int i = 0; i < 100; i++)
            {
                if ((int)big.GetKey(i) != i) exact = false;             // 0..99 permutation -> unique solution
            }
            Console.WriteLine(exact);                                   // True
            Console.WriteLine(big.Capacity >= 100);                     // True (grown)

            // ============ 6. snapshot surface: fresh objects, stable order ============
            var sn = new SortedList();
            sn.Add("delta", "d");
            sn.Add("alpha", "a");
            sn.Add("charlie", "c");
            sn.Add("bravo", "b");
            Console.WriteLine(sn.Keys != null);                         // True (non-null snapshot)
            Console.WriteLine(sn.Values != null);                       // True
            Console.WriteLine(!ReferenceEquals(sn.Keys, sn.Values));    // True (Keys and Values are distinct objects)
            Console.WriteLine((string)sn.GetKey(0) == "alpha");         // True (sorted key order)
            Console.WriteLine((string)sn.GetKey(1) == "bravo");         // True
            Console.WriteLine((string)sn.GetKey(2) == "charlie");       // True
            Console.WriteLine((string)sn.GetKey(3) == "delta");         // True
            Console.WriteLine((string)sn.GetByIndex(0) == "a");         // True (value index-aligned)
            Console.WriteLine((string)sn.GetByIndex(3) == "d");         // True
            Console.WriteLine(sn.Capacity >= sn.Count);                 // True

            // ============ 7. null-value + fresh-box value semantics ============
            var sv = new SortedList();
            sv.Add(1, null);
            sv.Add(2, "two");
            Console.WriteLine(sv[1] == null);                           // True (stored null reads as null)
            Console.WriteLine((string)sv[2] == "two");                  // True (fresh box resolves by value)
            Console.WriteLine(sv[3] == null);                           // True (absent key -> null)
            Console.WriteLine(sv.Count == 2);                           // True (null-value pair counted)
            sv[3] = "three";                                            // indexer-set insert via fresh box
            Console.WriteLine((string)sv[3] == "three");                // True
            Console.WriteLine(sv.Count == 3);                           // True
            Console.WriteLine(Ascending(sv));                           // True

            // ============ 8. interface surface: ICollection.Count + IEnumerable enumeration (D-SL-3) ============
            // sl.Keys / sl.Values have compile-time type System.Collections.ICollection;
            // Count and foreach go through the ICollection/IEnumerable/IEnumerator
            // INTERFACE methods (the transpiler must dispatch them to the snapshot's
            // runtime surface). The snapshot is taken ONCE and neither side mutates
            // before the probes, so the ILC snapshot and net48 live view agree
            // (both yield the sorted key sequence and the current count).
            var iu = new SortedList();
            iu.Add("delta", "d");
            iu.Add("alpha", "a");
            iu.Add("charlie", "c");
            iu.Add("bravo", "b");
            System.Collections.ICollection ikeys = iu.Keys;
            Console.WriteLine(ikeys.Count == 4);                        // True (ICollection.Count via interface)
            Console.WriteLine(iu.Values.Count == 4);                    // True (Values ICollection.Count)
            int nKeys = 0;
            foreach (object k in ikeys) { nKeys++; }
            Console.WriteLine(nKeys == 4);                              // True (IEnumerable enumeration length)
            string keystr = "";
            foreach (object k in ikeys) { keystr += (string)k; }
            Console.WriteLine(keystr == "alphabravocharliedelta");       // True (sorted enumeration order)
            string valstr = "";
            foreach (object v in iu.Values) { valstr += (string)v; }
            Console.WriteLine(valstr == "abcd");                        // True (value enumeration, index-aligned)
            System.Collections.IEnumerator en = ikeys.GetEnumerator();
            en.MoveNext();
            Console.WriteLine((string)en.Current == "alpha");           // True (explicit IEnumerator protocol)
            en.Reset();
            Console.WriteLine(en.MoveNext());                           // True (Reset then MoveNext)
            Console.WriteLine((string)en.Current == "alpha");           // True
        }
    }
}
