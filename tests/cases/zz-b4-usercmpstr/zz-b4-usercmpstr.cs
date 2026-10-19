using System;
using System.Collections.Generic;
namespace Cases {
  public sealed class RevStr : IComparer<string> { public int Compare(string a, string b) { return string.CompareOrdinal(b, a); } }
  public static class ZZB4Usercmpstr {
    public static void Main(){
      var arr = new[] { "b", "a", "c" };
      try { Array.Sort(arr, new RevStr()); Console.WriteLine("[array_user_cmp_str]=" + string.Join(",", arr)); }
      catch (Exception e) { Console.WriteLine("[array_user_cmp_str]=" + e.GetType().Name); }
    }
  }
}
