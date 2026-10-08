//------------------------------------------------------------------------------
// boxop2.cs -- BOX OPERATION deep probe 2 (e2e batch C27, "box operations deep probe 2:
// TypeCode all types / conversion / concatenation -- after the type_code fix"). Built AFTER commit e6f41f9
// (icall_object_get_type_code full 12-type mapping, typecheck.cpp 192-278:
// Int32=9, UInt32=10, Int64=11, UInt64=12, Byte=6, SByte=5, Int16=7, UInt16=8,
// Single=13, Double=14, Boolean=3, Char=4, String=18, Decimal=15) to VERIFY the
// fix across the FULL boxed-value surface. boxconv (C26) probed the 12-type
// GetTypeCode sweep + ToInt32 shapes; boxop2 goes DEEPER:
//
//   * TYPECODE FULL SWEEP -- every boxed primitive + string + decimal via the
//     IConvertible cast, INCLUDING the TypeCode enum VALUES (the net48
//     constants) so a wrong mapping flips a byte.
//   * TYPECODE EDGE VALUES -- int.MinValue / uint.MaxValue / -0.0 / float
//     min / char max; decimal boxed at a fractional value.
//   * CONVERT FAMILY VIA UNBOX -- the WIRED Convert shapes applied to BOXED
//     values (Convert.ToInt32/ToInt64/ToDouble/ToBoolean/ToChar/ToString on
//     the unboxed primitive), plus the string/char parse routes.
//   * BOXED-VALUE STRING CONCAT -- `"v=" + box` for EVERY boxed primitive
//     (the object Concat path routes the box through icall_object_to_string);
//     the decimal concat exercises the 32-byte box ToString path.
//   * BOXED GetTypeCode through an object[] (element static type object) --
//     runtime-type dispatch, not compile-time.
//
// Every printed line is a bool assertion -- a diff pinpoints the flipped line.
// net48 is the byte-for-byte reference oracle.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Boxop2
    {
        public static void Main()
        {
            // ===== 1. TypeCode full sweep -- all boxed primitives + string + decimal =====
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
            object bdec = 123.45m;
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
            Console.WriteLine(((IConvertible)bdec).GetTypeCode() == TypeCode.Decimal); // True (decimal = 15)

            // ===== 2. TypeCode edge values =====
            object bmin = -2147483648;
            object bumax = 4294967295u;
            object bneg0 = -0.0;
            object bfltmin = float.MinValue;
            object bcharmax = char.MaxValue;
            Console.WriteLine(((IConvertible)bmin).GetTypeCode() == TypeCode.Int32);  // True
            Console.WriteLine(((IConvertible)bumax).GetTypeCode() == TypeCode.UInt32); // True
            Console.WriteLine(((IConvertible)bneg0).GetTypeCode() == TypeCode.Double); // True (-0.0 is a double)
            Console.WriteLine(((IConvertible)bfltmin).GetTypeCode() == TypeCode.Single); // True (float.MinValue)
            Console.WriteLine(((IConvertible)bcharmax).GetTypeCode() == TypeCode.Char);  // True (char.MaxValue)

            // ===== 3. Convert family via unbox =====
            Console.WriteLine((int)bi == 42);                                         // True (int unbox round-trip)
            Console.WriteLine(Convert.ToInt64((string)"1234567890123") == 1234567890123L); // True (string -> long parse)
            Console.WriteLine(Convert.ToDouble((int)bi) == 42.0);                     // True (int -> double)
            Console.WriteLine(Convert.ToDouble((string)"3.25") == 3.25);              // True (string -> double parse)
            Console.WriteLine(Convert.ToBoolean((int)0) == false);                    // True (0 -> false)
            Console.WriteLine(Convert.ToBoolean((int)42) == true);                    // True (nonzero -> true)
            Console.WriteLine(Convert.ToChar((int)65) == 'A');                        // True (65 -> 'A')
            Console.WriteLine(Convert.ToString((int)bi) == "42");                     // True (int -> string)
            Console.WriteLine(Convert.ToString((long)bl) == "1234567890123");         // True (long -> string)
            Console.WriteLine(Convert.ToString((double)bd) == "3.25");                // True (double -> string)
            Console.WriteLine(Convert.ToString((bool)bb) == "True");                  // True (bool -> string)
            Console.WriteLine(Convert.ToString((char)bc) == "A");                     // True (char -> string)
            Console.WriteLine(Convert.ToString((int)(short)bs) == "-5");              // True (short box -> short -> int -> string)

            // ===== 4. boxed-value string concat -- every primitive + decimal =====
            Console.WriteLine(("v=" + bi) == "v=42");                                 // True
            Console.WriteLine(("v=" + bl) == "v=1234567890123");                      // True
            Console.WriteLine(("v=" + bu) == "v=4000000000");                         // True (uint full range)
            Console.WriteLine(("v=" + bul) == "v=18446744073709551615");              // True (ulong max)
            Console.WriteLine(("v=" + buy) == "v=200");                               // True (byte)
            Console.WriteLine(("v=" + bsy) == "v=-1");                                // True (sbyte)
            Console.WriteLine(("v=" + bs) == "v=-5");                                 // True (short)
            Console.WriteLine(("v=" + bus) == "v=65535");                             // True (ushort)
            Console.WriteLine(("v=" + bd) == "v=3.25");                               // True (double)
            Console.WriteLine(("v=" + bf) == "v=1.5");                                // True (float)
            Console.WriteLine(("v=" + bb) == "v=True");                               // True (bool)
            Console.WriteLine(("v=" + bc) == "v=A");                                  // True (char)
            Console.WriteLine(("v=" + bdec) == "v=123.45");                           // True (decimal 32-byte box)

            // ===== 5. boxed GetTypeCode through an object[] (runtime-type dispatch) =====
            object[] arr = new object[] { 42, 1234567890123L, true, 'A', 3.25, 1.5f, (byte)200, 4000000000u };
            int[] want = new int[] { (int)TypeCode.Int32, (int)TypeCode.Int64, (int)TypeCode.Boolean,
                (int)TypeCode.Char, (int)TypeCode.Double, (int)TypeCode.Single, (int)TypeCode.Byte, (int)TypeCode.UInt32 };
            bool allOk = true;
            for (int i = 0; i < arr.Length; i++)
                allOk = allOk && (int)((IConvertible)arr[i]).GetTypeCode() == want[i];
            Console.WriteLine(allOk);                                                // True (array dispatch)

            // ===== 6. boxed ToString value text (regression after type_code fix) =====
            Console.WriteLine(bi.ToString() == "42");                                // True
            Console.WriteLine(bl.ToString() == "1234567890123");                     // True
            Console.WriteLine(buy.ToString() == "200");                              // True
            Console.WriteLine(bsy.ToString() == "-1");                               // True
            Console.WriteLine(bs.ToString() == "-5");                                // True
            Console.WriteLine(bus.ToString() == "65535");                            // True
            Console.WriteLine(bb.ToString() == "True");                              // True
            Console.WriteLine(bc.ToString() == "A");                                 // True
            Console.WriteLine(bdec.ToString() == "123.45");                          // True (decimal box ToString)
        }
    }
}
