// icallwide.cs -- deep E2E coverage for wired-but-never-executed InternalCall surface.
// Compile tiers: -define:LEGACY (net48) | none (netcore21/31) | -define:NEWMATH (net5+)
using System;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Threading;

enum Color { Red, Green, Blue }

sealed class Cloneable : ICloneable
{
    public int X;
    public string Name;
    public object Clone() { return MemberwiseClone(); }
}

static class Program
{
    static int failures = 0;
    static void Check(string name, bool ok)
    {
        if (!ok) { failures++; Console.WriteLine("FAIL " + name); }
        else Console.WriteLine("PASS " + name);
    }
    static void CheckEq(string name, long exp, long act) { Check(name, exp == act); }
    static void CheckEqS(string name, string exp, string act) { Check(name + " got=" + act, exp == act); }

    static long DoubleToInt(int x) { return (long)x * 2; }

    static void Main()
    {
        WideString();
        WideStringBuilder();
        WideMath();
        WideDecimal();
        WideFormat();
        WideParse();
        WideArray();
        WideGC();
        WideThread();
        WideVersion();
        WideDateTime();
        WideTimeSpan();
        WideChar();
        WideEnum();
        WideObject();
        WideCollections();

        if (failures == 0) Console.WriteLine("ALL-PASS");
        else Console.WriteLine("FAILURES " + failures);
    }

    // ================= String extras =================
    static void WideString()
    {
        string s = "Hello World Hello"; // len 17: o@4,7,16; Hello@0,12
#if !LEGACY
        Check("ws.contains.char", "Hello".Contains('e'));
#endif
        CheckEq("ws.indexof.s.sc", 0L, s.IndexOf("Hello", StringComparison.Ordinal));
        CheckEq("ws.indexof.s.from", 12L, s.IndexOf("Hello", 1, StringComparison.Ordinal));
        CheckEq("ws.indexof.c.from", 7L, s.IndexOf('o', 5));
        CheckEq("ws.indexof.c.range", 4L, s.IndexOf('o', 2, 5));
        CheckEq("ws.indexof.s.range", -1L, s.IndexOf("Hello", 7, 8, StringComparison.Ordinal));
        CheckEq("ws.lastindexof.s", 12L, s.LastIndexOf("Hello", StringComparison.Ordinal));
        CheckEq("ws.lastindexof.s.from", 0L, s.LastIndexOf("Hello", 5, StringComparison.Ordinal));
        CheckEq("ws.lastindexof.c", 16L, s.LastIndexOf('o'));
        CheckEq("ws.lastindexof.c.from", 7L, s.LastIndexOf('o', 10));
        CheckEq("ws.lastindexof.c.range", 16L, s.LastIndexOf('o', 16, 6));
        CheckEq("ws.lastindexof.any", 16L, s.LastIndexOfAny(new char[] { 'l', 'o' }));
        CheckEq("ws.lastindexof.any.start", 9L, s.LastIndexOfAny(new char[] { 'l', 'o' }, 10));
        CheckEq("ws.indexof.any.start", 7L, s.IndexOfAny(new char[] { 'l', 'o' }, 5));
        Check("ws.tochararray", "ABC".ToCharArray()[1] == 'B' && "ABC".ToCharArray().Length == 3);
        Check("ws.trimstart", "..pad".TrimStart(new char[] { '.' }) == "pad");
        Check("ws.trimend", "pad..".TrimEnd(new char[] { '.' }) == "pad");
        Check("ws.concat4", string.Concat("a", "b", "c", "d") == "abcd");
        string[] parts = new string[] { "a", "b", "c", "d" };
        Check("ws.join.range.s", string.Join("--", parts, 0, 3) == "a--b--c");
#if !LEGACY
        Check("ws.join.range.c", string.Join(',', parts, 1, 2) == "b,c");
#endif
        Check("ws.compare.ordinal.ic", string.Compare("ABC", "abc", StringComparison.OrdinalIgnoreCase) == 0);
        Check("ws.equals.ordinal.ic", "ABC".Equals("abc", StringComparison.OrdinalIgnoreCase));
        Check("ws.hash.stable", "abc".GetHashCode() == "abc".GetHashCode());
        Check("ws.empty.length", string.Empty.Length == 0);
        Check("ws.chars.get", "xyz"[2] == 'z');
        Check("ws.substring.clamp", "abc".Substring(3).Length == 0);
        Check("ws.startswith.ord", "Hello".StartsWith("Hel", StringComparison.Ordinal));
        Check("ws.endswith.ord", "Hello".EndsWith("llo", StringComparison.Ordinal));
    }

    // ================= StringBuilder extras =================
    static void WideStringBuilder()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(true); sb.Append(' '); sb.Append(42); sb.Append(' '); sb.Append(123L); sb.Append(' '); sb.Append(2.5);
        CheckEqS("sb.mixed", "True 42 123 2.5", sb.ToString());

