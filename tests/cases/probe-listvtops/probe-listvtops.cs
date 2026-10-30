//------------------------------------------------------------------------------
// probe-listvtops -- the BY-VALUE VALUE-STRUCT List operator surface
// (List<DateTime> / List<TimeSpan> / List<Guid> / List<DateTimeOffset>).
//
// Before this seat MethodMap.Linq.cs rejected every operator over these list
// element kinds except Count/Any/LongCount/ToArray/DefaultIfEmpty with a
// transpile-time LOUD ("...has a cell ABI for Count/Any/LongCount/ToArray/
// DefaultIfEmpty only"). The runtime already had the ARRAY kernels; the LIST
// domain was missing the thin snapshot wrappers and the by-value delegate
// dispatch (PredicateOn / the per-family accessors / the `val` aggregate
// family).
//
// This probe pins the whole matrix against the net48 oracle, byte-exact:
//   element-returning : First / FirstOrDefault / Last / LastOrDefault /
//                       Single / SingleOrDefault / ElementAt / Contains
//                       (+ their predicate forms)
//   predicate         : Where / All / Any(pred) / Count(pred) /
//                       TakeWhile / SkipWhile (+ the index overloads)
//   set / range       : Take / Skip / Reverse / Concat / Distinct / Except /
//                       Union / Intersect / DefaultIfEmpty
//   aggregate         : Aggregate(seed, func) / Aggregate(seed, func, sel)
//
// Every family is exercised over a NON-EMPTY, a SINGLE-element and an EMPTY
// source so the default/throw truth table is covered too.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeListVtOps
    {
        private static string L(long v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        //--------------------------------------------------------------------------
        // List<DateTime>
        //--------------------------------------------------------------------------
        private static List<DateTime> DtList()
        {
            List<DateTime> l = new List<DateTime>();
            l.Add(new DateTime(2020, 1, 2, 3, 4, 5));
            l.Add(new DateTime(1999, 12, 31));
            l.Add(new DateTime(2010, 6, 15, 12, 0, 0));
            l.Add(new DateTime(2020, 1, 2, 3, 4, 5)); // duplicate of [0]
            return l;
        }

        private static void DateTimeOps()
        {
            List<DateTime> l = DtList();
            Console.WriteLine("[dt_first]=" + L(l.First().Ticks));
            Console.WriteLine("[dt_last]=" + L(l.Last().Ticks));
            Console.WriteLine("[dt_first_pred]=" + L(l.First(x => x.Year == 2010).Ticks));
            Console.WriteLine("[dt_last_pred]=" + L(l.Last(x => x.Year == 2020).Ticks));
            Console.WriteLine("[dt_elem_at1]=" + L(l.ElementAt(1).Ticks));
            Console.WriteLine("[dt_elem_at_or_default]=" + L(l.ElementAtOrDefault(1).Ticks));
            Console.WriteLine("[dt_elem_at_or_default_oob]=" + L(l.ElementAtOrDefault(99).Ticks));
            Console.WriteLine("[dt_elem_at_or_default_neg]=" + L(l.ElementAtOrDefault(-1).Ticks));
            Console.WriteLine("[dt_contains]=" + Enumerable.Contains(l, new DateTime(1999, 12, 31)));
            Console.WriteLine("[dt_not_contains]=" + Enumerable.Contains(l, new DateTime(1998, 1, 1)));

            List<DateTime> one = new List<DateTime>();
            one.Add(new DateTime(2001, 2, 3));
            Console.WriteLine("[dt_single]=" + L(one.Single().Ticks));
            Console.WriteLine("[dt_single_or_default]=" + L(one.SingleOrDefault().Ticks));

            List<DateTime> empty = new List<DateTime>();
            Console.WriteLine("[dt_empty_count]=" + I(empty.Count()));
            Console.WriteLine("[dt_empty_first_or_default]=" + L(empty.FirstOrDefault().Ticks));
            Console.WriteLine("[dt_empty_last_or_default]=" + L(empty.LastOrDefault().Ticks));
            Console.WriteLine("[dt_empty_single_or_default]=" + L(empty.SingleOrDefault().Ticks));
            Console.WriteLine("[dt_empty_any]=" + empty.Any());
            Console.WriteLine("[dt_empty_all]=" + empty.All(x => x.Year > 0));
            Console.WriteLine("[dt_empty_count_pred]=" + I(empty.Count(x => x.Year > 0)));
            Console.WriteLine("[dt_empty_agg]=" + L(empty.Aggregate(7L, (a, x) => a + x.Ticks)));

            Console.WriteLine("[dt_where_count]=" + I(l.Where(x => x.Year == 2020).Count()));
            Console.WriteLine("[dt_where_idx_count]=" + I(l.Where((x, i) => i % 2 == 0).Count()));
            Console.WriteLine("[dt_all]=" + l.All(x => x.Year >= 1999));
            Console.WriteLine("[dt_any_pred]=" + l.Any(x => x.Month == 6));
            Console.WriteLine("[dt_count_pred]=" + I(l.Count(x => x.Year == 2020)));
            Console.WriteLine("[dt_take_while]=" + I(l.TakeWhile(x => x.Year != 2010).Count()));
            Console.WriteLine("[dt_take_while_idx]=" + I(l.TakeWhile((x, i) => i < 2).Count()));
            Console.WriteLine("[dt_skip_while]=" + I(l.SkipWhile(x => x.Year != 2010).Count()));
            Console.WriteLine("[dt_skip_while_idx]=" + I(l.SkipWhile((x, i) => i < 1).Count()));

            Console.WriteLine("[dt_take2]=" + L(l.Take(2).ElementAt(1).Ticks));
            Console.WriteLine("[dt_skip2_first]=" + L(l.Skip(2).First().Ticks));
            Console.WriteLine("[dt_reverse_first]=" + L(Enumerable.Reverse(l).First().Ticks));
            Console.WriteLine("[dt_distinct_count]=" + I(l.Distinct().Count()));
            Console.WriteLine("[dt_distinct_first]=" + L(l.Distinct().First().Ticks));

            List<DateTime> second = new List<DateTime>();
            second.Add(new DateTime(2010, 6, 15, 12, 0, 0));
            second.Add(new DateTime(2030, 5, 5));
            Console.WriteLine("[dt_concat_count]=" + I(l.Concat(second).Count()));
            Console.WriteLine("[dt_except_count]=" + I(l.Except(second).Count()));
            Console.WriteLine("[dt_union_count]=" + I(l.Union(second).Count()));
            Console.WriteLine("[dt_intersect_count]=" + I(l.Intersect(second).Count()));
            Console.WriteLine("[dt_intersect_first]=" + L(l.Intersect(second).First().Ticks));

            Console.WriteLine("[dt_default_empty]=" + L(empty.DefaultIfEmpty().First().Ticks));
            Console.WriteLine("[dt_default_value]=" +
                L(empty.DefaultIfEmpty(new DateTime(1970, 1, 1)).First().Ticks));

            List<DateTime> dtCopy = l.ToList();
            Console.WriteLine("[dt_tolist_count]=" + I(dtCopy.Count) + "/" + L(dtCopy[0].Ticks));
            Console.WriteLine("[dt_agg_ticks]=" + L(l.Aggregate(0L, (a, x) => a + x.Ticks % 1000)));
            Console.WriteLine("[dt_agg_rs]=" +
                L(l.Aggregate(0L, (a, x) => a + x.Ticks % 1000, t => t * 2)));
            Console.WriteLine("[dt_agg_dec]=" +
                l.Aggregate(0m, (a, x) => a + 1m).ToString(CultureInfo.InvariantCulture));
        }

        //--------------------------------------------------------------------------
        // List<TimeSpan>
        //--------------------------------------------------------------------------
        private static List<TimeSpan> TsList()
        {
            List<TimeSpan> l = new List<TimeSpan>();
            l.Add(TimeSpan.FromMinutes(90));
            l.Add(TimeSpan.FromSeconds(-5));
            l.Add(TimeSpan.FromHours(2));
            l.Add(TimeSpan.FromMinutes(90)); // duplicate
            return l;
        }

        private static void TimeSpanOps()
        {
            List<TimeSpan> l = TsList();
            Console.WriteLine("[ts_first]=" + L(l.First().Ticks));
            Console.WriteLine("[ts_last]=" + L(l.Last().Ticks));
            Console.WriteLine("[ts_first_pred]=" + L(l.First(x => x.Ticks < 0).Ticks));
            Console.WriteLine("[ts_last_pred]=" + L(l.Last(x => x.Ticks > 0).Ticks));
            Console.WriteLine("[ts_elem_at2]=" + L(l.ElementAt(2).Ticks));
            Console.WriteLine("[ts_elem_at_or_default]=" + L(l.ElementAtOrDefault(3).Ticks));
            Console.WriteLine("[ts_elem_at_or_default_oob]=" + L(l.ElementAtOrDefault(9).Ticks));
            Console.WriteLine("[ts_contains]=" + Enumerable.Contains(l, TimeSpan.FromHours(2)));
            Console.WriteLine("[ts_not_contains]=" + Enumerable.Contains(l, TimeSpan.FromHours(3)));

            List<TimeSpan> empty = new List<TimeSpan>();
            Console.WriteLine("[ts_empty_first_or_default]=" + L(empty.FirstOrDefault().Ticks));
            Console.WriteLine("[ts_empty_last_or_default]=" + L(empty.LastOrDefault().Ticks));
            Console.WriteLine("[ts_empty_count]=" + I(empty.Count()));
            Console.WriteLine("[ts_empty_agg]=" + L(empty.Aggregate(3L, (a, x) => a + x.Ticks)));

            Console.WriteLine("[ts_where_count]=" + I(l.Where(x => x.Ticks > 0).Count()));
            Console.WriteLine("[ts_all]=" + l.All(x => x.Ticks != 0));
            Console.WriteLine("[ts_any_pred]=" + l.Any(x => x.Ticks == 72000000000L));
            Console.WriteLine("[ts_count_pred]=" + I(l.Count(x => x.Ticks > 0)));
            Console.WriteLine("[ts_take_while]=" + I(l.TakeWhile(x => x.Ticks > 0).Count()));
            Console.WriteLine("[ts_skip_while]=" + I(l.SkipWhile(x => x.Ticks > 0).Count()));
            Console.WriteLine("[ts_take_while_idx]=" + I(l.TakeWhile((x, i) => i < 2).Count()));
            Console.WriteLine("[ts_skip_while_idx]=" + I(l.SkipWhile((x, i) => i < 3).Count()));

            Console.WriteLine("[ts_take2]=" + L(l.Take(2).ElementAt(1).Ticks));
            Console.WriteLine("[ts_skip1_first]=" + L(l.Skip(1).First().Ticks));
            Console.WriteLine("[ts_reverse_first]=" + L(Enumerable.Reverse(l).First().Ticks));
            Console.WriteLine("[ts_distinct_count]=" + I(l.Distinct().Count()));

            List<TimeSpan> second = new List<TimeSpan>();
            second.Add(TimeSpan.FromHours(2));
            second.Add(TimeSpan.FromDays(1));
            Console.WriteLine("[ts_concat_count]=" + I(l.Concat(second).Count()));
            Console.WriteLine("[ts_except_count]=" + I(l.Except(second).Count()));
            Console.WriteLine("[ts_union_count]=" + I(l.Union(second).Count()));
            Console.WriteLine("[ts_intersect_count]=" + I(l.Intersect(second).Count()));
            Console.WriteLine("[ts_intersect_first]=" + L(l.Intersect(second).First().Ticks));

            Console.WriteLine("[ts_tolist]=" + I(l.ToList().Count) + "/" + L(l.ToList()[1].Ticks));
            Console.WriteLine("[ts_agg]=" + L(l.Aggregate(0L, (a, x) => a + x.Ticks % 1000)));
            Console.WriteLine("[ts_agg_rs]=" +
                L(l.Aggregate(0L, (a, x) => a + x.Ticks % 1000, t => t + 1)));
        }

        //--------------------------------------------------------------------------
        // List<Guid>
        //--------------------------------------------------------------------------
        private static Guid G(int a, int b)
        {
            return new Guid(a, 0, 0, (byte)b, 0, 0, 0, 0, 0, 0, 0);
        }

        private static List<Guid> GuidList()
        {
            List<Guid> l = new List<Guid>();
            l.Add(G(1, 1));
            l.Add(G(2, 2));
            l.Add(G(3, 3));
            l.Add(G(1, 1)); // duplicate
            return l;
        }

        private static void GuidOps()
        {
            List<Guid> l = GuidList();
            Console.WriteLine("[guid_first]=" + l.First().ToString());
            Console.WriteLine("[guid_last]=" + l.Last().ToString());
            Console.WriteLine("[guid_first_pred]=" + l.First(x => x == G(2, 2)).ToString());
            Console.WriteLine("[guid_last_pred]=" + l.Last(x => x == G(1, 1)).ToString());
            Console.WriteLine("[guid_elem_at2]=" + l.ElementAt(2).ToString());
            Console.WriteLine("[guid_elem_at_or_default]=" + l.ElementAtOrDefault(1).ToString());
            Console.WriteLine("[guid_elem_at_or_default_oob]=" + l.ElementAtOrDefault(9).ToString());
            Console.WriteLine("[guid_contains]=" + Enumerable.Contains(l, G(3, 3)));
            Console.WriteLine("[guid_not_contains]=" + Enumerable.Contains(l, G(9, 9)));

            List<Guid> one = new List<Guid>();
            one.Add(G(7, 7));
            Console.WriteLine("[guid_single]=" + one.Single().ToString());

            List<Guid> empty = new List<Guid>();
            Console.WriteLine("[guid_empty_first_or_default]=" + empty.FirstOrDefault().ToString());
            Console.WriteLine("[guid_empty_last_or_default]=" + empty.LastOrDefault().ToString());
            Console.WriteLine("[guid_empty_single_or_default]=" + empty.SingleOrDefault().ToString());
            Console.WriteLine("[guid_empty_any]=" + empty.Any());
            Console.WriteLine("[guid_empty_count_pred]=" + I(empty.Count(x => x == G(1, 1))));

            Console.WriteLine("[guid_where_count]=" + I(l.Where(x => x == G(1, 1)).Count()));
            Console.WriteLine("[guid_all]=" + l.All(x => x != G(9, 9)));
            Console.WriteLine("[guid_any_pred]=" + l.Any(x => x == G(2, 2)));
            Console.WriteLine("[guid_count_pred]=" + I(l.Count(x => x == G(1, 1))));
            Console.WriteLine("[guid_take_while]=" + I(l.TakeWhile(x => x != G(3, 3)).Count()));
            Console.WriteLine("[guid_skip_while]=" + I(l.SkipWhile(x => x != G(3, 3)).Count()));
            Console.WriteLine("[guid_take_while_idx]=" + I(l.TakeWhile((x, i) => i < 2).Count()));
            Console.WriteLine("[guid_skip_while_idx]=" + I(l.SkipWhile((x, i) => i < 2).Count()));

            Console.WriteLine("[guid_take2]=" + l.Take(2).ElementAt(1).ToString());
            Console.WriteLine("[guid_skip2_first]=" + l.Skip(2).First().ToString());
            Console.WriteLine("[guid_reverse_first]=" + Enumerable.Reverse(l).First().ToString());
            Console.WriteLine("[guid_distinct_count]=" + I(l.Distinct().Count()));

            List<Guid> second = new List<Guid>();
            second.Add(G(3, 3));
            second.Add(G(4, 4));
            Console.WriteLine("[guid_concat_count]=" + I(l.Concat(second).Count()));
            Console.WriteLine("[guid_except_count]=" + I(l.Except(second).Count()));
            Console.WriteLine("[guid_union_count]=" + I(l.Union(second).Count()));
            Console.WriteLine("[guid_intersect_count]=" + I(l.Intersect(second).Count()));
            Console.WriteLine("[guid_intersect_first]=" + l.Intersect(second).First().ToString());

            Console.WriteLine("[guid_tolist]=" + l.ToList().First().ToString());
            Console.WriteLine("[guid_agg]=" + I(l.Aggregate(0, (a, x) => a + 1)));
            Console.WriteLine("[guid_agg_rs]=" + I(l.Aggregate(0, (a, x) => a + 1, t => t * 10)));
        }

        //--------------------------------------------------------------------------
        // List<DateTimeOffset>
        //--------------------------------------------------------------------------
        private static List<DateTimeOffset> DtoList()
        {
            List<DateTimeOffset> l = new List<DateTimeOffset>();
            l.Add(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero));
            l.Add(new DateTimeOffset(new DateTime(2000, 2, 29, 12, 0, 0), new TimeSpan(8, 0, 0)));
            l.Add(new DateTimeOffset(2010, 6, 15, 0, 0, 0, TimeSpan.Zero));
            l.Add(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero)); // duplicate
            return l;
        }

        private static void DateTimeOffsetOps()
        {
            List<DateTimeOffset> l = DtoList();
            Console.WriteLine("[dto_first]=" + L(l.First().Ticks));
            Console.WriteLine("[dto_last]=" + L(l.Last().Ticks));
            Console.WriteLine("[dto_first_pred]=" + L(l.First(x => x.Year == 2000).Ticks));
            Console.WriteLine("[dto_last_pred]=" + L(l.Last(x => x.Year == 2020).Ticks));
            Console.WriteLine("[dto_elem_at1]=" + L(l.ElementAt(1).Ticks));
            Console.WriteLine("[dto_elem_at1_off]=" + L(l.ElementAt(1).Offset.Ticks));
            Console.WriteLine("[dto_elem_at_or_default]=" + L(l.ElementAtOrDefault(3).Ticks));
            Console.WriteLine("[dto_elem_at_or_default_oob]=" + L(l.ElementAtOrDefault(9).Ticks));
            Console.WriteLine("[dto_contains]=" + Enumerable.Contains(l, new DateTimeOffset(2010, 6, 15, 0, 0, 0, TimeSpan.Zero)));
            Console.WriteLine("[dto_not_contains]=" +
                Enumerable.Contains(l, new DateTimeOffset(2011, 6, 15, 0, 0, 0, TimeSpan.Zero)));

            List<DateTimeOffset> empty = new List<DateTimeOffset>();
            Console.WriteLine("[dto_empty_first_or_default]=" + L(empty.FirstOrDefault().Ticks));
            Console.WriteLine("[dto_empty_last_or_default]=" + L(empty.LastOrDefault().Ticks));
            Console.WriteLine("[dto_empty_any]=" + empty.Any());
            Console.WriteLine("[dto_empty_agg]=" + I(empty.Aggregate(5, (a, x) => a + 1)));

            Console.WriteLine("[dto_where_count]=" + I(l.Where(x => x.Year == 2020).Count()));
            Console.WriteLine("[dto_all]=" + l.All(x => x.Year >= 2000));
            Console.WriteLine("[dto_any_pred]=" + l.Any(x => x.Month == 6));
            Console.WriteLine("[dto_count_pred]=" + I(l.Count(x => x.Year == 2020)));
            Console.WriteLine("[dto_take_while]=" + I(l.TakeWhile(x => x.Year != 2010).Count()));
            Console.WriteLine("[dto_skip_while]=" + I(l.SkipWhile(x => x.Year != 2010).Count()));
            Console.WriteLine("[dto_take_while_idx]=" + I(l.TakeWhile((x, i) => i < 3).Count()));
            Console.WriteLine("[dto_skip_while_idx]=" + I(l.SkipWhile((x, i) => i < 2).Count()));

            Console.WriteLine("[dto_take2]=" + L(l.Take(2).ElementAt(1).Ticks));
            Console.WriteLine("[dto_skip3_first]=" + L(l.Skip(3).First().Ticks));
            Console.WriteLine("[dto_reverse_first]=" + L(Enumerable.Reverse(l).First().Ticks));
            Console.WriteLine("[dto_distinct_count]=" + I(l.Distinct().Count()));

            List<DateTimeOffset> second = new List<DateTimeOffset>();
            second.Add(new DateTimeOffset(2010, 6, 15, 0, 0, 0, TimeSpan.Zero));
            second.Add(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Console.WriteLine("[dto_concat_count]=" + I(l.Concat(second).Count()));
            Console.WriteLine("[dto_except_count]=" + I(l.Except(second).Count()));
            Console.WriteLine("[dto_union_count]=" + I(l.Union(second).Count()));
            Console.WriteLine("[dto_intersect_count]=" + I(l.Intersect(second).Count()));
            Console.WriteLine("[dto_intersect_first]=" + L(l.Intersect(second).First().Ticks));

            Console.WriteLine("[dto_tolist]=" + L(l.ToList()[2].Ticks));
            Console.WriteLine("[dto_agg]=" + L(l.Aggregate(0L, (a, x) => a + x.Ticks % 1000)));
            Console.WriteLine("[dto_agg_rs]=" +
                L(l.Aggregate(0L, (a, x) => a + x.Ticks % 1000, t => t - 1)));
        }

        public static void Main()
        {
            DateTimeOps();
            TimeSpanOps();
            GuidOps();
            DateTimeOffsetOps();
            Console.WriteLine("done");
        }
    }
}
