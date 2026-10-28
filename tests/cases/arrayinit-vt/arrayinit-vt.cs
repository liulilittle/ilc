//------------------------------------------------------------------------------
// arrayinit-vt.cs -- VALUE-TYPE array elements in an array initializer.
//
// Contract: an array initializer over the value-type element families must
// allocate the ELEMENT stride of the type and the element read/store must move
// the full value:
//   * DateTimeOffset[] (16-byte runtime value: utcTicks + offsetMinutes)
//   * Guid[]           (16-byte raw value)
//   * DateTime[]       (8-byte CLR packed cell image)
//   * TimeSpan[]       (8-byte ticks)
//   * decimal[]        (16-byte decimal image; regression anchor)
//   * enum[] over the int / byte / long underlying widths
// then read/store/compare the initialized elements (an element must survive the
// ldelem/stelem round trip unchanged, including the DateTimeOffset offset and
// the Guid byte order).
//
// Every printed line is deterministic: no NewGuid, no current time, no culture
// dependent formatting (Guid.ToString is the invariant "D" shape).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class ArrayInitVt
    {
        private enum E4 { A = 1, B = 2, C = 7 }
        private enum E1 : byte { A = 1, B = 200 }
        private enum E8 : long { A = 1, B = 1L << 40 }

        public static void Main()
        {
            // ---- DateTimeOffset[]: initializer, read, store, compare ----
            DateTimeOffset[] dto =
            {
                new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)),
                new DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.FromHours(-5)),
            };
            Console.WriteLine("dto len=" + dto.Length);
            Console.WriteLine("dto[0].ticks=" + dto[0].Ticks);
            Console.WriteLine("dto[0].utc=" + dto[0].UtcTicks);
            Console.WriteLine("dto[0].off=" + dto[0].Offset.Ticks);
            Console.WriteLine("dto[1].ticks=" + dto[1].Ticks);
            Console.WriteLine("dto[1].utc=" + dto[1].UtcTicks);
            Console.WriteLine("dto[1].off=" + dto[1].Offset.Ticks);
            Console.WriteLine("dto eq=" + (dto[0] == dto[1]));
            dto[1] = dto[0];
            Console.WriteLine("dto store eq=" + (dto[0] == dto[1]));
            Console.WriteLine("dto store ticks=" + dto[1].Ticks);
            dto[0] = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);
            Console.WriteLine("dto overwrite utc=" + dto[0].UtcTicks);
            Console.WriteLine("dto overwrite off=" + dto[0].Offset.Ticks);
            Console.WriteLine("dto ne=" + (dto[0] != dto[1]));

            // ---- Guid[]: initializer, read, store, compare ----
            Guid g0 = new Guid("0aa79e57-1c2b-4d3e-9f40-112233445566");
            Guid g1 = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
            Guid[] gs = { g0, g1, Guid.Empty };
            Console.WriteLine("guid len=" + gs.Length);
            Console.WriteLine("guid[0]=" + gs[0].ToString());
            Console.WriteLine("guid[1]=" + gs[1].ToString());
            Console.WriteLine("guid[0]==g0=" + (gs[0] == g0));
            Console.WriteLine("guid[2]==empty=" + (gs[2] == Guid.Empty));
            Console.WriteLine("guid cmp=" + gs[0].CompareTo(gs[1]));
            gs[2] = gs[0];
            Console.WriteLine("guid store eq=" + (gs[2] == g0));
            gs[1] = Guid.Empty;
            Console.WriteLine("guid overwrite empty=" + (gs[1] == Guid.Empty));

            // ---- DateTime[]: initializer, read, store, compare ----
            DateTime[] dts =
            {
                new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc),
                new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            };
            Console.WriteLine("dt[0]=" + dts[0].Ticks);
            Console.WriteLine("dt[0].kind=" + (int)dts[0].Kind);
            Console.WriteLine("dt[1]=" + dts[1].Ticks);
            Console.WriteLine("dt lt=" + (dts[1] < dts[0]));
            dts[1] = dts[0];
            Console.WriteLine("dt store eq=" + (dts[0] == dts[1]));
            dts[0] = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Local);
            Console.WriteLine("dt overwrite kind=" + (int)dts[0].Kind);
            Console.WriteLine("dt overwrite ticks=" + dts[0].Ticks);

            // ---- TimeSpan[]: initializer, read, store, compare ----
            TimeSpan[] tss =
            {
                new TimeSpan(1, 2, 3, 4, 5),
                TimeSpan.FromTicks(-1234567890L),
            };
            Console.WriteLine("ts[0]=" + tss[0].Ticks);
            Console.WriteLine("ts[1]=" + tss[1].Ticks);
            Console.WriteLine("ts cmp=" + (tss[1] < tss[0]));
            tss[1] = tss[0];
            Console.WriteLine("ts store eq=" + (tss[0] == tss[1]));

            // ---- decimal[]: initializer, read, store, compare (regression) ----
            decimal[] decs = { 1.5m, 2.5m, decimal.MaxValue };
            Console.WriteLine("dec[0]=" + decs[0].ToString());
            Console.WriteLine("dec[1]=" + decs[1].ToString());
            Console.WriteLine("dec[2] max=" + (decs[2] == decimal.MaxValue));
            decs[2] = decs[0] + decs[1];
            Console.WriteLine("dec store=" + decs[2].ToString());
            Console.WriteLine("dec cmp=" + (decs[0] < decs[1]));

            // ---- enum[]: int / byte / long underlying widths ----
            E4[] e4 = { E4.A, E4.C, E4.B };
            Console.WriteLine("e4 len=" + e4.Length);
            Console.WriteLine("e4[1]=" + (int)e4[1]);
            Console.WriteLine("e4[0]==A=" + (e4[0] == E4.A));
            e4[0] = E4.B;
            Console.WriteLine("e4 store=" + (int)e4[0]);
            Console.WriteLine("e4 sortfree=" + ((int)e4[0] + (int)e4[1] + (int)e4[2]));

            E1[] e1 = { E1.A, E1.B };
            Console.WriteLine("e1 len=" + e1.Length);
            Console.WriteLine("e1[1]=" + (int)e1[1]);
            e1[0] = E1.B;
            Console.WriteLine("e1 store=" + (int)e1[0]);
            Console.WriteLine("e1 eq=" + (e1[0] == e1[1]));

            E8[] e8 = { E8.A, E8.B };
            Console.WriteLine("e8 len=" + e8.Length);
            Console.WriteLine("e8[1]=" + (long)e8[1]);
            e8[0] = E8.B;
            Console.WriteLine("e8 store=" + (long)e8[0]);
            Console.WriteLine("e8 eq=" + (e8[0] == e8[1]));

            // ---- a 3-element DateTimeOffset array with an element overwrite ----
            DateTimeOffset[] three =
            {
                new DateTimeOffset(2010, 6, 7, 8, 9, 10, TimeSpan.FromMinutes(330)),
                new DateTimeOffset(2011, 6, 7, 8, 9, 10, TimeSpan.FromMinutes(-330)),
                new DateTimeOffset(2012, 6, 7, 8, 9, 10, TimeSpan.Zero),
            };
            Console.WriteLine("three len=" + three.Length);
            Console.WriteLine("three utc sum=" + (three[0].UtcTicks + three[1].UtcTicks + three[2].UtcTicks));
            Console.WriteLine("three off sum=" + (three[0].Offset.Ticks + three[1].Offset.Ticks + three[2].Offset.Ticks));
            three[0] = three[2];
            Console.WriteLine("three store eq=" + (three[0] == three[2]));
            Console.WriteLine("three untouched=" + three[1].UtcTicks);
        }
    }
}