// boxconv.cs -- BOX CONVERSION deep probe (e2e batch C26, "box conversion deep probe:
// boxed value conversion / IConvertible / ToInt32 shapes -- after the box-domain fix"). Builds on the box-domain
// surface (boxops/boxunbox/boxdeep/boxing2: boxed ToString/GetHashCode/Equals/
// unbox + IConvertible.GetTypeCode) and goes DEEPER on the boxed-VALUE
// CONVERSION surface:
//
//   * IConvertible.GetTypeCode for EVERY boxed primitive -- the full 12-type
//     sweep (int/long/uint/ulong/byte/sbyte/short/ushort/double/float/bool/
//     char) + string (boxops covered 8; this adds uint/ulong/sbyte/short/
//     ushort), plus EDGE boxed VALUES (int.MinValue/uint.MaxValue/0).
//   * ToInt32 SHAPES: every WIRED route into an int32 --
//       (a) plain unbox  (int)box;
//       (b) Convert.ToInt32((double)box)   -- unbox double + round-half-even;
//       (c) Convert.ToInt32((long)box)     -- unbox long + range check;
//       (d) Convert.ToInt32((string)box)   -- cast + numeric parse;
//       (e) Convert.ToInt32((char)box)     -- unbox char + code-unit.
//     Wrong-typed unboxes throw InvalidCastException; null throws NRE.
//   * Convert-family boxed forms: ToString((int)/(bool)/(char) after unbox),
//     ToDouble((int)box), ToBoolean((int)box).
//   * BOXED-VALUE string concat: `"v=" + box` (the object Concat path routes
//     the BOX through icall_object_to_string -- value-based text).
//   * Boxed ToString / GetHashCode value semantics (int: hash == value;
//     bool/char: value text; the non-Int32 value-hash gap is a RECORDED
//     defect -- boxops GAP-BOX-6 -- re-confirmed here on short/uint).
//
// Every printed line is a bool assertion -- a diff pinpoints the flipped line.
// The direct `((IConvertible)box).ToInt32(...)` dispatch is NOT probeable (the
// box interface slot maps are UNWIRED -- a build abort, recorded in boxops);
// the wired Convert forms above are the observable ToInt32 surface.
using System;

