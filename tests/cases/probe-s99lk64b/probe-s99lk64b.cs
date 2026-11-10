using System;
using System.Linq;
static class P { static void Main() { var a = new[] { 10L, 20L, 10L }; var lk = a.ToLookup(x => x); Console.WriteLine("[enum] " + string.Join(";", lk.Select(g => g.Key + ":" + string.Join(",", g)))); } }
