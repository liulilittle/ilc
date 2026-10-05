//------------------------------------------------------------------------------
// dictcase.cs -- end-to-end reference case for the collection/dictionary
// DEEP-CHAIN domains: generic Dictionary<string,int>, non-generic SortedList
// (sorted int keys), and a mixed Queue/Stack FIFO/LIFO chain.
//
// Deliberately does NOT re-test the non-generic Hashtable deep chain:
// hashtablecase + collchain already cover it (PASS), and its icall domain is
// shared with nothing here.
//
// EVERY line of output is a single bool, by construction:
//   * no raw values are printed (no dictionary/sorted-list enumeration
//     order, no formatting drift);
//   * SortedList keys are ints, so the sort order is deterministic on net48
//     (ascending) and matches the runtime's Int32-ascending sort contract.
//
// Runs on .NET Framework 4.8 (the reference runtime). Deterministic by
// construction: no filesystem/environment input.
//
// WIRING STATUS (2026-08-06 transpiler):
//   * Dictionary`2   -- NOT WIRED (transpiler): MethodMap.Dictionary.cs is an
//                       empty SKELETON (TryMapDictionary returns null); the
//                       runtime dictsi.cpp/h IS built (il2cpp_dict_si_*).
//                       Standard gap shape: fallback mangled-name call ->
//                       undefined symbol at C++ build. GAP RECORDED.
//   * SortedList      -- NOT WIRED (transpiler): no MethodMap.SortedList.cs,
//                       no MethodRefResolver dispatch; the runtime
//                       sortedlist.cpp/h IS built (il2cpp_sortedlist_*).
//                       GAP RECORDED.
//   * Stack/Queue     -- WIRED (MethodMap.Stack.cs / MethodMap.Queue.cs ->
//                       stackqueue.h): ctor / Push+Pop+Peek / Enqueue+
//                       Dequeue+Peek / Count / Clear.
//   * Hashtable       -- WIRED + PASS (hashtablecase/collchain), not re-tested.
//------------------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;

namespace Cases { public static class DictCase {
    public static void Main() {
        // ========= 1. Dictionary<string,int> (NOT WIRED -- recorded gap) =========
        var d = new Dictionary<string, int>();
        d.Add("one", 1);
        d.Add("two", 2);
        d.Add("three", 3);
        Console.WriteLine(d.Count == 3);                        // True
        int v;
        Console.WriteLine(d.TryGetValue("two", out v) && v == 2); // True (hit)
        Console.WriteLine(!d.TryGetValue("nope", out v));       // True (miss)
        Console.WriteLine(d.ContainsKey("one"));                // True
        Console.WriteLine(!d.ContainsKey("ONE"));               // True (ordinal keys)
        Console.WriteLine(d["two"] == 2);                       // True (indexer get)
        d["two"] = 22;                                          // indexer set: replace
        Console.WriteLine(d["two"] == 22);                      // True
        d["four"] = 4;                                          // indexer set: insert
        Console.WriteLine(d.Count == 4);                        // True
        bool dupThrown = false;
        try { d.Add("one", 111); } catch (ArgumentException) { dupThrown = true; }
        Console.WriteLine(dupThrown);                           // True (duplicate Add)
        bool nullKeyThrown = false;
        try { d.Add(null, 5); } catch (ArgumentNullException) { nullKeyThrown = true; }
        Console.WriteLine(nullKeyThrown);                       // True (null key)
        Console.WriteLine(d.Remove("three"));                   // True (was present)
        Console.WriteLine(!d.Remove("ghost"));                  // True (absent -> false)
        Console.WriteLine(d.Count == 3);                        // True
        d.Clear();
        Console.WriteLine(d.Count == 0);                        // True
        d.Add("again", 9);
        Console.WriteLine(d["again"] == 9);                     // True (reusable)
        Console.WriteLine(d.Count == 1);                        // True

        // =============== 2. SortedList (NOT WIRED -- recorded gap) ===============
        var sl = new SortedList();
        sl.Add(5, "five");
        sl.Add(2, "two");
        sl.Add(9, "nine");
        sl.Add(1, "one");
        Console.WriteLine(sl.Count == 4);                       // True
        Console.WriteLine((string)sl[2] == "two");              // True (GetByKey: indexer)
        Console.WriteLine((string)sl.GetByIndex(0) == "one");   // True (sorted: 1 first)
        Console.WriteLine((string)sl.GetByIndex(3) == "nine");  // True (sorted: 9 last)
        Console.WriteLine((int)sl.GetKey(0) == 1);              // True (Keys ascending)
        Console.WriteLine((int)sl.GetKey(1) == 2);              // True
        Console.WriteLine((int)sl.GetKey(2) == 5);              // True
        Console.WriteLine((int)sl.GetKey(3) == 9);              // True
        Console.WriteLine(sl.ContainsKey(9));                   // True
        Console.WriteLine(!sl.ContainsKey(4));                  // True
        bool slDupThrown = false;
        try { sl.Add(2, "dupe"); } catch (ArgumentException) { slDupThrown = true; }
        Console.WriteLine(slDupThrown);                         // True (duplicate Add)
        sl.Remove(5);
        Console.WriteLine(sl.Count == 3);                       // True
        Console.WriteLine(!sl.ContainsKey(5));                  // True
        sl[7] = "seven";                                        // indexer set: insert (sorted)
        Console.WriteLine(sl.Count == 4);                       // True
        Console.WriteLine((string)sl[7] == "seven");            // True (GetByKey after insert)
        Console.WriteLine((int)sl.GetKey(2) == 7);              // True (7 sorted between 2 and 9)
        Console.WriteLine((string)sl.GetByIndex(2) == "seven"); // True (index-aligned value)
        sl.Clear();
        Console.WriteLine(sl.Count == 0);                       // True

        // ============== 3. Queue/Stack mixed FIFO/LIFO chain (WIRED) ==============
        var q = new Queue();
        var s = new Stack();
        q.Enqueue("a");
        s.Push(1);
        q.Enqueue("b");
        s.Push(2);
        q.Enqueue("c");
        Console.WriteLine((string)q.Dequeue() == "a");          // True (FIFO: a first)
        Console.WriteLine((int)s.Pop() == 2);                   // True (LIFO: 2 first)
        Console.WriteLine((string)q.Dequeue() == "b");          // True
        Console.WriteLine((int)s.Peek() == 1);                  // True (Peek non-destructive)
        Console.WriteLine(s.Count == 1);                        // True
        Console.WriteLine((string)q.Dequeue() == "c");          // True
        Console.WriteLine((int)s.Pop() == 1);                   // True
        Console.WriteLine(q.Count == 0 && s.Count == 0);        // True (both drained)
        bool emptyPopThrown = false;
        try { s.Pop(); } catch (InvalidOperationException) { emptyPopThrown = true; }
        Console.WriteLine(emptyPopThrown);                      // True (Pop on empty)
        q.Enqueue("z");
        q.Clear();
        s.Push("z");
        s.Clear();
        Console.WriteLine(q.Count == 0 && s.Count == 0);        // True
    }
}}
