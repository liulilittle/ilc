using System;
using System.Data;
namespace Cases { public static class DataTableCase {
    public static void Main() {
        DataTable dt = new DataTable();
        dt.Columns.Add("Name");
        dt.Columns.Add("Age");
        Console.WriteLine(dt.Columns.Count);
        dt.Rows.Add(new object[] { "Alice", 30 });
        dt.Rows.Add(new object[] { "Bob", 25 });
        dt.Rows.Add(new object[] { "Carol", 40 });
        Console.WriteLine(dt.Rows.Count);
        DataRow r0 = dt.Rows[0];
        Console.WriteLine(r0[0]);
        Console.WriteLine(r0[1]);
        Console.WriteLine(dt.Rows[1][0]);
        r0[1] = 31;
        Console.WriteLine(r0[1]);
        Console.WriteLine(dt.Columns[0].ColumnName);
        Console.WriteLine(dt.Columns[1].ColumnName);
        dt.Rows.RemoveAt(1);
        Console.WriteLine(dt.Rows.Count);
        Console.WriteLine(dt.Rows[1][0]);
        DataRow carol = dt.Rows[1];
        dt.Rows.Remove(carol);
        Console.WriteLine(dt.Rows.Count);
        Console.WriteLine(dt.Rows[0][0]);
        dt.Rows.Add(new object[] { "Dave", 50 });
        dt.Rows.Add(new object[] { "Eve", 55 });
        Console.WriteLine(dt.Rows.Count);
        dt.Clear();
        Console.WriteLine(dt.Rows.Count);
        Console.WriteLine(dt.Columns.Count);
        dt.Rows.Add(new object[] { "After", 1 });
        Console.WriteLine(dt.Rows.Count);
        Console.WriteLine(dt.Rows[0][0]);
        DataRow nr = dt.NewRow();
        nr[0] = "NewRow1";
        nr[1] = 100;
        dt.Rows.Add(nr);
        Console.WriteLine(dt.Rows.Count);
        Console.WriteLine(dt.Rows[1][0]);
        DataRow nr2 = dt.NewRow();
        nr2.ItemArray = new object[] { "Items", 200 };
        dt.Rows.Add(nr2);
        object[] ia = dt.Rows[2].ItemArray;
        Console.WriteLine(ia[0]);
        Console.WriteLine(ia[1]);
        DataRow[] sel = dt.Select("Name = 'After'");
        Console.WriteLine(sel.Length);
        Console.WriteLine(sel[0][1]);
        DataRow[] sel2 = dt.Select("Age > 100");
        Console.WriteLine(sel2.Length);
        Console.WriteLine(sel2[0][0]);
        DataRow[] sel3 = dt.Select("Age <> 1");
        Console.WriteLine(sel3.Length);
        DataRow[] sel4 = dt.Select("Age <= 100");
        Console.WriteLine(sel4.Length);
        // ADO stage 3: AND join
        DataRow[] sel5 = dt.Select("Age >= 1 AND Name = 'After'");
        Console.WriteLine(sel5.Length);
        Console.WriteLine(sel5[0][0]);
        DataRow[] sel6 = dt.Select("Age <= 100 AND Name = 'Items'");
        Console.WriteLine(sel6.Length);
        DataRow[] sel7 = dt.Select("Age > 100 AND Age < 300");
        Console.WriteLine(sel7.Length);
        // ADO stage 3: Compute -- COUNT (net48: the Age column is untyped
        // (String), so SUM/AVG/MIN/MAX throw DataException there; the ILC
        // numeric aggregates are covered by the documented deviation).
        Console.WriteLine(dt.Compute("COUNT(Age)", ""));
        Console.WriteLine(dt.Compute("COUNT(Age)", "Name = 'After'"));
        // ADO stage 3: Select with sort
        DataRow[] srt = dt.Select("", "Age ASC");
        Console.WriteLine(srt.Length);
        Console.WriteLine(srt[0][0]);
        Console.WriteLine(srt[2][0]);
        DataRow[] srt2 = dt.Select("", "Age DESC");
        Console.WriteLine(srt2[0][0]);
        DataRow[] srt3 = dt.Select("Name <> 'After'", "Name ASC");
        Console.WriteLine(srt3.Length);
        Console.WriteLine(srt3[0][0]);
        // ADO stage 3: Clone
        DataTable clone = dt.Clone();
        Console.WriteLine(clone.Columns.Count);
        Console.WriteLine(clone.Rows.Count);
        clone.Rows.Add(new object[] { "Cloned", 7 });
        Console.WriteLine(clone.Rows.Count);
        Console.WriteLine(clone.Rows[0][0]);
        Console.WriteLine(dt.Rows.Count);
        DataSet ds = new DataSet();
        Console.WriteLine(ds.Tables.Count);
        ds.Tables.Add(dt);
        DataTable dt2 = new DataTable();
        dt2.Columns.Add("X");
        ds.Tables.Add(dt2);
        Console.WriteLine(ds.Tables.Count);
        DataTable t0 = ds.Tables[0];
        Console.WriteLine(t0.Rows.Count);
        Console.WriteLine(ds.Tables[1].Columns.Count);
        // ADO stage 3: TableName + DataRow.Table
        dt.TableName = "People";
        Console.WriteLine(dt.TableName);
        DataTable named = ds.Tables[0];
        Console.WriteLine(named.TableName);
        DataRow someRow = dt.Rows[0];
        Console.WriteLine(someRow.Table == dt);
        Console.WriteLine(someRow.Table.Rows.Count);
    }
}}