namespace Cases
{
    public static class Boxconv
    {
        public static void Main()
        {
            // ===== 1. IConvertible.GetTypeCode -- full 12-type sweep =====
            // DEFECT (re-confirmed, pre-existing -- boxops already fails on
            // long/float/byte): icall_object_get_type_code (typecheck.cpp)
            // maps only Int32/Boolean/Char/Double/String/Decimal; the
            // boxingext2 box classes (Int64/UInt32/UInt64/Byte/SByte/Int16/
            // UInt16/Single) fall back to TypeCode.Object (1) -- lines 2-8,
            // 10, 15 below read False on the runtime instead of True.
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
            Console.WriteLine(((IConvertible)bi).GetTypeCode() == TypeCode.Int32);    // True
            Console.WriteLine(((IConvertible)bl).GetTypeCode() == TypeCode.Int64);    // True
            Console.WriteLine(((IConvertible)bu).GetTypeCode() == TypeCode.UInt32);   // True
            Console.WriteLine(((IConvertible)bul).GetTypeCode() == TypeCode.UInt64);  // True
            Console.WriteLine(((IConvertible)buy).GetTypeCode() == TypeCode.Byte);    // True
            Console.WriteLine(((IConvertible)bsy).GetTypeCode() == TypeCode.SByte);   // True
            Console.WriteLine(((IConvertible)bs).GetTypeCode() == TypeCode.Int16);    // True
            Console.WriteLine(((IConvertible)bus).GetTypeCode() == TypeCode.UInt16);  // True
            Console.WriteLine(((IConvertible)bd).GetTypeCode() == TypeCode.Double);   // True
            Console.WriteLine(((IConvertible)bf).GetTypeCode() == TypeCode.Single);   // True
            Console.WriteLine(((IConvertible)bb).GetTypeCode() == TypeCode.Boolean);  // True
            Console.WriteLine(((IConvertible)bc).GetTypeCode() == TypeCode.Char);     // True
            Console.WriteLine(((IConvertible)"hi").GetTypeCode() == TypeCode.String); // True

            // edge boxed values: int.MinValue / uint.MaxValue / zero
            object bmin = -2147483648;
            object bumax = 4294967295u;
            object bzero = 0;
            Console.WriteLine(((IConvertible)bmin).GetTypeCode() == TypeCode.Int32);  // True
            Console.WriteLine(((IConvertible)bumax).GetTypeCode() == TypeCode.UInt32); // True
            Console.WriteLine(((IConvertible)bzero).GetTypeCode() == TypeCode.Int32);  // True

            // ===== 2. ToInt32 shapes (all wired routes) =====
            // (a) plain unbox
            int u1 = (int)bi;
            Console.WriteLine(u1 == 42);                                            // True
            // (b) unbox double -> Convert.ToInt32 (round half-to-even)
            Console.WriteLine(Convert.ToInt32((double)bd) == 3);                    // True (3.25 -> 3)
            object bd2 = 2.5;
            Console.WriteLine(Convert.ToInt32((double)bd2) == 2);                   // True (2.5 -> even 2)
            object bd3 = 3.5;
            Console.WriteLine(Convert.ToInt32((double)bd3) == 4);                   // True (3.5 -> even 4)
            // (c) unbox long -> Convert.ToInt32 (range check)
            object bl2 = 2147483647L;
            Console.WriteLine(Convert.ToInt32((long)bl2) == int.MaxValue);          // True
            object bl3 = -2147483648L;
            Console.WriteLine(Convert.ToInt32((long)bl3) == int.MinValue);          // True
            bool o1 = false;
            try { int x = Convert.ToInt32((long)2147483648L); o1 = true; }
            catch (OverflowException) { o1 = false; }
            Console.WriteLine(o1);                                                  // False (overflow caught)
            // (d) cast string -> Convert.ToInt32 (numeric parse)
            object bstr = "42";
            Console.WriteLine(Convert.ToInt32((string)bstr) == 42);                 // True
            object bstr2 = "  -7  ";
            Console.WriteLine(Convert.ToInt32((string)bstr2) == -7);                // True (trimmed)
            // (e) unbox char -> Convert.ToInt32 (code unit)
            Console.WriteLine(Convert.ToInt32((char)bc) == 65);                     // True ('A' -> 65)

            // ===== 3. Convert-family on unboxed values =====
            Console.WriteLine(Convert.ToString((int)bi) == "42");                   // True
            Console.WriteLine(Convert.ToString((bool)bb) == "True");                // True
            Console.WriteLine(Convert.ToString((char)bc) == "A");                   // True
            Console.WriteLine(Convert.ToDouble((int)bi) == 42.0);                   // True
            Console.WriteLine(Convert.ToBoolean((int)bzero) == false);              // True (0 -> false)
            Console.WriteLine(Convert.ToBoolean((int)bi) == true);                  // True (42 -> true)

            // ===== 4. boxed-value string concat (object Concat -> icall_object_to_string) =====
            Console.WriteLine(("v=" + bi) == "v=42");                               // True
            Console.WriteLine(("v=" + bd) == "v=3.25");                             // True
            Console.WriteLine(("v=" + bb) == "v=True");                             // True
            Console.WriteLine(("v=" + bc) == "v=A");                                // True
            Console.WriteLine(("v=" + bu) == "v=4000000000");                       // True (uint full range)
            Console.WriteLine(("v=" + bul) == "v=18446744073709551615");            // True (ulong max)

            // ===== 5. error paths (wrong-type unbox / null unbox) =====
            bool e1 = false;
            try { int x = (int)bd; e1 = true; }
            catch (InvalidCastException) { e1 = false; }
            Console.WriteLine(e1);                                                  // False (double box as int)
            bool e2 = false;
            try { int x = (int)bu; e2 = true; }
            catch (InvalidCastException) { e2 = false; }
            Console.WriteLine(e2);                                                  // False (uint box as int)
            bool e3 = false;
            try { short x = (short)bi; e3 = true; }
            catch (InvalidCastException) { e3 = false; }
            Console.WriteLine(e3);                                                  // False (int box as short)
            bool e4 = false;
            try { double x = (double)bi; e4 = true; }
            catch (InvalidCastException) { e4 = false; }
            Console.WriteLine(e4);                                                  // False (int box as double)
            bool e5 = false;
            try { int x = (int)(object)null; e5 = true; }
            catch (NullReferenceException) { e5 = false; }
            Console.WriteLine(e5);                                                  // False (null unbox)

            // ===== 6. boxed value semantics (ToString value text; GetHashCode) =====
            Console.WriteLine(bi.ToString() == "42");                               // True
            Console.WriteLine(bb.ToString() == "True");                             // True
            Console.WriteLine(bc.ToString() == "A");                                // True
            Console.WriteLine(bs.ToString() == "-5");                               // True (short sign-extended)
            Console.WriteLine(buy.ToString() == "200");                             // True (byte)
            Console.WriteLine(bi.GetHashCode() == 42);                              // True (Int32 hash == value)
            Console.WriteLine(((object)0).GetHashCode() == 0);                      // True
            object h1 = 42;
            object h2 = 42;
            Console.WriteLine(h1.GetHashCode() == h2.GetHashCode());                // True (equal values)
            object bs1 = (short)7;
            object bs2 = (short)7;
            Console.WriteLine(bs1.GetHashCode() == bs2.GetHashCode());              // net48 True; a non-Int32 value-hash gap shows False (GAP-BOX-6)
        }
    }
}
