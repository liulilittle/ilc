using System;
using System.Threading;

internal static class Program
{
    private static int _fail;

    private static void Expect(bool ok, string what)
    {
        Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
        if (!ok) _fail++;
    }

    private static int Main()
    {
        // Signed 32/64 already worked; these cover the UNSIGNED widths plus the
        // .NET 9 And/Or pair, all of which are bodiless public wrappers with no
        // IL body -- an unmapped arm emitted an undefined symbol, so the whole
        // program failed to compile rather than misbehaving at run time.
        int i = 1;
        Expect(Interlocked.Exchange(ref i, 5) == 1 && i == 5, "a.i32_exchange");

        uint u = 2;
        Expect(Interlocked.Exchange(ref u, 9u) == 2u && u == 9u, "b.u32_exchange");
        Expect(Interlocked.CompareExchange(ref u, 7u, 9u) == 9u && u == 7u, "c.u32_cas");
        Expect(Interlocked.Increment(ref u) == 8u, "d.u32_increment");
        Expect(Interlocked.Add(ref u, 4u) == 12u, "e.u32_add");

        ulong ul = 3;
        Expect(Interlocked.Add(ref ul, 4ul) == 7ul, "f.u64_add");
        Expect(Interlocked.Increment(ref ul) == 8ul, "g.u64_increment");

        long l = 8;
        Expect(Interlocked.Or(ref l, 4) == 8 && l == 12, "h.i64_or");
        Expect(Interlocked.And(ref l, 10) == 12 && l == 8, "i.i64_and");

        long l2 = 8;
        Expect(Interlocked.And(ref l2, 10) == 8 && l2 == 8, "j.i64_and_identity");

        return _fail == 0 ? 0 : 1;
    }
}
