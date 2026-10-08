// numops.cs -- numeric operations deep probe (e2e batch C21, "numeric operations deep probe:
// Convert all forms / Parse boundaries / format roundtrip, after the convert/parse fix"). Uses ONLY the
// wired surface (MethodMap.Convert.cs M7 + MethodMap.ValueType.cs M7/M8 +
// MethodMap.Format.cs) so the transpile is clean; the net48 oracle is the
// byte-diff authority.
//
// Sections:
//   S1 Convert all forms -- every wired Convert form with a boundary matrix:
//      ToInt32(string/long/double/char/(string,base)), ToInt64(string),
//      ToDouble(string/int), ToString(int/long/double/bool/char/(int,base)),
//      ToBoolean(int), ToChar(int), BitConverter bit round-trips.
//   S2 Parse boundaries -- int.Parse / int.TryParse consistency matrix, long/byte/
//      bool/double TryParse boundaries, double.Parse throws.
//   S3 format roundtrip -- ToString(D/X) -> Parse / Convert-fromBase round-trips for
//      int/int64/double, exact-string pins for well-known doubles.
//
// AVOIDED (documented gaps, would NOT diff clean): long.Parse (LOUD-unwired),
// int.Parse(null)/double.Parse(null) (runtime FormatException vs net48
// ArgumentNullException), double "Infinity" ASCII words (zh-CN culture),
// NumberStyles/IFormatProvider overloads, Convert.To{UInt32,UInt64,Int16,...}.
// Every exception prints its TYPE NAME only (messages are OS-localized).
using System;

namespace Cases
{
    public static class Numops
    {
        private static void P(string label, string v) { Console.WriteLine(label + "=" + v); }

        // Parse/TryParse consistency row: prints both outcomes + agreement.
        // Typed catches (no catch variable) -- parseops-proven shape: the
        // `catch (Exception e)` inline-consumption variant (first handler
        // statement not using `e`) trips StackFold's catch-entry detection
        // (recorded defect C21-numops-1).
        private static void Consist(int idx, string s)
        {
            int pv = 0;
            string perr = "-";
            bool pok = true;
            try { pv = int.Parse(s); }
            catch (FormatException) { pok = false; perr = "FormatException"; }
            catch (OverflowException) { pok = false; perr = "OverflowException"; }
            int tv = 0;
            bool tok = int.TryParse(s, out tv);
            Console.WriteLine("pc" + idx + "=" + pok + ":" + perr + ":" + tok
                + ":" + (pok == tok) + ":" + (pok && pv == tv));
        }

