using System;
using System.Data;

namespace Cases
{
    // DataTable.Compute aggregate RESULT TYPES, per column DataType.
    //
    // net48 (measured against .NET Framework 4.8, one process per probe):
    //   int column     MIN/MAX Int32, SUM Int64, AVG Int32 (INTEGER division),
    //                  COUNT Int32
    //   double column  MIN/MAX/SUM/AVG Double, COUNT Int32
    //   decimal column MIN/MAX/SUM/AVG Decimal, COUNT Int32
    //   empty/all-DBNull  MIN/MAX/SUM/AVG DBNull, COUNT Int32 0
    //
    // Before the fix every numeric aggregate was a boxed Double -- a type
    // net48 never produces, and the wrong VALUE for an int average.
    public static class DataTableAgg1
    {
        static string TypeName(object v)
        {
            if (v == null) return "<null>";
            if (v == DBNull.Value) return "DBNull";
            return v.GetType().Name;
        }

        static string Text(object v)
        {
            if (v == null) return "<null>";
            if (v == DBNull.Value) return "<DBNull>";
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        static void Row(string tag, DataTable dt, string expr)
        {
            try
            {
                object r = dt.Compute(expr, null);
                Console.WriteLine("[" + tag + "]type=" + TypeName(r) + " value=" + Text(r));
            }
            catch (Exception e)
            {
                Console.WriteLine("[" + tag + "]EX " + e.GetType().Name);
            }
        }

        static DataTable Typed(Type columnType, params object[] values)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("x", columnType);
            for (int i = 0; i < values.Length; i++) dt.Rows.Add(new object[] { values[i] });
            return dt;
        }

        public static void Main()
        {
            foreach (string fn in new[] { "MIN", "MAX", "SUM", "AVG", "COUNT" })
            {
                Row("int." + fn, Typed(typeof(int), 1, 2), fn + "(x)");
                Row("double." + fn, Typed(typeof(double), 1.5, 2.5), fn + "(x)");
                Row("decimal." + fn, Typed(typeof(decimal), 1.5m, 2.5m), fn + "(x)");
                Row("empty." + fn, Typed(typeof(int)), fn + "(x)");
            }

            // int AVG is INTEGER division: truncation, not rounding.
            Row("intavg.third", Typed(typeof(int), 1, 2, 2), "AVG(x)");
            Row("intavg.neg", Typed(typeof(int), -1, 0), "AVG(x)");
            // int SUM is Int64: it survives an int32 overflow.
            Row("intsum.overflow", Typed(typeof(int), 2000000000, 2000000000), "SUM(x)");
            // DBNull cells are skipped by every aggregate.
            Row("dbnull.sum", Typed(typeof(int), 1, DBNull.Value, 2), "SUM(x)");
            Row("dbnull.count", Typed(typeof(int), 1, DBNull.Value, 2), "COUNT(x)");
            Row("dbnullonly.sum", Typed(typeof(int), DBNull.Value, DBNull.Value), "SUM(x)");

            // The typed-column surface itself.
            DataTable viaColumn = new DataTable();
            viaColumn.Columns.Add(new DataColumn("x", typeof(decimal)));
            viaColumn.Rows.Add(new object[] { 1.5m });
            viaColumn.Rows.Add(new object[] { 2.5m });
            Row("datacolumn.sum", viaColumn, "SUM(x)");
            Console.WriteLine("[datacolumn.type]=" + viaColumn.Columns[0].DataType.Name);

            DataTable viaSetter = new DataTable();
            viaSetter.Columns.Add("x");
            viaSetter.Columns[0].DataType = typeof(double);
            viaSetter.Rows.Add(new object[] { 1.5 });
            viaSetter.Rows.Add(new object[] { 2.5 });
            Row("setter.avg", viaSetter, "AVG(x)");
            Console.WriteLine("[setter.type]=" + viaSetter.Columns[0].DataType.Name);

            // A column with no declared type is net48's String column: MIN/MAX
            // answer the value text, SUM/AVG throw DataException.
            DataTable untyped = new DataTable();
            untyped.Columns.Add("x");
            untyped.Rows.Add(new object[] { 1 });
            untyped.Rows.Add(new object[] { 2 });
            Row("untyped.min", untyped, "MIN(x)");
            Row("untyped.sum", untyped, "SUM(x)");
            Row("untyped.avg", untyped, "AVG(x)");
            Row("untyped.count", untyped, "COUNT(x)");
            Console.WriteLine("[untyped.type]=" + untyped.Columns[0].DataType.Name);

            // A filter narrows the rows without changing the result type.
            DataTable filtered = new DataTable();
            filtered.Columns.Add("x", typeof(int));
            filtered.Columns.Add("y", typeof(int));
            filtered.Rows.Add(new object[] { 1, 0 });
            filtered.Rows.Add(new object[] { 2, 5 });
            filtered.Rows.Add(new object[] { 4, 5 });
            try
            {
                object r = filtered.Compute("SUM(x)", "y > 1");
                Console.WriteLine("[filter.sum]type=" + TypeName(r) + " value=" + Text(r));
            }
            catch (Exception e) { Console.WriteLine("[filter.sum]EX " + e.GetType().Name); }
            try
            {
                object r = filtered.Compute("AVG(x)", "y > 1");
                Console.WriteLine("[filter.avg]type=" + TypeName(r) + " value=" + Text(r));
            }
            catch (Exception e) { Console.WriteLine("[filter.avg]EX " + e.GetType().Name); }
            try
            {
                object r = filtered.Compute("COUNT(x)", "y > 100");
                Console.WriteLine("[filter.count.none]type=" + TypeName(r) + " value=" + Text(r));
            }
            catch (Exception e) { Console.WriteLine("[filter.count.none]EX " + e.GetType().Name); }
        }
    }
}
