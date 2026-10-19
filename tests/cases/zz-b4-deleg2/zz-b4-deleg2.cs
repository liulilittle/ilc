using System;
using System.Collections.Generic;
namespace Cases {
  public static class ZZB4Deleg2 {
    static string J(int[] a){ var s=new List<string>(); foreach(var x in a)s.Add(x.ToString()); return string.Join(",",s.ToArray()); }
    static void T(string tag, Action a){ try { a(); } catch (Exception e) { Console.WriteLine(tag + "=>" + e.GetType().Name + ":" + e.Message); } }
    public static void Main(){
      var a1 = new[] { 1, 3, 2, 5, 4 };
      T("[asc]", () => Array.Sort(a1, (x, y) => x.CompareTo(y))); Console.WriteLine("[asc]=" + J(a1));
      var a2 = new[] { 1, 3, 2, 5, 4 };
      T("[desc]", () => Array.Sort(a2, (x, y) => y.CompareTo(x))); Console.WriteLine("[desc]=" + J(a2));
      var a3 = new[] { 1, 3, 2, 5, 4 };
      T("[sub]", () => Array.Sort(a3, (x, y) => x - y)); Console.WriteLine("[sub]=" + J(a3));
    }
  }
}
