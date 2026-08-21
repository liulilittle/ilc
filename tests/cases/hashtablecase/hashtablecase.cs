//------------------------------------------------------------------------------
// hashtablecase.cs -- end-to-end reference case for the non-generic
// System.Collections.Hashtable runtime domain.
//
// Runs on .NET Framework 4.8 (the reference runtime) and demonstrates the
// exact semantics the runtime icalls implement (see il2cpp_hashtable.h):
//   * Add + indexer set/get, string keys (ordinal hashing);
//   * boxed int keys (value hashing);
//   * duplicate Add -> ArgumentException (indexer set replaces instead);
//   * indexer get of a missing key -> null (no exception);
//   * null values are legal;
//   * Remove of a missing key -> silent success;
//   * Clear + reuse, Count, ContainsKey, capacity ctor.
//
// Every line of output is deterministic and mirrors the icall contract.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class HashtableCase {
    public static void Main() {
        var t = new Hashtable();                 // default ctor
        t.Add("one", 1);                         // string key, boxed int value
        t.Add("two", 2);
        t.Add(10, "ten");                        // boxed int key
        Console.WriteLine(t.Count);              // 3
        Console.WriteLine(t["one"]);             // 1
        t["two"] = 22;                           // indexer set: replace
        Console.WriteLine(t["two"]);             // 22
        t["three"] = 3;                          // indexer set: insert
        Console.WriteLine(t.Count);              // 4
        Console.WriteLine(t.ContainsKey("three")); // True
        Console.WriteLine(t.ContainsKey("nope"));  // False
        Console.WriteLine(t["nope"] == null ? "null" : "x"); // null (missing key)
        t["nv"] = null;                          // null value is legal
        Console.WriteLine(t["nv"] == null ? "nullval" : "x"); // nullval
        Console.WriteLine(t[10]);                // ten
        t.Remove("one");                 // 5 pairs -> 4
        Console.WriteLine(t.Count);      // 4
        Console.WriteLine(t.ContainsKey("one")); // False
        t.Remove("missing");                     // silent success
        try { t.Add("two", 9); }                 // duplicate -> ArgumentException
        catch (ArgumentException) { Console.WriteLine("dup"); }
        Console.WriteLine(t["two"]);             // 22 (unchanged by the failed Add)
        var s = new Hashtable(3);                // capacity ctor
        s["a"] = "b";
        Console.WriteLine(s.Count);              // 1
        s.Clear();
        Console.WriteLine(s.Count);              // 0
    }
}}
