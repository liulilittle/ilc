using System;
using System.Text;
using System.Threading;

static class Program
{
    static int failures = 0;

    static void Check(string name, bool ok)
    {
        if (!ok) { failures++; Console.WriteLine($"FAIL {name}"); }
        else Console.WriteLine($"PASS {name}");
    }

    static int Main()
    {
        // ---- Math ----
        Check("math.sqrt", Math.Sqrt(144.0) == 12.0);
        Check("math.pow", Math.Pow(2.0, 10.0) == 1024.0);
        Check("math.abs", Math.Abs(-7.5) == 7.5 && Math.Abs(-9) == 9 && Math.Abs(-9L) == 9L);
        Check("math.minmax", Math.Min(3, 5) == 3 && Math.Max(3.0, 5.0) == 5.0);
        Check("math.floorceil", Math.Floor(2.7) == 2.0 && Math.Ceiling(2.1) == 3.0);
        Check("math.round", Math.Round(2.5) == 2.0 && Math.Round(3.5) == 4.0);
        Check("math.truncate", Math.Truncate(2.9) == 2.0);
        Check("math.log", Math.Log(Math.E) == 1.0 && Math.Log10(100.0) == 2.0);
        Check("math.sign", Math.Sign(-5) == -1 && Math.Sign(0) == 0 && Math.Sign(5.5) == 1);
        Check("math.ieeeremainder", Math.IEEERemainder(7.0, 4.0) == -1.0);
        int drRem;
        Check("math.divrem", Math.DivRem(17, 5, out drRem) == 3 && drRem == 2);
        Check("math.clamp", Math.Clamp(15, 0, 10) == 10 && Math.Clamp(-1.0, 0.0, 10.0) == 0.0);
        // D1-D6 valuetuple-local implementation: the IL local's GENERICINST
        // signature types it as the tuple struct; reads are direct field
        // accesses (no object-cast roundtrip).
        var sc = Math.SinCos(0.0);
        Check("math.sincos", sc.Item1 == 0.0 && sc.Item2 == 1.0);

        // ---- MathF ----
        Check("mathf.sqrt", MathF.Sqrt(16.0f) == 4.0f);
        Check("mathf.pow", MathF.Pow(2.0f, 5.0f) == 32.0f);
        Check("mathf.abs", MathF.Abs(-2.5f) == 2.5f);
        Check("mathf.minmax", MathF.Min(1.5f, 2.5f) == 1.5f && MathF.Max(1.5f, 2.5f) == 2.5f);
        Check("mathf.floorceil", MathF.Floor(1.9f) == 1.0f && MathF.Ceiling(1.1f) == 2.0f);
        Check("mathf.truncround", MathF.Truncate(1.9f) == 1.0f && MathF.Round(1.4f) == 1.0f);

        // ---- String ----
        string s = "Hello, World";
        Check("string.basic", s.Length == 12 && s.Substring(7) == "World");
        Check("string.indexof", s.IndexOf("World") == 7 && s.IndexOf('o') == 4);
        Check("string.replace", s.Replace("World", "ILC") == "Hello, ILC");
        Check("string.case", "abc".ToUpperInvariant() == "ABC" && "ABC".ToLowerInvariant() == "abc");
        Check("string.split", string.Join("|", "a,b,c".Split(new[] { ',' })) == "a|b|c");
        Check("string.trim", "  x  ".Trim() == "x");
        Check("string.startsends", s.StartsWith("Hello") && s.EndsWith("World"));
        Check("string.ordinal", string.CompareOrdinal("a", "b") < 0);
        Check("string.concatjoin", string.Concat("a", "b", "c") == "abc" && string.Join("-", new[] { "x", "y" }) == "x-y");
        Check("string.isnullor", string.IsNullOrEmpty("") && !string.IsNullOrEmpty("x") && string.IsNullOrWhiteSpace("  "));
        Check("string.pad", "5".PadLeft(3, '0') == "005" && "ab".PadRight(4, '.') == "ab..");
        Check("string.insertremove", "ad".Insert(1, "bc").Remove(0, 1) == "bcd");
        Check("string.chars", s[0] == 'H' && s.Contains("lo, W"));

        // ---- Interlocked ----
        int i32 = 10;
        Check("il.add32", Interlocked.Add(ref i32, 5) == 15 && i32 == 15);
        long i64 = 100L;
        Check("il.add64", Interlocked.Add(ref i64, 50L) == 150L && i64 == 150L);
        int i32x = 7;
        Check("il.exch32", Interlocked.Exchange(ref i32x, 9) == 7 && i32x == 9);
        long i64x = 11L;
        Check("il.exch64", Interlocked.Exchange(ref i64x, 22L) == 11L && i64x == 22L);
        float fx = 1.5f;
        Check("il.exchf", Interlocked.Exchange(ref fx, 2.5f) == 1.5f && fx == 2.5f);
        double dx = 3.5;
        Check("il.exchd", Interlocked.Exchange(ref dx, 4.5) == 3.5 && dx == 4.5);
        object ox = new object();
        object oxOld = ox;
        object oxNew = new object();
        Check("il.excho", ReferenceEquals(Interlocked.Exchange(ref ox, oxNew), oxOld));
        int cmp = 5;
        Check("il.cas32.hit", Interlocked.CompareExchange(ref cmp, 8, 5) == 5 && cmp == 8);
        Check("il.cas32.miss", Interlocked.CompareExchange(ref cmp, 99, 5) == 8 && cmp == 8);
        long cmp64 = 1L;
        Check("il.cas64", Interlocked.CompareExchange(ref cmp64, 2L, 1L) == 1L && cmp64 == 2L);
        double dcmp = 0.5;
        Check("il.casd", Interlocked.CompareExchange(ref dcmp, 1.5, 0.5) == 0.5 && dcmp == 1.5);
        int gi = 42;
        Check("il.generic.exch", Interlocked.Exchange(ref gi, 43) == 42 && gi == 43);
        Check("il.incdec", Interlocked.Increment(ref i32) == 16 && Interlocked.Decrement(ref i32) == 15);

        // ---- GC ----
        GC.Collect();
        Check("gc.totalmemory", GC.GetTotalMemory(false) > 0);
        // Heap-allocated receiver: g_strPool LITERALS are static data (not GC
        // blocks) and GetGeneration correctly reports -1 for them -- assert on
        // a real allocation.
        Check("gc.generation", GC.GetGeneration(new string('x', 3)) >= 0);
        Check("gc.maxgeneration", GC.MaxGeneration >= 0);
        Check("gc.keepalive", (GC.GetAllocatedBytesForCurrentThread() >= 0));

        // ---- Environment ----
        Check("env.cpu", Environment.ProcessorCount > 0);
        Check("env.threadid", Environment.CurrentManagedThreadId > 0);
        Environment.ExitCode = 0;
        Check("env.exitcode", Environment.ExitCode == 0);
        Check("env.newline", Environment.NewLine.Length > 0);

        // ---- Array ----
        int[] arr = { 5, 3, 1, 4, 2 };
        Array.Sort(arr);
        Check("arr.sort", arr[0] == 1 && arr[4] == 5);
        Check("arr.binarysearch", Array.BinarySearch(arr, 3) == 2);
        Check("arr.indexof", Array.IndexOf(arr, 4) == 3 && Array.LastIndexOf(arr, 1) == 0);
        Array.Reverse(arr);
        Check("arr.reverse", arr[0] == 5 && arr[4] == 1);
        int[] dst = new int[5];
        Array.Copy(arr, 0, dst, 0, 5);
        Check("arr.copy", dst[0] == 5);
        Array.Clear(dst, 0, 5);
        Check("arr.clear", dst[0] == 0 && dst[4] == 0);
        Array.Resize(ref dst, 3);
        Check("arr.resize", dst.Length == 3);
        Check("arr.find", Array.Find(new[] { 1, 7, 3 }, x => x > 5) == 7);
        Check("arr.exists", Array.Exists(new[] { 1, 2 }, x => x == 2));
        Check("arr.trueforall", Array.TrueForAll(new[] { 2, 4 }, x => x % 2 == 0));
        Check("arr.findall", Array.FindAll(new[] { 1, 2, 3, 4 }, x => x % 2 == 0).Length == 2);
        Check("arr.convertall", Array.ConvertAll(new[] { "1", "22" }, int.Parse)[1] == 22);
        Check("arr.empty", Array.Empty<string>().Length == 0);
        Check("arr.lengths", arr.GetLength(0) == 5 && arr.GetLowerBound(0) == 0 && arr.GetUpperBound(0) == 4);

        // ---- Convert ----
        Check("conv.toint32", Convert.ToInt32("42") == 42 && Convert.ToString(42) == "42");
        Check("conv.bool", Convert.ToBoolean(1) && Convert.ToInt32(true) == 1);
        Check("conv.double", Convert.ToDouble("2.5") == 2.5);

        // ---- DateTime / TimeSpan ----
        Check("dt.leap", DateTime.IsLeapYear(2024) && !DateTime.IsLeapYear(2023));
        Check("dt.daysinmonth", DateTime.DaysInMonth(2024, 2) == 29);
        var dt = new DateTime(2024, 1, 31).AddDays(1);
        Check("dt.addday", dt.Month == 2 && dt.Day == 1);
        Check("ts.hours", TimeSpan.FromMinutes(90).TotalHours == 1.5);

        // ---- Guid ----
        var g = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");
        Check("guid.parse", g.ToString() == "6f9619ff-8b86-d011-b42d-00cf4fc964ff");

        // ---- Random (fixed seed, .NET Core algorithm stable since 6) ----
        var rng = new Random(42);
        int r1 = rng.Next(1000);
        var rng2 = new Random(42);
        Check("random.seed", r1 == rng2.Next(1000) && r1 >= 0 && r1 < 1000);

        // ---- StringBuilder ----
        var sb = new StringBuilder();
        sb.Append("Hello").Append(',').Append(' ').AppendFormat("{0}", 42);
        Check("sb.append", sb.ToString() == "Hello, 42");
        sb.Insert(0, ">> ");
        Check("sb.insert", sb.ToString().StartsWith(">> Hello"));
        sb.Remove(0, 3);
        Check("sb.remove", sb.ToString() == "Hello, 42");
        sb.Replace("42", "ILC");
        Check("sb.replace", sb.ToString() == "Hello, ILC");
        Check("sb.capacity", sb.Capacity >= sb.Length);
        Check("sb.length", sb.Length == 10);

        // ---- decimal ----
        // Literals PINNED (TempSlotPass dec-kind fix): Roslyn lowers 6m to
        // newobj Decimal::.ctor(int32) -- the fold+typing must keep the
        // 16-byte slot. Parse-based checks retained as cross-validation.
        decimal dm = 0.1m + 0.2m;
        Check("decimal.add", dm == 0.3m && dm == decimal.Parse("0.3"));
        Check("decimal.mulcmp", (2m * 3m) == 6m && decimal.Compare(2m, 3m) < 0);
        Check("decimal.parse", decimal.Parse("123.45") == 123.45m && 123.45m.ToString() == "123.45");

        // ---- BitConverter ----
        byte[] bytes = BitConverter.GetBytes(258);
        Check("bitconv.roundtrip", BitConverter.ToInt32(bytes, 0) == 258);

        // ---- Monitor / lock ----
        object gate = new object();
        int shared = 0;
        lock (gate) { shared = 5; }
        Check("monitor.lock", shared == 5);
        bool entered = false;
        Monitor.Enter(gate, ref entered);
        Monitor.Exit(gate);
        Check("monitor.enterexit", entered);

        // ---- Thread ----
        Thread.Sleep(0);
        Check("thread.id", Thread.CurrentThread.ManagedThreadId > 0);
        Thread.SpinWait(1);
        Check("thread.spin", true);

        // ---- char ----
        Check("char.cls", char.IsDigit('5') && char.IsLetter('a') && char.IsWhiteSpace(' '));
        Check("char.conv", char.ToUpper('a') == 'A' && char.ToLower('Z') == 'z');

        Console.WriteLine(failures == 0 ? "ALL-PASS" : string.Concat("FAILURES=", failures.ToString()));
        return failures == 0 ? 0 : 1;
    }
}
