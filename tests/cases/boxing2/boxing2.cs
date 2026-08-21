//------------------------------------------------------------------------------
// boxing2.cs -- end-to-end reference case for extended boxing/unboxing of the
// small integral types, long, float and ulong, plus Equals semantics.
//
// Runs on .NET Framework 4.8 (the reference runtime). The runtime domain
// (boxingext2) implements the corresponding icalls; the transpiled output
// must match this byte-for-byte once the wiring lands:
//   * box/unbox round-trips for byte/short/long/float/ulong;
//   * object.Equals between boxed values: value equality within the same
//     type, False across different boxed types;
//   * boxed float NaN: Single.Equals(NaN, NaN) == True (unlike ==);
//   * wrong-type unbox -> InvalidCastException, null unbox -> NullReferenceException.
//
// Every line of output is deterministic.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class Boxing2 {
    public static void Main() {
        // --- byte box/unbox ---
        object ob = (byte)200;
        Console.WriteLine((byte)ob);                      // 200
        Console.WriteLine(ob.GetType().Name);             // Byte
        Console.WriteLine(ob.Equals((byte)200));          // True
        Console.WriteLine(ob.Equals((byte)201));          // False
        Console.WriteLine(ob.Equals((object)(sbyte)100)); // False (Byte vs SByte)
        object ob2 = (byte)100;
        Console.WriteLine(ob2.Equals((object)(sbyte)100));// False (Byte vs SByte)
        Console.WriteLine(ob2.Equals((byte)100));         // True
        // --- short box/unbox ---
        object os = (short)-32768;
        Console.WriteLine((short)os);                     // -32768
        Console.WriteLine(os.GetType().Name);             // Int16
        Console.WriteLine(os.Equals((short)-32768));      // True
        object os2 = (short)32767;
        Console.WriteLine((short)os2);                    // 32767
        Console.WriteLine(os.Equals((short)32767));       // False
        // --- long box/unbox ---
        object ol = 1234567890123L;
        Console.WriteLine((long)ol);                      // 1234567890123
        Console.WriteLine(ol.GetType().Name);             // Int64
        Console.WriteLine(ol.Equals(1234567890123L));     // True
        Console.WriteLine(ol.Equals(1234567890124L));     // False
        Console.WriteLine(ol.Equals(1234567890123));      // True (literal overflows int, is a long)
        object omin = long.MinValue;
        Console.WriteLine((long)omin);                    // -9223372036854775808
        Console.WriteLine(omin.Equals(long.MinValue));    // True
        // --- float box/unbox ---
        object of = 1.5f;
        Console.WriteLine((float)of);                     // 1.5
        Console.WriteLine(of.GetType().Name);             // Single
        Console.WriteLine(of.Equals(1.5f));               // True
        Console.WriteLine(of.Equals(1.5));                // False (float vs double)
        object of2 = (float)float.NaN;
        Console.WriteLine(of2.Equals((float)float.NaN));  // True (Single.Equals)
        Console.WriteLine(of2.Equals(float.PositiveInfinity)); // False
        // --- ulong box/unbox ---
        object ou = 18446744073709551615UL;
        Console.WriteLine((ulong)ou);                     // 18446744073709551615
        Console.WriteLine(ou.GetType().Name);             // UInt64
        Console.WriteLine(ou.Equals(18446744073709551615UL)); // True
        Console.WriteLine(ou.Equals(18446744073709551614UL)); // False
        Console.WriteLine(ou.Equals(18446744073709551615L));  // False (UInt64 vs Int64)
        // --- object.Equals / ReferenceEquals on two boxed copies ---
        object b1 = (byte)7, b2 = (byte)7;
        Console.WriteLine(object.Equals(b1, b2));         // True (value equality)
        Console.WriteLine(ReferenceEquals(b1, b2));       // False (distinct boxes)
        // --- wrong-type unbox ---
        try { short x = (short)ob; Console.WriteLine(x); }
        catch (InvalidCastException e) { Console.WriteLine(e.GetType().Name); } // InvalidCastException
        // --- null unbox ---
        try { int x = (int)(object)null; Console.WriteLine(x); }
        catch (NullReferenceException e) { Console.WriteLine(e.GetType().Name); } // NullReferenceException
    }
}}
