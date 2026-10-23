// boxwire4 -- boxed values through the KLASS-DISPATCHED object members:
//   * ((object)v).GetHashCode()  -- must equal the value's own hash
//   * ((object)v).Equals((object)v) -- value equality on two distinct boxes
//   * object.Equals((object)v, (object)v) -- the STATIC form (same dispatch)
// Every line prints a bool so the harness stays culture independent.
using System;
namespace Cases { public static class BoxWire4 {
    public static void Main() {
        DateTime dt = new DateTime(2020, 1, 2, 3, 4, 5);
        TimeSpan ts = new TimeSpan(1, 2, 3);
        Guid g = new Guid("00112233-4455-6677-8899-aabbccddeeff");

        object od1 = dt, od2 = dt;
        object ot1 = ts, ot2 = ts;
        object og1 = g, og2 = g;

        // hash: the boxed hash is the value's own hash (net48 value semantics)
        Console.WriteLine("dt_hash_eq=" + (od1.GetHashCode() == dt.GetHashCode()));
        Console.WriteLine("ts_hash_eq=" + (ot1.GetHashCode() == ts.GetHashCode()));
        Console.WriteLine("guid_hash_eq=" + (og1.GetHashCode() == g.GetHashCode()));
        // ... and the ABSOLUTE hashes are net48's (pins the value folds, not
        // just their mutual consistency: a hash that is merely stable but
        // different from net48 would still pass the _eq lines above).
        Console.WriteLine("dt_hash=" + dt.GetHashCode());
        Console.WriteLine("ts_hash=" + ts.GetHashCode());
        Console.WriteLine("guid_hash=" + g.GetHashCode());
        Console.WriteLine("dt_box_hash=" + od1.GetHashCode());
        Console.WriteLine("ts_box_hash=" + ot1.GetHashCode());
        Console.WriteLine("guid_box_hash=" + og1.GetHashCode());
        Console.WriteLine("dt_hash_stable=" + (od1.GetHashCode() == od2.GetHashCode()));
        Console.WriteLine("ts_hash_stable=" + (ot1.GetHashCode() == ot2.GetHashCode()));
        Console.WriteLine("guid_hash_stable=" + (og1.GetHashCode() == og2.GetHashCode()));

        // instance Equals on two DISTINCT boxes of the same value
        Console.WriteLine("dt_equals=" + od1.Equals(od2));
        Console.WriteLine("ts_equals=" + ot1.Equals(ot2));
        Console.WriteLine("guid_equals=" + og1.Equals(og2));

        // static Object.Equals -- the same value equality
        Console.WriteLine("dt_static_equals=" + object.Equals(od1, od2));
        Console.WriteLine("ts_static_equals=" + object.Equals(ot1, ot2));
        Console.WriteLine("guid_static_equals=" + object.Equals(og1, og2));

        // cross-type boxes never compare equal
        Console.WriteLine("dt_vs_ts=" + od1.Equals(ot1));
        Console.WriteLine("guid_vs_dt=" + og1.Equals(od1));
    }
} }