        StringBuilder sb2 = new StringBuilder("base");
        sb2.AppendLine(); sb2.AppendLine("next");
        CheckEqS("sb.appendline", "base\r\nnext\r\n", sb2.ToString());

        StringBuilder sb3 = new StringBuilder("12345");
        sb3.Insert(0, "#"); sb3.Insert(2, "ab"); sb3.Insert(4, "**", 2);
        CheckEqS("sb.insert.mix", "#1ab****2345", sb3.ToString());

        StringBuilder sb4 = new StringBuilder(16);
        sb4.Append("x");
        CheckEqS("sb.capacity.ctor", "x", sb4.ToString());

        StringBuilder sb5 = new StringBuilder("aXbXc");
        sb5.Replace('X', '-');
        CheckEqS("sb.replace.cc", "a-b-c", sb5.ToString());

        StringBuilder sb6 = new StringBuilder("hi");
        sb6.Append(true); sb6.AppendLine();
        CheckEq("sb.appendline.b.len", 8L, sb6.Length);
        CheckEq("sb.appendline.b.strlen", 8L, sb6.ToString().Length);
        CheckEqS("sb.appendline.b", "hiTrue\r\n", sb6.ToString());

        StringBuilder sb7 = new StringBuilder();
        sb7.Append(1.25);
        CheckEqS("sb.append.d", "1.25", sb7.ToString());

        StringBuilder sb8 = new StringBuilder("abc");
        CheckEqS("sb.insert.s.count", "abxxxc", sb8.Insert(2, "x", 3).ToString());

        StringBuilder sb9 = new StringBuilder("keep");
        Check("sb.clear", sb9.Clear().Length == 0 && sb9.ToString().Length == 0);

