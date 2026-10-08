//------------------------------------------------------------------------------
// boxconv2.cs -- BOX CONVERSION deep probe 2 (e2e batch C55, "boxing-conversion deep probe 2:
// boxing / unboxing / conversion-chain composition -- after the box-domain fix"). Builds on the box-domain surface
// (boxconv C26: GetTypeCode sweep + ToInt32 shapes; boxop2 C27: TypeCode
// full sweep + Convert via unbox; boxdeep2 C50: boxed uint ToString/GetType)
// and goes DEEPER on the CHAIN axis: multi-step box -> unbox -> convert ->
// rebox pipelines and cross-type conversion lattices -- byte-exact vs the
// net48 oracle, dual-compiler (mingw + msvc).
//
// Every probe is a labeled bool: a diff failure pinpoints the exact line.
// All boxes live in OBJECT-typed locals so boxed ToString routes through
// the wired icall_object_to_string (the UInt32 whitelist gap GAP-BOX-5 only
// hits a compile-time-tracked UInt32 receiver -- avoided here).
//
// Wired surface exercised (MethodMap.Convert.cs + MethodMap.Object.cs +
// MethodMap.String.cs):
//   * unbox:    (int)/(long)/(double)/(bool)/(char)/(string)/(decimal) on
//               the exact box type (wrong type -> InvalidCastException).
//   * Convert:  ToInt32((long)/(double)/(char)/(string)), ToInt64(string),
//               ToDouble((int)/string), ToString((int)/(long)/(double)/
//               (bool)/(char)), ToBoolean((int)), ToChar((int)).
//               NOTE: Convert.ToInt32((int)box) / ToInt64((long)box) /
//               ToDouble((double)box) are NOT wired (MethodMap.Convert.cs
//               param-type switch) -- the probes stay on the wired params.
//   * rebox:    every converted value returns through an object local, then
//               ToString / unbox / composite-format again (the CHAIN axis).
//   * boxed concat: "v=" + box (object Concat -> icall_object_to_string)
//               chained across multiple boxes on one line.
//   * decimal  box/unbox round trip (boxing.cpp icall_box_decimal /
//               icall_unbox_decimal) + boxed decimal ToString.
//
// Determinism: pure managed; N/F "." decimal + "," group (same in zh-CN and
// invariant). Expected values in trailing comments.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Boxconv2 {
    public static void Main() {
        // ===== 1. CHAIN: box int -> unbox -> Convert -> rebox -> ToString =====
        object bi = 42;
        int u1 = (int)bi;
        object r1 = Convert.ToDouble(u1);            // 42.0 (rebox double)
        Console.WriteLine("c1_dbl=" + r1.ToString());                       // 42
        object r2 = Convert.ToInt32(r1.ToString());  // parse the text back -> int box
        Console.WriteLine("c1_rt=" + ((int)r2 == 42));                      // True
        object r3 = Convert.ToBoolean(u1);           // 42 -> true (rebox bool)
        Console.WriteLine("c1_bool=" + r3.ToString());                      // True
        object r4 = Convert.ToChar(u1);              // 42 -> '*' (rebox char)
        Console.WriteLine("c1_char=" + r4.ToString());                      // *

        // ===== 2. CHAIN: box double -> unbox -> ToInt32 (rounding) -> rebox =====
        object bd = 2.5;
        int i2 = Convert.ToInt32((double)bd);        // 2.5 -> 2 (banker's)
        object r5 = i2;
        Console.WriteLine("c2_bank=" + r5.ToString());                      // 2
        object bd2 = 3.5;
        int i2b = Convert.ToInt32((double)bd2);      // 3.5 -> 4 (banker's)
        object r6 = i2b;
        Console.WriteLine("c2_bank2=" + r6.ToString());                     // 4
        object bd3 = 3.7;
        int i2c = Convert.ToInt32((double)bd3);      // 3.7 -> 4 (half-away ties only)
        Console.WriteLine("c2_round=" + (i2c == 4));                        // True
        object r7 = Convert.ToInt32(Convert.ToString(i2)); // "2" text round trip
        Console.WriteLine("c2_parsed=" + ((int)r7 == 2));                   // True

        // ===== 3. CHAIN: box long -> unbox -> ToInt32 (range) -> rebox =====
        object bl = 2147483647L;
        object r8 = Convert.ToInt32((long)bl);
        Console.WriteLine("c3_max=" + ((int)r8 == int.MaxValue));           // True
        object bl2 = -2147483648L;
        object r9 = Convert.ToInt32((long)bl2);
        Console.WriteLine("c3_min=" + ((int)r9 == int.MinValue));           // True
        bool of = false;
        try { int x = Convert.ToInt32((long)2147483648L); of = true; }
        catch (OverflowException) { of = false; }
        Console.WriteLine("c3_ovf=" + of);                                 // False (caught)

        // ===== 4. CHAIN: box char -> unbox -> code unit -> rebox char =====
        object bc = 'A';
        int code = Convert.ToInt32((char)bc);        // 65
        object r10 = Convert.ToChar(code);           // rebox 'A'
        Console.WriteLine("c4_char=" + r10.ToString());                     // A
        Console.WriteLine("c4_code=" + (code == 65));                       // True
        Console.WriteLine("c4_code2=" + (Convert.ToInt32((char)bc) == 65)); // True

        // ===== 5. CHAIN: box string -> parse -> rebox -> Convert family =====
        object bs = "  -7  ";
        object r12 = Convert.ToInt32((string)bs);    // trimmed parse -> -7
        Console.WriteLine("c5_neg=" + ((int)r12 == -7));                    // True
        object r13 = Convert.ToString((int)r12);     // "-7" (rebox string)
        Console.WriteLine("c5_str=" + r13.ToString());                      // -7
        object bs2 = "42";
        object r14 = Convert.ToInt64((string)bs2);   // long parse -> 42L
        Console.WriteLine("c5_lng=" + (Convert.ToString((long)r14) == "42")); // True
        object r15 = Convert.ToDouble((int)r12);     // -7 -> -7.0
        Console.WriteLine("c5_dbl=" + (r15.ToString() == "-7"));            // True

        // ===== 6. CHAIN: boxed-value concat (multiple boxes, one line) =====
        object n1 = 7;
        object n2 = 3.5;
        object n3 = true;
        object n4 = 'Z';
        Console.WriteLine("c6_mix=" + ("a" + n1 + "|" + n2 + "|" + n3 + "|" + n4)); // a7|3.5|True|Z
        Console.WriteLine("c6_chain=" + (("v=" + n1) + "!"));               // v=7!
        object n5 = 1234567890123L;
        Console.WriteLine("c6_long=" + ("v=" + n5));                        // v=1234567890123

        // ===== 7. CHAIN: object[] traversal -- unbox/convert/rebox per element =====
        // (isinst Int64 is a recorded compile gap GAP-BOX-2, so the long
        //  element is selected by the string/fallback ordering instead of an
        //  `is long` probe -- same code path, no long isinst.)
        object[] arr = new object[] { 5, 2.5, true, 'B', 100L, "  -3  " };
        int[] wantInt = new int[] { 5, 2, 1, 66, 100, -3 };
        bool allConv = true;
        for (int i = 0; i < arr.Length; i++)
        {
            int v;
            if (arr[i] is int) v = (int)arr[i];
            else if (arr[i] is double) v = Convert.ToInt32((double)arr[i]);
            else if (arr[i] is bool) v = ((bool)arr[i]) ? 1 : 0;
            else if (arr[i] is char) v = Convert.ToInt32((char)arr[i]);
            else if (arr[i] is string) v = Convert.ToInt32((string)arr[i]);
            else v = Convert.ToInt32((long)arr[i]);
            allConv = allConv && (v == wantInt[i]);
        }
        Console.WriteLine("c7_all=" + allConv);                             // True

        S8Decimal();


        // ===== 9. CHAIN: wrong-type unbox mid-chain -> InvalidCastException =====
        bool e1 = false;
        try { int x = (int)bd; e1 = true; }
        catch (InvalidCastException) { e1 = false; }
        Console.WriteLine("c9_dblasint=" + e1);                             // False
        bool e2 = false;
        try { double x = (double)bi; e2 = true; }
        catch (InvalidCastException) { e2 = false; }
        Console.WriteLine("c9_intasdbl=" + e2);                             // False
        bool e3 = false;
        try { long x = (long)bi; e3 = true; }
        catch (InvalidCastException) { e3 = false; }
        Console.WriteLine("c9_intaslng=" + e3);                             // False

        // ===== 10. boxed ToString value semantics re-confirmed on chains =====
        Console.WriteLine("c10_int=" + bi.ToString());                      // 42
        Console.WriteLine("c10_dbl=" + bd.ToString());                      // 2.5
        Console.WriteLine("c10_bool=" + n3.ToString());                     // True
        Console.WriteLine("c10_char=" + n4.ToString());                     // Z
        Console.WriteLine("c10_long=" + n5.ToString());                     // 1234567890123
        object bu = 4000000000u;
        Console.WriteLine("c10_uint=" + bu.ToString());                     // 4000000000
    }

    // ===== 8. CHAIN: boxed decimal -> unbox -> rebox -> ToString =====
    // (decimal ops in their OWN method -- the temp-bucket rule: decimal
    //  icalls share the 16-byte Il2CppDecimal temp slots, so a method that
    //  interleaves decimal and int32 expressions collides temp types --
    //  decimalops2/deep S1 precedent.)
    private static void S8Decimal()
    {
        object bdec = 123.45m;
        decimal dec = (decimal)bdec;
        object r16 = dec;                            // rebox decimal
        Console.WriteLine("c8_dec=" + r16.ToString());                      // 123.45
        object r17 = ((decimal)bdec) * 2m;
        Console.WriteLine("c8_mul=" + r17.ToString());                      // 246.90
        Console.WriteLine("c8_comp=" + string.Format("{0}", r17));          // 246.90 (composite channel)
        Console.WriteLine("c8_dec2=" + bdec.ToString());                    // 123.45 (boxed decimal ToString)
    }
}}
