using System;

namespace Cases { public static class ConvCase {
    // The C# language has no explicit int -> bool cast; the runtime primitive
    // icall_conv_bool_i4 (conv.i1/conv.u1 to bool: non-zero -> 1) is exercised
    // through this helper, mirroring how generated code emits the conversion.
    private static bool ToBool(int v) { return v != 0; }

    // The overflow-intentional constant conversions must be written with
    // explicit unchecked(...): C# constant folding is checked even when the
    // build default is unchecked (a constant that overflows is CS0221 unless
    // the context says unchecked).
    public static void Main() {
        // --- int32 -> int64 (conv.i8): sign extension ---
        Console.WriteLine((long)0);               // 0
        Console.WriteLine((long)-1);              // -1
        Console.WriteLine((long)int.MaxValue);    // 2147483647
        Console.WriteLine((long)int.MinValue);    // -2147483648

        // --- int64 -> int32 (conv.i4, unchecked): low 32 bits ---
        Console.WriteLine(unchecked((int)(long)0x123456789ABCDEF0)); // 0x9ABCDEF0 as int32
        Console.WriteLine((int)(long)int.MaxValue);       // 2147483647
        Console.WriteLine((int)(long)int.MinValue);       // -2147483648
        Console.WriteLine(unchecked((int)long.MaxValue)); // low 32 bits: -1
        Console.WriteLine(unchecked((int)long.MinValue)); // low 32 bits: 0
        Console.WriteLine(unchecked((int)0x100000000L));  // low 32 bits: 0

        // --- double -> int64 (conv.i8, unchecked): truncate toward zero;
        // overflow / NaN / Infinity -> "integer indefinite" (constants need
        // explicit unchecked; the runtime path via variables is probed again
        // below) ---
        Console.WriteLine((long)0.9);                     // 0
        Console.WriteLine((long)1.5);                     // 1
        Console.WriteLine((long)-0.5);                    // 0
        Console.WriteLine((long)-1.5);                    // -1
        Console.WriteLine((long)5.7);                     // 5
        Console.WriteLine((long)-5.7);                    // -5
        Console.WriteLine((long)9223372036854774784.0);   // 2^63 - 1024 (largest double < 2^63)
        Console.WriteLine(unchecked((long)9223372036854775808.0));  // 2^63 exactly -> out of range
        Console.WriteLine((long)-9223372036854775808.0);  // -2^63 -> boundary
        Console.WriteLine(unchecked((long)-9223372036854777856.0)); // -(2^63 + 2048) -> out of range
        Console.WriteLine(unchecked((long)double.NaN));   // NaN
        Console.WriteLine(unchecked((long)double.PositiveInfinity)); // +Infinity
        Console.WriteLine(unchecked((long)double.NegativeInfinity)); // -Infinity
        Console.WriteLine(unchecked((long)1.0e300));      // far out of range

        // --- double -> int64: the RUNTIME path (variables -- the JIT's
        // conversion, not Roslyn's constant folding) ---
        double d2p63 = 9223372036854775808.0;     // 2^63 exactly
        double d2p63m1024 = 9223372036854774784.0;
        double dm2p63 = -9223372036854775808.0;   // -2^63 exactly
        double dm2p63m2048 = -9223372036854777856.0;
        double dNaN = double.NaN;
        double dPosInf = double.PositiveInfinity;
        double dNegInf = double.NegativeInfinity;
        double dBig = 1.0e300;
        Console.WriteLine("rt 2^63-1024:   " + (long)d2p63m1024);
        Console.WriteLine("rt 2^63:        " + (long)d2p63);
        Console.WriteLine("rt -2^63:       " + (long)dm2p63);
        Console.WriteLine("rt -(2^63+2048):" + (long)dm2p63m2048);
        Console.WriteLine("rt NaN:         " + (long)dNaN);
        Console.WriteLine("rt +Inf:        " + (long)dPosInf);
        Console.WriteLine("rt -Inf:        " + (long)dNegInf);
        Console.WriteLine("rt 1e300:       " + (long)dBig);

        // --- double -> int32 (conv.i4, unchecked): truncate toward zero;
        // overflow / NaN / Infinity -> "integer indefinite" ---
        Console.WriteLine((int)0.9);              // 0
        Console.WriteLine((int)1.5);              // 1
        Console.WriteLine((int)-0.5);             // 0
        Console.WriteLine((int)-1.5);             // -1
        Console.WriteLine((int)2147483647.0);     // 2^31 - 1 -> in range
        Console.WriteLine((int)2147483647.9);     // truncation
        Console.WriteLine(unchecked((int)2147483648.0));  // 2^31 -> out of range
        Console.WriteLine((int)-2147483648.0);    // -2^31 -> boundary
        Console.WriteLine(unchecked((int)-2147483649.0)); // rounds to -2^31 as double
        Console.WriteLine(unchecked((int)double.NaN));    // NaN
        Console.WriteLine(unchecked((int)double.PositiveInfinity)); // +Infinity

        // --- double -> int32: the RUNTIME path (variables -- the JIT's
        // conversion, not Roslyn's constant folding) ---
        double i2p31 = 2147483648.0;              // 2^31 exactly
        double i2p31m1 = 2147483647.0;
        double im2p31 = -2147483648.0;            // -2^31 exactly
        double im2p31m1 = -2147483649.0;          // rounds to -2^31 as double
        double iNaN = double.NaN;
        double iPosInf = double.PositiveInfinity;
        Console.WriteLine("rt32 2^31-1:  " + (int)i2p31m1);
        Console.WriteLine("rt32 2^31:    " + (int)i2p31);
        Console.WriteLine("rt32 -2^31:   " + (int)im2p31);
        Console.WriteLine("rt32 -(2^31+1):" + (int)im2p31m1);
        Console.WriteLine("rt32 NaN:     " + (int)iNaN);
        Console.WriteLine("rt32 +Inf:    " + (int)iPosInf);

        // --- int64 -> double (conv.r8): correct rounding ---
        Console.WriteLine((double)5L);                 // 5
        Console.WriteLine((double)-123456789L);        // -123456789
        Console.WriteLine((double)long.MaxValue);      // rounds to 2^63
        Console.WriteLine((double)long.MinValue);      // -2^63
        Console.WriteLine((double)(1L << 53));         // 2^53 exact

        // --- int32 -> double (conv.r8): exact ---
        Console.WriteLine((double)0);             // 0
        Console.WriteLine((double)-1);            // -1
        Console.WriteLine((double)int.MaxValue);  // 2147483647
        Console.WriteLine((double)int.MinValue);  // -2147483648

        // --- int32 <-> uint32 (conv.u4 / conv.i4 bit patterns) ---
        Console.WriteLine((uint)0);               // 0
        Console.WriteLine(unchecked((uint)-1));   // 4294967295
        Console.WriteLine(unchecked((uint)int.MinValue)); // 2147483648
        Console.WriteLine(unchecked((int)uint.MaxValue)); // -1
        Console.WriteLine(unchecked((int)0x80000000u));   // -2147483648

        // --- Console.Write(uint): no newline, full-range unsigned decimal ---
        Console.Write(unchecked((uint)-1));       // 4294967295 (never -1)
        Console.WriteLine();
        Console.Write(uint.MaxValue);             // 4294967295
        Console.WriteLine();
        Console.Write(0x80000000u);               // 2147483648
        Console.WriteLine();

        // --- int64 <-> uint64 (conv.u8 / conv.i8 bit patterns) ---
        Console.WriteLine((ulong)0L);                  // 0
        Console.WriteLine(unchecked((ulong)-1L));      // 18446744073709551615
        Console.WriteLine(unchecked((ulong)long.MinValue)); // 9223372036854775808
        Console.WriteLine(unchecked((long)ulong.MaxValue)); // -1
        Console.WriteLine(unchecked((long)0x8000000000000000UL)); // -9223372036854775808

        // --- int32 -> bool (conv.i1/conv.u1): non-zero -> true ---
        Console.WriteLine(ToBool(0));             // False
        Console.WriteLine(ToBool(1));             // True
        Console.WriteLine(ToBool(-7));            // True
        Console.WriteLine(ToBool(int.MinValue));  // True

        // --- checked conversions (conv.ovf.i8 / conv.ovf.i4) ---
        // The overflow cases go through VARIABLES: checked() constant-folds,
        // so a literal overflow is a compile-time error (CS0221) -- the
        // runtime check only fires on non-constant operands.
        long vMax32 = int.MaxValue;
        long vMin32 = int.MinValue;
        long vMax64 = long.MaxValue;
        long vMin64 = long.MinValue;
        long vPlus1 = (long)int.MaxValue + 1;
        try { Console.WriteLine("ovf i8_i4: " + checked((long)int.MaxValue)); }
        catch (OverflowException) { Console.WriteLine("ovf i8_i4: OVERFLOW"); }
        try { Console.WriteLine("ovf i4_i8: " + checked((int)12345L)); }
        catch (OverflowException) { Console.WriteLine("ovf i4_i8: OVERFLOW"); }
        try { Console.WriteLine("ovf i4_i8 max32: " + checked((int)vMax32)); }
        catch (OverflowException) { Console.WriteLine("ovf i4_i8 max32: OVERFLOW"); }
        try { Console.WriteLine("ovf i4_i8 min32: " + checked((int)vMin32)); }
        catch (OverflowException) { Console.WriteLine("ovf i4_i8 min32: OVERFLOW"); }
        try { Console.WriteLine("ovf i4_i8 max64: " + checked((int)vMax64)); }
        catch (OverflowException) { Console.WriteLine("ovf i4_i8 max64: OVERFLOW"); }
        try { Console.WriteLine("ovf i4_i8 min64: " + checked((int)vMin64)); }
        catch (OverflowException) { Console.WriteLine("ovf i4_i8 min64: OVERFLOW"); }
        try { Console.WriteLine("ovf i4_i8 +1: " + checked((int)vPlus1)); }
        catch (OverflowException) { Console.WriteLine("ovf i4_i8 +1: OVERFLOW"); }
    }
}}
