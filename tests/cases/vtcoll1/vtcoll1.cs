using System;
using System.Collections.Generic;
using System.Linq;
namespace Cases { public static class VtColl1 {
  public static void Main() {
    var lst = new List<(int, string)>();
    lst.Add((1, "a"));
    lst.Add((2, "b"));
    Console.WriteLine("lst.count=" + lst.Count);
    foreach (var it in lst) Console.WriteLine("lst=" + it.Item1 + "/" + it.Item2);
    var dic = new Dictionary<(int, int), string>();
    dic[(1, 2)] = "x";
    Console.WriteLine("dic=" + dic[(1, 2)]);
    var hs = new HashSet<(int, int)>();
    hs.Add((1, 2)); hs.Add((1, 2)); hs.Add((2, 3));
    Console.WriteLine("hs.count=" + hs.Count);
    var sel = new List<int> { 1, 2, 3 };
    foreach (var t in sel.Select(x => (x, x * 2))) Console.WriteLine("sel=" + t.Item1 + "/" + t.Item2);
    var arr = new (int, int)[2];
    arr[0] = (7, 8); arr[1] = (9, 10);
    Console.WriteLine("arr=" + arr[1].Item1 + "/" + arr[1].Item2 + "/" + arr.Length);
  }
}}