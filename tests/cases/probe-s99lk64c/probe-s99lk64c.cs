using System;
using System.Linq;
static class P { static void Main() { var a = new[] { 3L, 1L }; var lk = a.ToLookup(x => x); var g = lk[3L]; Console.WriteLine("[sum] " + g.Sum()); Console.WriteLine("[sel] " + string.Join(",", g.Select(x => x * 10))); } }
