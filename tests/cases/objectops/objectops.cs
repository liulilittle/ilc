//------------------------------------------------------------------------------
// objectops.cs -- System.Object DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C69 (2026-08-12), AFTER the object /
// value-semantics domain wiring (MethodMap.Object.cs -> icall_object_*,
// valuesemantics.cpp + boxingext2.cpp boxed-value dispatch).
//
// DEEP DIMENSIONS (all surfaces below are WIRED):
//   1. INSTANCE Equals on boxed values (callvirt Object::Equals(object) ->
//      icall_boxed_equals_ext2): same value True, different value False,
//      CROSS-TYPE False (boxed int vs boxed long), double NaN equals NaN
//      True, +0.0 vs -0.0 True, bool/char/long/uint boxes, Equals(null)
//      False.
//   2. STATIC object.Equals(a,b) -> icall_object_equals: null==null True,
//      string content equality, boxed value equality, cross-type False.
//   3. object.ReferenceEquals -> the pointer-compare fold: same instance
//      True, distinct instances False, null==null True, two independent
//      boxes of the same value False.
//   4. GetHashCode: boxed-value VALUE-based hashes pinned to the net48
//      constants (int -> value, long -> low^high, bool -> 1/0, char ->
//      c|(c<<16), double -> bits-XOR with the 0 shortcut, uint32 -> the raw
//      pattern, byte/sbyte/int16/uint16 -> the per-type formula) and
//      STRING ordinal-hash CONSISTENCY (net48 string hash is randomized per
//      process -- absolute string-hash values are NOT reproducible, so only
//      equality-of-equal-strings is asserted; D-OBJ-1).
//   5. ToString: boxed values print their net48 VALUE text (int/long/
//      double/bool/char/byte/uint), string identity, new object() -> its
//      full type name "System.Object".
//   6. GetType().Name (short) + GetType().ToString() (full) on boxes.
//   7. BOXING SURFACE: box -> unbox roundtrips per type, is/as checks,
//      wrong-type unbox -> InvalidCastException, null unbox ->
//      NullReferenceException.
//
// (User-struct ValueType::GetHashCode is NOT asserted: the constrained
// receiver's first-field classification is not reachable from this shape
// yet -- D-OBJ-2 in GAPS.)
//
// EVERY line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

