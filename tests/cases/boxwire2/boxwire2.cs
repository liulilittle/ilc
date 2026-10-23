// boxwire2 -- `unbox.any` of the three BCL value structs: (DateTime)obj,
// (TimeSpan)obj, (Guid)obj. Also the FAILING unbox: a box of a DIFFERENT
// value type must throw InvalidCastException (never silently reinterpret).
using System;
namespace Cases { public static class BoxWire2 {
    public static void Main() {
        DateTime dt = new DateTime(2020, 1, 2, 3, 4, 5);
        TimeSpan ts = new TimeSpan(1, 2, 3);
        Guid g = new Guid("00112233-4455-6677-8899-aabbccddeeff");

        object od = dt;
        object ot = ts;
        object og = g;

        DateTime dt2 = (DateTime)od;
        TimeSpan ts2 = (TimeSpan)ot;
        Guid g2 = (Guid)og;

        Console.WriteLine("dt_roundtrip=" + (dt2 == dt));
        Console.WriteLine("dt_ticks=" + dt2.Ticks);
        Console.WriteLine("ts_roundtrip=" + (ts2 == ts));
        Console.WriteLine("ts_ticks=" + ts2.Ticks);
        Console.WriteLine("guid_roundtrip=" + (g2 == g));
        Console.WriteLine("guid_str=" + g2.ToString());

        // unbox -> rebox keeps the value
        object od2 = (object)dt2;
        Console.WriteLine("rebox=" + (((DateTime)od2) == dt));

        // wrong-type unbox must throw InvalidCastException
        try { DateTime bad = (DateTime)ot; Console.WriteLine("bad_dt=no_throw"); }
        catch (InvalidCastException) { Console.WriteLine("bad_dt=ice"); }
        try { TimeSpan bad2 = (TimeSpan)og; Console.WriteLine("bad_ts=no_throw"); }
        catch (InvalidCastException) { Console.WriteLine("bad_ts=ice"); }
        try { Guid bad3 = (Guid)od; Console.WriteLine("bad_guid=no_throw"); }
        catch (InvalidCastException) { Console.WriteLine("bad_guid=ice"); }

        // unbox null -> NullReferenceException
        object on = null;
        try { DateTime bad4 = (DateTime)on; Console.WriteLine("null_dt=no_throw"); }
        catch (NullReferenceException) { Console.WriteLine("null_dt=nre"); }
    }
} }
