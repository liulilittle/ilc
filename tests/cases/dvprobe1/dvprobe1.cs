// dvprobe1.cs -- delegate Invoke edge shapes for VALUE-TYPE generic args.
//
// delegvalue covers the common shapes. This probe pushes the remaining
// statically-analyzable ones so a LOUD reject (or a silent ABI tear) is
// visible in the differential run:
//   * a Guid RETURN through a delegate (Func<Guid,Guid>, Func<int,Guid>)
//   * a delegate stored in a field / passed as an argument and invoked there
//   * a delegate whose composition is built by Delegate.Combine explicitly
//   * Action<decimal> (void return, 16-byte param)
//   * Func<DateTime,TimeSpan> (two different 8-byte structs)
//   * a multicast chain where the LAST leaf is an instance method
using System;

namespace Cases
{
    public class Vals
    {
        public int Bias;
        public Vals(int b) { Bias = b; }

        public Guid MakeGuid(int seed)
        {
            return Guid.Parse("00000000-0000-0000-0000-0000000000" + (seed + Bias).ToString("00"));
        }

        public TimeSpan ToSpan(DateTime d) { return TimeSpan.FromTicks(d.Ticks + Bias); }

        public int MakeGuidLen(Guid g) { return g.ToString().Length + Bias; }
    }

    public static class DelegateValueProbe
    {
        private static Guid Zero()
        {
            return Guid.Parse("00000000-0000-0000-0000-000000000000");
        }

        private static Guid Echo(Guid g) { return g; }

        private static Guid FromInt(int v)
        {
            return Guid.Parse("00000000-0000-0000-0000-0000000000" + v.ToString("00"));
        }

        private static void DecSink(decimal d) { Console.WriteLine("p_dec_sink=" + d.ToString()); }

        private static TimeSpan Span(DateTime d) { return TimeSpan.FromTicks(d.Ticks); }

        private static TimeSpan SpanPlus(DateTime d) { return TimeSpan.FromTicks(d.Ticks + 1); }

        private static int Run(Func<Guid, Guid> f, Guid arg)
        {
            return f(arg).ToString().Length;
        }

        public static void Main()
        {
            Guid one = Guid.Parse("00000000-0000-0000-0000-000000000001");
            Vals v = new Vals(3);

            // ---- Guid RETURN through a delegate ----
            Func<Guid> gz = Zero;
            Console.WriteLine("p_gret_zero=" + gz().ToString());

            Func<Guid, Guid> ge = Echo;
            Console.WriteLine("p_gret_echo=" + ge(one).ToString());

            Func<int, Guid> gf = FromInt;
            Console.WriteLine("p_gret_fromint=" + gf(7).ToString());

            // ---- Guid return through a MULTICAST (last leaf wins) ----
            Func<Guid, Guid> gem = Echo;
            gem += Echo;
            Console.WriteLine("p_gret_multi=" + gem(one).ToString());

            // ---- delegate passed as an argument, invoked in the callee ----
            Console.WriteLine("p_gret_asarg=" + Run(Echo, one));

            // ---- delegate held in a field ----
            Holder h = new Holder(Echo);
            Console.WriteLine("p_gret_field=" + h.Call(one).ToString());

            // ---- explicit Delegate.Combine composition ----
            Func<Guid, int> r1 = GuidLen;
            Func<Guid, int> r2 = GuidLen2;
            Func<Guid, int> rcomb = (Func<Guid, int>)Delegate.Combine(r1, r2);
            Console.WriteLine("p_combine=" + rcomb(one));

            // ---- Action<decimal> (void return, 16-byte param) ----
            Action<decimal> ad = DecSink;
            ad(2.5m);
            ad += DecSink;
            ad(3.5m);

            // ---- Func<DateTime,TimeSpan> (two different struct types) ----
            DateTime d0 = new DateTime(2021, 3, 4, 5, 6, 7);
            Func<DateTime, TimeSpan> fs = Span;
            Console.WriteLine("p_dt_ts_direct=" + fs(d0).Ticks);
            fs += SpanPlus;
            Console.WriteLine("p_dt_ts_multi=" + fs(d0).Ticks);
            Func<DateTime, TimeSpan> fsm = Span;
            fsm += v.ToSpan;
            Console.WriteLine("p_dt_ts_mixed=" + fsm(d0).Ticks);

            // ---- multicast chain ending in an INSTANCE method ----
            Func<Guid, int> rm = GuidLen;
            rm += v.MakeGuidLen;
            Console.WriteLine("p_last_instance=" + rm(one));
        }

        private static int GuidLen(Guid g) { return g.ToString().Length; }
        private static int GuidLen2(Guid g) { return g.ToString().Length + 100; }
    }

    public class Holder
    {
        private readonly Func<Guid, Guid> _f;
        public Holder(Func<Guid, Guid> f) { _f = f; }
        public Guid Call(Guid g) { return _f(g); }
    }
}
