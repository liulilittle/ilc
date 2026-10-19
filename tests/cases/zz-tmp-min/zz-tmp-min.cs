using System;
using System.Linq;
namespace Cases { public static class ZZMin { public static void Main() {
  int[] d = {1,2,3,4,5};
  var grouped = d.GroupBy(x => x % 2);
  foreach (var g in grouped) { Console.WriteLine("[k]=" + g.Key); break; }
} } }
