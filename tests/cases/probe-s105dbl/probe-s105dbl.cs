using System;
using System.Linq;
static class P { static void Main() { var a = new[] { 2.5, 1.5, 2.5 }; var g = a.GroupBy(x => (int)x).First(); Console.WriteLine("[denum] " + string.Join(",", g)); } }
