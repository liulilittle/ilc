// p-iface.cs -- GN-0 probe: interface-CONSTRAINED generic (where T:IComparable)
// (GENERICS-FULL-ARG-GAPS.md 2 interface-constraint + 5, P-iface). Locks the
// status quo; no implementation changes in this batch.
//
// Design per gap doc 1.6: an interface constraint makes the parameter
// "directed" -> the whole definition goes per-instance (conservative and
// correct by design). This probe locks whether a per-instance constrained
// Box<Item> with a CompareTo through T actually round-trips under net48
// semantics.
//------------------------------------------------------------------------------
using System;

namespace Cases {

class Box<T> where T : IComparable {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
    public int Compare(T other) { return item.CompareTo(other); }
}

class Item : IComparable {
    public int N;
    public int CompareTo(object o) {
        Item it = (Item)o;
        return N.CompareTo(it.N);
    }
}

public static class PIface {
    public static void Main() {
        // ---- 1. per-instance constrained Box<Item> storage round-trip ----
        Box<Item> b = new Box<Item>();
        b.Set(new Item { N = 5 });
        Console.WriteLine(b.Get().N);            // 5

        // ---- 2. CompareTo through the T slot (per-instance dispatch) ----
        Box<Item> b2 = new Box<Item>();
        b2.Set(new Item { N = 3 });
        Console.WriteLine(b.Compare(b2.Get()));  // 1 (5 vs 3)

        // ---- 3. null-default slot + storage on a second instance ----
        Box<Item> b3 = new Box<Item>();
        Console.WriteLine(b3.Get() == null);     // True (default slot)
        b3.Set(new Item { N = 7 });
        Console.WriteLine(b3.Get().N);           // 7
    }
}}