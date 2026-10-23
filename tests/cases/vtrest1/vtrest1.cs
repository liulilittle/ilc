using System;
namespace Cases { public static class VtRest1 {
  public static void Main() {
    var t7 = (1, 2, 3, 4, 5, 6, 7);
    Console.WriteLine("t7.to=" + t7.ToString());
    Console.WriteLine("t7.hash=" + t7.GetHashCode());
    var t8 = (1, 2, 3, 4, 5, 6, 7, 8);
    Console.WriteLine("t8.to=" + t8.ToString());
    Console.WriteLine("t8.hash=" + t8.GetHashCode());
    Console.WriteLine("t8.rest.to=" + t8.Rest.ToString());
    Console.WriteLine("t8.rest.hash=" + t8.Rest.GetHashCode());
    Console.WriteLine("t8.item8=" + t8.Rest.Item1);
    Console.WriteLine("t8.eq=" + t8.Equals((1, 2, 3, 4, 5, 6, 7, 8)));
    var t9 = (1, 2, 3, 4, 5, 6, 7, 8, 9);
    Console.WriteLine("t9.to=" + t9.ToString());
    Console.WriteLine("t9.hash=" + t9.GetHashCode());
    Console.WriteLine("t9.item9=" + t9.Rest.Item2);
    Console.WriteLine("t9.rest.to=" + t9.Rest.ToString());
    var t10 = (1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
    Console.WriteLine("t10.to=" + t10.ToString());
    Console.WriteLine("t10.hash=" + t10.GetHashCode());
    var x8 = (1, 2, 3, 4, 5, 6, 7, (8, 9));
    Console.WriteLine("x8.to=" + x8.ToString());
    Console.WriteLine("x8.hash=" + x8.GetHashCode());
    Console.WriteLine("x8.rest.to=" + x8.Rest.ToString());
    var n7 = (1, 2, 3, 4, 5, 6, (7, 8));
    Console.WriteLine("n7.to=" + n7.ToString());
    Console.WriteLine("n7.hash=" + n7.GetHashCode());
    Console.WriteLine("n7.i7=" + n7.Item7.Item1 + "/" + n7.Item7.Item2);
  }
}}