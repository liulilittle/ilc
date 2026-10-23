using System; using System.Collections.Generic;
class Prog { static void Main() {
  HashSet<int> a = new HashSet<int>(new int[] { 1, 2, 3, 4, 5 });
  Console.WriteLine("a_count=" + a.Count);
  HashSet<int> u = new HashSet<int>(a);
  u.UnionWith(a);
  Console.WriteLine("u=" + u.Count);
  HashSet<int> i = new HashSet<int>(a); i.IntersectWith(a);
  Console.WriteLine("i=" + i.Count);
  HashSet<int> e = new HashSet<int>(a); e.ExceptWith(a);
  Console.WriteLine("e=" + e.Count);
  HashSet<int> x = new HashSet<int>(a); x.SymmetricExceptWith(a);
  Console.WriteLine("x=" + x.Count);
}}