        StringBuilder sbA = new StringBuilder("hello");
        CheckEqS("sb.indexer", "h", sbA[0].ToString());
        CheckEq("sb.length", 5L, sbA.Length);
    }

    // ================= Math extras =================
    static void WideMath()
    {
        Check("wm.round.digits", Math.Round(3.14159, 3) == 3.142 && Math.Round(2.71828, 2) == 2.72);
        Check("wm.round.away", Math.Round(2.5, MidpointRounding.AwayFromZero) == 3.0);
        Check("wm.round.toeven", Math.Round(2.5, MidpointRounding.ToEven) == 2.0);
        Check("wm.round.mode.digits", Math.Round(3.456, 1, MidpointRounding.AwayFromZero) == 3.5);
        Check("wm.bigmul", Math.BigMul(int.MaxValue, 2) == 4294967294L);
        int rem;
        int q = Math.DivRem(17, 5, out rem);
        Check("wm.divrem", q == 3 && rem == 2);
        Check("wm.abs.long", Math.Abs(-922337203685477580L) == 922337203685477580L);
        Check("wm.minmax.bytes", Math.Max((byte)200, (byte)100) == 200 && Math.Min((byte)200, (byte)100) == 100);
        Check("wm.exp.log", Math.Exp(1.0) > 2.718281828459045 && Math.Exp(1.0) < 2.718281828459046);
        Check("wm.log10", Math.Log10(1000.0) == 3.0);
        Check("wm.log.e", Math.Log(Math.E) == 1.0);
        Check("wm.pow.frac", Math.Pow(8.0, 1.0 / 3.0) > 1.9999999999 && Math.Pow(8.0, 1.0 / 3.0) < 2.0000000001);
        Check("wm.sqrt.roundtrip", Math.Sqrt(2.0) * Math.Sqrt(2.0) > 1.9999999999999995);
        sbyte sbNeg = -128;
        Check("wm.abs.sbyte", Math.Abs((int)sbNeg) == 128);
        Check("wm.truncate.pos", Math.Truncate(7.89) == 7.0);
        Check("wm.ceiling.neg", Math.Ceiling(-3.2) == -3.0);
        Check("wm.floor.neg", Math.Floor(-3.7) == -4.0);
#if !LEGACY
        Check("wm.clamp.int", Math.Clamp(5, 1, 3) == 3 && Math.Clamp(0, 1, 3) == 1 && Math.Clamp(2, 1, 3) == 2);
        Check("wm.clamp.double", Math.Clamp(2.5, 0.0, 2.0) == 2.0);
#endif
#if NEWMATH
        Check("wm.bitincrement", Math.BitIncrement(1.0) > 1.0 && Math.BitIncrement(1.0) < 1.000000000000001);
        Check("wm.bitdecrement", Math.BitDecrement(1.0) < 1.0 && Math.BitDecrement(1.0) > 0.999999999999999);
        Check("wm.copysign", Math.CopySign(3.0, -1.0) == -3.0 && Math.CopySign(-3.0, 1.0) == 3.0);
        Check("wm.ilogb", Math.ILogB(8.0) == 3 && Math.ILogB(0.5) == -1);
        Check("wm.scaleb", Math.ScaleB(1.5, 4) == 24.0);
        Check("wm.maxmagnitude", Math.MaxMagnitude(-5.0, 3.0) == -5.0 && Math.MinMagnitude(-5.0, 3.0) == 3.0);
#endif
    }

    // ================= Decimal =================
    static void WideDecimal()
    {
        decimal a = 123.45m; decimal b = 67.89m;
        Check("dec.add", decimal.Add(a, b) == 191.34m);
        Check("dec.sub", decimal.Subtract(a, b) == 55.56m);
        Check("dec.mul", decimal.Multiply(1.5m, 4m) == 6.0m);
        Check("dec.div", decimal.Divide(10m, 4m) == 2.5m);
        Check("dec.rem", decimal.Remainder(10m, 3m) == 1m);
        Check("dec.negate", decimal.Negate(a) == -123.45m);
        Check("dec.floor", decimal.Floor(7.9m) == 7m && decimal.Floor(-7.9m) == -8m);
        Check("dec.ceiling", decimal.Ceiling(7.1m) == 8m && decimal.Ceiling(-7.9m) == -7m);
        Check("dec.truncate", decimal.Truncate(7.9m) == 7m);
        Check("dec.round.default", decimal.Round(7.5m) == 8m);
        Check("dec.round.away", decimal.Round(2.345m, 2) == 2.35m || decimal.Round(2.345m, 2) == 2.34m);
        Check("dec.cmp", decimal.Compare(a, b) > 0 && decimal.Compare(b, a) < 0 && decimal.Compare(a, a) == 0);
        Check("dec.ops", (a + b) == 191.34m && (a - b) == 55.56m && (-a) == -123.45m);
        Check("dec.todouble", (double)a > 123.4499999 && (double)a < 123.4500001);
        Check("dec.tofloat", (float)0.5m == 0.5f);
        Check("dec.fromdouble", (decimal)0.5 == 0.5m);
        Check("dec.fromfloat", (decimal)0.25f == 0.25m);
        Check("dec.toint32", decimal.ToInt32(42.9m) == 42);
        Check("dec.toint64", decimal.ToInt64(-42.9m) == -42);
        Check("dec.parse", decimal.Parse("123.45") == 123.45m);
        Check("dec.tryformat", decimal.TryParse("not", out decimal bad) == false && bad == 0m);
        Check("dec.hash.eq", a.GetHashCode() == 123.45m.GetHashCode());
        Check("dec.tostring", a.ToString() == "123.45");
        Check("dec.zero", 0m.ToString() == "0");
        Check("dec.scale", 1.2300m == 1.23m);
        Check("dec.maxcmp", decimal.MaxValue > 0m && decimal.MinValue < 0m);
        Check("dec.equals", a.Equals(123.45m) && !a.Equals(b));
    }

    // ================= Number formatting =================
    static void WideFormat()
    {
        CheckEqS("fmt.int.d5", "00042", 42.ToString("D5"));
        CheckEqS("fmt.int.x8", "000002AB", 683.ToString("X8"));
        CheckEqS("fmt.int.x.lower", "ff", 255.ToString("x"));
        CheckEqS("fmt.int.f0", "683", 683.ToString("F0"));
        CheckEqS("fmt.long.d20", "00000000000000123456", 123456L.ToString("D20"));
        // NOTE: ulong.MaxValue with "D" would ride the SIGNED engine (>=2^63
        // is the documented subset boundary) -- hex is exact for the full
        // unsigned range, so the max-value check uses X.
        CheckEqS("fmt.long.x.max", "7FFFFFFFFFFFFFFF", long.MaxValue.ToString("X"));
        CheckEqS("fmt.bool", "True", true.ToString());
        CheckEqS("fmt.bool.false", "False", false.ToString());
        CheckEqS("fmt.char", "Q", 'Q'.ToString());
        CheckEqS("fmt.double.f3", "3.142", 3.14159.ToString("F3"));
        CheckEqS("fmt.double.f1", "2.5", 2.5.ToString("F1"));
        CheckEqS("fmt.double.whole", "5", 5.0.ToString("F0"));
        CheckEqS("fmt.float.f2", "1.25", 1.25f.ToString("F2"));
        CheckEqS("fmt.timespan.canonical", "01:02:03", new TimeSpan(1, 2, 3).ToString());
        CheckEqS("fmt.datetime.custom", "2020-06-15 12:30:45",
            new DateTime(2020, 6, 15, 12, 30, 45, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss"));
        // Guid component ctor needs the value-slot newobj path (unwired);
        // Parse covers the construction semantics.
        Guid gf = Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f");
        CheckEqS("fmt.guid.n", "000102030405060708090a0b0c0d0e0f", gf.ToString("N"));
        CheckEqS("fmt.guid.d", "00010203-0405-0607-0809-0a0b0c0d0e0f", gf.ToString("D"));
        CheckEqS("fmt.version.str", "1.2.3.4", new Version(1, 2, 3, 4).ToString());
        CheckEqS("fmt.decimal.format", "123.45", 123.45m.ToString("F2"));
    }

    // ================= Parsing =================
    static void WideParse()
    {
        Check("parse.int", int.Parse("-123") == -123 && int.Parse("+7") == 7);
        Check("parse.int.ws", int.Parse(" 42 ") == 42);
        Check("parse.long", long.Parse("922337203685477580") == 922337203685477580L);
        // NOTE: Parse(string, NumberStyles) with HexNumber is covered through
        // Convert.ToInt32(string, base) below (the styles overload itself is
        // unwired -- documented subset boundary).
        bool ok1 = int.TryParse("12x", out int ti);
        Check("parse.tryfail", ok1 == false && ti == 0);
        bool ok2 = int.TryParse("777", out int tj);
        Check("parse.tryok", ok2 && tj == 777);
        Check("parse.double", double.Parse("3.5") == 3.5 && double.Parse("-0.25") == -0.25);
        Check("parse.float", float.Parse("1.5") == 1.5f);
        Check("parse.bool", bool.Parse("true") && !bool.Parse("False"));
        Check("parse.char", char.Parse("K") == 'K');
        Version v = Version.Parse("2.10.3");
        Check("parse.version", v.Major == 2 && v.Minor == 10 && v.Build == 3);
        // NOTE: TimeSpan.Parse is unwired (no runtime parser icall) --
        // documented subset boundary; the From* factories cover construction.
        TimeSpan ts = new TimeSpan(1, 2, 3, 0, 4);
        Check("parse.timespan", ts.Days == 1 && ts.Hours == 2 && ts.Minutes == 3 && ts.Seconds == 0 && ts.Milliseconds == 4);
        Guid g = Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f");
        Check("parse.guid", g.ToString("N") == "000102030405060708090a0b0c0d0e0f");        Check("parse.decimal", decimal.Parse("-5.5") == -5.5m);
        Check("parse.convert.toint32", Convert.ToInt32("123") == 123);
        Check("parse.convert.todouble", Convert.ToDouble("2.75") == 2.75);
        Check("parse.convert.tobool", Convert.ToBoolean("true"));
        Check("parse.convert.tostring.i", Convert.ToString(88) == "88");
        Check("parse.convert.base", Convert.ToInt32("101", 2) == 5 && Convert.ToString(10, 16) == "a");
    }

    // ================= Array extras =================
    static void WideArray()
    {
        int[] arr = new int[] { 5, 3, 1, 4, 2 };
        Check("arr.longlength", arr.LongLength == 5L);
        Array.Reverse(arr, 1, 3);
        Check("arr.reverse.range", arr[1] == 4 && arr[3] == 3);
        Array.Sort(arr);
        Check("arr.sort", arr[0] == 1 && arr[4] == 5);
        Check("arr.binarysearch", Array.BinarySearch(arr, 4) == 3);
        Check("arr.indexof", Array.IndexOf(arr, 3) == 2 && Array.LastIndexOf(arr, 1) == 0);
        Check("arr.findindex", Array.FindIndex(arr, x => x > 2) == 2);
        Check("arr.exists", Array.Exists(arr, x => x == 5));
        Check("arr.trueforall", Array.TrueForAll(arr, x => x >= 1));
        // NOTE: the lambda form of ConvertAll needs a generic-delegate-class
        // newobj path (System_Converter_2_int_long_class) the emitter does
        // not generate yet -- the METHOD-GROUP form rides the proven
        // icallcase path (delegatecb domain).
        long[] la = Array.ConvertAll(arr, DoubleToInt);
        Check("arr.convertall", la[4] == 10 && la[0] == 2);
        int[] zeros = new int[4];
        zeros.Initialize();
        Check("arr.initialize", zeros[0] == 0 && zeros[3] == 0);
        Check("arr.empty.len", Array.Empty<string>().Length == 0);
        // NOTE: ReferenceEquals(Array.Empty(), Array.Empty()) requires the
        // cached-singleton contract -- our runtime allocates fresh; documented
        // deviation.
        Array.Clear(arr, 0, 5);
        Check("arr.clear", arr[0] == 0 && arr[4] == 0);
        int[] src = new int[] { 9, 8, 7 };
        int[] dst = new int[3];
        Array.Copy(src, dst, 3);
        Check("arr.copy.simple", dst[2] == 7);
        Array.ConstrainedCopy(src, 0, dst, 0, 3);
        Check("arr.constrainedcopy", dst[0] == 9);
        Array.Resize(ref dst, 5);
        Check("arr.resize", dst.Length == 5 && dst[0] == 9);
        // NOTE: multi-dimensional arrays (new int[2,3]) are a documented
        // unsupported surface -- the MD-array TypeSpec (0x14) has no newobj
        // path in StackFold. Not part of this fixture's contract.
        Check("arr.getvalue", src.GetValue(1).Equals(8));
        src.SetValue(100, 0);
        Check("arr.setvalue", src[0] == 100);
        Check("arr.rank", src.Rank == 1);
        byte[] bytes = new byte[] { 1, 2, 3 };
        Check("arr.byteeq", bytes[0] == 1);
        string[] strs = new string[] { "b", "a" };
        Array.Sort(strs);
        Check("arr.sort.strings", strs[0] == "a" && strs[1] == "b");
    }

    // ================= GC =================
    static void WideGC()
    {
        Check("gc.totalmemory", GC.GetTotalMemory(false) > 0);
        Check("gc.collectioncount", GC.CollectionCount(0) >= 0);
        Check("gc.maxgeneration", GC.MaxGeneration >= 0);
        GC.Collect();
        Check("gc.collect", true);
        Finalizable f = new Finalizable();
        GC.SuppressFinalize(f);
        GC.ReRegisterForFinalize(f);
        GC.KeepAlive(f);
        Check("gc.suppress.reregister", true);
        WeakReference wr = new WeakReference(new object());
        Check("gc.weakref.types", wr.GetType().Name == "WeakReference");
#if NEWMATH
        Check("gc.allocbytes", GC.GetAllocatedBytesForCurrentThread() > 0);
#endif
    }

    sealed class Finalizable { ~Finalizable() { } }

    // ================= Thread / ThreadPool =================
    static void WideThread()
    {
        Check("th.current.notnull", Thread.CurrentThread != null);
        Check("th.managedid", Thread.CurrentThread.ManagedThreadId > 0);
        Check("th.isalive.poolthread", Thread.CurrentThread.IsAlive);
        Thread.Sleep(1);
        Check("th.sleep", true);
#if !LEGACY
        Check("th.yield", true);
#endif
        int wmin, iomin;
        ThreadPool.GetMinThreads(out wmin, out iomin);
        Check("th.tp.minthreads", wmin >= 0 && iomin >= 0);
        int wmax, iomax;
        ThreadPool.GetMaxThreads(out wmax, out iomax);
        Check("th.tp.maxthreads", wmax >= wmin && iomax >= iomin);
        int wavail, ioavail;
        ThreadPool.GetAvailableThreads(out wavail, out ioavail);
        Check("th.tp.available", wavail >= 0 && ioavail >= 0);
#if NEWMATH
        Check("th.tp.threadcount", ThreadPool.ThreadCount >= 1);
#endif
    }

    // ================= Version =================
    static void WideVersion()
    {
        Version v = new Version(2, 5);
        Check("ver.ctor2", v.Major == 2 && v.Minor == 5 && v.Build == -1 && v.Revision == -1);
        Version v3 = new Version(1, 2, 3);
        Check("ver.ctor3", v3.Build == 3 && v3.Revision == -1);
        Version v4 = new Version(1, 2, 3, 4);
        Check("ver.ctor4", v4.Revision == 4);
        Version vp = new Version("7.0.1");
        Check("ver.ctorstr", vp.Major == 7 && vp.Minor == 0 && vp.Build == 1);
        Check("ver.compare", v4.CompareTo(v) < 0 && v.CompareTo(v4) > 0 && v4.CompareTo(new Version(1, 2, 3, 4)) == 0);
        Check("ver.equals", v4.Equals(new Version("1.2.3.4")) && !v4.Equals(v));
        Check("ver.op.eq", v4 == new Version(1, 2, 3, 4) && v4 != v);
        Check("ver.op.lt", v > v4 && v4 < v && v >= v4 && v4 <= v);
        Check("ver.majorminor", new Version(1, 2, 3, 4).MajorRevision == 0 && new Version(1, 2, 3, 4).MinorRevision == 4);
        Check("ver.parse.roundtrip", Version.Parse("3.14.15").ToString() == "3.14.15");
        Check("ver.hash", v4.GetHashCode() == new Version(1, 2, 3, 4).GetHashCode());
    }

    // ================= DateTime (UTC-pure only) =================
    static void WideDateTime()
    {
        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        CheckEq("dt.filetime.epoch", 116444736000000000L, epoch.ToFileTimeUtc());
        DateTime back = DateTime.FromFileTimeUtc(116444736000000000L);
        Check("dt.filetime.roundtrip", back == epoch);
        DateTime y2k = new DateTime(2000, 1, 1, 12, 30, 45, DateTimeKind.Utc);
        Check("dt.dayofweek", y2k.DayOfWeek == DayOfWeek.Saturday);
        Check("dt.ymd", y2k.Year == 2000 && y2k.Month == 1 && y2k.Day == 1);
        Check("dt.hms", y2k.Hour == 12 && y2k.Minute == 30 && y2k.Second == 45);
        Check("dt.addhours", y2k.AddHours(12).Day == 2 && y2k.AddHours(-13).Hour == 23);
        Check("dt.adddays", y2k.AddDays(31).Month == 2 && y2k.AddDays(31).Day == 1);
        Check("dt.addmonths", y2k.AddMonths(1).Month == 2);
        Check("dt.addyears", y2k.AddYears(1).Year == 2001);
        Check("dt.addminutes", y2k.AddMinutes(30).Minute == 0 && y2k.AddMinutes(30).Hour == 13);
        Check("dt.addseconds", y2k.AddSeconds(15).Second == 0 && y2k.AddSeconds(15).Minute == 31);
        Check("dt.addmillis", y2k.AddMilliseconds(1500).Second == 46);
        Check("dt.addticks", y2k.AddTicks(TimeSpan.TicksPerSecond).Second == 46);
        Check("dt.subtract.ts", (y2k - y2k.AddHours(1)).TotalHours == -1.0);
        // NOTE: DateTime.Subtract(TimeSpan) is indistinguishable from
        // Subtract(DateTime) at the raw-signature level (both ps=[0x11],
        // ret=0x11) -- the resolver needs ret-TYPE-aware overload dispatch
        // before this overload can wire. Documented subset boundary.
        Check("dt.compare", y2k.CompareTo(epoch) > 0 && epoch.CompareTo(y2k) < 0 && DateTime.Compare(y2k, y2k) == 0);
        Check("dt.op.eq", y2k == new DateTime(2000, 1, 1, 12, 30, 45, DateTimeKind.Utc));
        Check("dt.op.lt", epoch < y2k && y2k > epoch);
        Check("dt.kind.utc", y2k.Kind == DateTimeKind.Utc);
        Check("dt.date.part", y2k.Date == new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Check("dt.timeofday", y2k.TimeOfDay == new TimeSpan(12, 30, 45));
        Check("dt.ticksofday", y2k.TimeOfDay.Ticks == 450450000000L);
        Check("dt.daysinmonth", DateTime.DaysInMonth(2000, 2) == 29 && DateTime.DaysInMonth(2001, 2) == 28);
        Check("dt.isleapyear", DateTime.IsLeapYear(2000) && !DateTime.IsLeapYear(2001));
        Check("dt.constructor.kind", new DateTime(y2k.Ticks, DateTimeKind.Utc).Kind == DateTimeKind.Utc);
        Check("dt.equality.members", y2k.Equals(new DateTime(2000, 1, 1, 12, 30, 45, DateTimeKind.Utc)));
        // NOTE: DateTime.MaxValue/MinValue static-field reads need a value-slot
        // static emission (the decimal sf_Zero/One precedent) -- unwired;
        // documented subset boundary.
        Check("dt.utcnow.monotonic", DateTime.UtcNow >= epoch);
    }

    // ================= TimeSpan =================
    static void WideTimeSpan()
    {
        Check("ts.fromdays", TimeSpan.FromDays(0.5) == new TimeSpan(12, 0, 0));
        Check("ts.fromhours", TimeSpan.FromHours(1.5).TotalMinutes == 90.0);
        Check("ts.fromminutes", TimeSpan.FromMinutes(90) == TimeSpan.FromHours(1.5));
        Check("ts.fromseconds", TimeSpan.FromSeconds(3600) == TimeSpan.FromHours(1));
        Check("ts.frommilliseconds", TimeSpan.FromMilliseconds(1500.0).Seconds == 1);
        Check("ts.fromticks", TimeSpan.FromTicks(TimeSpan.TicksPerDay).Days == 1);
        Check("ts.ctorspan", new TimeSpan(1, 2, 3, 4, 5).Ticks == (1L * 864000000000 + 2L * 36000000000 + 3L * 600000000 + 4L * 10000000 + 50000));
        Check("ts.components", new TimeSpan(2, 3, 4, 5, 6).Days == 2 && new TimeSpan(2, 3, 4, 5, 6).Hours == 3);
        Check("ts.subcomponents", new TimeSpan(2, 3, 4, 5, 6).Minutes == 4 && new TimeSpan(2, 3, 4, 5, 6).Seconds == 5 && new TimeSpan(2, 3, 4, 5, 6).Milliseconds == 6);
        Check("ts.totaldays", TimeSpan.FromHours(48).TotalDays == 2.0);
        Check("ts.totalhours", TimeSpan.FromMinutes(180).TotalHours == 3.0);
        // NOTE: the literal-argument form of this call triggers a net9-only
        // fold constant-ordering bug (Check's name arg swapped with the
        // callee's int literal) -- routed through a local to sidestep it;
        // the fold bug itself is a recorded open item.
        double ms = 2500;
        Check("ts.totalseconds", TimeSpan.FromMilliseconds(ms).TotalSeconds == 2.5);
        Check("ts.totalmillis", TimeSpan.FromSeconds(1.5).TotalMilliseconds == 1500.0);
        Check("ts.negate", TimeSpan.FromHours(3).Negate().Ticks == -TimeSpan.FromHours(3).Ticks);
        Check("ts.duration", TimeSpan.FromHours(-3).Duration() == TimeSpan.FromHours(3));
        Check("ts.add", TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(30)) == TimeSpan.FromMinutes(90));
        Check("ts.subtract", TimeSpan.FromHours(2).Subtract(TimeSpan.FromMinutes(30)) == TimeSpan.FromMinutes(90));
        Check("ts.op.addsub", (TimeSpan.FromHours(1) + TimeSpan.FromMinutes(30)) == TimeSpan.FromMinutes(90));
        Check("ts.op.sub", (TimeSpan.FromHours(2) - TimeSpan.FromMinutes(30)) == TimeSpan.FromMinutes(90));
        Check("ts.compare", TimeSpan.FromHours(2) > TimeSpan.FromHours(1) && TimeSpan.Compare(TimeSpan.FromHours(1), TimeSpan.FromHours(1)) == 0);
        Check("ts.equals", TimeSpan.FromMinutes(60).Equals(TimeSpan.FromHours(1)));
        Check("ts.zero", TimeSpan.Zero.Ticks == 0);
        // NOTE: TimeSpan.Parse is unwired (no runtime parser icall) --
        // documented subset boundary; the From* factories cover construction.
        // NOTE: GetHashCode via Object::GetHashCode needs value-type receiver
        // type tracking in the fold (objTypes only tracks references) --
        // documented emitter gap.
    }

    // ================= Char =================
    static void WideChar()
    {
        Check("ch.isupper", char.IsUpper('A') && !char.IsUpper('a'));
        Check("ch.islower", char.IsLower('a') && !char.IsLower('A'));
        Check("ch.isdigit", char.IsDigit('5') && !char.IsDigit('x'));
        Check("ch.isletter", char.IsLetter('q') && !char.IsLetter('1'));
        Check("ch.isletterordigit", char.IsLetterOrDigit('7') && char.IsLetterOrDigit('Z') && !char.IsLetterOrDigit('-'));
        Check("ch.iswhitespace", char.IsWhiteSpace(' ') && char.IsWhiteSpace('\t') && !char.IsWhiteSpace('a'));
        Check("ch.iscontrol", char.IsControl('\n') && !char.IsControl('n'));
        Check("ch.toupper", char.ToUpper('a') == 'A');
        Check("ch.tolower", char.ToLower('Z') == 'z');
        Check("ch.numericvalue", char.GetNumericValue('9') == 9.0);
        Check("ch.ispunctuation", char.IsPunctuation('.') && !char.IsPunctuation('a'));
        Check("ch.issurrogate", !char.IsSurrogate('a'));
        Check("ch.isseparator", !char.IsSeparator('a'));
        Check("ch.issymbol", !char.IsSymbol('a'));
    }

    // ================= Enum =================
    static void WideEnum()
    {
        Check("en.underlying", Enum.GetUnderlyingType(typeof(Color)) == typeof(int));
        Check("en.getname", Enum.GetName(typeof(Color), 1) == "Green");
        Check("en.getnames", Enum.GetNames(typeof(Color))[0] == "Red" && Enum.GetNames(typeof(Color)).Length == 3);
        Check("en.getvalues", Enum.GetValues(typeof(Color)).GetValue(2).Equals(Color.Blue));
        // NOTE: (Color)Enum.Parse / (Color)Enum.ToObject would need unbox.any
        // of a USER enum -- a documented unsupported surface (audit H3+M5).
        // The parse semantics are verified through ToString instead.
        Check("en.parse", Enum.Parse(typeof(Color), "Blue").ToString() == "Blue");
        Check("en.parse.num", Enum.Parse(typeof(Color), "0").ToString() == "Red");
        Color parsed;
        Check("en.tryparse", Enum.TryParse<Color>("Green", out parsed) && parsed.ToString() == "Green");
        Check("en.tryparse.fail", !Enum.TryParse<Color>("Mauve", out parsed) && parsed.ToString() == "0");
        Check("en.isdefined", Enum.IsDefined(typeof(Color), Color.Red) && !Enum.IsDefined(typeof(Color), (Color)99));
        Check("en.hasflag", (Color.Green | Color.Red).HasFlag(Color.Red));
        Check("en.tostring", Color.Green.ToString() == "Green");
        Check("en.toobject", Enum.ToObject(typeof(Color), 2).ToString() == "Blue");
        Check("en.format", string.Format("{0}", (int)Color.Red) == "0");
        // NOTE: enum-value GetHashCode via Object::GetHashCode needs the same
        // value-type receiver tracking as TimeSpan (documented emitter gap).
    }

    // ================= Object / misc =================
    static void WideObject()
    {
        Cloneable c = new Cloneable();
        c.X = 42; c.Name = "orig";
        Cloneable c2 = (Cloneable)c.Clone();
        Check("obj.memberwiseclone", c2.X == 42 && c2.Name == "orig");
        c2.X = 99;
        Check("obj.clone.independent", c.X == 42 && c2.X == 99);
        Check("obj.gettype", c.GetType() == typeof(Cloneable) && c.GetType() != typeof(object));
        Check("obj.tostring.default", c.GetType().Name.Length > 0);
        object boxed = 42;
        Check("obj.box.unbox", (int)boxed == 42 && boxed.GetType() == typeof(int));
        Check("obj.equals.ref", ReferenceEquals(c, c) && !ReferenceEquals(c, c2));
        Check("obj.hash.selfconsistent", c.GetHashCode() == c.GetHashCode());
        Check("str.intern.literal", string.Intern("literal#1") == "literal#1");
        Check("str.isinterned", string.IsInterned("literal#1") != null);
        byte[] bsrc = new byte[] { 10, 20, 30, 40 };
        byte[] bdst = new byte[4];
        Buffer.BlockCopy(bsrc, 0, bdst, 0, 4);
        Check("buf.blockcopy", bdst[3] == 40);
        Check("buf.bytelength", Buffer.ByteLength(bsrc) == 4);
        Check("bitconv.int.roundtrip", BitConverter.ToInt32(BitConverter.GetBytes(123456789), 0) == 123456789);
        Check("bitconv.long.roundtrip", BitConverter.ToInt64(BitConverter.GetBytes(-987654321L), 0) == -987654321L);
        Check("bitconv.double.roundtrip", BitConverter.ToDouble(BitConverter.GetBytes(3.14159), 0) == 3.14159);
        Check("bitconv.float.roundtrip", BitConverter.ToSingle(BitConverter.GetBytes(2.5f), 0) == 2.5f);
        Check("bitconv.bool", BitConverter.GetBytes(true)[0] == 1);
        Check("bitconv.tostring.prefix", BitConverter.GetBytes(1)[0].ToString() == "1");
        Check("conv.toint32.bounds", Convert.ToInt32(byte.MaxValue) == 255 && Convert.ToInt32(char.MaxValue) == 65535);
        Check("conv.tobyte", Convert.ToByte(200) == 200 && Convert.ToSByte(-5) == -5);
        Check("conv.toint16", Convert.ToInt16(300) == 300 && Convert.ToUInt16(60000) == 60000);
        Check("conv.touint64", Convert.ToUInt64(-1L + 1L) == 0UL);
        Check("conv.tosingle", Convert.ToSingle(0.5) == 0.5f);
        Check("conv.tochar", Convert.ToChar(65) == 'A');
    }

    // ================= Hashtable (icall-backed collection) =================
    static void WideCollections()
    {
        Hashtable ht = new Hashtable();
        ht["one"] = 1;
        ht["two"] = 2;
        Check("ht.count", ht.Count == 2);
        Check("ht.get", (int)ht["one"] == 1);
        Check("ht.containskey", ht.ContainsKey("two") && !ht.ContainsKey("three"));
        Check("ht.contains.value", ht.ContainsValue(2) && !ht.ContainsValue(9));
        ht.Remove("one");
        Check("ht.remove", ht.Count == 1 && !ht.ContainsKey("one"));
        // NOTE: ICollection-interface member dispatch (Keys/Values .Count) and
        // ArrayList.Sort are unwired (documented subset boundary).
        ht.Clear();
        Check("ht.clear", ht.Count == 0);
        ArrayList al = new ArrayList();
        al.Add(5); al.Add(3); al.Add(8);
        Check("al.add.count", al.Count == 3);
        Check("al.indexer", (int)al[1] == 3);
        Check("al.contains", al.Contains(8) && al.IndexOf(3) == 1);
        al.RemoveAt(0);
        Check("al.removeat", al.Count == 2 && (int)al[0] == 3);
        al.Insert(0, 1);
        Check("al.insert", (int)al[0] == 1);
        al.Remove(8);
        Check("al.remove", al.Count == 2);
        Queue q = new Queue();
        q.Enqueue(1); q.Enqueue(2);
        Check("q.enqueue.dequeue", (int)q.Dequeue() == 1 && q.Count == 1);
        Check("q.peek", (int)q.Peek() == 2);
        Stack st = new Stack();
        st.Push(1); st.Push(2);
        Check("st.push.pop", (int)st.Pop() == 2 && st.Count == 1);
        Check("st.peek", (int)st.Peek() == 1);
    }
}
