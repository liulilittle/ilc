using System;
using System.Linq;
static class P { static void Main() { var a = new[] { 3L, 1L, 2L }; var lk = a.ToLookup(x => x); Console.WriteLine("[cnt] " + lk.Count); Console.WriteLine("[k1] " + string.Join(",", lk[1L])); Console.WriteLine("[k2] " + string.Join(",", lk[2L])); Console.WriteLine("[has3] " + lk.Contains(3L)); Console.WriteLine("[has9] " + lk.Contains(9L)); } }
