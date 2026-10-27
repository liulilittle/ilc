//------------------------------------------------------------------------------
// closure-dt.cs -- a CLOSURE-CAPTURED VALUE TYPE keeps its real runtime value
// slot in the nested display class.
//
// Why this case exists: a captured value-type local of a closure lives in the
// compiler-generated nested display class (<>c__DisplayClassN_0). The nested
// field decode collapsed every value type to an int32 slot, so a captured
// DateTime became `int32_t d;` while the IL value is the 16-byte
// il2cpp::runtime::Il2CppDateTime:
//   * the store (->d = t1) did not compile (int32_t = Il2CppDateTime), and
//   * the read (icall_datetime_get_year(*reinterpret_cast<Il2CppDateTime*>
//     (&->d))) silently took 8 bytes out of a 4-byte slot.
//
// The contract under test: the captured value type occupies its REAL runtime
// value slot (DateTime/Decimal 16 bytes = two pointer-size slots, TimeSpan
// 8 bytes = one slot, Guid 16 bytes = two slots), the neighbouring captured
// fields keep their own slots, and the captured VALUE survives the round trip.
//
// Coverage:
//   C1  PURE   closure capturing a DateTime (the captured field is read).
//   C2  IMPURE closure capturing a DateTime AND a counter (a 16-byte value
//              field next to an int32 field in the same display class).
//   C3  PURE   closure capturing a TimeSpan (8-byte value struct).
//   C4  PURE   closure capturing a decimal (16-byte value struct).
//   C5  PURE   closure capturing a Guid (16-byte value struct, the B3
//              precedent that already worked).
//
// Determinism: every assertion is an integer, a boolean or a fixed literal
// (Ticks / Year / equality) -- no culture, no clock, no randomness.
//------------------------------------------------------------------------------
using System;

public static class ClosureDt
{
    // C1: the captured DateTime is read through the display class.
    private static int PureDateTimeYear(DateTime d)
    {
        Func<int> f = () => d.Year;
        return f();
    }

    // C2: the captured DateTime and a captured counter share ONE display
    // class -- the 16-byte value field and the int32 field must both keep
    // their own slot (a truncated slot would corrupt the neighbour).
    private static long ImpureDateTimeTicks(DateTime d, int seed)
    {
        int calls = seed;
        Func<long> f = () => { calls++; return d.Ticks + calls; };
        return f();
    }

    // C3: a second captured value type -- TimeSpan (8 bytes).
    private static long PureTimeSpanTicks(TimeSpan ts)
    {
        Func<long> f = () => ts.Ticks;
        return f();
    }

    // C4: a third captured value type -- decimal (16 bytes). The comparison
    // happens INSIDE the closure, so the operand is the captured FIELD (an
    // lvalue) -- the decimal comparison takes its operand's address. (A
    // decimal comparison whose operand is a delegate-invoke RESULT -- a
    // prvalue -- is a pre-existing gap, unrelated to the closure slot; see
    // docs/audit/b5-gaps.md.)
    private static bool PureDecimalEq(decimal m)
    {
        Func<bool> f = () => m == 2.5m;
        return f();
    }

    // C5: the B3 precedent -- Guid (16 bytes).
    private static bool PureGuidEq(Guid g)
    {
        Func<Guid> f = () => g;
        return f() == g;
    }

    public static void Main()
    {
        DateTime d = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
        TimeSpan ts = TimeSpan.FromSeconds(90);
        Guid g = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        Console.WriteLine("[cdt] year=" + PureDateTimeYear(d));        // 2020
        Console.WriteLine("[cdt] ticks=" + ImpureDateTimeTicks(d, 41)); // 637135310450000042
        Console.WriteLine("[cdt] ts=" + PureTimeSpanTicks(ts));        // 900000000
        Console.WriteLine("[cdt] dec=" + PureDecimalEq(2.5m));         // True
        Console.WriteLine("[cdt] guid=" + PureGuidEq(g));              // True
    }
}