class Program
{
    static void Main()
    {
        // ============ 1. instance Equals on boxed values ============
        Console.WriteLine("eq_int=" + (((object)42).Equals((object)42)));
        Console.WriteLine("eq_int_diff=" + (((object)42).Equals((object)43)));
        Console.WriteLine("eq_int_long=" + (((object)42).Equals((object)42L)));
        Console.WriteLine("eq_long=" + (((object)42L).Equals((object)42L)));
        Console.WriteLine("eq_long_diff=" + (((object)42L).Equals((object)43L)));
        Console.WriteLine("eq_double=" + (((object)3.25).Equals((object)3.25)));
        Console.WriteLine("eq_double_diff=" + (((object)3.25).Equals((object)3.5)));
        Console.WriteLine("eq_double_nan=" + (((object)double.NaN).Equals((object)double.NaN)));
        Console.WriteLine("eq_double_zero=" + (((object)0.0).Equals((object)(-0.0))));
        Console.WriteLine("eq_bool=" + (((object)true).Equals((object)true)));
        Console.WriteLine("eq_bool_diff=" + (((object)true).Equals((object)false)));
        Console.WriteLine("eq_char=" + (((object)'A').Equals((object)'A')));
        Console.WriteLine("eq_char_diff=" + (((object)'A').Equals((object)'B')));
        Console.WriteLine("eq_uint=" + (((object)uint.MaxValue).Equals((object)uint.MaxValue)));
        Console.WriteLine("eq_int_null=" + (((object)42).Equals(null)));

        // ============ 2. static object.Equals ============
        Console.WriteLine("se_nullnull=" + object.Equals(null, null));
        Console.WriteLine("se_nullint=" + object.Equals(null, (object)42));
        Console.WriteLine("se_str=" + object.Equals("abc", "abc"));
        Console.WriteLine("se_str_diff=" + object.Equals("abc", "abd"));
        Console.WriteLine("se_int=" + object.Equals((object)42, (object)42));
        Console.WriteLine("se_int_diff=" + object.Equals((object)42, (object)43));
        Console.WriteLine("se_int_long=" + object.Equals((object)42, (object)42L));
        Console.WriteLine("se_double=" + object.Equals((object)3.25, (object)3.25));
        Console.WriteLine("se_double_nan=" + object.Equals((object)double.NaN, (object)double.NaN));
        Console.WriteLine("se_obj=" + (object.Equals(new object(), new object()) == false));

        // ============ 3. object.ReferenceEquals ============
        object o1 = new object();
        object o2 = new object();
        Console.WriteLine("re_self=" + object.ReferenceEquals(o1, o1));
        Console.WriteLine("re_distinct=" + (object.ReferenceEquals(o1, o2) == false));
        Console.WriteLine("re_nullnull=" + object.ReferenceEquals(null, null));
        Console.WriteLine("re_boxes=" + (object.ReferenceEquals((object)42, (object)42) == false));

        // ============ 4. GetHashCode: boxed value hashes ============
        Console.WriteLine("h_int42=" + (((object)42).GetHashCode() == 42));
        Console.WriteLine("h_intneg=" + (((object)(-42)).GetHashCode() == -42));
        Console.WriteLine("h_long42=" + (((object)42L).GetHashCode() == 42));
        Console.WriteLine("h_longbig=" + (((object)0x123456789L).GetHashCode() == unchecked((int)(0x123456789L ^ (0x123456789L >> 32)))));
        Console.WriteLine("h_booltrue=" + (((object)true).GetHashCode() == 1));
        Console.WriteLine("h_boolfalse=" + (((object)false).GetHashCode() == 0));
        Console.WriteLine("h_charA=" + (((object)'A').GetHashCode() == 4259905));
        Console.WriteLine("h_double0=" + (((object)0.0).GetHashCode() == 0));
        Console.WriteLine("h_doubleneg0=" + (((object)(-0.0)).GetHashCode() == 0));
        Console.WriteLine("h_double1=" + (((object)1.0).GetHashCode() == 1072693248));
        Console.WriteLine("h_uintmax=" + (((object)uint.MaxValue).GetHashCode() == -1));
        Console.WriteLine("h_byte255=" + (((object)(byte)255).GetHashCode() == 255));
        Console.WriteLine("h_sbyte5=" + (((object)(sbyte)-5).GetHashCode() == 1275));
        Console.WriteLine("h_short12=" + (((object)(short)-12).GetHashCode() == -720908));
        Console.WriteLine("h_ushortmax=" + (((object)(ushort)65535).GetHashCode() == 65535));

        // string hash CONSISTENCY (D-OBJ-1: net48 randomizes the seed per
        // process, so absolute string-hash values are not reproducible).
        Console.WriteLine("h_str_self=" + ("hello world".GetHashCode() == "hello world".GetHashCode()));
        Console.WriteLine("h_str_equal=" + (("abc".GetHashCode() == "abc".GetHashCode()) && "abc".Equals("abc")));

        // ============ 5. ToString on boxed values ============
        Console.WriteLine("ts_int=" + (((object)42).ToString() == "42"));
        Console.WriteLine("ts_intneg=" + (((object)(-42)).ToString() == "-42"));
        Console.WriteLine("ts_long=" + (((object)1234567890123L).ToString() == "1234567890123"));
        Console.WriteLine("ts_double=" + (((object)3.25).ToString() == "3.25"));
        Console.WriteLine("ts_bool=" + (((object)true).ToString() == "True"));
        Console.WriteLine("ts_char=" + (((object)'A').ToString() == "A"));
        Console.WriteLine("ts_byte=" + (((object)(byte)255).ToString() == "255"));
        Console.WriteLine("ts_uintmax=" + (((object)uint.MaxValue).ToString() == "4294967295"));
        Console.WriteLine("ts_string=" + (((object)"abc").ToString() == "abc"));
        Console.WriteLine("ts_newobj=" + (new object().ToString() == "System.Object"));

        // ============ 6. GetType().Name / GetType().ToString() ============
        Console.WriteLine("gt_int=" + (((object)42).GetType().Name == "Int32"));
        Console.WriteLine("gt_long=" + (((object)42L).GetType().Name == "Int64"));
        Console.WriteLine("gt_double=" + (((object)3.25).GetType().Name == "Double"));
        Console.WriteLine("gt_bool=" + (((object)true).GetType().Name == "Boolean"));
        Console.WriteLine("gt_char=" + (((object)'A').GetType().Name == "Char"));
        Console.WriteLine("gt_uint=" + (((object)uint.MaxValue).GetType().Name == "UInt32"));
        Console.WriteLine("gt_obj=" + (new object().GetType().Name == "Object"));
        Console.WriteLine("gt_int_full=" + (((object)42).GetType().ToString() == "System.Int32"));
        Console.WriteLine("gt_obj_full=" + (new object().GetType().ToString() == "System.Object"));

        // ============ 7. boxing surface: roundtrips + is/as + unbox errors ============
        object bx = 42;
        Console.WriteLine("box_int=" + ((int)bx == 42));
        Console.WriteLine("box_int_is=" + (bx is int));
        Console.WriteLine("box_int_is_long=" + (bx is long == false));
        object bl = 42L;
        Console.WriteLine("box_long=" + ((long)bl == 42L));
        Console.WriteLine("box_long_is=" + (bl is long));
        object bd = 3.25;
        Console.WriteLine("box_double=" + ((double)bd == 3.25));
        Console.WriteLine("box_double_is=" + (bd is double));
        object bb = true;
        Console.WriteLine("box_bool=" + ((bool)bb));
        Console.WriteLine("box_bool_is=" + (bb is bool));
        object bc = 'A';
        Console.WriteLine("box_char=" + ((char)bc == 'A'));
        Console.WriteLine("box_char_is=" + (bc is char));
        object bu = uint.MaxValue;
        Console.WriteLine("box_uint=" + ((uint)bu == uint.MaxValue));
        Console.WriteLine("box_uint_is=" + (bu is uint));
        Console.WriteLine("box_uint_is_int=" + (bu is int == false));
        try { int x = (int)bl; Console.WriteLine(false); }
        catch (InvalidCastException) { Console.WriteLine(true); }
        try { uint x = (uint)bx; Console.WriteLine(false); }
        catch (InvalidCastException) { Console.WriteLine(true); }
        try { int x = (int)(object)null; Console.WriteLine(false); }
        catch (NullReferenceException) { Console.WriteLine(true); }
    }
}
