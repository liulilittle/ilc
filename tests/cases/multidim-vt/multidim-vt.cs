//------------------------------------------------------------------------------
// multidim-vt.cs -- VALUE-TYPE elements in MULTI-DIMENSIONAL arrays.
//
// Contract: `new DateTime[2,2]` / `new decimal[3,2]` / `new Guid[2,3]` /
// `new DateTimeOffset[2,2]` / `new TimeSpan[2,2]` and enum arrays over the
// int/byte/long underlying widths (including a 3-D `[,,]`) must ALLOCATE the
// element stride of the type (the M3 allocation layer previously rejected the
// VALUETYPE element byte loudly) and the per-dim element read/write must move
// the full value:
//   * allocation (newobj ctor fold) -- mdnew value-element channel
//   * element read  (Get)  -- typed value accessor
//   * element write (Set)  -- typed value accessor
//   * Length / GetLength / GetValue / flat traversal
//
// Every printed line is deterministic: fixed ticks, fixed Guids, no culture
// dependent formatting (Guid.ToString is the invariant "D" shape).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public enum E4 { A = 1, B = 2, C = 7 }
    public enum E1 : byte { A = 1, B = 200 }
    public enum E8 : long { A = 1, B = 1L << 40 }

    public static class MultidimVt
    {
        public static void Main()
        {
            // ---- DateTime[2,2]: allocation, write, read, traverse ----
            DateTime[,] dts = new DateTime[2, 2];
            Console.WriteLine("dt len=" + dts.Length);
            Console.WriteLine("dt rank=" + dts.Rank);
            Console.WriteLine("dt g0=" + dts.GetLength(0));
            Console.WriteLine("dt g1=" + dts.GetLength(1));
            dts[0, 0] = new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc);
            dts[1, 1] = new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc);
            dts[0, 1] = dts[0, 0].AddDays(1);
            dts[1, 0] = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Local);
            Console.WriteLine("dt[0,0]=" + dts[0, 0].Ticks);
            Console.WriteLine("dt[0,0].kind=" + (int)dts[0, 0].Kind);
            Console.WriteLine("dt[0,1]=" + dts[0, 1].Ticks);
            Console.WriteLine("dt[1,0].kind=" + (int)dts[1, 0].Kind);
            Console.WriteLine("dt[1,1]=" + dts[1, 1].Ticks);
            Console.WriteLine("dt lt=" + (dts[1, 1] < dts[0, 0]));
            long dtSum = 0;
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    dtSum += dts[i, j].Ticks;
            Console.WriteLine("dt sum=" + dtSum);

            // ---- TimeSpan[2,2] ----
            TimeSpan[,] tss = new TimeSpan[2, 2];
            tss[0, 0] = new TimeSpan(1, 2, 3, 4, 5);
            tss[1, 1] = TimeSpan.FromTicks(-1234567890L);
            tss[0, 1] = tss[0, 0].Add(TimeSpan.FromMinutes(30));
            Console.WriteLine("ts[0,0]=" + tss[0, 0].Ticks);
            Console.WriteLine("ts[0,1]=" + tss[0, 1].Ticks);
            Console.WriteLine("ts[1,1]=" + tss[1, 1].Ticks);
            Console.WriteLine("ts cmp=" + (tss[1, 1] < tss[0, 0]));

            // ---- decimal[3,2] ----
            decimal[,] decs = new decimal[3, 2];
            Console.WriteLine("dec len=" + decs.Length);
            Console.WriteLine("dec g0=" + decs.GetLength(0));
            Console.WriteLine("dec zero=" + (decs[2, 1] == 0m));
            decs[0, 0] = 1.5m;
            decs[2, 1] = decimal.MaxValue;
            decs[1, 0] = decs[0, 0] + decs[0, 0];
            Console.WriteLine("dec[0,0]=" + decs[0, 0].ToString());
            Console.WriteLine("dec[1,0]=" + decs[1, 0].ToString());
            Console.WriteLine("dec[2,1] max=" + (decs[2, 1] == decimal.MaxValue));
            Console.WriteLine("dec cmp=" + (decs[0, 0] < decs[1, 0]));

            // ---- Guid[2,3] ----
            // (Guid construction uses the Parse static: the `new Guid(string)`
            // ldloca+call ctor shape carries a pre-existing fold-stack
            // imbalance (4f1cde64) that this case must not trip -- the
            // multidim surface under test is the allocation + accessors.)
            Guid[,] gs = new Guid[2, 3];
            Console.WriteLine("guid len=" + gs.Length);
            Guid g0 = Guid.Parse("0aa79e57-1c2b-4d3e-9f40-112233445566");
            Guid g1 = Guid.Parse("01020304-0506-0708-090a-0b0c0d0e0f10");
            gs[0, 0] = g0;
            gs[1, 2] = g1;
            Console.WriteLine("guid zero=" + (gs[0, 2] == Guid.Empty));
            Console.WriteLine("guid[0,0]=" + gs[0, 0].ToString());
            Console.WriteLine("guid[1,2]=" + gs[1, 2].ToString());
            Console.WriteLine("guid cmp=" + gs[0, 0].CompareTo(gs[1, 2]));
            gs[0, 0] = gs[1, 2];
            Console.WriteLine("guid store eq=" + (gs[0, 0] == g1));

            // ---- DateTimeOffset[2,2] ----
            DateTimeOffset[,] dto = new DateTimeOffset[2, 2];
            dto[0, 0] = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
            dto[1, 1] = new DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.FromHours(-5));
            Console.WriteLine("dto[0,0].utc=" + dto[0, 0].UtcTicks);
            Console.WriteLine("dto[0,0].off=" + dto[0, 0].Offset.Ticks);
            Console.WriteLine("dto[1,1].utc=" + dto[1, 1].UtcTicks);
            Console.WriteLine("dto[1,1].off=" + dto[1, 1].Offset.Ticks);
            dto[0, 1] = dto[0, 0];
            Console.WriteLine("dto store eq=" + (dto[0, 1] == dto[0, 0]));
            Console.WriteLine("dto ne=" + (dto[0, 1] != dto[1, 1]));

            // ---- enum[2,2] over the int underlying width ----
            E4[,] e4 = new E4[2, 2];
            e4[0, 0] = E4.A;
            e4[1, 1] = E4.C;
            e4[0, 1] = e4[1, 1];
            Console.WriteLine("e4 len=" + e4.Length);
            Console.WriteLine("e4[0,0]=" + (int)e4[0, 0]);
            Console.WriteLine("e4[0,1]=" + (int)e4[0, 1]);
            Console.WriteLine("e4[1,1]=" + (int)e4[1, 1]);
            Console.WriteLine("e4 eq=" + (e4[0, 1] == E4.C));
            int e4Sum = 0;
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    e4Sum += (int)e4[i, j];
            Console.WriteLine("e4 sum=" + e4Sum);

            // ---- enum[2,2] over the byte underlying width ----
            E1[,] e1 = new E1[2, 2];
            e1[0, 0] = E1.A;
            e1[1, 1] = E1.B;
            Console.WriteLine("e1[0,0]=" + (int)e1[0, 0]);
            Console.WriteLine("e1[1,1]=" + (int)e1[1, 1]);
            Console.WriteLine("e1 eq=" + (e1[1, 1] == E1.B));

            // ---- enum[2,2] over the long underlying width ----
            E8[,] e8 = new E8[2, 2];
            e8[0, 0] = E8.A;
            e8[1, 1] = E8.B;
            Console.WriteLine("e8[0,0]=" + (long)e8[0, 0]);
            Console.WriteLine("e8[1,1]=" + (long)e8[1, 1]);
            Console.WriteLine("e8 eq=" + (e8[1, 1] == E8.B));

            // ---- enum[2,2,2] THREE-dimensional allocation + traversal ----
            E4[,,] e3 = new E4[2, 2, 2];
            Console.WriteLine("e3 len=" + e3.Length);
            Console.WriteLine("e3 rank=" + e3.Rank);
            e3[0, 0, 0] = E4.B;
            e3[1, 1, 1] = E4.C;
            Console.WriteLine("e3[0,0,0]=" + (int)e3[0, 0, 0]);
            Console.WriteLine("e3[1,1,1]=" + (int)e3[1, 1, 1]);
            Console.WriteLine("e3 zero=" + (int)e3[0, 1, 0]);

            // ---- decimal[2,2,2] THREE-dimensional value allocation ----
            decimal[,,] d3 = new decimal[2, 2, 2];
            d3[1, 0, 1] = 2.5m;
            Console.WriteLine("d3 len=" + d3.Length);
            Console.WriteLine("d3[1,0,1]=" + d3[1, 0, 1].ToString());
            Console.WriteLine("d3 zero=" + (d3[0, 0, 0] == 0m));

            // ---- initializer-shaped allocation `new T[a,b] {{...}}` ----
            decimal[,] decInit =
            {
                { 1.5m, 2.5m },
                { 3.5m, 4.5m },
            };
            Console.WriteLine("decInit[0,1]=" + decInit[0, 1].ToString());
            Console.WriteLine("decInit[1,0]=" + decInit[1, 0].ToString());
            Console.WriteLine("decInit len=" + decInit.Length);

            DateTime[,] dtInit =
            {
                { new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc), new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc) },
                { new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc), new DateTime(2010, 6, 7, 8, 9, 10, DateTimeKind.Utc) },
            };
            Console.WriteLine("dtInit[0,0]=" + dtInit[0, 0].Ticks);
            Console.WriteLine("dtInit[0,1]=" + dtInit[0, 1].Ticks);
            Console.WriteLine("dtInit[1,0]=" + dtInit[1, 0].Ticks);
            Console.WriteLine("dtInit[1,1]=" + dtInit[1, 1].Ticks);

            E4[,] e4Init =
            {
                { E4.A, E4.B },
                { E4.C, E4.A },
            };
            Console.WriteLine("e4Init[0,1]=" + (int)e4Init[0, 1]);
            Console.WriteLine("e4Init[1,0]=" + (int)e4Init[1, 0]);
        }
    }
}
