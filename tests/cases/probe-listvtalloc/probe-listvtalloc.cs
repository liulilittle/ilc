//------------------------------------------------------------------------------
// probe-listvtalloc -- the ALLOCATION descriptor of a VALUE-STRUCT element
// List<T> built with `new List<T>()` (CodeGenerator BclAllocExpr gap).
//
// The BCL allocation table registered List`1_int (listi4.h) and every
// REFERENCE / small-value instantiation (listobj.h), but NOT the closed
// value-STRUCT instantiations that the runtime already serves from the shared
// listobj descriptor:
//   List<decimal>          listobj *_dec       forwarders
//   List<DateTime>         listobj *_dt        forwarders
//   List<TimeSpan>         listobj *_ts        forwarders
//   List<Guid>             listobj *_guid      forwarders
//   List<DateTimeOffset>   listobj *_dtoffset  forwarders
// A `new List<decimal>()` therefore fell through to the generated-descriptor
// fallback il2cpp_object_new(&System_Collections_Generic_List_1_System_Decimal_class)
// -- an UNDEFINED symbol (the transpiler emits no descriptor for a BCL type).
// Member calls on a ToList()-materialised List<decimal> worked (the runtime
// CREATES the list object), so the defect only surfaced on the `new` shape.
//
// Shapes pinned here: parameterless new + Add / index get+set / Capacity /
// Count / ToArray / foreach, the collection-initializer form, and (decimal)
// the LINQ Aggregate over the NEW list receiver.
//
// SCOPE NOTE (measured, separate defect -- NOT the allocation surface): a
// source-only `l.Sum()` over a List<decimal> is still unmapped -- LinqListKind
// answers "12" but the list sum arm has no decimal suffix and falls back to
// il2cpp_linq_list_sum_i4 (int32 result), so the generated assignment of the
// int32 result into an Il2CppDecimal does not compile ("no match for
// 'operator='" in the RED build log). That is the LINQ list-aggregate kernel
// surface (MethodMap.Linq.cs + linq_list.h/.cpp), outside this seat.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeListVtAlloc
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string L(long v) { return v.ToString(CultureInfo.InvariantCulture); }

        private static void DecimalShape()
        {
            List<decimal> l = new List<decimal>();
            Console.WriteLine("[dec_new_count]=" + I(l.Count));
            Console.WriteLine("[dec_new_cap]=" + I(l.Capacity));
            l.Add(1.25m);
            l.Add(-3m);
            l.Add(0.0001m);
            Console.WriteLine("[dec_add_count]=" + I(l.Count));
            Console.WriteLine("[dec_add_cap]=" + I(l.Capacity));
            Console.WriteLine("[dec_idx0]=" + D(l[0]));
            Console.WriteLine("[dec_idx1]=" + D(l[1]));
            Console.WriteLine("[dec_idx2]=" + D(l[2]));
            l[1] = 7.5m;
            Console.WriteLine("[dec_set1]=" + D(l[1]));
            decimal loop = 0m;
            for (int i = 0; i < l.Count; i++) { loop = loop + l[i]; }
            Console.WriteLine("[dec_loop_sum]=" + D(loop));
            Console.WriteLine("[dec_agg]=" + D(l.Aggregate(0m, (a, x) => a + x)));
            l.Capacity = 8;
            Console.WriteLine("[dec_cap8]=" + I(l.Capacity));
            Console.WriteLine("[dec_cap8_count]=" + I(l.Count));
            Console.WriteLine("[dec_cap8_agg]=" + D(l.Aggregate(0m, (a, x) => a + x)));
        }

        private static void DecimalInitializer()
        {
            List<decimal> l = new List<decimal> { 1m, 2m };
            Console.WriteLine("[decinit_count]=" + I(l.Count));
            Console.WriteLine("[decinit_idx]=" + D(l[0]) + "/" + D(l[1]));
            Console.WriteLine("[decinit_agg]=" + D(l.Aggregate(0m, (a, x) => a + x)));
            decimal[] arr = l.ToArray();
            Console.WriteLine("[decinit_toarray]=" + I(arr.Length) + "/" + D(arr[0]) + "/" + D(arr[1]));
        }

        private static void DateTimeShape()
        {
            List<DateTime> l = new List<DateTime>();
            l.Add(new DateTime(2020, 1, 2, 3, 4, 5));
            l.Add(new DateTime(1999, 12, 31));
            Console.WriteLine("[dt_new_count]=" + I(l.Count));
            Console.WriteLine("[dt_cap]=" + I(l.Capacity));
            Console.WriteLine("[dt_0_ticks]=" + L(l[0].Ticks));
            Console.WriteLine("[dt_1_ticks]=" + L(l[1].Ticks));
            l[1] = new DateTime(2001, 2, 3, 4, 5, 6);
            Console.WriteLine("[dt_set1_ticks]=" + L(l[1].Ticks));
            Console.WriteLine("[dt_0_kind]=" + I((int)l[0].Kind));
            long sum = 0;
            foreach (DateTime v in l) { sum = sum + v.Ticks; }
            Console.WriteLine("[dt_foreach_ticks]=" + L(sum));
        }

        private static void GuidShape()
        {
            List<Guid> l = new List<Guid>();
            l.Add(new Guid(0x00112233, 0x4455, 0x6677, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff));
            l.Add(new Guid(0x00000000, 0x0000, 0x0000, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01));
            Console.WriteLine("[guid_new_count]=" + I(l.Count));
            Console.WriteLine("[guid_cap]=" + I(l.Capacity));
            Console.WriteLine("[guid_0]=" + l[0].ToString());
            Console.WriteLine("[guid_1]=" + l[1].ToString());
            Guid g = new Guid(0x0a0b0c0d, 0x0102, 0x0304, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c);
            l[1] = g;
            Console.WriteLine("[guid_set1]=" + l[1].ToString());
        }

        private static void TimeSpanShape()
        {
            List<TimeSpan> l = new List<TimeSpan>();
            l.Add(TimeSpan.FromMinutes(90));
            l.Add(TimeSpan.FromSeconds(-5));
            Console.WriteLine("[ts_new_count]=" + I(l.Count));
            Console.WriteLine("[ts_cap]=" + I(l.Capacity));
            Console.WriteLine("[ts_0_ticks]=" + L(l[0].Ticks));
            Console.WriteLine("[ts_1_ticks]=" + L(l[1].Ticks));
            l[1] = TimeSpan.FromHours(2);
            Console.WriteLine("[ts_set1_ticks]=" + L(l[1].Ticks));
        }

        private static void DateTimeOffsetShape()
        {
            List<DateTimeOffset> l = new List<DateTimeOffset>();
            l.Add(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero));
            l.Add(new DateTimeOffset(new DateTime(2000, 2, 29, 12, 0, 0), new TimeSpan(8, 0, 0)));
            Console.WriteLine("[dto_new_count]=" + I(l.Count));
            Console.WriteLine("[dto_cap]=" + I(l.Capacity));
            Console.WriteLine("[dto_0_ticks]=" + L(l[0].Ticks));
            Console.WriteLine("[dto_1_ticks]=" + L(l[1].Ticks));
            Console.WriteLine("[dto_0_offset]=" + L(l[0].Offset.Ticks));
            l[1] = new DateTimeOffset(2001, 6, 7, 8, 9, 10, TimeSpan.Zero);
            Console.WriteLine("[dto_set1_ticks]=" + L(l[1].Ticks));
        }

        public static void Main()
        {
            DecimalShape();
            DecimalInitializer();
            DateTimeShape();
            GuidShape();
            TimeSpanShape();
            DateTimeOffsetShape();
            Console.WriteLine("done");
        }
    }
}
