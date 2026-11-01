//------------------------------------------------------------------------------
// dtoobjcmp1.cs -- System.DateTimeOffset.Equals(object) / CompareTo(object)
// e2e oracle.
//
// net48 contract (measured, see the seat NOTES):
//   * Equals(object): null -> false, any non-DateTimeOffset object -> false,
//     a boxed DateTimeOffset -> UTC-INSTANT equality (the offset is
//     irrelevant: 12:00 +08:00 equals 04:00 +00:00).
//   * CompareTo(object) (explicit IComparable implementation): null -> 1, any
//     non-DateTimeOffset object -> ArgumentException "Object must be of type
//     DateTimeOffset.", a boxed DateTimeOffset -> -1/0/1 by UTC instant.
//   * A BOXED receiver behaves exactly like the unboxed one.
// Every output is a deterministic bool/char so the file is byte-exact.
//------------------------------------------------------------------------------

using System;

namespace Cases { public static class DtoObjCmp1 {
    public static void Main() {
        // base: local 2020-01-01 12:00:00 at +08:00 -> UTC 04:00:00
        DateTimeOffset a = new DateTimeOffset(new DateTime(2020, 1, 1, 12, 0, 0), new TimeSpan(8, 0, 0));
        // same instant, offset +00:00
        DateTimeOffset same = new DateTimeOffset(new DateTime(2020, 1, 1, 4, 0, 0), TimeSpan.Zero);
        // same instant, offset -05:00
        DateTimeOffset sameNeg = new DateTimeOffset(new DateTime(2019, 12, 31, 23, 0, 0), new TimeSpan(-5, 0, 0));
        // one tick away from a
        DateTimeOffset earlier = a.AddTicks(-1);
        DateTimeOffset later = a.AddTicks(1);

        // ---- 1. Equals(object) on the unboxed receiver ----
        Console.WriteLine("[eq.self] " + a.Equals((object)a));
        Console.WriteLine("[eq.same] " + a.Equals((object)same));
        Console.WriteLine("[eq.sameNeg] " + a.Equals((object)sameNeg));
        Console.WriteLine("[eq.earlier] " + a.Equals((object)earlier));
        Console.WriteLine("[eq.later] " + a.Equals((object)later));
        Console.WriteLine("[eq.null] " + a.Equals(null));
        Console.WriteLine("[eq.str] " + a.Equals((object)"hello"));
        Console.WriteLine("[eq.int] " + a.Equals((object)42));
        Console.WriteLine("[eq.dt] " + a.Equals((object)new DateTime(2020, 1, 1)));
        Console.WriteLine("[eq.ts] " + a.Equals((object)TimeSpan.Zero));

        // ---- 2. Equals(object) on a BOXED receiver ----
        object oa = a;
        object osame = same;
        Console.WriteLine("[box.eq.same] " + oa.Equals(osame));
        Console.WriteLine("[box.eq.earlier] " + oa.Equals((object)earlier));
        Console.WriteLine("[box.eq.self] " + oa.Equals(oa));
        Console.WriteLine("[box.eq.null] " + oa.Equals(null));
        Console.WriteLine("[box.eq.str] " + oa.Equals((object)"hello"));

        // ---- 3. CompareTo(object) via the explicit IComparable implementation ----
        IComparable ic = a;
        Console.WriteLine("[cmp.self] " + ic.CompareTo((object)a));
        Console.WriteLine("[cmp.same] " + ic.CompareTo((object)same));
        Console.WriteLine("[cmp.sameNeg] " + ic.CompareTo((object)sameNeg));
        Console.WriteLine("[cmp.earlier] " + ic.CompareTo((object)earlier));
        Console.WriteLine("[cmp.later] " + ic.CompareTo((object)later));
        Console.WriteLine("[cmp.null] " + ic.CompareTo(null));

        bool aneStr = false, aneInt = false, aneDt = false, aneTs = false;
        try { ic.CompareTo((object)"hello"); } catch (ArgumentException) { aneStr = true; }
        try { ic.CompareTo((object)42); } catch (ArgumentException) { aneInt = true; }
        try { ic.CompareTo((object)new DateTime(2020, 1, 1)); } catch (ArgumentException) { aneDt = true; }
        try { ic.CompareTo((object)TimeSpan.Zero); } catch (ArgumentException) { aneTs = true; }
        Console.WriteLine("[cmp.strAne] " + aneStr);
        Console.WriteLine("[cmp.intAne] " + aneInt);
        Console.WriteLine("[cmp.dtAne] " + aneDt);
        Console.WriteLine("[cmp.tsAne] " + aneTs);

        // ---- 4. CompareTo(object) on a BOXED receiver ----
        IComparable oic = same;
        Console.WriteLine("[box.cmp.same] " + oic.CompareTo((object)a));
        Console.WriteLine("[box.cmp.earlier] " + oic.CompareTo((object)earlier));
        Console.WriteLine("[box.cmp.null] " + oic.CompareTo(null));
        bool boxAne = false;
        try { oic.CompareTo((object)"x"); } catch (ArgumentException) { boxAne = true; }
        Console.WriteLine("[box.cmp.strAne] " + boxAne);

        // ---- 5. equality is UTC-instant based, the offset is NOT part of it ----
        Console.WriteLine("[def.sameInstantEq] " + a.Equals((object)same));
        Console.WriteLine("[def.offsetDiffers] " + (a.Offset != same.Offset));
        Console.WriteLine("[def.cmpZero] " + (ic.CompareTo((object)same) == 0));

        // ---- 6. default / MinValue / MaxValue through the object overloads ----
        Console.WriteLine("[dflt.eq] " + default(DateTimeOffset).Equals((object)DateTimeOffset.MinValue));
        Console.WriteLine("[dflt.cmpMax] " + ((IComparable)default(DateTimeOffset)).CompareTo((object)DateTimeOffset.MaxValue));
        Console.WriteLine("[min.cmpMin] " + ((IComparable)DateTimeOffset.MinValue).CompareTo((object)DateTimeOffset.MinValue));
        Console.WriteLine("[max.cmpMax] " + ((IComparable)DateTimeOffset.MaxValue).CompareTo((object)DateTimeOffset.MaxValue));

        // ---- 7. object.Equals / List<DateTimeOffset> scans still agree ----
        Console.WriteLine("[obj.eq] " + object.Equals(a, same));
        var list = new System.Collections.Generic.List<DateTimeOffset>();
        list.Add(a);
        Console.WriteLine("[list.contains] " + list.Contains(same));
        Console.WriteLine("[list.index] " + list.IndexOf(sameNeg));
    }
}}
