using System;
using System.Globalization;

class ProbeConvDt
{
    static void W(string k, object v) { Console.WriteLine(k + "=" + v); }

    static void Main()
    {
        Console.WriteLine("=== ToDateTime(string) ===");
        W("dt_basic", Convert.ToDateTime("2020-01-02T03:04:05").ToString("o"));
        W("dt_date", Convert.ToDateTime("2020-01-02").ToString("o"));
        W("dt_slash", Convert.ToDateTime("01/02/2020").ToString("o"));
        W("dt_null", Convert.ToDateTime(null).ToString("o"));
        W("dt_strp", Convert.ToDateTime("2020-01-02T03:04:05", CultureInfo.InvariantCulture).ToString("o"));

        Console.WriteLine("=== ToDateTime(numeric) -> InvalidCastException ===");
        try { Convert.ToDateTime(0); } catch (Exception e) { W("dt_i32", e.GetType().Name); }
        try { Convert.ToDateTime(0L); } catch (Exception e) { W("dt_i64", e.GetType().Name); }
        try { Convert.ToDateTime(0.0); } catch (Exception e) { W("dt_d", e.GetType().Name); }
        try { Convert.ToDateTime(0m); } catch (Exception e) { W("dt_dec", e.GetType().Name); }
        try { Convert.ToDateTime(true); } catch (Exception e) { W("dt_bool", e.GetType().Name); }
        try { Convert.ToDateTime((byte)0); } catch (Exception e) { W("dt_byte", e.GetType().Name); }
        try { Convert.ToDateTime((short)0); } catch (Exception e) { W("dt_i16", e.GetType().Name); }
        try { Convert.ToDateTime((ushort)0); } catch (Exception e) { W("dt_u16", e.GetType().Name); }
        try { Convert.ToDateTime((uint)0); } catch (Exception e) { W("dt_u32", e.GetType().Name); }
        try { Convert.ToDateTime((ulong)0); } catch (Exception e) { W("dt_u64", e.GetType().Name); }
        try { Convert.ToDateTime(0f); } catch (Exception e) { W("dt_s", e.GetType().Name); }
        try { Convert.ToDateTime('A'); } catch (Exception e) { W("dt_char", e.GetType().Name); }

        Console.WriteLine("=== ToDateTime(DateTime) identity ===");
        W("dt_dt", Convert.ToDateTime(new DateTime(2020, 1, 2, 3, 4, 5)).ToString("o"));

        Console.WriteLine("=== ToDateTime(object) ===");
        try { Convert.ToDateTime(new object()); } catch (Exception e) { W("dt_obj", e.GetType().Name); }
        try { Convert.ToDateTime(DBNull.Value); } catch (Exception e) { W("dt_dbnull", e.GetType().Name); }

        Console.WriteLine("=== IsDBNull ===");
        W("dbnull", Convert.IsDBNull(DBNull.Value));
        W("dbnull_obj", Convert.IsDBNull(new object()));
        W("dbnull_null", Convert.IsDBNull(null));

    }
}