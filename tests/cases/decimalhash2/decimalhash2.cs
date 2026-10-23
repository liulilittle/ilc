//------------------------------------------------------------------------------
// decimalhash2.cs -- USER-VISIBLE CONSEQUENCE probe (e2e).
//
// A hash mismatch is not cosmetic: every keyed container BUCKETS on
// GetHashCode(). With the old .NET Core hash the runtime produced a different
// number than net48 for the same decimal, so net48-equal keys landed in
// different buckets and container state (Count after adding the same value
// written with a different scale, ContainsKey / indexer results) diverged.
//
// CONTAINER CHOICE: the non-generic System.Collections.Hashtable. It is the
// wired keyed container that accepts decimal keys -- the transpiler boxes the
// decimal and the runtime hashes it through icall_object_get_hash_code ->
// icall_decimal_get_hash_code, which is exactly the code path under repair.
// (Dictionary<decimal,int> / HashSet<decimal> have no AOT expansion in this
// transpiler yet -- a separate recorded gap, NOT this defect.)
//
// Every line is a deterministic bool: net48 Hashtable enumeration order is NOT
// printed (it is not reproducible), only Count / ContainsKey / indexer / Add
// results, which are order-independent.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class DecimalHash2 {
    public static void Main() {
        // =========== 1. insert / lookup / count ===========
        var t = new Hashtable();
        t[1.5m] = "a";
        t[0.1m] = "b";
        t[1234.5678m] = "c";
        t[-2.25m] = "d";
        Console.WriteLine("t1=" + (t.Count == 4));
        Console.WriteLine("t2=" + ((string)t[1.5m] == "a"));
        Console.WriteLine("t3=" + ((string)t[0.1m] == "b"));
        Console.WriteLine("t4=" + ((string)t[1234.5678m] == "c"));
        Console.WriteLine("t5=" + ((string)t[-2.25m] == "d"));

        // =========== 2. the SAME VALUE written with a different scale ===========
        // 1.50m == 1.5m, so net48 overwrites the existing entry: Count stays 4.
        t[1.50m] = "A";
        Console.WriteLine("t6=" + (t.Count == 4));
        Console.WriteLine("t7=" + ((string)t[1.5m] == "A"));
        // and 0.10m likewise
        t[0.10m] = "B";
        Console.WriteLine("t8=" + (t.Count == 4));
        Console.WriteLine("t9=" + ((string)t[0.1m] == "B"));

        // =========== 3. ContainsKey across spellings ===========
        Console.WriteLine("t10=" + t.ContainsKey(1.5m));
        Console.WriteLine("t11=" + t.ContainsKey(1.50m));
        Console.WriteLine("t12=" + t.ContainsKey(0.1m));
        Console.WriteLine("t13=" + t.ContainsKey(0.100m));
        Console.WriteLine("t14=" + t.ContainsKey(2.5m));     // absent
        Console.WriteLine("t15=" + t.ContainsKey(9.9m));     // absent
        Console.WriteLine("t16=" + t.ContainsKey(1234.5678m));
        Console.WriteLine("t17=" + t.ContainsKey(1234.56780m));

        // =========== 4. Remove / re-add ===========
        // (Hashtable.Remove returns void, so the effect is observed on Count.)
        t.Remove(1234.5678m);
        Console.WriteLine("t18=" + (t.Count == 3));
        Console.WriteLine("t19=" + t.ContainsKey(1234.5678m));
        t.Remove(1234.5678m);                               // already gone
        Console.WriteLine("t20=" + (t.Count == 3));
        t[1234.5678m] = "c2";
        Console.WriteLine("t21=" + (t.Count == 4));
        Console.WriteLine("t22=" + ((string)t[1234.5678m] == "c2"));

        // =========== 5. hash consistency invariants (the container contract) ===
        // equal decimals MUST hash equal, or lookups break by construction.
        Console.WriteLine("v1=" + (1.5m == 1.50m));
        Console.WriteLine("v2=" + (1.5m.GetHashCode() == 1.50m.GetHashCode()));
        Console.WriteLine("v3=" + (0.1m == 0.10m));
        Console.WriteLine("v4=" + (0.1m.GetHashCode() == 0.10m.GetHashCode()));
        Console.WriteLine("v5=" + (1m == 1.0m));
        Console.WriteLine("v6=" + (1m.GetHashCode() == 1.0m.GetHashCode()));
        Console.WriteLine("v7=" + (1234.5678m == 1234.56780m));
        Console.WriteLine("v8=" + (1234.5678m.GetHashCode() == 1234.56780m.GetHashCode()));
        // distinct values must not collide in these samples
        Console.WriteLine("v9=" + (1.5m.GetHashCode() != 2.5m.GetHashCode()));
        Console.WriteLine("v10=" + (0.1m.GetHashCode() != 0.2m.GetHashCode()));
        Console.WriteLine("v11=" + (1m.GetHashCode() != 2m.GetHashCode()));

        // =========== 6. boxed-decimal equality must follow the hash ===========
        // (Hashtable compares keys with object.Equals; a boxed decimal compares
        // by value in net48, so a mismatched hash would be unrecoverable.)
        object boxedA = 1.5m;
        object boxedB = 1.50m;
        Console.WriteLine("w1=" + boxedA.Equals(boxedB));
        Console.WriteLine("w2=" + (boxedA.GetHashCode() == boxedB.GetHashCode()));
    } } }
