using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

static class Program
{
    static int failures = 0;
    static void Check(string name, bool ok)
    {
        if (!ok) { failures++; Console.WriteLine(string.Concat("FAIL ", name)); }
        else Console.WriteLine(string.Concat("PASS ", name));
    }

    static void Main()
    {
        // ---- foreach over string ----
        int count = 0;
        foreach (char c in "abc") { if (c >= 'a' && c <= 'z') count++; }
        Check("foreach.string", count == 3);

        // ---- string concat with number ----
        string msg = string.Concat("val=", 42.ToString());
        Check("str.concat.num", msg == "val=42");

        // ---- LINQ basics on arrays ----
        var nums = new[] { 5, 3, 8, 1, 9, 2 };
        var sorted = nums.OrderBy(x => x).ToArray();
        Check("linq.orderby", sorted[0] == 1 && sorted[5] == 9);
        var filtered = nums.Where(x => x > 4).ToArray();
        Check("linq.where", filtered.Length == 3);
        var mapped = nums.Select(x => x * 2).ToArray();
        Check("linq.select", mapped[0] == 10);
        Check("linq.first", nums.First(x => x > 7) == 8);
        Check("linq.sum", nums.Sum() == 28);
        Check("linq.minmax", nums.Min() == 1 && nums.Max() == 9);
        Check("linq.count", nums.Count(x => x > 4) == 3);
        Check("linq.any", nums.Any(x => x == 8));
        Check("linq.all", nums.All(x => x > 0));

        // ---- Exception handling ----
        try { int.Parse("notanumber"); Check("exc.parse", false); }
        catch (FormatException) { Check("exc.parse", true); }
        try { throw new InvalidOperationException("test"); }
        catch (InvalidOperationException ex) { Check("exc.msg", ex.Message == "test"); }

        // ---- StringBuilder chain ----
        var sb = new StringBuilder();
        sb.Append("Hello").Append(',').Append(' ').AppendFormat("{0}", 42);
        sb.Insert(0, ">> ");
        sb.Remove(0, 3);
        sb.Replace("42", "ILC");
        Check("sb.chain", sb.ToString() == "Hello, ILC");

        Console.WriteLine(failures == 0 ? "ALL-PASS" : string.Concat("FAILURES=", failures.ToString()));
    }
}
