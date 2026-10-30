// probe-vtsel.cs -- the STRUCT-TYPED selector / key-selector ABI over
// List<DateTime|TimeSpan|Guid|DateTimeOffset> (and the matching ARRAY face).
//
// Baseline (c96b737b) LOUDs every one of these:
//   "Enumerable.Select over a List<System.DateTime> is not wired: this
//    operator needs a struct-typed delegate / key-selector ABI".
// The trap this probe pins down: simply removing that gate produces SILENT
// WRONG VALUES, because the list select kernel reads every element through
// ReadI4 (the first 4 bytes of an 8/16-byte cell) and calls the lambda
// through the int32 ABI.
//
// net48 semantics pinned here (measured against the net48 oracle):
//   * Select preserves the DateTime Kind (the packed array cell carries it;
//     DateTimeKind Utc=1, Local=2, Unspecified=0);
//   * OrderBy/GroupBy/ThenBy key a DateTime by TICKS only (Kind is ignored),
//     so two identical ticks with different Kinds share one bucket / sort
//     together (stable);
//   * TimeSpan keys compare by ticks (negative spans order before zero);
//   * Guid keys use the net48 field-wise CompareTo and 16-byte equality
//     (Guid.Empty and the all-0xFF maximum are the extremes);
//   * DateTimeOffset orders by UtcTicks and its default equality ALSO
//     requires the same Offset (DateTimeOffset.Equals: UtcTicks AND Offset).
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ProbeVtSel
    {
        static int fails;

        static void Eq(string tag, long expected, long actual)
        {
            if (expected != actual)
            {
                fails++;
                Console.WriteLine("FAIL " + tag + " expected=" + expected + " actual=" + actual);
            }
        }

        static void SeqL(string tag, long[] expected, long[] actual)
        {
            bool ok = expected.Length == actual.Length;
            for (int i = 0; ok && i < expected.Length; i++) ok = expected[i] == actual[i];
            if (!ok)
            {
                fails++;
                Console.WriteLine("FAIL " + tag + " expected=[" + string.Join(",", expected) +
                                  "] actual=[" + string.Join(",", actual) + "]");
            }
        }

        static long[] Ticks(IEnumerable<DateTime> xs)
        {
            var list = xs.ToList();
            var r = new long[list.Count];
            for (int i = 0; i < list.Count; i++) r[i] = list[i].Ticks;
            return r;
        }

        static long[] SpanTicks(IEnumerable<TimeSpan> xs)
        {
            var list = xs.ToList();
            var r = new long[list.Count];
            for (int i = 0; i < list.Count; i++) r[i] = list[i].Ticks;
            return r;
        }

        public static void Main()
        {
            var d0 = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var d1 = new DateTime(2020, 1, 2);
            var d2 = new DateTime(2020, 1, 3);

            var dates = new List<DateTime>();
            dates.Add(d2);
            dates.Add(d0);
            dates.Add(d1);

            // ===== 1. Select over a List<DateTime> source =====
            // dt -> dt (identity, must stay by value)
            SeqL("sel.dt.dt", new[] { d2.Ticks, d0.Ticks, d1.Ticks },
                Ticks(dates.Select(x => x)));
            // dt -> ts
            SeqL("sel.dt.ts", new[] { (d2 - d0).Ticks, 0L, (d1 - d0).Ticks },
                SpanTicks(dates.Select(x => x - d0)));
            // dt -> i8 (ticks) and dt -> i4
            SeqL("sel.dt.i8", new[] { d2.Ticks, d0.Ticks, d1.Ticks },
                dates.Select(x => x.Ticks).ToArray());
            SeqL("sel.dt.i4", new[] { 3L, 1L, 2L },
                dates.Select(x => (long)x.Day).ToArray());
            // dt -> ref
            Eq("sel.dt.ref.0", 3L, dates.Select(x => x.Day.ToString()).ToArray()[0] == "3" ? 3L : -1L);


            // Select PRESERVES the Kind (the packed 8-byte cell carries it).
            var utc = new DateTime(d0.Ticks, DateTimeKind.Utc);
            var local = new DateTime(d0.Ticks, DateTimeKind.Local);
            var kinds = new List<DateTime>();
            kinds.Add(utc);
            kinds.Add(local);
            kinds.Add(d0);
            var kindOut = kinds.Select(x => x).ToList();
            Eq("sel.kind.utc", 1L, (long)kindOut[0].Kind);
            Eq("sel.kind.local", 2L, (long)kindOut[1].Kind);
            Eq("sel.kind.unspec", 0L, (long)kindOut[2].Kind);
            Eq("sel.kind.ticks", d0.Ticks, kindOut[0].Ticks);

            // ===== 2. Select(idx) =====
            SeqL("selidx.dt.dt", new[] { d2.Ticks, d0.Ticks, d1.Ticks },
                Ticks(dates.Select((x, i) => x)));
            SeqL("selidx.dt.i4", new[] { 0L, 1L, 2L },
                dates.Select((x, i) => (long)i).ToArray());
            SeqL("selidx.dt.ts", new[] { (d2 - d0).Ticks, 0L, (d1 - d0).Ticks },
                SpanTicks(dates.Select((x, i) => x - d0)));

            // ===== 3. List<TimeSpan> source (negative span included) =====
            var spans = new List<TimeSpan>();
            spans.Add(TimeSpan.FromSeconds(3));
            spans.Add(TimeSpan.FromSeconds(-2));
            spans.Add(TimeSpan.Zero);

            SeqL("sel.ts.ts", new[] { 30000000L, -20000000L, 0L },
                SpanTicks(spans.Select(x => x)));
            SeqL("sel.ts.dt", new[] { d0.Ticks + 30000000L, d0.Ticks - 20000000L, d0.Ticks },
                Ticks(spans.Select(x => d0 + x)));
            SeqL("sel.ts.i8", new[] { 30000000L, -20000000L, 0L },
                spans.Select(x => x.Ticks).ToArray());
            SeqL("sel.ts.guid.len", new[] { 36L, 36L, 36L },
                spans.Select(x => GuidFromTicks(x.Ticks))
                     .Select(g => (long)g.ToString().Length).ToArray());

            // ===== 4. List<Guid> source (Empty and the 0xFF maximum) =====
            var gEmpty = Guid.Empty;
            var gMax = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
            var gMid = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
            var guids = new List<Guid>();
            guids.Add(gMax);
            guids.Add(gEmpty);
            guids.Add(gMid);

            var guidEcho = guids.Select(x => x).ToList();
            Eq("sel.guid.guid.0", gMax == guidEcho[0] ? 1L : 0L, 1L);
            Eq("sel.guid.guid.1", gEmpty == guidEcho[1] ? 1L : 0L, 1L);
            Eq("sel.guid.guid.2", gMid == guidEcho[2] ? 1L : 0L, 1L);
            SeqL("sel.guid.i4", new[] { 0L, 1L, 0L },
                guids.Select(x => x == Guid.Empty ? 1L : 0L).ToArray());
            SeqL("sel.guid.ref", new[] { 1L, 1L, 1L },
                guids.Select(x => x.ToString() == gEmpty.ToString() || x.ToString() == gMax.ToString()
                                   || x.ToString() == gMid.ToString() ? 1L : 0L).ToArray());
            var guidToDt = guids.Select(x => x == Guid.Empty ? d0 : (x == gMax ? d1 : d2)).ToList();
            Eq("sel.guid.dt.0", d0.Ticks, guidToDt[1].Ticks);
            Eq("sel.guid.dt.1", d1.Ticks, guidToDt[0].Ticks);
            Eq("sel.guid.dt.2", d2.Ticks, guidToDt[2].Ticks);

            // ===== 5. List<DateTimeOffset> source =====
            var o0 = new DateTimeOffset(d0, TimeSpan.FromHours(2));
            var o1 = new DateTimeOffset(d1, TimeSpan.Zero);
            var o2 = new DateTimeOffset(d2, TimeSpan.FromHours(-5));
            var offs = new List<DateTimeOffset>();
            offs.Add(o0);
            offs.Add(o1);
            offs.Add(o2);

            var dtoEcho = offs.Select(x => x).ToList();
            Eq("sel.dto.dto.0", o0.Ticks, dtoEcho[0].Ticks);
            Eq("sel.dto.dto.1", o1.Ticks, dtoEcho[1].Ticks);
            Eq("sel.dto.dto.2", o2.Ticks, dtoEcho[2].Ticks);
            Eq("sel.dto.off.0", 120L, (long)dtoEcho[0].Offset.TotalMinutes);
            SeqL("sel.dto.dt", new[] { d0.Ticks, d1.Ticks, d2.Ticks },
                Ticks(offs.Select(x => x.DateTime)));
            SeqL("sel.dto.i8", new[] { o0.Ticks, o1.Ticks, o2.Ticks },
                offs.Select(x => x.Ticks).ToArray());

            // ===== 6. OrderBy / OrderByDescending =====
            SeqL("ord.dt.dtkey", new[] { d0.Ticks, d1.Ticks, d2.Ticks },
                Ticks(dates.OrderBy(x => x)));
            SeqL("ord.dt.dtkey.desc", new[] { d2.Ticks, d1.Ticks, d0.Ticks },
                Ticks(dates.OrderByDescending(x => x)));
            SeqL("ord.dt.i4key", new[] { d0.Ticks, d1.Ticks, d2.Ticks },
                Ticks(dates.OrderBy(x => x.Day)));
            SeqL("ord.dt.guidkey", new[] { d0.Ticks, d1.Ticks, d2.Ticks },
                Ticks(dates.OrderBy(x => GuidFromTicks(x.Ticks))));
            SeqL("ord.ts.tskey", new[] { -20000000L, 0L, 30000000L },
                SpanTicks(spans.OrderBy(x => x)));
            SeqL("ord.guid.guidkey", new[] { 1L, 2L, 0L },
                GidOrder(guids.OrderBy(x => x)));
            SeqL("ord.dto.dtokey", new[] { o0.Ticks, o1.Ticks, o2.Ticks },
                ToLongs(offs.OrderBy(x => x.DateTime)));

            // DateTime: equal ticks + different Kind sort together (ticks only),
            // and the sort is STABLE (the first-seen variant stays first).
            var k1 = new DateTime(d1.Ticks, DateTimeKind.Utc);
            var k2 = new DateTime(d1.Ticks, DateTimeKind.Local);
            var mix = new List<DateTime>();
            mix.Add(d1);
            mix.Add(k1);
            mix.Add(d0);
            mix.Add(k2);
            var ob = mix.OrderBy(x => x).ToList();
            Eq("ord.kind.0", d0.Ticks, ob[0].Ticks);
            Eq("ord.kind.1", d1.Ticks, ob[1].Ticks);
            Eq("ord.kind.2", d1.Ticks, ob[2].Ticks);
            Eq("ord.kind.3", d1.Ticks, ob[3].Ticks);
            Eq("ord.kind.stable", 0L, (long)ob[1].Kind);

            // ===== 7. ThenBy / ThenByDescending =====
            var keys = new List<DateTime>();
            keys.Add(d1);
            keys.Add(d2);
            keys.Add(d0);
            keys.Add(d1);
            keys.Add(d2);
            SeqL("thenby.dt.asc", new[] { 1L, 2L, 2L, 3L, 3L },
                keys.OrderBy(x => x).ThenBy(x => x.Day).Select(x => (long)x.Day).ToArray());
            SeqL("thenby.dt.desc", new[] { 1L, 2L, 2L, 3L, 3L },
                keys.OrderBy(x => x).ThenByDescending(x => x.Day)
                    .Select(x => (long)x.Day).ToArray());

            // ===== 8. GroupBy (bucket walk: the supported IGrouping surface) =====
            var gsrc = new List<DateTime>();
            gsrc.Add(d2);
            gsrc.Add(d0);
            gsrc.Add(d1);
            gsrc.Add(d2);
            foreach (var g in gsrc.GroupBy(x => x))
            {
                Console.WriteLine("grp.dt.key k=" + g.Key.Ticks + " n=" + g.Count());
                foreach (var e in g) Console.WriteLine("grp.dt.key e=" + e.Ticks);
            }
            var gk = new List<DateTime>();
            gk.Add(d1);
            gk.Add(k1);
            gk.Add(k2);
            gk.Add(d0);
            foreach (var g in gk.GroupBy(x => x))
                Console.WriteLine("grp.dt.kindmerge k=" + g.Key.Ticks + " n=" + g.Count());
            // The DateTime SOURCE cell is the packed kind<<62|ticks image: the
            // ticks must come back RAW (a raw-word read folded the kind bits in)
            // and the three Kinds of ONE instant share a single bucket.
            int kindBuckets = 0;
            foreach (var g in kinds.GroupBy(x => x))
            {
                kindBuckets++;
                Eq("grp.dt.kind.ticks", d0.Ticks, g.Key.Ticks);
                Eq("grp.dt.kind.count", 3L, (long)g.Count());
            }
            Eq("grp.dt.kind.buckets", 1L, (long)kindBuckets);
            Eq("grp.ts.count", 3L, (long)spans.GroupBy(x => x).Count());
            var sameTicks = new List<TimeSpan>();
            sameTicks.Add(TimeSpan.FromMilliseconds(1500));
            sameTicks.Add(TimeSpan.FromTicks(15000000));
            Eq("grp.ts.ticks", 1L, (long)sameTicks.GroupBy(x => x).Count());
            foreach (var g in spans.GroupBy(x => x))
                Console.WriteLine("grp.ts k=" + g.Key.Ticks + " n=" + g.Count());
            Eq("grp.guid.key", 3L, (long)guids.GroupBy(x => x).Count());
            foreach (var g in guids.GroupBy(x => x))
                Console.WriteLine("grp.guid k=" + GidTag(g.Key) + " n=" + g.Count());
            Eq("grp.dto.count", 3L, (long)offs.GroupBy(x => x).Count());
            foreach (var g in offs.GroupBy(x => x))
                Console.WriteLine("grp.dto k=" + g.Key.UtcTicks + " n=" + g.Count());

            // DateTimeOffset: equal UTC instant, DIFFERENT offset -> net48 treats them as ONE key.
            var instant = new DateTimeOffset(new DateTime(2020, 5, 5), TimeSpan.Zero);
            var sameInstant = new DateTimeOffset(instant.DateTime.AddHours(-3), TimeSpan.FromHours(-3)).ToOffset(TimeSpan.FromHours(3));
            Eq("dto.utcequals", 1L,
                sameInstant.UtcTicks == instant.UtcTicks ? 1L : 0L);
            var twoDto = new List<DateTimeOffset>();
            twoDto.Add(instant);
            twoDto.Add(sameInstant);
            foreach (var g in twoDto.GroupBy(x => x))
                Console.WriteLine("grp.dto.offset k=" + g.Key.UtcTicks + " n=" + g.Count());

            // ===== 9. Zip (each side its own family) =====
            var zs = dates.Zip(spans, (a, b) => a + b).ToList();
            Eq("zip.dt.ts.0", d2.Ticks + 30000000L, zs[0].Ticks);
            Eq("zip.dt.ts.1", d0.Ticks - 20000000L, zs[1].Ticks);
            Eq("zip.dt.ts.2", d1.Ticks, zs[2].Ticks);
            var zipIdx = new List<DateTime>();
            zipIdx.Add(d2);
            zipIdx.Add(d0);
            zipIdx.Add(d1);
            SeqL("zip.dt.i4.i4", new[] { 3L, 1L, 2L },
                zipIdx.Zip(new[] { 0, 0, 0 }, (a, b) => a.Day + b)
                       .Select(v => (long)v).ToArray());
            SeqL("zip.guid.dt.dt", new[] { d0.Ticks, d0.Ticks, d2.Ticks },
                Ticks(guids.Zip(offs, (g, o) => g == gMax ? o.DateTime : (g == gEmpty ? d0 : d2))));
            // truncation to the shorter side
            Eq("zip.short", 2L,
                (long)d2b().Zip(new[] { 1, 2 }, (a, b) => a).Count());
            try
            {
                d2b().Zip((IEnumerable<int>)null, (a, b) => a).ToList();
                fails++;
                Console.WriteLine("FAIL zip.null no exception");
            }
            catch (ArgumentNullException)
            {
            }

            // ===== 10. empty / single / chained =====
            Eq("empty.sel", 0L, (long)new List<DateTime>().Select(x => x).Count());
            Eq("empty.ord", 0L, (long)new List<DateTime>().OrderBy(x => x).Count());
            Eq("empty.grp", 0L, (long)new List<Guid>().GroupBy(x => x).Count());
            Eq("single", d0.Ticks, new List<DateTime>().Concat(Single(d0)).Select(x => x).First().Ticks);
            var chainSrc = new List<DateTime>();
            chainSrc.Add(d2);
            chainSrc.Add(d0);
            chainSrc.Add(d1);
            var chained = chainSrc.Select(x => x).OrderBy(x => x).ToList();
            Eq("chain.count", 3L, (long)chained.Count);
            Eq("chain.0", d0.Ticks, chained[0].Ticks);
            int seen = 0;
            foreach (var x in chained.Select(x => x)) seen++;
            Eq("chain.foreach", 3L, (long)seen);
            // foreach over a DateTime BUCKET (16-byte struct cells) and over a
            // DateTime SELECT result (packed 8-byte cells) must both carry the
            // real DateTime value.
            long bucketTicks = 0;
            foreach (var g in gsrc.GroupBy(x => x))
                foreach (var e in g) bucketTicks += e.Ticks;
            Eq("grp.foreach.ticks", d2.Ticks + d0.Ticks + d1.Ticks + d2.Ticks, bucketTicks);
            long selTicks = 0;
            foreach (var x in dates.Select(x => x)) selTicks += x.Ticks;
            Eq("sel.foreach.ticks", d2.Ticks + d0.Ticks + d1.Ticks, selTicks);

            // ===== 11. ARRAY face (List<->array parity) =====
            var arr = dates.ToArray();
            SeqL("arr.sel.dt.dt", new[] { d2.Ticks, d0.Ticks, d1.Ticks }, Ticks(arr.Select(x => x)));
            SeqL("arr.sel.dt.ts", new[] { (d2 - d0).Ticks, 0L, (d1 - d0).Ticks },
                SpanTicks(arr.Select(x => x - d0)));
            SeqL("arr.sel.dt.i4", new[] { 3L, 1L, 2L }, arr.Select(x => (long)x.Day).ToArray());
            SeqL("arr.sel.idx.dt", new[] { d2.Ticks, d0.Ticks, d1.Ticks },
                Ticks(arr.Select((x, i) => x)));
            SeqL("arr.ord.dtkey", new[] { d0.Ticks, d1.Ticks, d2.Ticks }, Ticks(arr.OrderBy(x => x)));
            var garr = guids.ToArray();
            var garrEcho = garr.Select(x => x).ToList();
            Eq("arr.guid.sel", gMax == garrEcho[0] ? 3L : 0L, 3L);
            SeqL("arr.ts.ord", new[] { -20000000L, 0L, 30000000L },
                SpanTicks(spans.ToArray().OrderBy(x => x)));

            // Select -> ToList keeps the struct family.
            var back = dates.Select(x => x).ToList();
            Eq("tolist.count", 3L, (long)back.Count);
            Eq("tolist.0", d2.Ticks, back[0].Ticks);
            Eq("tolist.kind", 0L, (long)back[0].Kind);

            Console.WriteLine(fails == 0 ? "probe-vtsel OK" : ("probe-vtsel FAILS=" + fails));
        }

        // A deterministic Guid built from a tick count (the int/short/short/byte[]
        // Guid ctor is not on the transpiler surface; a formatted string is).
        static Guid GuidFromTicks(long ticks)
        {
            int v = unchecked((int)ticks);
            return new Guid(v.ToString("x8") + "-0000-0000-0000-000000000000");
        }

        static long[] ToLongs(IEnumerable<DateTimeOffset> xs)
        {
            var list = xs.ToList();
            var r = new long[list.Count];
            for (int i = 0; i < list.Count; i++) r[i] = list[i].Ticks;
            return r;
        }

        static long GidTag(Guid x)
        {
            if (x == Guid.Empty) return 1L;
            if (x == new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff")) return 0L;
            return 2L;
        }

        static long[] GidOrder(IEnumerable<Guid> xs)
        {
            var list = xs.ToList();
            var r = new long[list.Count];
            for (int i = 0; i < list.Count; i++)
                r[i] = list[i] == Guid.Empty ? 1L : (list[i] == new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff") ? 0L : 2L);
            return r;
        }

        static List<DateTime> d2b()
        {
            var l = new List<DateTime>();
            l.Add(new DateTime(2020, 1, 1));
            l.Add(new DateTime(2020, 1, 2));
            l.Add(new DateTime(2020, 1, 3));
            return l;
        }

        static List<DateTime> Single(DateTime x)
        {
            var l = new List<DateTime>();
            l.Add(x);
            return l;
        }
    }
}











