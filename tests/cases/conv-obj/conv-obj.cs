using System;
using System.Globalization;

//------------------------------------------------------------------------------
// conv-obj -- batch-8 Convert residuals: the OBJECT-source Convert overloads
// (Convert.ToXxx(object value)) that were LOUD before this batch.
//
// net48: Convert.ToXxx(object) applies the Convert "null means zero" rule FIRST
// (null -> 0 / "" / 0m), then dispatches on the boxed value's IConvertible
// implementation. Every boxed primitive implements IConvertible; a
// non-IConvertible object (DateTime/Guid/TimeSpan/a user struct/an enum) ->
// InvalidCastException. The per-source conversions reuse the value-source
// icalls, so the object overloads cannot drift from the value overloads.
//
// Every probe prints a labeled result (or the exception type name for a
// rejected source); byte-exact against the net48 oracle.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class ConvObj
    {
        private static string Probe(Func<object> f)
        {
            try { return Convert.ToString(f()); }
            catch (Exception e) { return e.GetType().FullName; }
        }

        public static void Main()
        {
            // ===== ToInt32(object) =====
            Console.WriteLine("i_null=" + Convert.ToInt32((object)null));
            Console.WriteLine("i_int=" + Convert.ToInt32((object)42));
            Console.WriteLine("i_intm=" + Convert.ToInt32((object)(-7)));
            Console.WriteLine("i_i8=" + Convert.ToInt32((object)1234567L));
            Console.WriteLine("i_u8=" + Convert.ToInt32((object)42u));
            Console.WriteLine("i_d=" + Convert.ToInt32((object)3.5));
            Console.WriteLine("i_dm=" + Convert.ToInt32((object)(-2.5)));
            Console.WriteLine("i_f=" + Convert.ToInt32((object)2.9f));
            Console.WriteLine("i_b=" + Convert.ToInt32((object)true));
            Console.WriteLine("i_bf=" + Convert.ToInt32((object)false));
            Console.WriteLine("i_c=" + Convert.ToInt32((object)'A'));
            Console.WriteLine("i_s=" + Convert.ToInt32((object)"1234"));
            Console.WriteLine("i_dec=" + Convert.ToInt32((object)3.5m));
            Console.WriteLine("i_dt=" + Probe(delegate { return Convert.ToInt32((object)DateTime.Now); }));
            Console.WriteLine("i_u32ovf=" + Probe(delegate { return Convert.ToInt32((object)3000000000u); }));
            Console.WriteLine("i_i8ovf=" + Probe(delegate { return Convert.ToInt32((object)3000000000L); }));
            Console.WriteLine("i_dovf=" + Probe(delegate { return Convert.ToInt32((object)3.0e10); }));

            // ===== ToInt64(object) =====
            Console.WriteLine("l_null=" + Convert.ToInt64((object)null));
            Console.WriteLine("l_int=" + Convert.ToInt64((object)42));
            Console.WriteLine("l_i8=" + Convert.ToInt64((object)1234567890123L));
            Console.WriteLine("l_u8=" + Convert.ToInt64((object)1234567890123UL));
            Console.WriteLine("l_d=" + Convert.ToInt64((object)3.5));
            Console.WriteLine("l_f=" + Convert.ToInt64((object)2.9f));
            Console.WriteLine("l_b=" + Convert.ToInt64((object)true));
            Console.WriteLine("l_c=" + Convert.ToInt64((object)'A'));
            Console.WriteLine("l_s=" + Convert.ToInt64((object)"1234567890123"));
            Console.WriteLine("l_dec=" + Convert.ToInt64((object)3.5m));
            Console.WriteLine("l_dt=" + Probe(delegate { return Convert.ToInt64((object)DateTime.Now); }));

            // ===== ToDouble(object) =====
            Console.WriteLine("d_null=" + Convert.ToDouble((object)null));
            Console.WriteLine("d_int=" + Convert.ToDouble((object)42));
            Console.WriteLine("d_i8=" + Convert.ToDouble((object)1234567890123L));
            Console.WriteLine("d_u8=" + Convert.ToDouble((object)18446744073709551615UL));
            Console.WriteLine("d_d=" + Convert.ToDouble((object)3.25));
            Console.WriteLine("d_f=" + Convert.ToDouble((object)1.5f));
            Console.WriteLine("d_b=" + Probe(delegate { return Convert.ToDouble((object)true); }));
            Console.WriteLine("d_c=" + Probe(delegate { return Convert.ToDouble((object)'A'); }));
            Console.WriteLine("d_s=" + Convert.ToDouble((object)"3.25"));
            Console.WriteLine("d_dec=" + Convert.ToDouble((object)3.5m));

            // ===== ToSingle(object) =====
            Console.WriteLine("f_null=" + Convert.ToSingle((object)null));
            Console.WriteLine("f_int=" + Convert.ToSingle((object)42));
            Console.WriteLine("f_i8=" + Convert.ToSingle((object)1234567890123L));
            Console.WriteLine("f_d=" + Convert.ToSingle((object)3.25));
            Console.WriteLine("f_f=" + Convert.ToSingle((object)1.5f));
            Console.WriteLine("f_b=" + Convert.ToSingle((object)true));
            Console.WriteLine("f_c=" + Probe(delegate { return Convert.ToSingle((object)'A'); }));
            Console.WriteLine("f_s=" + Convert.ToSingle((object)"1.5"));
            Console.WriteLine("f_dec=" + Convert.ToSingle((object)3.5m));

            // ===== ToBoolean(object) =====
            Console.WriteLine("z_null=" + Convert.ToBoolean((object)null));
            Console.WriteLine("z_int=" + Convert.ToBoolean((object)42));
            Console.WriteLine("z_int0=" + Convert.ToBoolean((object)0));
            Console.WriteLine("z_b=" + Convert.ToBoolean((object)true));
            Console.WriteLine("z_i8=" + Convert.ToBoolean((object)3000000000L));
            Console.WriteLine("z_u8=" + Convert.ToBoolean((object)3000000000UL));
            Console.WriteLine("z_s=" + Convert.ToBoolean((object)"true"));
            Console.WriteLine("z_c=" + Probe(delegate { return Convert.ToBoolean((object)'A'); }));
            Console.WriteLine("z_d=" + Probe(delegate { return Convert.ToBoolean((object)3.5); }));

            // ===== ToChar(object) =====
            Console.WriteLine("c_null=" + ((int)Convert.ToChar((object)null)));
            Console.WriteLine("c_c=" + ((int)Convert.ToChar((object)'A')));
            Console.WriteLine("c_int=" + ((int)Convert.ToChar((object)65)));
            Console.WriteLine("c_i8=" + ((int)Convert.ToChar((object)65L)));
            Console.WriteLine("c_u8=" + ((int)Convert.ToChar((object)65UL)));
            Console.WriteLine("c_s=" + ((int)Convert.ToChar((object)"A")));
            Console.WriteLine("c_b=" + Probe(delegate { return (int)Convert.ToChar((object)true); }));
            Console.WriteLine("c_d=" + Probe(delegate { return (int)Convert.ToChar((object)3.5); }));
            Console.WriteLine("c_ovf=" + Probe(delegate { return (int)Convert.ToChar((object)70000); }));

            // ===== narrow targets (object) =====
            Console.WriteLine("b_null=" + Convert.ToByte((object)null));
            Console.WriteLine("b_int=" + Convert.ToByte((object)200));
            Console.WriteLine("b_c=" + Convert.ToByte((object)'A'));
            Console.WriteLine("b_d=" + Convert.ToByte((object)3.5));
            Console.WriteLine("b_s=" + Convert.ToByte((object)"200"));
            Console.WriteLine("b_ovf=" + Probe(delegate { return Convert.ToByte((object)300); }));
            Console.WriteLine("sb_int=" + Convert.ToSByte((object)(-5)));
            Console.WriteLine("sb_ovf=" + Probe(delegate { return Convert.ToSByte((object)200); }));
            Console.WriteLine("i2_int=" + Convert.ToInt16((object)1234));
            Console.WriteLine("i2_ovf=" + Probe(delegate { return Convert.ToInt16((object)40000); }));
            Console.WriteLine("u2_int=" + Convert.ToUInt16((object)65535));
            Console.WriteLine("u2_ovf=" + Probe(delegate { return Convert.ToUInt16((object)(-1)); }));

            // ===== ToUInt32 / ToUInt64 (object) =====
            Console.WriteLine("u4_null=" + Convert.ToUInt32((object)null));
            Console.WriteLine("u4_int=" + Convert.ToUInt32((object)42));
            Console.WriteLine("u4_u4=" + Convert.ToUInt32((object)3000000000u));
            Console.WriteLine("u4_i8=" + Convert.ToUInt32((object)3000000000L));
            Console.WriteLine("u4_s=" + Convert.ToUInt32((object)"123456"));
            Console.WriteLine("u4_neg=" + Probe(delegate { return Convert.ToUInt32((object)(-1)); }));
            Console.WriteLine("u8_null=" + Convert.ToUInt64((object)null));
            Console.WriteLine("u8_int=" + Convert.ToUInt64((object)42));
            Console.WriteLine("u8_u8=" + Convert.ToUInt64((object)18446744073709551615UL));
            Console.WriteLine("u8_i8=" + Convert.ToUInt64((object)3000000000L));
            Console.WriteLine("u8_s=" + Convert.ToUInt64((object)"1234567890123"));
            Console.WriteLine("u8_neg=" + Probe(delegate { return Convert.ToUInt64((object)(-1)); }));

            // ===== ToDecimal(object) =====
            Console.WriteLine("m_null=" + Convert.ToDecimal((object)null));
            Console.WriteLine("m_int=" + Convert.ToDecimal((object)42));
            Console.WriteLine("m_i8=" + Convert.ToDecimal((object)1234567890123L));
            Console.WriteLine("m_d=" + Convert.ToDecimal((object)3.25));
            Console.WriteLine("m_f=" + Convert.ToDecimal((object)1.5f));
            Console.WriteLine("m_b=" + Convert.ToDecimal((object)true));
            Console.WriteLine("m_s=" + Convert.ToDecimal((object)"3.25"));
            Console.WriteLine("m_dec=" + Convert.ToDecimal((object)3.5m));
            Console.WriteLine("m_c=" + Probe(delegate { return Convert.ToDecimal((object)'A'); }));

            // ===== ToString(object) (already wired; regression pin) =====
            Console.WriteLine("s_null=" + Convert.ToString((object)null));
            Console.WriteLine("s_int=" + Convert.ToString((object)42));
            Console.WriteLine("s_s=" + Convert.ToString((object)"hi"));
            Console.WriteLine("s_b=" + Convert.ToString((object)true));

            // ===== value-source residuals (batch 8; oracle-pinned) =====
            Console.WriteLine("v_tobool_d=" + Convert.ToBoolean(3.5));
            Console.WriteLine("v_tobool_f=" + Convert.ToBoolean(1.5f));
            Console.WriteLine("v_tobool_i8=" + Convert.ToBoolean(3000000000L));
            Console.WriteLine("v_tobool_u8=" + Convert.ToBoolean(3000000000UL));
            Console.WriteLine("v_tobool_dec=" + Convert.ToBoolean(3.5m));
            Console.WriteLine("v_todouble_b=" + Convert.ToDouble(true));
            Console.WriteLine("v_todouble_bf=" + Convert.ToDouble(false));
            Console.WriteLine("v_tochar_i8=" + ((int)Convert.ToChar(65L)));
            Console.WriteLine("v_tochar_u8=" + ((int)Convert.ToChar(65UL)));
            Console.WriteLine("v_tochar_i8ovf=" + Probe(delegate { return (int)Convert.ToChar(70000L); }));
            Console.WriteLine("v_tochar_u8ovf=" + Probe(delegate { return (int)Convert.ToChar(70000UL); }));

            // ===== ToString missing sources (batch 8) =====
            Console.WriteLine("w_tostr_u4=" + Convert.ToString(4000000000u));
            Console.WriteLine("w_tostr_u4b=" + Convert.ToString(42u));
            Console.WriteLine("w_tostr_u8=" + Convert.ToString(18446744073709551615UL));
            Console.WriteLine("w_tostr_f=" + Convert.ToString(1.5f));
            Console.WriteLine("w_tostr_dec=" + Convert.ToString(3.5m));
            Console.WriteLine("w_tostr_decp=" + Convert.ToString(3.5m, CultureInfo.InvariantCulture));
            Console.WriteLine("w_tostr_u4p=" + Convert.ToString(4000000000u, CultureInfo.InvariantCulture));
            Console.WriteLine("w_tostr_u8p=" + Convert.ToString(18446744073709551615UL, CultureInfo.InvariantCulture));
            Console.WriteLine("w_tostr_fp=" + Convert.ToString(1.5f, CultureInfo.InvariantCulture));
            Console.WriteLine("w_tostr_sp=" + Convert.ToString("hi", CultureInfo.InvariantCulture));

            // ===== InvalidCastException residuals (batch 8) =====
            Console.WriteLine("w_todouble_c=" + Probe(delegate { return Convert.ToDouble('A'); }));
            Console.WriteLine("w_tobool_c=" + Probe(delegate { return Convert.ToBoolean('A'); }));
            Console.WriteLine("w_tochar_b=" + Probe(delegate { return (int)Convert.ToChar(true); }));
            Console.WriteLine("w_tochar_d=" + Probe(delegate { return (int)Convert.ToChar(3.5); }));
            Console.WriteLine("w_tochar_f=" + Probe(delegate { return (int)Convert.ToChar(1.5f); }));

            // ===== Base64 arity/options overloads (batch 8) =====
            System.Collections.Generic.List<byte> tmp = new System.Collections.Generic.List<byte>();
            for (int i = 0; i < 64; i++) tmp.Add((byte)(i + 1));
            byte[] b64 = tmp.ToArray();
            Console.WriteLine("b64_range=" + Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 }, 1, 3));
            Console.WriteLine("b64_range0=" + Convert.ToBase64String(new byte[] { 1, 2, 3 }, 0, 3));
            Console.WriteLine("b64_opts_none=" + Convert.ToBase64String(new byte[] { 1, 2, 3 }, Base64FormattingOptions.None));
            Console.WriteLine("b64_opts_breaks=" + Convert.ToBase64String(b64, Base64FormattingOptions.InsertLineBreaks));
            Console.WriteLine("b64_range_opts=" + Convert.ToBase64String(b64, 4, 40, Base64FormattingOptions.InsertLineBreaks));
            Console.WriteLine("b64_fromca=" + Convert.ToBase64String(Convert.FromBase64CharArray("YWJj".ToCharArray(), 0, 4)));
            Console.WriteLine("b64_fromca_sub=" + Convert.ToBase64String(Convert.FromBase64CharArray("xxYWJjyy".ToCharArray(), 2, 4)));
            Console.WriteLine("b64_fromca_ovf=" + Probe(delegate { return Convert.FromBase64CharArray("YW".ToCharArray(), 0, 2); }));
            Console.WriteLine("b64_range_ovf=" + Probe(delegate { return Convert.ToBase64String(b64, 60, 10); }));
            Console.WriteLine("b64_ovf_off=" + Probe(delegate { return Convert.ToBase64String(b64, 64, 0); }));
            Console.WriteLine("b64_ovf_off2=" + Probe(delegate { return Convert.ToBase64String(b64, 65, 0); }));
            Console.WriteLine("b64_ovf_len=" + Probe(delegate { return Convert.ToBase64String(b64, 0, 65); }));
            Console.WriteLine("b64_ovf_neg=" + Probe(delegate { return Convert.ToBase64String(b64, -1, 10); }));
            Console.WriteLine("b64_fromca_ovf_off=" + Probe(delegate { return Convert.FromBase64CharArray("YWJj".ToCharArray(), 4, 0).Length; }));
            Console.WriteLine("b64_fromca_ovf_off2=" + Probe(delegate { return Convert.FromBase64CharArray("YWJj".ToCharArray(), 5, 0); }));
            Console.WriteLine("b64_fromca_ovf_len=" + Probe(delegate { return Convert.FromBase64CharArray("YWJj".ToCharArray(), 0, 5); }));

            // ===== ToDecimal(value, IFormatProvider) (batch 8) =====
            Console.WriteLine("d2_int=" + Convert.ToDecimal(42, CultureInfo.InvariantCulture));
            Console.WriteLine("d2_str=" + Convert.ToDecimal("3.25", CultureInfo.InvariantCulture));
            Console.WriteLine("d2_dec=" + Convert.ToDecimal(3.5m, CultureInfo.InvariantCulture));
            Console.WriteLine("d2_null=" + Convert.ToDecimal((string)null, CultureInfo.InvariantCulture));
            Console.WriteLine("d2_obj=" + Convert.ToDecimal((object)42, CultureInfo.InvariantCulture));
            Console.WriteLine("d2_bool=" + Convert.ToDecimal(true, CultureInfo.InvariantCulture));
            Console.WriteLine("d2_dbl=" + Convert.ToDecimal(3.25, CultureInfo.InvariantCulture));
        }
    }
}
