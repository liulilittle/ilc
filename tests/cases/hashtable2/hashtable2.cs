//------------------------------------------------------------------------------
// hashtable2.cs -- end-to-end reference case for the non-generic
// System.Collections.Hashtable: add/query/remove, string keys (ordinal),
// boxed int keys, and -- crucially -- every key remains queryable after the
// internal bucket array grows past the 0.72 load-factor resizes.
//
// Runs on .NET Framework 4.8 (the reference runtime). Deterministic by
// construction: Random(12345) fixed seed, no filesystem/environment input.
//
// Every line of output is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class Hashtable2 {
    public static void Main() {
        var t = new Hashtable();
        // --- add/query with string keys ---
        t.Add("one", 1);
        t.Add("two", 2);
        t.Add("three", 3);
        Console.WriteLine(t.Count);                       // 3
        Console.WriteLine(t["one"]);                      // 1
        Console.WriteLine(t["missing"] == null);          // True
        Console.WriteLine(t.ContainsKey("two"));          // True
        Console.WriteLine(t.ContainsKey("TWO"));          // False (ordinal keys)
        // --- indexer: replace existing / insert new ---
        t["two"] = 22;
        Console.WriteLine(t["two"]);                      // 22
        t["four"] = 4;
        Console.WriteLine(t.Count);                       // 4
        // --- duplicate Add ---
        try { t.Add("one", 111); } catch (ArgumentException e) {
            Console.WriteLine(e.GetType().Name);          // ArgumentException
        }
        // --- null key ---
        try { t.Add(null, 5); } catch (ArgumentNullException e) {
            Console.WriteLine(e.GetType().Name);          // ArgumentNullException
        }
        // --- remove ---
        t.Remove("three");
        Console.WriteLine(t.ContainsKey("three"));        // False
        Console.WriteLine(t.Count);                       // 3
        t.Remove("ghost");                                // silent on missing key
        Console.WriteLine(t.Count);                       // 3
        // --- expansion: 100 fixed-seed keys, all still queryable ---
        var rng = new Random(12345);
        for (int i = 0; i < 100; i++) t.Add("k" + i, rng.Next(1000));
        Console.WriteLine(t.Count);                       // 103
        int found = 0;
        for (int i = 0; i < 100; i++) if (t.ContainsKey("k" + i)) found++;
        Console.WriteLine(found);                         // 100
        Console.WriteLine(t["k50"]);                      // deterministic (seed 12345)
        Console.WriteLine(t["k99"]);                      // deterministic (seed 12345)
        // --- boxed int keys ---
        t.Add(7, "seven");
        Console.WriteLine(t[7]);                          // seven
        Console.WriteLine(t[8] == null);                  // True
        Console.WriteLine(t.ContainsKey(7));              // True
        // --- clear + reuse ---
        t.Clear();
        Console.WriteLine(t.Count);                       // 0
        Console.WriteLine(t.ContainsKey("k0"));           // False
        t.Add("again", 1);
        Console.WriteLine(t["again"]);                    // 1
        Console.WriteLine(t.Count);                       // 1
    }
}}
