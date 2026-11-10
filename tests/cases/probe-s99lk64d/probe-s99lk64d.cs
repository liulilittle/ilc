using System;
using System.Linq;
static class P { static void Main() { var a = new[] { 3L, 1L }; var lk = a.ToLookup(x => x, x => x * 10); Console.WriteLine("[es] " + string.Join(",", lk[1L])); Console.WriteLine("[es3] " + string.Join(",", lk[3L])); } }
