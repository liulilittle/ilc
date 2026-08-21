//------------------------------------------------------------------------------
// collchain.cs -- end-to-end reference case for a DEEP CHAIN of non-generic
// System.Collections containers: Hashtable (string keys, boxed int keys,
// expansion past the 0.72 load factor, clear + reuse), Stack and Queue
// (interleaved FIFO/LIFO order assertions), and ArrayList.
//
// Deliberately avoids the generic List<T> domain (not wired).
//
// EVERY line of output is a single bool, by construction:
//   * no raw values are printed (no Hashtable enumeration order, no value
//     formatting drift);
//   * string-key Hashtable enumeration is order-nondeterministic on net48
//     (randomized string hash), so "Keys enumeration" is SIMPLIFIED to a
//     ContainsKey sweep over all inserted keys -- same coverage, deterministic.
//
// Runs on .NET Framework 4.8 (the reference runtime). Deterministic by
// construction: Random(12345) fixed seed, no filesystem/environment input.
//
// WIRING STATUS (2026-08-03 transpiler):
//   * Hashtable   -- WIRED  (MethodMap.Hashtable.cs -> il2cpp_hashtable.h):
//                    Add / indexer get+set / ContainsKey / Remove / Count /
//                    Clear / parameterless + capacity ctor. get_Keys is NOT
//                    wired -> the case only uses the ContainsKey sweep.
//   * Stack/Queue -- WIRED  (MethodMap.Stack.cs / MethodMap.Queue.cs ->
//                    stackqueue.h): ctor / Push+Pop+Peek / Enqueue+Dequeue+
//                    Peek / Count / Clear.
//   * ArrayList    -- NOT WIRED (no MethodMap.ArrayList.cs, no runtime
//                    arraylist icalls). The ArrayList section below is the
//                    documented intended semantics; the transpiler aborts at
//                    the first ArrayList call (fallback mangled-name method
//                    -> undefined symbol at C++ build, standard gap shape).
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class CollChain {
    public static void Main() {
        // ================= 1. Hashtable key-value chain =================
        var t = new Hashtable();
        t.Add("one", 1);
        t.Add("two", 2);
        t.Add("three", 3);
        Console.WriteLine(t.Count == 3);                  // True
        Console.WriteLine((int)t["one"] == 1);            // True
        Console.WriteLine(t["missing"] == null);          // True (missing key -> null)
        Console.WriteLine(t.ContainsKey("two"));          // True
        Console.WriteLine(!t.ContainsKey("TWO"));         // True (ordinal keys)
        t["two"] = 22;                                    // indexer set: replace
        Console.WriteLine((int)t["two"] == 22);           // True
        t["four"] = 4;                                    // indexer set: insert
        Console.WriteLine(t.Count == 4);                  // True
        bool dupThrown = false;
        try { t.Add("one", 111); } catch (ArgumentException) { dupThrown = true; }
        Console.WriteLine(dupThrown);                     // True (duplicate Add)
        bool nullKeyThrown = false;
        try { t.Add(null, 5); } catch (ArgumentNullException) { nullKeyThrown = true; }
        Console.WriteLine(nullKeyThrown);                 // True (null key)
        t.Remove("three");
        Console.WriteLine(!t.ContainsKey("three"));       // True
        Console.WriteLine(t.Count == 3);                  // True
        t.Remove("ghost");                                // silent on missing key
        Console.WriteLine(t.Count == 3);                  // True
        t.Remove("four");
        t.Add("four", 44);                                // remove -> re-add chain
        Console.WriteLine((int)t["four"] == 44);          // True
        Console.WriteLine(t.Count == 3);                  // True
        Console.WriteLine(t.ContainsKey("one") && t.ContainsKey("two") && t.ContainsKey("four")); // True
        t.Add(7, "seven");                                // boxed int key
        Console.WriteLine((string)t[7] == "seven");       // True
        Console.WriteLine(t[8] == null);                  // True
        Console.WriteLine(t.ContainsKey(7));              // True
        t["nv"] = null;                                   // null value is legal
        Console.WriteLine(t["nv"] == null);               // True
        Console.WriteLine(t.ContainsKey("nv"));           // True
        // --- expansion past load factor: 100 fixed-seed keys ---
        var rng = new Random(12345);
        bool kvOk = true;
        for (int i = 0; i < 100; i++) { int v = rng.Next(1000); t.Add("k" + i, v); if ((int)t["k" + i] != v) kvOk = false; }
        Console.WriteLine(kvOk);                          // True
        Console.WriteLine(t.Count == 105);                // True (5 existing keys + 100)
        int found = 0;                                    // simplified Keys sweep
        for (int i = 0; i < 100; i++) if (t.ContainsKey("k" + i)) found++;
        Console.WriteLine(found == 100);                  // True
        // --- clear + reuse ---
        t.Clear();
        Console.WriteLine(t.Count == 0);                  // True
        Console.WriteLine(!t.ContainsKey("k0"));          // True
        t.Add("again", 1);
        Console.WriteLine((int)t["again"] == 1);          // True
        Console.WriteLine(t.Count == 1);                  // True

        // ================= 2. Stack/Queue mixed FIFO/LIFO order =================
        var q = new Queue();
        var s = new Stack();
        q.Enqueue("a");
        s.Push(1);
        q.Enqueue("b");
        s.Push(2);
        s.Push(3);
        q.Enqueue("c");
        Console.WriteLine((string)q.Dequeue() == "a");    // True (FIFO: a first)
        Console.WriteLine((int)s.Pop() == 3);             // True (LIFO: 3 first)
        Console.WriteLine((string)q.Dequeue() == "b");    // True
        Console.WriteLine((int)s.Pop() == 2);             // True
        Console.WriteLine((int)s.Peek() == 1);            // True (Peek non-destructive)
        Console.WriteLine(s.Count == 1);                  // True (Peek did not pop)
        Console.WriteLine((string)q.Dequeue() == "c");    // True
        Console.WriteLine((int)s.Pop() == 1);             // True
        Console.WriteLine(q.Count == 0 && s.Count == 0);  // True (both drained)
        bool emptyPopThrown = false;
        try { s.Pop(); } catch (InvalidOperationException) { emptyPopThrown = true; }
        Console.WriteLine(emptyPopThrown);                // True (Pop on empty)
        bool emptyDqThrown = false;
        try { q.Dequeue(); } catch (InvalidOperationException) { emptyDqThrown = true; }
        Console.WriteLine(emptyDqThrown);                 // True (Dequeue on empty)
        q.Enqueue("z");
        q.Clear();
        Console.WriteLine(q.Count == 0);                  // True
        s.Push("z");
        s.Clear();
        Console.WriteLine(s.Count == 0);                  // True

        // ================= 3. ArrayList (NOT WIRED -- documented gap) =================
        var arr = new ArrayList();
        arr.Add("a");
        arr.Add("b");
        arr.Add(3);                                       // boxed int element
        Console.WriteLine(arr.Count == 3);                // True
        Console.WriteLine((string)arr[0] == "a");         // True (indexer get)
        Console.WriteLine((int)arr[2] == 3);              // True
        arr[1] = "B";                                     // indexer set
        Console.WriteLine((string)arr[1] == "B");         // True
        arr.Remove("a");                                  // remove shifts elements
        Console.WriteLine(arr.Count == 2);                // True
        Console.WriteLine((string)arr[0] == "B");         // True (set to "B", then Remove shifts it to index 0)
    }
}}
