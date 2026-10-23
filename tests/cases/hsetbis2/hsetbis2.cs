using System; using System.Collections.Generic;
class Prog { static void Main() {
  HashSet<int> t = new HashSet<int>(new int[] { 10, 20, 30 });
  int got;
  Console.WriteLine("tryget_hit=" + t.TryGetValue(20, out got) + ":" + got);
  Console.WriteLine("tryget_miss=" + t.TryGetValue(99, out got) + ":" + got);
  int[] dst = new int[5];
  t.CopyTo(dst);
  Console.WriteLine("copyto=" + dst[0] + "," + dst[1] + "," + dst[2]);
}}