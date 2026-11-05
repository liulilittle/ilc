using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeVtOps
    {
        static string J(DateTime[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString("s", CultureInfo.InvariantCulture); } return s; }
        static string J(int[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString(CultureInfo.InvariantCulture); } return s; }
        static string J(long[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString(CultureInfo.InvariantCulture); } return s; }
        static string S(DateTime d) { return d.ToString("s", CultureInfo.InvariantCulture); }

        static List<DateTime> L()
        {
            return new List<DateTime>
            {
                new DateTime(2020, 3, 4, 5, 6, 7),
                new DateTime(2019, 1, 2, 3, 4, 5),
                new DateTime(2021, 5, 6, 7, 8, 9),
                new DateTime(2019, 6, 7, 8, 9, 10),
                new DateTime(2020, 1, 2, 3, 4, 5),
            };
        }

        public static void Main()
        {
            List<DateTime> l = L();
            DateTime[] a = l.ToArray();

            Console.WriteLine("[l.where]=" + J(l.Where(d => d.Year == 2019).ToArray()));
            Console.WriteLine("[a.where]=" + J(a.Where(d => d.Year == 2019).ToArray()));
            Console.WriteLine("[l.whereidx]=" + J(l.Where((d, i) => i % 2 == 0).ToArray()));
            Console.WriteLine("[a.whereidx]=" + J(a.Where((d, i) => i % 2 == 0).ToArray()));
            Console.WriteLine("[l.takewhile]=" + J(l.TakeWhile(d => d.Year != 2021).ToArray()));
            Console.WriteLine("[l.skipwhile]=" + J(l.SkipWhile(d => d.Year != 2020).ToArray()));
            Console.WriteLine("[a.takewhile]=" + J(a.TakeWhile(d => d.Year != 2021).ToArray()));
            Console.WriteLine("[l.all]=" + l.All(d => d.Year >= 2019));
            Console.WriteLine("[l.anypred]=" + l.Any(d => d.Month == 6));
            Console.WriteLine("[l.countpred]=" + l.Count(d => d.Day > 3));
            Console.WriteLine("[l.first]=" + S(l.First()));
            Console.WriteLine("[l.last]=" + S(l.Last()));
            Console.WriteLine("[l.firstord]=" + S(l.FirstOrDefault(d => d.Year == 1999)));
            Console.WriteLine("[l.elem]=" + S(l.ElementAt(2)));
            Console.WriteLine("[l.contains]=" + l.Contains(new DateTime(2019, 1, 2, 3, 4, 5)));
            Console.WriteLine("[l.take]=" + J(l.Take(2).ToArray()));
            Console.WriteLine("[l.skip]=" + J(l.Skip(3).ToArray()));
            Console.WriteLine("[a.rev]=" + J(Enumerable.Reverse(a).ToArray()));
            Console.WriteLine("[l.distinct]=" + J(l.Distinct().ToArray()));
            Console.WriteLine("[l.concat]=" + J(l.Concat(new[] { new DateTime(2000, 1, 1) }).ToArray()));
            Console.WriteLine("[l.orderby]=" + J(l.OrderBy(d => d).ToArray()));
            Console.WriteLine("[l.obdesc]=" + J(l.OrderByDescending(d => d.Month).ToArray()));
            Console.WriteLine("[l.selint]=" + J(l.Select(d => d.Month).ToArray()));
            Console.WriteLine("[l.sellong]=" + J(l.Select(d => d.Ticks).ToArray()));
            Console.WriteLine("[l.aggint]=" + l.Aggregate(0, (acc, d) => acc + d.Day));
            Console.WriteLine("[l.aggref]=" + l.Aggregate("", (acc, d) => acc + d.Day + ";"));
            Console.WriteLine("[l.aggrs]=" + l.Aggregate(0, (acc, d) => acc + d.Day, r => r * 2));
            Console.WriteLine("[l.count]=" + l.Count());
            Console.WriteLine("[l.any]=" + l.Any());
            Console.WriteLine("[l.longcount]=" + l.LongCount());
            Console.WriteLine("[l.tolist]=" + J(l.OrderBy(d => d.Day).ToList().ToArray()));
            Console.WriteLine("[l.defempty]=" + J(new List<DateTime>().DefaultIfEmpty().ToArray()));
            Console.WriteLine("[l.seqeq]=" + l.SequenceEqual(L()));
        }
    }
}