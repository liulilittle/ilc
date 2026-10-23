using System;
namespace Cases { public static class VtCreate1 {
  public static void Main() {
    Console.WriteLine("vt0=" + ValueTuple.Create().ToString());
    Console.WriteLine("vt0.hash=" + ValueTuple.Create().GetHashCode());
    Console.WriteLine("vt1=" + ValueTuple.Create(7).ToString());
    Console.WriteLine("vt2=" + ValueTuple.Create(1, "a").ToString());
    Console.WriteLine("vt3=" + ValueTuple.Create(1, "a", 2.5).ToString());
    Console.WriteLine("vt8=" + ValueTuple.Create(1, 2, 3, 4, 5, 6, 7, 8).ToString());
    var v = ValueTuple.Create(1, "a");
    Console.WriteLine("vt.item=" + v.Item1 + "/" + v.Item2);
    Console.WriteLine("vt.hash=" + v.GetHashCode());
    Console.WriteLine("tuple1=" + Tuple.Create(7).ToString());
    Console.WriteLine("tuple2=" + Tuple.Create(1, "a").ToString());
    Console.WriteLine("tuple8=" + Tuple.Create(1, 2, 3, 4, 5, 6, 7, 8).ToString());
    Console.WriteLine("tuple.null=" + Tuple.Create((string)null, 1).ToString());
    var tc = Tuple.Create(1, "a");
    Console.WriteLine("tuple.hash=" + tc.GetHashCode());
    Console.WriteLine("tuple.eq=" + tc.Equals(Tuple.Create(1, "a")));
    Console.WriteLine("tuple.i2=" + tc.Item2);
    var t8 = Tuple.Create(1, 2, 3, 4, 5, 6, 7, 8);
    Console.WriteLine("tuple8.hash=" + t8.GetHashCode());
    Console.WriteLine("tuple8.rest=" + t8.Rest.Item1);
    Console.WriteLine("tuple8.rest.to=" + t8.Rest.ToString());
  }
}}