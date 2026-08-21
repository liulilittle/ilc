//------------------------------------------------------------------------------
// boxops.cs -- BOX/UNBOX DEEP e2e reference case (net48 oracle, byte-for-byte).
// Created in e2e batch C16 (2026-08-10).
//
// Exercises the boxed-VALUE surface after the box-domain audit (batches 21/
// 22b/35 landed the numeric box classes + interface tables + value-semantics
// dispatch). Every line prints True/False -- a diff failure shows exactly
// which assertion flipped.
//
// Covered families:
//   1. BOXED ToString -- value-based text for every boxable primitive:
//      int/long/uint/ulong/byte/sbyte/short/ushort/double/float/bool/char
//      (the icall_object_to_string dispatch, valuesemantics.cpp).
//   2. BOXED GetHashCode -- VALUE-BASED: two distinct boxes with the same
//      value hash identically; a boxed Int32 hashes to its own value
//      (net48 Int32.GetHashCode == the value). String boxes hash by
//      CONTENT (two distinct instances with equal content -> equal hash).
//   3. BOXED Equals CROSS-TYPE -- value equality is false across different
//      boxed types even for numerically equal values (Int32 vs Int64, Int32
//      vs Byte, Int32 vs Short, Double vs Single, Boolean vs Int32 -- net48
//      Object.Equals gate); true within the same type incl. Double.Equals
//      NaN==NaN and +0.0==-0.0.
//   4. IConvertible -- GetTypeCode() through the interface cast on boxed
//      primitives (TypeCode.Int32/Int64/Boolean/Char/Double/Single/Byte/
//      String). net48 values are the empirical ground truth (Single == 13,
//      NOT 13 for Double -- typecheckcase precedent).
//   5. UNBOX -- round-trip value + wrong-type unbox InvalidCastException
//      + null unbox NullReferenceException (regression).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class BoxOps
    {
        public static void Main()
        {
            // ===== 1. BOXED ToString -- value-based text =====
            object bi = 42;
            object bl = 1234567890123L;
            object bu = 4000000000u;
            object bul = 18446744073709551615UL;
            object buy = (byte)200;
            object bsy = (sbyte)-1;
            object bs = (short)-5;
            object bus = (ushort)65535;
            object bd = 3.25;
            object bf = 1.5f;
            object bb = true;
            object bc = 'A';
            Console.WriteLine(bi.ToString() == "42");                  // True
            Console.WriteLine(bl.ToString() == "1234567890123");       // True
            Console.WriteLine(bu.ToString() == "4000000000");          // True (uint, full range)
            Console.WriteLine(bul.ToString() == "18446744073709551615"); // True (ulong max)
            Console.WriteLine(buy.ToString() == "200");                // True (byte)
            Console.WriteLine(bsy.ToString() == "-1");                 // True (sbyte sign-extended)
            Console.WriteLine(bs.ToString() == "-5");                  // True (short)
            Console.WriteLine(bus.ToString() == "65535");              // True (ushort)
            Console.WriteLine(bd.ToString() == "3.25");                // True (double)
            Console.WriteLine(bf.ToString() == "1.5");                 // True (float widened to double)
            Console.WriteLine(bb.ToString() == "True");                // True (bool)
            Console.WriteLine(bc.ToString() == "A");                   // True (char)
            Console.WriteLine(((object)(-2147483648)).ToString() == "-2147483648"); // True (int min)

            // ===== 2. BOXED GetHashCode -- VALUE-BASED =====
            object h1 = 42;
            object h2 = 42;
            Console.WriteLine(h1.GetHashCode() == 42);                 // True (Int32.GetHashCode == value)
            Console.WriteLine(h1.GetHashCode() == h2.GetHashCode());   // True (equal values -> equal hash)
            object h3 = 43;
            Console.WriteLine(h1.GetHashCode() != h3.GetHashCode());   // True (different values differ)
            object hs1 = new string(new char[] { 'a', 'b' });
            object hs2 = new string(new char[] { 'a', 'b' });
            Console.WriteLine(hs1.GetHashCode() == hs2.GetHashCode()); // True (string: equal CONTENT -> equal hash)

            // ===== 3. BOXED Equals -- CROSS-TYPE =====
            object e1 = 5;
            object e2 = 5L;
            object e3 = (byte)5;
            object e4 = (short)5;
            object e5 = 5.0;
            object e6 = 5.0f;
            object eb1 = true;
            object eb2 = 1;
            Console.WriteLine(e1.Equals(e2) == false);                 // True (Int32 vs Int64)
            Console.WriteLine(e1.Equals(e3) == false);                 // True (Int32 vs Byte)
            Console.WriteLine(e1.Equals(e4) == false);                 // True (Int32 vs Int16)
            Console.WriteLine(e1.Equals(e5) == false);                 // True (Int32 vs Double)
            Console.WriteLine(e5.Equals(e6) == false);                 // True (Double vs Single)
            Console.WriteLine(eb1.Equals(eb2) == false);               // True (Boolean vs Int32)
            Console.WriteLine(e1.Equals(5) == true);                   // True (Int32 vs Int32 value)
            Console.WriteLine(e3.Equals((byte)5) == true);             // True (Byte vs Byte value)
            object nan1 = double.NaN;
            object nan2 = double.NaN;
            Console.WriteLine(nan1.Equals(nan2) == true);              // True (Double.Equals NaN == NaN)
            object z1 = 0.0;
            object z2 = -0.0;
            Console.WriteLine(z1.Equals(z2) == true);                  // True (+0.0 == -0.0, Double.Equals)
            object fnan1 = float.NaN;
            object fnan2 = float.NaN;
            Console.WriteLine(fnan1.Equals(fnan2) == true);            // True (Single.Equals NaN == NaN)

            // ===== 4. IConvertible -- GetTypeCode() on boxed values =====
            Console.WriteLine(((IConvertible)bi).GetTypeCode() == TypeCode.Int32);   // True (9)
            Console.WriteLine(((IConvertible)bl).GetTypeCode() == TypeCode.Int64);   // True (13)
            Console.WriteLine(((IConvertible)bb).GetTypeCode() == TypeCode.Boolean); // True (3)
            Console.WriteLine(((IConvertible)bc).GetTypeCode() == TypeCode.Char);    // True (4)
            Console.WriteLine(((IConvertible)bd).GetTypeCode() == TypeCode.Double);  // True (14)
            Console.WriteLine(((IConvertible)bf).GetTypeCode() == TypeCode.Single);  // True (13)
            Console.WriteLine(((IConvertible)buy).GetTypeCode() == TypeCode.Byte);   // True (6)
            Console.WriteLine(((IConvertible)"hi").GetTypeCode() == TypeCode.String);// True (18)

            // ===== 5. UNBOX -- round-trip + error paths =====
            object un = 42;
            Console.WriteLine((int)un == 42);                          // True
            object und = 3.25;
            Console.WriteLine((double)und == 3.25);                     // True
            object unl = 1234567890123L;
            Console.WriteLine((long)unl == 1234567890123L);             // True
            bool threw = false;
            try { short x = (short)un; } catch (InvalidCastException) { threw = true; }
            Console.WriteLine(threw);                                  // True (Int32 box as Int16)
            threw = false;
            try { int x = (int)und; } catch (InvalidCastException) { threw = true; }
            Console.WriteLine(threw);                                  // True (Double box as Int32)
            threw = false;
            try { int x = (int)(object)null; } catch (NullReferenceException) { threw = true; }
            Console.WriteLine(threw);                                  // True (null unbox)
        }
    }
}
