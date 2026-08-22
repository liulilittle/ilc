using System;
using System.Text;
using System.Threading;

static class Program
{
    static int failures = 0;
    static void Check(string name, bool ok)
    {
        if (!ok) { failures++; Console.WriteLine("FAIL " + name); }
        else Console.WriteLine("PASS " + name);
    }

    static void Main()
    {
        // ---- Math ----
        Check("math.abs", Math.Abs(-42) == 42);
        Check("math.max", Math.Max(3, 7) == 7 && Math.Min(3, 7) == 3);
        Check("math.sqrt", Math.Sqrt(144.0) == 12.0);
        Check("math.pow", Math.Pow(2.0, 10.0) == 1024.0);
        Check("math.floor", Math.Floor(3.7) == 3.0 && Math.Ceiling(3.2) == 4.0);
        Check("math.round", Math.Round(3.5) == 4.0 && Math.Truncate(-3.7) == -3.0);
        Check("math.sign", Math.Sign(-5) == -1 && Math.Sign(0) == 0 && Math.Sign(9) == 1);

        // ---- String ----
        string s = "Hello World";
        Check("str.length", s.Length == 11);
        Check("str.substring", s.Substring(6) == "World" && s.Substring(0, 5) == "Hello");
        Check("str.indexof", s.IndexOf('o') == 4 && s.IndexOf("World") == 6);
        Check("str.toupper", s.ToUpper() == "HELLO WORLD");
        Check("str.tolower", s.ToLower() == "hello world");
        Check("str.replace", s.Replace("World", "ILC") == "Hello ILC");
        Check("str.startswith", s.StartsWith("Hello") && s.EndsWith("World"));
        Check("str.split", s.Split(' ')[0] == "Hello" && s.Split(' ')[1] == "World");
        Check("str.trim", "  pad  ".Trim() == "pad");
        Check("str.pad", "5".PadLeft(3, '0') == "005" && "ab".PadRight(4, 'x') == "abxx");
        Check("str.insert", "Held".Insert(3, "l") == "Helld");
        Check("str.remove", "Hello".Remove(4) == "Hell" && "Hello".Remove(1, 3) == "Ho");
        Check("str.compare", string.Compare("abc", "abd") < 0 && string.Compare("b", "a") > 0);
        Check("str.concat", string.Concat("a", "b", "c") == "abc");
        Check("str.join", string.Join("-", new string[] { "x", "y" }) == "x-y");
        Check("str.isnullorempty", string.IsNullOrEmpty("") && !string.IsNullOrEmpty("a"));
        Check("str.chars", s[0] == 'H');
        Check("str.indexofany", "hello".IndexOfAny(new char[] { 'l', 'o' }) == 2);
        char[] cbuf = new char[5];
        s.CopyTo(6, cbuf, 0, 5);
        Check("str.copyto", new string(cbuf) == "World");

        // ---- Interlocked ----
        int iv = 5;
        Check("il.inc", Interlocked.Increment(ref iv) == 6 && iv == 6);
        Check("il.dec", Interlocked.Decrement(ref iv) == 5 && iv == 5);
        Check("il.add", Interlocked.Add(ref iv, 10) == 15 && iv == 15);
        Check("il.exchange", Interlocked.Exchange(ref iv, 20) == 15 && iv == 20);
        int orig = Interlocked.CompareExchange(ref iv, 30, 20);
        Check("il.cas.hit", orig == 20 && iv == 30);
        orig = Interlocked.CompareExchange(ref iv, 99, 1);
        Check("il.cas.miss", orig == 30 && iv == 30);
        long lv = 100L;
        Check("il.add64", Interlocked.Add(ref lv, 50L) == 150L && lv == 150L);
        Interlocked.MemoryBarrier();
        Check("il.fence", true);

        // ---- GC ----
        object gco = new object();
        Check("gc.gen", GC.GetGeneration(gco) >= 0);
        Check("gc.maxgen", GC.MaxGeneration >= 0);
        long before = GC.GetTotalMemory(false);
        GC.Collect();
        GC.KeepAlive(gco);
        Check("gc.collect", GC.GetTotalMemory(false) > 0);
        GC.SuppressFinalize(gco);

        // ---- Environment ----
        Check("env.tickcount", Environment.TickCount != 0);
        Check("env.procs", Environment.ProcessorCount >= 1);
        Check("env.shutdownflag", Environment.HasShutdownStarted == false);
        Check("env.threadid", Environment.CurrentManagedThreadId > 0);

        // ---- Array ----
        int[] arr = new int[] { 3, 1, 2 };
        Check("arr.length", arr.Length == 3);
        Check("arr.rank", arr.Rank == 1);
        Check("arr.bounds", arr.GetLowerBound(0) == 0 && arr.GetUpperBound(0) == 2);
        arr.SetValue(9, 0);
        Check("arr.setvalue", arr.GetValue(0).Equals(9));
        Array.Sort(arr);
        Check("arr.sort", arr[0] == 1 && arr[2] == 9);
        Array.Reverse(arr);
        Check("arr.reverse", arr[0] == 9 && arr[2] == 1);
        int[] dst = new int[3];
        Array.Copy(arr, dst, 3);
        Check("arr.copy", dst[0] == 9 && dst[2] == 1);
        Array.Clear(dst, 0, 3);
        Check("arr.clear", dst[0] == 0 && dst[2] == 0);
        Check("arr.indexof", Array.IndexOf(arr, 9) == 0);
        int[] rs = new int[2];
        Array.Resize(ref rs, 4);
        Check("arr.resize", rs.Length == 4);
        Check("arr.longlength", arr.LongLength == 3L);

        // ---- Convert ----
        Check("conv.int32", Convert.ToInt32("123") == 123 && Convert.ToInt32(true) == 1);
        Check("conv.str", Convert.ToString(456) == "456");
        Check("conv.bool", Convert.ToBoolean(1) && !Convert.ToBoolean(0));
        Check("conv.double", Convert.ToDouble("2.5") == 2.5);
        Check("conv.byte", Convert.ToByte(200) == 200);
        Check("conv.char", Convert.ToChar(65) == 'A');

        // ---- DateTime ----
        Check("dt.leap", DateTime.IsLeapYear(2024) && !DateTime.IsLeapYear(2023));
        Check("dt.daysinmonth", DateTime.DaysInMonth(2024, 2) == 29 && DateTime.DaysInMonth(2023, 2) == 28);
        DateTime dt = new DateTime(2024, 6, 15);
        Check("dt.adddays", dt.AddDays(1).Day == 16);
        Check("dt.compare", DateTime.Compare(dt, dt.AddDays(-1)) > 0);

        // ---- Guid ----
        Guid g1 = Guid.NewGuid();
        Guid g2 = Guid.NewGuid();
        Check("guid.new", g1 != g2 && g1.ToString().Length == 36);
        Check("guid.empty", Guid.Empty.ToString() == "00000000-0000-0000-0000-000000000000");

        // ---- StringBuilder ----
        StringBuilder sb = new StringBuilder();
        sb.Append("Hello");
        sb.Append(", ").Append("World");
        sb.Insert(0, ">> ");
        sb.Remove(0, 3);
        sb.Replace("World", "ILC");
        Check("sb.chain", sb.ToString() == "Hello, ILC");
        sb.Clear();
        Check("sb.clear", sb.Length == 0);
        sb.AppendFormat("{0}-{1}", 7, "x");
        Check("sb.appendformat", sb.ToString() == "7-x");

        // ---- decimal ----
        decimal d1 = 0.1m; d1 = 0.1m;
        decimal sum = 0.1m + 0.2m;
        Check("dec.add", sum == 0.3m);
        Check("dec.parse", decimal.Parse("123.45") == 123.45m);
        Check("dec.tostring", (123.45m).ToString() == "123.45");
        Check("dec.compare", decimal.Compare(1.5m, 2.5m) < 0);

        // ---- BitConverter ----
        byte[] b4 = BitConverter.GetBytes(258);
        Check("bits.roundtrip", BitConverter.ToInt32(b4, 0) == 258);
        Check("bits.double", BitConverter.DoubleToInt64Bits(2.0) == 4611686018427387904L);

        // ---- char ----
        Check("char.digit", char.IsDigit('5') && !char.IsDigit('x'));
        Check("char.letter", char.IsLetter('a') && !char.IsLetter('1'));
        Check("char.upper", char.ToUpper('a') == 'A' && char.ToLower('Z') == 'z');
        Check("char.ws", char.IsWhiteSpace(' ') && !char.IsWhiteSpace('a'));

        // ---- Thread ----
        Thread.Sleep(0);
        Check("thr.sleep0", true);
        Check("thr.current", Thread.CurrentThread != null);
        Thread.SpinWait(10);
        Check("thr.spinwait", true);

        // ---- Random (seeded = deterministic) ----
        Random rng = new Random(42);
        int r1 = rng.Next(100);
        Random rng2 = new Random(42);
        Check("rand.seeded", rng2.Next(100) == r1 && r1 >= 0 && r1 < 100);

        Console.WriteLine(failures == 0 ? "ALL-PASS" : "FAILURES=" + failures);
    }
}