        public static void Main()
        {
            // ================= S1: Convert all forms =================
            // -- ToInt32(string): whitespace / sign / zeros / boundaries --
            P("c32_s0", Convert.ToString(Convert.ToInt32("0")));                 // 0
            P("c32_sign", Convert.ToString(Convert.ToInt32("  +123  ")));        // 123
            P("c32_neg", Convert.ToString(Convert.ToInt32("-2147483648")));      // -2147483648
            P("c32_max", Convert.ToString(Convert.ToInt32("2147483647")));       // 2147483647
            P("c32_zpad", Convert.ToString(Convert.ToInt32("00000000042")));     // 42
            P("c32_null", Convert.ToString(Convert.ToInt32(null)));              // 0 (null -> 0)
            try { Convert.ToInt32("2147483648"); }
            catch (Exception e) { Console.WriteLine("c32_ovf=" + e.GetType().Name); }        // OverflowException
            try { Convert.ToInt32("-2147483649"); }
            catch (Exception e) { Console.WriteLine("c32_ovfneg=" + e.GetType().Name); }     // OverflowException
            try { Convert.ToInt32("12a"); }
            catch (Exception e) { Console.WriteLine("c32_fmt=" + e.GetType().Name); }        // FormatException
            try { Convert.ToInt32(""); }
            catch (Exception e) { Console.WriteLine("c32_empty=" + e.GetType().Name); }      // FormatException
            try { Convert.ToInt32("   "); }
            catch (Exception e) { Console.WriteLine("c32_ws=" + e.GetType().Name); }         // FormatException

            // -- ToInt64(string) boundaries --
            P("c64_max", Convert.ToString(Convert.ToInt64("9223372036854775807")));   // max
            P("c64_min", Convert.ToString(Convert.ToInt64("-9223372036854775808")));  // min
            P("c64_null", Convert.ToString(Convert.ToInt64(null)));                   // 0
            try { Convert.ToInt64("9223372036854775808"); }
            catch (Exception e) { Console.WriteLine("c64_ovf=" + e.GetType().Name); } // OverflowException

            // -- ToInt32(long): checked range --
            P("c32l_ok", Convert.ToString(Convert.ToInt32(2147483647L)));        // 2147483647
            P("c32l_neg", Convert.ToString(Convert.ToInt32(-2147483648L)));      // -2147483648
            try { Convert.ToInt32(2147483648L); }
            catch (Exception e) { Console.WriteLine("c32l_ovf=" + e.GetType().Name); }  // OverflowException
            try { Convert.ToInt32(-2147483649L); }
            catch (Exception e) { Console.WriteLine("c32l_ovfneg=" + e.GetType().Name); } // OverflowException
            try { Convert.ToInt32(long.MaxValue); }
            catch (Exception e) { Console.WriteLine("c32l_ovfmax=" + e.GetType().Name); } // OverflowException

            // -- ToInt32(double): round half-to-even + boundary/NaN/Inf --
            P("d_05", Convert.ToString(Convert.ToInt32(0.5)));            // 0
            P("d_neg05", Convert.ToString(Convert.ToInt32(-0.5)));        // 0
            P("d_15", Convert.ToString(Convert.ToInt32(1.5)));            // 2
            P("d_neg15", Convert.ToString(Convert.ToInt32(-1.5)));        // -2
            P("d_25", Convert.ToString(Convert.ToInt32(2.5)));            // 2
            P("d_neg25", Convert.ToString(Convert.ToInt32(-2.5)));        // -2
            P("d_35", Convert.ToString(Convert.ToInt32(3.5)));            // 4
            P("d_neg35", Convert.ToString(Convert.ToInt32(-3.5)));        // -4
            P("d_049", Convert.ToString(Convert.ToInt32(0.49)));          // 0
            P("d_051", Convert.ToString(Convert.ToInt32(0.51)));          // 1
            P("d_big", Convert.ToString(Convert.ToInt32(2147483646.5)));  // 2147483646
            P("d_negbig", Convert.ToString(Convert.ToInt32(-2147483648.5))); // -2147483648 (legal)
            try { Convert.ToInt32(2147483647.5); }
            catch (Exception e) { Console.WriteLine("d_ovf_rnd=" + e.GetType().Name); }   // OverflowException
            try { Convert.ToInt32(2147483648.0); }
            catch (Exception e) { Console.WriteLine("d_ovf_lim=" + e.GetType().Name); }   // OverflowException
            try { Convert.ToInt32(double.NaN); }
            catch (Exception e) { Console.WriteLine("d_nan=" + e.GetType().Name); }       // OverflowException
            try { Convert.ToInt32(double.PositiveInfinity); }
            catch (Exception e) { Console.WriteLine("d_inf=" + e.GetType().Name); }       // OverflowException

            // -- ToInt32(char) --
            P("c32ch_a", Convert.ToString(Convert.ToInt32('A')));          // 65

            // -- ToDouble(string) --
            P("dbl_35", Convert.ToString(Convert.ToDouble("3.5")));        // 3.5
            P("dbl_exp", Convert.ToString(Convert.ToDouble("  -1.25e2  "))); // -125
            P("dbl_frac", Convert.ToString(Convert.ToDouble("12.5e-2")));  // 0.125
            P("dbl_dot", Convert.ToString(Convert.ToDouble(".5")));        // 0.5
            P("dbl_plus", Convert.ToString(Convert.ToDouble("+3.5")));     // 3.5
            P("dbl_null", Convert.ToString(Convert.ToDouble(null)));       // 0
            try { Convert.ToDouble("1e999"); }
            catch (Exception e) { Console.WriteLine("dbl_ovf=" + e.GetType().Name); }  // OverflowException
            try { Convert.ToDouble("abc"); }
            catch (Exception e) { Console.WriteLine("dbl_fmt=" + e.GetType().Name); }  // FormatException
            try { Convert.ToDouble(""); }
            catch (Exception e) { Console.WriteLine("dbl_empty=" + e.GetType().Name); } // FormatException

            // -- ToDouble(int) --
            P("dbl_i_max", Convert.ToString(Convert.ToDouble(2147483647))); // 2147483647
            P("dbl_i_neg", Convert.ToString(Convert.ToDouble(-7)));         // -7

            // -- ToString(int / long / double / bool / char) --
            P("s_i_min", Convert.ToString(int.MinValue));               // -2147483648
            P("s_i_max", Convert.ToString(int.MaxValue));               // 2147483647
            P("s_i_0", Convert.ToString(0));                            // 0
            P("s_l_min", Convert.ToString(long.MinValue));              // -9223372036854775808
            P("s_l_max", Convert.ToString(long.MaxValue));              // 9223372036854775807
            P("s_d_05", Convert.ToString(0.5));                         // 0.5
            P("s_d_100", Convert.ToString(100.0));                      // 100
            P("s_d_0", Convert.ToString(0.0));                          // 0
            P("s_d_225", Convert.ToString(2.25));                       // 2.25
            P("s_d_01", Convert.ToString(0.1));                         // 0.1
            P("s_b_t", Convert.ToString(true));                         // True
            P("s_b_f", Convert.ToString(false));                        // False
            P("s_c_A", Convert.ToString('A'));                          // A
            P("s_c_0", Convert.ToString('0'));                          // 0

            // -- ToString(int, toBase): uint32 two's complement on 2/8/16 --
            P("sb_16_255", Convert.ToString(255, 16));                  // ff
            P("sb_2_255", Convert.ToString(255, 2));                    // 11111111
            P("sb_8_255", Convert.ToString(255, 8));                    // 377
            P("sb_10_255", Convert.ToString(255, 10));                  // 255
            P("sb_16_neg1", Convert.ToString(-1, 16));                  // ffffffff
            P("sb_16_neg255", Convert.ToString(-255, 16));              // ffffff01
            P("sb_16_0", Convert.ToString(0, 16));                      // 0
            P("sb_16_big", Convert.ToString(305419896, 16));            // 12345678
            try { Convert.ToString(10, 3); }
            catch (Exception e) { Console.WriteLine("sb_badbase=" + e.GetType().Name); } // ArgumentException

            // -- ToInt32(string, fromBase) --
            P("fb_16_ff", Convert.ToString(Convert.ToInt32("ff", 16)));      // 255
            P("fb_16_0x", Convert.ToString(Convert.ToInt32("0xFF", 16)));    // 255
            P("fb_16_0x1a", Convert.ToString(Convert.ToInt32("0x1a", 16)));  // 26
            P("fb_2_ones", Convert.ToString(Convert.ToInt32("11111111", 2)));// 255
            P("fb_8_377", Convert.ToString(Convert.ToInt32("377", 8)));      // 255
            try { Convert.ToInt32("-ff", 16); }
            catch (Exception e) { Console.WriteLine("fb_16_sign=" + e.GetType().Name); } // ArgumentException (sign not allowed with fromBase)
            P("fb_16_max", Convert.ToString(Convert.ToInt32("7fffffff", 16))); // 2147483647
            try { Convert.ToInt32("  ff  ", 16); }
            catch (Exception e) { Console.WriteLine("fb_16_ws=" + e.GetType().Name); } // FormatException (fromBase does NOT trim whitespace)
            P("fb_16_null", Convert.ToString(Convert.ToInt32(null, 16)));    // 0
            try { Convert.ToInt32("10", 3); }
            catch (Exception e) { Console.WriteLine("fb_badbase=" + e.GetType().Name); }   // ArgumentException
            try { Convert.ToInt32("", 16); }
            catch (Exception e) { Console.WriteLine("fb_empty=" + e.GetType().Name); }     // ArgumentOutOfRangeException
            try { Console.WriteLine("fb_80000000=" + Convert.ToInt32("80000000", 16)); }
            catch (Exception e) { Console.WriteLine("fb_80000000=" + e.GetType().Name); }   // -2147483648 (uint32 wrap, no throw)
            try { Console.WriteLine("fb_ffffffff=" + Convert.ToInt32("ffffffff", 16)); }
            catch (Exception e) { Console.WriteLine("fb_ffffffff=" + e.GetType().Name); }   // -1 (uint32 wrap, no throw)
            try { Convert.ToInt32("100000000", 16); }
            catch (Exception e) { Console.WriteLine("fb_ovf=" + e.GetType().Name); }        // OverflowException (beyond uint32)

            // -- ToBoolean(int): non-zero -> true --
            P("b_0", Convert.ToString(Convert.ToBoolean(0)));        // False
            P("b_1", Convert.ToString(Convert.ToBoolean(1)));        // True
            P("b_neg", Convert.ToString(Convert.ToBoolean(-1)));     // True
            P("b_max", Convert.ToString(Convert.ToBoolean(int.MaxValue))); // True

            // -- ToChar(int): checked --
            Console.WriteLine("ch_65=" + (Convert.ToChar(65) == 'A'));   // True
            Console.WriteLine("ch_0=" + ((int)Convert.ToChar(0) == 0));  // True
            Console.WriteLine("ch_ffff=" + ((int)Convert.ToChar(0xFFFF) == 65535)); // True
            try { Convert.ToChar(0x10000); }
            catch (Exception e) { Console.WriteLine("ch_ovf=" + e.GetType().Name); } // OverflowException
            try { Convert.ToChar(-1); }
            catch (Exception e) { Console.WriteLine("ch_neg=" + e.GetType().Name); }  // OverflowException

            // -- BitConverter bit round-trips (wired Convert/BitConverter domain) --
            Console.WriteLine("bc_d2i=" + (BitConverter.DoubleToInt64Bits(1.0) == 0x3FF0000000000000L)); // True
            Console.WriteLine("bc_i2d=" + (BitConverter.Int64BitsToDouble(0x3FF0000000000000L) == 1.0)); // True
            Console.WriteLine("bc_zero=" + (BitConverter.DoubleToInt64Bits(0.0) == 0L));                  // True

            // ================= S2: Parse boundaries =================
            P("p_max", int.Parse("2147483647").ToString());              // 2147483647
            P("p_min", int.Parse("-2147483648").ToString());             // -2147483648
            P("p_zpad", int.Parse("0000000000000000001").ToString());    // 1
            P("p_ws", int.Parse("  +7  ").ToString());                   // 7
            try { int.Parse("2147483648"); }
            catch (Exception e) { Console.WriteLine("p_ovf=" + e.GetType().Name); }   // OverflowException
            try { int.Parse("-2147483649"); }
            catch (Exception e) { Console.WriteLine("p_ovfneg=" + e.GetType().Name); } // OverflowException
            try { int.Parse(""); }
            catch (Exception e) { Console.WriteLine("p_empty=" + e.GetType().Name); }  // FormatException
            try { int.Parse("   "); }
            catch (Exception e) { Console.WriteLine("p_ws_only=" + e.GetType().Name); } // FormatException
            try { int.Parse("1.5"); }
            catch (Exception e) { Console.WriteLine("p_dot=" + e.GetType().Name); }    // FormatException
            try { int.Parse("0x10"); }
            catch (Exception e) { Console.WriteLine("p_hex=" + e.GetType().Name); }    // FormatException
            try { int.Parse("12a"); }
            catch (Exception e) { Console.WriteLine("p_tail=" + e.GetType().Name); }   // FormatException

            // Parse/TryParse CONSISTENCY matrix (value + exception agreement).
            Consist(0, "0");
            Consist(1, "-42");
            Consist(2, "  +999  ");
            Consist(3, "2147483647");
            Consist(4, "-2147483648");
            Consist(5, "2147483648");
            Consist(6, "");
            Consist(7, "  ");
            Consist(8, "1.5");
            Consist(9, "12a");

            // -- long.TryParse boundaries --
            long lv;
            Console.WriteLine("lt_max=" + long.TryParse("9223372036854775807", out lv) + ":" + (lv == long.MaxValue));   // True:True
            Console.WriteLine("lt_min=" + long.TryParse("-9223372036854775808", out lv) + ":" + (lv == long.MinValue));  // True:True
            Console.WriteLine("lt_ovf=" + long.TryParse("99999999999999999999", out lv));                               // False
            Console.WriteLine("lt_zero=" + long.TryParse("0", out lv) + ":" + (lv == 0));                              // True:True

            // -- byte.TryParse boundaries --
            byte by;
            Console.WriteLine("bt_255=" + byte.TryParse("255", out by) + ":" + (by == 255));  // True:True
            Console.WriteLine("bt_256=" + byte.TryParse("256", out by));                      // False
            Console.WriteLine("bt_neg=" + byte.TryParse("-1", out by));                       // False
            Console.WriteLine("bt_128=" + byte.TryParse("128", out by) + ":" + (by == 128));  // True:True

            // -- bool.TryParse (case-insensitive + whitespace) --
            bool bv;
            Console.WriteLine("bool_true=" + bool.TryParse("true", out bv) + ":" + bv);       // True:True
            Console.WriteLine("bool_ws=" + bool.TryParse(" TRUE ", out bv) + ":" + bv);       // True:True
            Console.WriteLine("bool_mixed=" + bool.TryParse("truE", out bv) + ":" + bv);      // True:True
            Console.WriteLine("bool_false=" + bool.TryParse("FALSE", out bv) + ":" + bv);     // True:False
            Console.WriteLine("bool_1=" + bool.TryParse("1", out bv));                        // False
            Console.WriteLine("bool_yes=" + bool.TryParse("yes", out bv));                    // False

            // -- double.Parse / TryParse --
            P("dp_35", double.Parse("3.5").ToString());              // 3.5
            P("dp_exp", double.Parse("-2.25e1").ToString());         // -22.5
            P("dp_dot", double.Parse(".25").ToString());             // 0.25
            P("dp_nan", double.Parse("NaN").ToString());             // NaN
            P("dp_ws", double.Parse("  -7.5  ").ToString());         // -7.5
            try { double.Parse("1e999"); }
            catch (Exception e) { Console.WriteLine("dp_ovf=" + e.GetType().Name); }  // OverflowException
            try { double.Parse("abc"); }
            catch (Exception e) { Console.WriteLine("dp_fmt=" + e.GetType().Name); }  // FormatException
            double dv;
            Console.WriteLine("dt_nan=" + double.TryParse("NaN", out dv) + ":" + dv.ToString());      // True:NaN
            Console.WriteLine("dt_exp=" + double.TryParse("1.5e-3", out dv) + ":" + dv.ToString());   // True:0.0015
            Console.WriteLine("dt_ovf=" + double.TryParse("1e999", out dv));                          // False
            Console.WriteLine("dt_empty=" + double.TryParse("", out dv));                             // False
            Console.WriteLine("dt_null=" + double.TryParse(null, out dv));                            // False

            // ================= S3: format roundtrip =================
            P("f_d4_42", (42).ToString("D4"));                       // 0042
            P("f_d4_neg", (-42).ToString("D4"));                     // -0042
            P("f_d4_0", (0).ToString("D4"));                         // 0000
            P("f_d4_big", (12345).ToString("D4"));                   // 12345
            P("f_d6_lng", (1234567890123L).ToString("D13"));         // 1234567890123
            P("f_x_255", (255).ToString("X"));                       // FF
            P("f_x_neg1", (-1).ToString("X"));                       // FFFFFFFF
            P("f_x_0", (0).ToString("X"));                           // 0
            P("f_x_max", (2147483647).ToString("X"));                // 7FFFFFFF
            P("f_x2_5", (5).ToString("X2"));                         // 05

            // D round-trip: ToString -> Parse == original.
            Console.WriteLine("rt_d4=" + (int.Parse("0042") == 42));     // True
            Console.WriteLine("rt_d4neg=" + (int.Parse("-0042") == -42));// True
            Console.WriteLine("rt_int_max=" + (int.Parse(int.MaxValue.ToString()) == int.MaxValue)); // True
            Console.WriteLine("rt_int_min=" + (int.Parse(int.MinValue.ToString()) == int.MinValue)); // True
            Console.WriteLine("rt_long_max=" + (long.TryParse(long.MaxValue.ToString(), out lv) && lv == long.MaxValue)); // True
            // Hex round-trip: ToString(v,16) -> ToInt32(s,16) == v.
            Console.WriteLine("rt_hex_0=" + (Convert.ToInt32(Convert.ToString(0, 16), 16) == 0));                 // True
            Console.WriteLine("rt_hex_255=" + (Convert.ToInt32(Convert.ToString(255, 16), 16) == 255));           // True
            Console.WriteLine("rt_hex_neg1=" + (Convert.ToInt32(Convert.ToString(-1, 16), 16) == -1));            // True
            Console.WriteLine("rt_hex_big=" + (Convert.ToInt32(Convert.ToString(305419896, 16), 16) == 305419896)); // True
            Console.WriteLine("rt_hex_max=" + (Convert.ToInt32(Convert.ToString(2147483647, 16), 16) == 2147483647)); // True
            // Double exact-string round-trips (well-known exact short decimals).
            P("rt_d_05", double.Parse((0.5).ToString()).ToString());      // 0.5
            P("rt_d_100", double.Parse((100.0).ToString()).ToString());   // 100
            P("rt_d_0", double.Parse((0.0).ToString()).ToString());       // 0
            P("rt_d_225", double.Parse((2.25).ToString()).ToString());    // 2.25
            P("rt_d_01", double.Parse((0.1).ToString()).ToString());      // 0.1
        }
    }
}
