using System; using System.Collections.Generic;
class Prog { static void Main() {
  HashSet<string> sa = new HashSet<string>(new string[] { "aa", "bb", "aa" });
  Console.WriteLine("str_ctor_count=" + sa.Count);
  Console.WriteLine("str_contains=" + sa.Contains("aa"));
  HashSet<int> cap = new HashSet<int>(64);
  cap.Add(1);
  Console.WriteLine("cap_ok=" + cap.Count);
  HashSet<string> ic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  Console.WriteLine("ic_add_a=" + ic.Add("Alpha"));
  Console.WriteLine("ic_add_b=" + ic.Add("alpha"));
  Console.WriteLine("ic_count=" + ic.Count);
  Console.WriteLine("ic_has_upper=" + ic.Contains("ALPHA"));
}}