using System;
namespace Cases { public static class ConvDeep {
    // Deep conversion-semantics pins (net48 reference baseline). This case
    // regression-pins two fixes from the P0 number-conversion batch:
    //   * convertcase fix: Convert.ToInt32(double) is a CHECKED round
    //     half-to-even (Math.Round semantics), not truncation -- 2.9 -> 3,
    //     3.5 -> 4, 2.5 -> 2 (even neighbor); NaN and values rounding past
    //     int32 range throw OverflowException.
    //   * valdeep fix: conv.u8 on a non-constant 32-bit source must
    //     ZERO-EXTEND the int32 slot as uint32 -- (ulong)uint.MaxValue ==
    //     4294967295UL, not -1 (icall_conv_u8_i4 path).
    // Plus checked-context conversion probes (conv.ovf.i4 ->
    // icall_conv_ovf_i4_i8) and the int -> double -> int round trip (exact:
    // every int32 is representable in double). All bool probes (no
    // ToString, per e2e convention); exceptions are caught and asserted by
    // type (messages are OS-localized, never byte-diffed).
    public static void Main() {
        // --- Convert.ToInt32(double): round half-to-even (convertcase fix) ---
        Console.WriteLine(Convert.ToInt32(2.9) == 3);                  // True (2.9 rounds to 3)
        Console.WriteLine(Convert.ToInt32(-2.9) == -3);                // True (-2.9 rounds to -3)
        Console.WriteLine(Convert.ToInt32(3.5) == 4);                  // True (3.5 -> even 4)
        Console.WriteLine(Convert.ToInt32(2.5) == 2);                  // True (2.5 -> even 2)
        Console.WriteLine(Convert.ToInt32(-3.5) == -4);                // True (-3.5 -> even -4)
        Console.WriteLine(Convert.ToInt32(-2.5) == -2);                // True (-2.5 -> even -2)

        // --- Convert.ToInt32(double): out-of-range throws OverflowException ---
        try { Convert.ToInt32(double.MaxValue); Console.WriteLine(false); }
        catch (OverflowException) { Console.WriteLine(true); }         // True (double.MaxValue)
        try { Convert.ToInt32(double.NaN); Console.WriteLine(false); }
        catch (OverflowException) { Console.WriteLine(true); }         // True (NaN)
        try { Convert.ToInt32(2147483647.5); Console.WriteLine(false); }
        catch (OverflowException) { Console.WriteLine(true); }         // True (rounds to 2^31)

        // --- Convert.ToInt32(long): in-range values and checked range check ---
        Console.WriteLine(Convert.ToInt32(0L) == 0);                   // True (0)
        Console.WriteLine(Convert.ToInt32(42L) == 42);                 // True (42)
        Console.WriteLine(Convert.ToInt32(2147483647L) == int.MaxValue);   // True (int.MaxValue)
        Console.WriteLine(Convert.ToInt32(-2147483648L) == int.MinValue); // True (int.MinValue)
        try { Convert.ToInt32(2147483648L); Console.WriteLine(false); }
        catch (OverflowException) { Console.WriteLine(true); }         // True (int.MaxValue+1)
        try { Convert.ToInt32(-2147483649L); Console.WriteLine(false); }
        catch (OverflowException) { Console.WriteLine(true); }         // True (int.MinValue-1)

        // --- conv.u8: 32-bit source zero-extends as uint32 (valdeep fix) ---
        Console.WriteLine((ulong)uint.MaxValue == 4294967295UL);       // True (const path)
        object ou = 4294967295u;
        uint u = (uint)ou;                                             // runtime (unbox) value
        Console.WriteLine((ulong)u == 4294967295UL);                   // True (non-const path)
        Console.WriteLine((long)u == 4294967295L);                     // True (conv.u8 to long)
        object os = 1u;
        uint small = (uint)os;
        Console.WriteLine((ulong)small == 1UL);                        // True (small non-const)

        // --- checked context: conv.ovf.i4 range checks (runtime icall) ---
        long l1 = 2147483648L;
        try { int z1 = checked((int)l1); Console.WriteLine(false); }
        catch (OverflowException) { Console.WriteLine(true); }         // True (2^31 out of range)
        long l2 = -2147483649L;
        try { int z2 = checked((int)l2); Console.WriteLine(false); }
        catch (OverflowException) { Console.WriteLine(true); }         // True (-2^31-1 out of range)
        long l3 = 42L;
        Console.WriteLine(checked((int)l3) == 42);                     // True (in range)
        long l4 = -2147483648L;
        Console.WriteLine(checked((int)l4) == int.MinValue);           // True (boundary legal)
        int a = int.MaxValue;
        Console.WriteLine(a + 1 == int.MinValue);                      // True (unchecked wraps)

        // --- int -> double -> int round trip (exact for all int32) ---
        Console.WriteLine((int)(double)42 == 42);                      // True (const)
        Console.WriteLine((int)(double)-42 == -42);                    // True (const)
        object oi = 123456789;
        int rt = (int)oi;                                              // runtime (unbox) value
        Console.WriteLine((int)(double)rt == 123456789);               // True (runtime round trip)
        Console.WriteLine((int)(double)int.MaxValue == int.MaxValue);  // True (const, boundary)
        Console.WriteLine((int)(double)int.MinValue == int.MinValue);  // True (const, boundary)
    }
}}
