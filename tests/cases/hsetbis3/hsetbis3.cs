using System; using System.Collections.Generic;
class Prog { static void Main() {
  HashSet<int> a = new HashSet<int>(new int[] { 1, 2, 3, 4, 5 });
  HashSet<int> b = new HashSet<int>(new int[] { 4, 5, 6, 7 });
  Console.WriteLine("seteq=" + a.SetEquals(new int[] { 5, 4, 3, 2, 1 }));
  Console.WriteLine("subset=" + new HashSet<int>(new int[] { 2, 3 }).IsSubsetOf(a));
  Console.WriteLine("propersub=" + a.IsProperSubsetOf(a));
  Console.WriteLine("superset=" + a.IsSupersetOf(new int[] { 1, 2 }));
  Console.WriteLine("overlaps=" + a.Overlaps(b));
}}