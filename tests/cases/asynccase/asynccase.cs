using System;
using System.Threading.Tasks;
namespace Cases { public static class AsyncCase {
    public static void Main() {
        Console.WriteLine(F().Result);
        Console.WriteLine(F().Result);
        Console.WriteLine(H().Result);
        Console.WriteLine(H().Result);
        // GC pressure: repeated async calls interleaved with allocations --
        // the state machine struct's builder/awaiter ref fields must survive
        // collections (batch 156c structFieldRoots).
        int total = 0;
        for (int i = 0; i < 200; i++) {
            total += F().Result;
        }
        Console.WriteLine(total);
        Console.WriteLine(H().Result);
        // exception path (batch 156d): the faulted task is completed; its
        // Result rethrows.
        Task<int> ft = T();
        Console.WriteLine(ft.IsCompleted);
        try {
            int r = ft.Result;
            Console.WriteLine("no-throw:" + r);
        } catch (System.Exception) {
            Console.WriteLine("caught");
        }
        Task<int> ft2 = T();
        Console.WriteLine(ft2.IsCompleted);
        try {
            int r = ft2.Result;
            Console.WriteLine("no-throw:" + r);
        } catch (System.Exception ex) {
            // batch 162: Task.Result wraps the fault in AggregateException
            // (net48 semantics -- the catch var is the exception's SHORT
            // type name in the M-model).
            Console.WriteLine(ex.GetType().Name);
        }
        // batch 162: GetAwaiter().GetResult() rethrows the ORIGINAL
        // exception (net48 unwraps the AggregateException).
        try {
            int r = T().GetAwaiter().GetResult();
            Console.WriteLine("no-throw:" + r);
        } catch (System.Exception ex) {
            Console.WriteLine(ex.GetType().Name);
        }
        Console.WriteLine(L().Result);
        Console.WriteLine(L().Result);
        Console.WriteLine(Big().Result);
        Console.WriteLine(Big().Result);
        // batch 163: Task<double> and Task<string> result carriers.
        Console.WriteLine(D().Result);
        Console.WriteLine(D().Result == 3.5);
        Console.WriteLine(S().Result);
        Console.WriteLine(S().Result == "hello");
        // batch 164: Task<bool> and Task<char> ride the int32 carrier.
        Console.WriteLine(B().Result);
        Console.WriteLine(C().Result);
        Console.WriteLine(C().Result == 'x');
        // batch 165: Task<ulong> / Task<object> / Task<float> carriers.
        Console.WriteLine(UF().Result);
        Console.WriteLine(UF().Result == 3000000001UL);
        Console.WriteLine(OF().Result == null);
        object o = OF().Result;
        Console.WriteLine(o == null ? "null" : o.GetType().Name);
        Console.WriteLine(FF().Result);
        Console.WriteLine(FF().Result == 2.5f);
        // batch 165b: Task<int[]> (array ref carrier) and Task<custom class>
        // (class ref carrier).
        int[] a = AF().Result;
        Console.WriteLine(a != null && a.Length == 3 && a[2] == 9);
        Point p = PF().Result;
        Console.WriteLine(p == null ? "null" : p.GetType().Name);
        // batch 169: the Task<T> audit -- small-integer family all ride the
        // int32 carrier (byte/sbyte/short/ushort/uint) with exact box types
        // (GetType().Name Byte/SByte/Int16/UInt16/UInt32), and Task<string[]>
        // rides the _s ref carrier (SZARRAY).
        Console.WriteLine(BY().Result);
        Console.WriteLine(BY().Result.GetType().Name);
        Console.WriteLine(SB().Result);
        Console.WriteLine(SB().Result.GetType().Name);
        Console.WriteLine(SH().Result);
        Console.WriteLine(US().Result);
        Console.WriteLine(UI().Result);
        Console.WriteLine(SA().Result.Length);
        Console.WriteLine(SA().Result[2]);
        // batch 166: Task.Delay(0) is a completed task (net48 fast path --
        // byte-exact). Delay(n>0) is a loud NotSupportedException at runtime
        // (the synchronous-completion model cannot suspend), tested in the
        // runtime suite instead of the byte-exact e2e.
        Console.WriteLine(Task.Delay(0).IsCompleted);
    }
    // async Task<bool> -- int32 carrier (bool box).
    static async Task<bool> B() {
        bool b = true;
        b = await Task.FromResult(!b);
        return b;
    }
    // async Task<char> -- int32 carrier (char box).
    static async Task<char> C() {
        char c = 'a';
        c = await Task.FromResult((char)(c + 23));
        return c;
    }
    // async Task<double> -- boxed double carrier.
    static async Task<double> D() {
        double d = 1.5;
        d = await Task.FromResult(d + 2.0);
        return d;
    }
    // async Task<string> -- string carrier.
    static async Task<string> S() {
        string s = "hel";
        s = await Task.FromResult(s + "lo");
        return s;
    }
    // async with int value-type hoisted local + awaited completed task.
    static async Task<int> F() {
        int a = 7;
        a = await G(a);
        return a + 1;
    }
    static Task<int> G(int x) {
        // batch 156c GC proof: force a collection while F()'s state machine
        // is suspended in Start/MoveNext -- the builder object lives ONLY in
        // the state machine struct local's <>t__builder field (a registered
        // frame root). A collection here would sweep it without the root.
        GC.Collect();
        return Task.FromResult(x + 1);
    }
    // async with a second await (two awaiter fields, two states).
    static async Task<int> H() {
        int c = 3;
        c = await Task.FromResult(c * 2);
        GC.Collect();
        c = await Task.FromResult(c + 4);
        return c;
    }
    // async that THROWS (batch 156d): the state machine's catch handler
    // calls SetException; get_Result on the faulted task rethrows.
    static async Task<int> T() {
        int a = 1;
        a = await Task.FromResult(a + 1);
        if (a == 2) throw new System.Exception("async boom");
        return a;
    }
    // async with an int64 hoisted local (batch 156d: the state machine
    // struct's int64 field -- long value-type field coverage).
    static async Task<long> L() {
        long b = 100;
        b = await Task.FromResult(b + 5);
        return b * 2;
    }
    // async with a > 2^31 long (batch 160: the int64 carrier must not
    // truncate through the int32 result icalls).
    static async Task<long> Big() {
        long b = 3000000000L;
        b = await Task.FromResult(b + 1);
        return b * 2;
    }
    // batch 165: async Task<ulong> -- the UInt64 box carrier (0x0B). The
    // result rides the bit-preserving int64 slot; WriteLine(ulong) and the
    // == comparison reinterpret unsigned via the "q" cast.
    static async Task<ulong> UF() {
        ulong b = 3000000000UL;
        b = await Task.FromResult(b + 1);
        return b;
    }
    // batch 165: async Task<object> -- rides the "s" ref carrier (the
    // object IS the result, no box). A null result round-trips as null.
    static async Task<object> OF() {
        return await Task.FromResult<object>(null);
    }
    // batch 165: async Task<float> -- the Single box carrier (0x0C); the M4
    // ABI carries float as double, so the value flows through the double
    // slot and boxes as System.Single.
    static async Task<float> FF() {
        float x = 1.5f;
        x = await Task.FromResult(x + 1.0f);
        return x;
    }
    // batch 165b: async Task<int[]> -- the array IS a managed reference, so
    // it rides the ref carrier (SZARRAY generic-arg decode in TypeEmitter).
    static async Task<int[]> AF() {
        int[] x = { 1, 2 };
        x = await Task.FromResult(new int[] { 1, 2, 9 });
        return x;
    }
    // batch 165b: async Task<Cases.AsyncCase.Point> -- a custom CLASS rides
    // the ref carrier (0x12 CLASS generic-arg).
    static async Task<Point> PF() {
        return await Task.FromResult<Point>(null);
    }
    // batch 169: Task<byte> -- int32 carrier. 200+55 = 255, the byte maximum
    // (the byte wraps in the int32 slot, no sign extension on the return).
    static async Task<byte> BY() {
        byte v = 200;
        v = await Task.FromResult((byte)(v + 55));
        return v;
    }
    // batch 169: Task<sbyte> -- int32 carrier. Negative value round-trips.
    static async Task<sbyte> SB() {
        sbyte v = -100;
        v = await Task.FromResult((sbyte)(v + 50));
        return v;
    }
    // batch 169: Task<short> -- int32 carrier. 31000 crosses 2^15, the
    // value must not truncate through the int32 slot.
    static async Task<short> SH() {
        short v = 30000;
        v = await Task.FromResult((short)(v + 1000));
        return v;
    }
    // batch 169: Task<ushort> -- int32 carrier. 61000 > 2^15 unsigned.
    static async Task<ushort> US() {
        ushort v = 60000;
        v = await Task.FromResult((ushort)(v + 1000));
        return v;
    }
    // batch 169: Task<uint> -- int32 carrier. 4000000001 > 2^31: the value
    // must not truncate through the int32 slot (bit pattern preserved).
    static async Task<uint> UI() {
        uint v = 4000000000U;
        v = await Task.FromResult(v + 1);
        return v;
    }
    // batch 169: Task<string[]> -- the SZARRAY rides the _s ref carrier.
    static async Task<string[]> SA() {
        string[] a = { "x" };
        a = await Task.FromResult(new string[] { "x", "y", "z" });
        return a;
    }
    class Point { }
}}
