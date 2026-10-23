using System;
using System.Collections;
using System.Collections.Generic;
namespace Cases { public static class VtMeta1 {
  public static void Main() {
    var a = (1, "a");
    var b = (1, "a");
    Console.WriteLine("h.1a=" + a.GetHashCode());
    Console.WriteLine("h.12=" + (1, 2).GetHashCode());
    Console.WriteLine("h.a1=" + ("a", 1).GetHashCode());
    Console.WriteLine("h.ll=" + (1L, 2L).GetHashCode());
    Console.WriteLine("h.dd=" + (1.5, 2.5).GetHashCode());
    Console.WriteLine("h.bb=" + (true, false).GetHashCode());
    Console.WriteLine("h.cc=" + ('a', 'b').GetHashCode());
    Console.WriteLine("h.null=" + ((string)null, 1).GetHashCode());
    Console.WriteLine("h.one=" + new ValueTuple<int>(5).GetHashCode());
    Console.WriteLine("h.zero=" + ValueTuple.Create().GetHashCode());
    Console.WriteLine("h.t7=" + (1, 2, 3, 4, 5, 6, 7).GetHashCode());
    Console.WriteLine("h.t8=" + (1, 2, 3, 4, 5, 6, 7, 8).GetHashCode());
    Console.WriteLine("eq.typed=" + a.Equals(b));
    Console.WriteLine("eq.obj=" + a.Equals((object)b));
    Console.WriteLine("eq.neg=" + a.Equals((2, "a")));
    Console.WriteLine("is.ise=" + (a is IStructuralEquatable));
    Console.WriteLine("is.isc=" + (a is IStructuralComparable));
    Console.WriteLine("is.icmp=" + (a is IComparable));
    Console.WriteLine("cmp.lt=" + ((IComparable)a).CompareTo((1, "b")));
    var ca = (1, 2);
    var cb = (1, 3);
    Console.WriteLine("isc=" + ((IStructuralComparable)ca).CompareTo(cb, Comparer<object>.Default));
    Console.WriteLine("ise.eq=" + ((IStructuralEquatable)a).Equals(b, EqualityComparer<object>.Default));
  }
}}