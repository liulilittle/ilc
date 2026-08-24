using System;
using System.Text;

// bcldeep -- D5 coverage deepening: exercises BCL icalls that were wired in
// MethodMap but never observed in any e2e fixture's generated code (the
// wired-but-never-run list). Self-asserting booleans; every line is True.
// Culture-safe (no culture-sensitive numeric formats; invariant forms only).
public static class Program
{
    static int s_failures;

    static void Check(bool cond, string what)
    {
        Console.WriteLine((cond ? "True" : "False") + " " + what);
        if (!cond) s_failures++;
    }

    public static int Main()
    {
        // --- DateTime operations / comparisons (datetime_* icall family) ---
        DateTime d0 = new DateTime(2024, 2, 29, 12, 34, 56, DateTimeKind.Utc);
        Check(d0.Year == 2024 && d0.Month == 2 && d0.Day == 29, "dt ctor basic");
        Check(d0.AddMonths(12).Year == 2025, "dt AddMonths");
        Check(d0.AddMilliseconds(1000).Second == 57, "dt AddMilliseconds");
        Check(d0.AddSeconds(5).Minute == 35, "dt AddSeconds");
        Check(d0.AddYears(1).Year == 2025, "dt AddYears");
        Check(d0.DayOfYear == 60, "dt DayOfYear");
        Check(DateTime.IsLeapYear(2024) && !DateTime.IsLeapYear(2025), "dt IsLeapYear");
        Check(DateTime.DaysInMonth(2024, 2) == 29 && DateTime.DaysInMonth(2025, 2) == 28, "dt DaysInMonth");
        Check(d0.TimeOfDay == new TimeSpan(12, 34, 56), "dt TimeOfDay");
        Check(d0.Date.Day == 29 && d0.Date.Hour == 0, "dt Date");
        Check(d0.Kind == DateTimeKind.Utc, "dt Kind");
        DateTime d1 = d0.AddDays(1);
        Check(d1 > d0 && d0 < d1 && d0 != d1, "dt operators");
        Check(d0.AddDays(0) == d0 && d1 <= d0.AddDays(2) && d1 >= d0, "dt op eq/le/ge");
        Check(d0.AddDays(1).Ticks == d0.Ticks + TimeSpan.TicksPerDay, "dt from ticks");
        Check(DateTime.SpecifyKind(d0, DateTimeKind.Local).Ticks == d0.Ticks, "dt SpecifyKind preserves the instant (single-zone model)");
        DateTime fut = DateTime.FromFileTimeUtc(128858047392543744L);
        Check(fut.Year == 2009 && fut.ToFileTimeUtc() == 128858047392543744L, "dt filetime roundtrip");
        Check(DateTime.MinValue.Ticks == 0, "dt MinValue");

        // --- TimeSpan arithmetic / operators (timespan_* icall family) ---
        TimeSpan ts = TimeSpan.FromDays(2) + TimeSpan.FromHours(3);
        Check(ts.TotalHours == 51.0, "ts add");
        Check(TimeSpan.FromMilliseconds(1500).TotalSeconds == 1.5, "ts FromMilliseconds");
        Check(TimeSpan.FromMinutes(90).TotalHours == 1.5, "ts FromMinutes");
        Check(TimeSpan.FromSeconds(3661).Minutes == 1 && TimeSpan.FromSeconds(3661).Hours == 1, "ts FromSeconds parts");
        Check(TimeSpan.FromTicks(10000000).Seconds == 1, "ts FromTicks");
        Check((-TimeSpan.FromMinutes(5)).Duration() == TimeSpan.FromMinutes(5), "ts Duration");
        Check(TimeSpan.FromHours(1) > TimeSpan.FromMinutes(30), "ts gt");
        Check(TimeSpan.FromSeconds(60) == TimeSpan.FromMinutes(1), "ts eq");
        Check(TimeSpan.FromMilliseconds(-5) < TimeSpan.Zero, "ts lt zero");
        Check((int)TimeSpan.FromMinutes(1).TotalSeconds == 60, "ts TotalSeconds");

        // --- decimal ctor/casts/ops (decimal_* icall family) ---
        decimal m1 = new decimal(12345, 0, 0, false, (byte)2);
        Check(m1.ToString() == "123.45", "dec ctor(scale) toString");
        decimal m2 = new decimal(2.5);
        Check(m2.ToString() == "2.5", "dec ctor(double)");
        decimal m3 = (decimal)1.5f;
        Check(m3 > 1.0m && m3 < 2.0m, "dec ctor(float) range");
        Check((double)m2 == 2.5, "dec to double");
        Check((float)m2 == 2.5f, "dec to float");
        Check((int)5.9m == 5 && (int)(-5.9m) == -5, "dec to int truncate");
        Check((uint)new decimal(4294967295ul) == uint.MaxValue, "dec to uint max");
        Check(new decimal(1) / new decimal(4) == 0.25m, "dec divide");
        Check(new decimal(2) * new decimal(3) == 6m, "dec multiply");
        Check(new decimal(10) - new decimal(3) == 7m, "dec subtract");
        Check(new decimal(10) % new decimal(3) == 1m, "dec remainder");
        Check(new decimal(5) > new decimal(3) && new decimal(3) < new decimal(5), "dec ops lt gt");
        Check(new decimal(2) == new decimal(2, 0, 0, false, 1) * new decimal(10), "dec composite eq");

        // --- Char classification / parsing (char_* icall family) ---
        Check(char.IsControl('\n') && char.IsControl('\u0007'), "char IsControl");
        Check(char.IsPunctuation('.') && char.IsPunctuation('?'), "char IsPunctuation");
        Check(char.IsSeparator(' ') && !char.IsSeparator('x'), "char IsSeparator (ASCII-exact domain)");
        Check(char.IsSymbol('$') && char.IsSymbol('+'), "char IsSymbol");
        Check(char.IsSurrogate('\ud800') && char.IsHighSurrogate('\ud800') && char.IsLowSurrogate('\udc00'), "char surrogate");
        Check(char.GetNumericValue('5') == 5.0, "char GetNumericValue (ASCII-exact domain)");
        Check(char.Parse("x") == 'x', "char Parse");

        // --- Math .NET7+/net8 added functions (math_* icall family) ---
        Check(Math.Abs(Math.Acosh(2.0) - 1.3169578969) < 1e-9, "math Acosh");
        Check(Math.Asinh(1.0) > 0.88 && Math.Asinh(1.0) < 0.89, "math Asinh");
        Check(Math.Atanh(0.5) > 0.549 && Math.Atanh(0.5) < 0.55, "math Atanh");
        Check(Math.BitDecrement(1.0) < 1.0 && Math.BitIncrement(1.0) > 1.0, "math bit inc/dec");
        Check(Math.Cbrt(27.0) == 3.0, "math Cbrt");
        Check(Math.Clamp(15, 0, 10) == 10 && Math.Clamp(-3, 0, 10) == 0 && Math.Clamp(5, 0, 10) == 5, "math Clamp(int)");
        Check(Math.Clamp(15L, 0L, 10L) == 10L, "math Clamp(long)");
        Check(Math.Clamp(3.5, 0.0, 10.0) == 3.5 && Math.Clamp(12.0, 0.0, 10.0) == 10.0, "math Clamp(double)");
        Check(Math.CopySign(-2.5, 3.0) == 2.5 && Math.CopySign(2.5, -3.0) == -2.5, "math CopySign");
        Check(Math.Abs(Math.FusedMultiplyAdd(2.0, 3.0, 4.0) - 10.0) < 1e-12, "math FMA");
        Check(Math.ILogB(8.0) == 3, "math ILogB");
        Check(Math.Log2(8.0) == 3.0, "math Log2");
        Check(Math.MaxMagnitude(-3.0, 2.0) == -3.0 && Math.MinMagnitude(-3.0, 2.0) == 2.0, "math max/min magnitude");
        Check(Math.ReciprocalEstimate(2.0) > 0.49 && Math.ReciprocalEstimate(2.0) < 0.51, "math ReciprocalEstimate");
        Check(Math.ReciprocalSqrtEstimate(4.0) > 0.49 && Math.ReciprocalSqrtEstimate(4.0) < 0.51, "math ReciprocalSqrtEstimate");
        Check(Math.ScaleB(2.0, 3) == 16.0, "math ScaleB");
        Check(Math.Sign(-5.0) == -1 && Math.Sign(0.0) == 0 && Math.Sign(7.0) == 1, "math Sign(double)");
        var scT = Math.SinCos(1.0);
        Check(scT.Sin > 0.84 && scT.Sin < 0.85, "math SinCos");

        // --- String extended API (string_* icall family) ---
        string s = "hello world hello";
        Check(s.IndexOfAny(new[] { 'w', 'z' }) == 6, "str IndexOfAny");
        Check(s.IndexOfAny(new[] { 'o', 'z' }, 5) == 7, "str IndexOfAny start");
        Check(s.LastIndexOfAny(new[] { 'l', 'x' }) == 15, "str LastIndexOfAny");
        Check(s.LastIndexOfAny(new[] { 'l' }, 12) == 9, "str LastIndexOfAny start");
        Check(s.Insert(5, ",") == "hello, world hello", "str Insert");
        Check(s.Remove(5, 6) == "hello hello", "str Remove range");
        Check(s.Remove(5) == "hello", "str Remove to end");
        Check(s.Split(new[] { ' ' }).Length == 3, "str Split char[]");
        Check(string.Join(",", new object[] { 1, 2, 3 }) == "1,2,3", "str Join object[] boxed");
        Check(string.Join('-', new[] { "a", "b", "c" }) == "a-b-c", "str Join char + string[]");
        Check(s.ToCharArray(6, 5).Length == 5, "str ToCharArray range");
        Check(s.Contains('w'), "str Contains char");
        Check(string.Compare("a", "b") < 0 && string.Compare("b", "a") > 0, "str Compare");
        Check(string.CompareOrdinal("abc", "abd") < 0, "str CompareOrdinal");

        // --- StringBuilder Clear / Chars (stringbuilder_* icall family) ---
        StringBuilder sb = new StringBuilder("abc");
        sb.Clear();
        Check(sb.Length == 0, "sb Clear");
        sb.Append("xyz");
        sb[1] = 'X';
        Check(sb.ToString() == "xXz" || sb.ToString() == "xXz", "sb set Chars");

        // --- Array extended (array_* icall family) ---
        int[] arr = { 1, 2, 3, 4, 5 };
        Check(Array.IndexOf(arr, 3) == 2 && Array.IndexOf(arr, 9) == -1, "arr IndexOf");
        Check(Array.ConvertAll(new[] { 1, 2, 3 }, x => x * 10).Length == 3, "arr ConvertAll");
        Check(Array.FindIndex(arr, x => x > 3) == 3, "arr FindIndex");

        // --- Environment / GC surface ---
        Check(Environment.Is64BitProcess, "env Is64BitProcess");
        Check(GC.GetTotalMemory(false) > 0, "gc GetTotalMemory");

        Console.WriteLine($"failures={s_failures}");
        return s_failures == 0 ? 0 : 1;
    }
}