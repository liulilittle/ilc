using System;
using System.Collections.Generic;
namespace Cases {
  public static class ZZB4Cmp4 {
    public static void Main(){
      var arr = new[] { 1, 3, 2, 5, 4 };
      var cmp = Comparer<int>.Create((a, b) => b.CompareTo(a));
      try { Array.Sort(arr, 1, 3, cmp); Console.WriteLine("[sort_range_cmp]=" + string.Join(",", arr)); }
      catch (Exception e) { Console.WriteLine("[sort_range_cmp]=" + e.GetType().Name); }
    }
  }
}
