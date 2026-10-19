using System;
using System.Collections.Generic;
namespace Cases {
  public static class ZZB4Bsearch2 {
    public static void Main(){
      var arr = new[] { 5, 4, 3, 2, 1 };
      var cmp = Comparer<int>.Create((a, b) => b.CompareTo(a));
      try { Console.WriteLine("[bs_cmp]=" + Array.BinarySearch(arr, 3, cmp)); }
      catch (Exception e) { Console.WriteLine("[bs_cmp]=" + e.GetType().Name); }
      try { Console.WriteLine("[bs_range_cmp]=" + Array.BinarySearch(arr, 0, 5, 3, cmp)); }
      catch (Exception e) { Console.WriteLine("[bs_range_cmp]=" + e.GetType().Name); }
    }
  }
}
