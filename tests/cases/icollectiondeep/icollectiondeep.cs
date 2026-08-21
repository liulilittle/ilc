//------------------------------------------------------------------------------
// icollectiondeep.cs -- System.Collections.ICollection DEEP e2e reference case
// (net48 oracle, byte-for-byte). Created in e2e batch C53 (2026-08-12), AFTER
// the ICollection/IEnumerable/IEnumerator interface surface wiring
// (MethodMap.Collections.cs -- TryMapCollectionInterface, the slic-b23 fix
// from e2e C37 sldeep).
//
// WIRED SURFACE COVERED:
//   * ICollection.get_Count over the SortedList Keys / Values snapshot arrays
//     (receiver pseudo-types "System.Collections.SortedList.KeyList" /
//     ".ValueList" -- il2cpp_sortedlist_snapshot_get_count).
//   * IEnumerable.GetEnumerator over the snapshots and over Stack / Queue
//     (the C42 sqenum-b23 snapshot enumerators).
//   * IEnumerator.MoveNext / get_Current / Reset over the snapshot and
//     Stack/Queue enumerator objects.
//
// DEEP SEMANTICS exercised:
//   1. COUNT via a compile-time ICollection reference: fresh Keys/Values
//      snapshots track the list across add / indexer-set replace / indexer-set
//      insert / remove / clear / re-add. (Snapshots are taken FRESH per access;
//      holding one across a mutation would diverge -- net48 Keys/Values are
//      LIVE cached views, the ILC runtime returns a frozen snapshot. See GAPS.)
//   2. ENUMERATION via IEnumerable: sorted ascending key order for Keys,
//      key-ordered value sequence for Values; the enumeration length equals
//      Count; empty snapshots enumerate zero times; duplicate values are
//      enumerated once per entry.
//   3. MANUAL IEnumerator protocol: MoveNext/Current stepping, Reset rewind,
//      MoveNext past the end returns false repeatedly.
//   4. STACK/QUEUE IEnumerable: foreach yields LIFO (Stack) and FIFO (Queue)
//      order, current contents at GetEnumerator time, empty containers
//      enumerate zero times.
//
// NOT PROBED (recorded GAPS, not fixable here): ICollection.IsSynchronized /
// SyncRoot / CopyTo (no MethodMap arm anywhere in the interface surface --
// generated-name undefined symbol at C++ build); ICollection.Count over
// Hashtable / Stack / Queue / ArrayList receivers (only the SortedList
// snapshot receivers are tracked).
//
// Every line is a labeled bool, so a diff failure shows exactly which
// assertion flipped. Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class ICollectionDeep {

    public static void Main() {
        // ============ 1. ICollection.Count deep (fresh snapshot per access) ============
        // NOTE: `sl.Keys` returns an ICollection-typed snapshot; every access
        // below reads a FRESH snapshot so the count always reflects the list
        // state on BOTH runtimes (net48 Keys/Values are LIVE views, the ILC
        // runtime returns a frozen snapshot -- holding one across a mutation
        // would diverge, so no reference is ever held across a mutation).
        var sl = new SortedList();
        Console.WriteLine(sl.Keys.Count == 0);                   // True (empty Keys)
        Console.WriteLine(sl.Values.Count == 0);                 // True (empty Values)
        sl.Add("alpha", 1);
        Console.WriteLine(sl.Keys.Count == 1);                   // True (fresh snapshot: 1)
        Console.WriteLine(sl.Values.Count == 1);                 // True
        sl.Add("bravo", 2);
        sl.Add("charlie", 3);
        sl.Add("delta", 4);
        Console.WriteLine(sl.Keys.Count == 4);                   // True
        Console.WriteLine(sl.Values.Count == 4);                 // True
        sl.Remove("bravo");
        Console.WriteLine(sl.Keys.Count == 3);                   // True (remove reflected)
        Console.WriteLine(sl.Values.Count == 3);                 // True
        sl["alpha"] = 10;                                        // indexer-set replace
        Console.WriteLine(sl.Keys.Count == 3);                   // True (replace does not add)
        sl["echo"] = 5;                                          // indexer-set insert
        Console.WriteLine(sl.Keys.Count == 4);                   // True (insert adds)
        sl.Clear();
        Console.WriteLine(sl.Keys.Count == 0);                   // True (clear)
        sl.Add("reuse", 1);
        Console.WriteLine(sl.Keys.Count == 1);                   // True (re-add)
        Console.WriteLine(sl.Values.Count == 1);                 // True
        sl.Add("with-null", null);                               // null value pair
        Console.WriteLine(sl.Keys.Count == 2);                   // True (null-value pair counted)
        Console.WriteLine(sl.Values.Count == 2);                 // True

        // ============ 2. IEnumerable enumeration over the snapshots ============
        var sn = new SortedList();
        sn.Add("pear", "p");
        sn.Add("apple", "a");
        sn.Add("cherry", "c");
        sn.Add("banana", "b");
        System.Collections.ICollection sk = sn.Keys;
        int n = 0;
        foreach (object k in sk) { n++; }
        Console.WriteLine(n == 4);                               // True (enumeration length)
        string ks = "";
        foreach (object k in sk) { ks += (string)k; }
        Console.WriteLine(ks == "applebananacherrypear");        // True (sorted ascending)
        string vs = "";
        foreach (object v in sn.Values) { vs += (string)v; }
        Console.WriteLine(vs == "abcp");                         // True (key-ordered values)
        bool ka = true;
        int ki = 0;
        foreach (object k in sk) { if ((string)sn.GetKey(ki) != (string)k) ka = false; ki++; }
        Console.WriteLine(ka && ki == sn.Count);                 // True (enumeration order == GetKey order)
        var empty = new SortedList();
        int en = 0;
        foreach (object k in empty.Keys) { en++; }
        Console.WriteLine(en == 0);                              // True (empty enumerates zero)
        var dup = new SortedList();
        dup.Add("x", "same");
        dup.Add("y", "same");                                    // duplicate value
        dup.Add("z", "same");
        string ds = "";
        foreach (object v in dup.Values) { ds += (string)v; }
        Console.WriteLine(ds == "samesamesame");                 // True (dup values enumerated once per entry)
        Console.WriteLine(dup.Keys.Count == 3);                  // True
        // two independent enumerators over the same snapshot agree
        string e1 = "", e2 = "";
        foreach (object k in sk) { e1 += (string)k; }
        foreach (object k in sk) { e2 += (string)k; }
        Console.WriteLine(e1 == e2);                             // True (independent enumerations agree)

        // ============ 3. manual IEnumerator protocol over a snapshot ============
        System.Collections.IEnumerator it = sn.Keys.GetEnumerator();
        Console.WriteLine(it.MoveNext());                        // True
        Console.WriteLine((string)it.Current == "apple");        // True (first in order)
        Console.WriteLine(it.MoveNext());                        // True
        Console.WriteLine((string)it.Current == "banana");       // True
        Console.WriteLine(it.MoveNext());                        // True
        Console.WriteLine(it.MoveNext());                        // True
        Console.WriteLine((string)it.Current == "pear");         // True (last)
        Console.WriteLine(!it.MoveNext());                       // True (past end)
        Console.WriteLine(!it.MoveNext());                       // True (stays false)
        it.Reset();
        Console.WriteLine(it.MoveNext());                        // True (reset re-enumerates)
        Console.WriteLine((string)it.Current == "apple");        // True (first again)
        // values enumerator manual protocol
        System.Collections.IEnumerator iv = sn.Values.GetEnumerator();
        string vm = "";
        while (iv.MoveNext()) { vm += (string)iv.Current; }
        Console.WriteLine(vm == "abcp");                         // True
        iv.Reset();
        iv.MoveNext();
        Console.WriteLine((string)iv.Current == "a");            // True

        // ============ 4. Stack / Queue IEnumerable (LIFO / FIFO) ============
        var st = new Stack();
        st.Push("first");
        st.Push("second");
        st.Push("third");
        string so = "";
        foreach (object o in st) { so += (string)o; }
        Console.WriteLine(so == "thirdsecondfirst");             // True (LIFO: top of stack first)
        int sc = 0;
        foreach (object o in st) { sc++; }
        Console.WriteLine(sc == 3);                              // True
        var qu = new Queue();
        qu.Enqueue("a");
        qu.Enqueue("b");
        qu.Enqueue("c");
        string qo = "";
        foreach (object o in qu) { qo += (string)o; }
        Console.WriteLine(qo == "abc");                          // True (FIFO: front first)
        st.Push("fourth");
        string so2 = "";
        foreach (object o in st) { so2 += (string)o; }
        Console.WriteLine(so2 == "fourththirdsecondfirst");      // True (fresh enumeration sees current contents)
        var est = new Stack();
        var equ = new Queue();
        int es = 0, eq = 0;
        foreach (object o in est) { es++; }
        foreach (object o in equ) { eq++; }
        Console.WriteLine(es == 0 && eq == 0);                   // True (empty containers)
        Console.WriteLine(st.Count == 4);                        // True
        Console.WriteLine(qu.Count == 3);                        // True
    }
}}
