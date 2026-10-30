//------------------------------------------------------------------------------
// probe-listctor -- System.Collections.Generic.List`1 constructors for EVERY
// non-int32 element family.
//
// net48 defines exactly three instance ctors:
//   List()                 -> empty list, Capacity 0
//   List(int capacity)     -> Count 0, Capacity == capacity (capacity < 0 ->
//                             ArgumentOutOfRangeException)
//   List(IEnumerable<T>)   -> a COPY of the source (null -> ArgumentNullException);
//                             for an ICollection<T> source (array / List) the
//                             initial Capacity equals the source Count
//                             (an empty source therefore has Capacity 0)
//
// The transpiler wired only the List<int32> expansion (listi4.h). Every other
// family fell into a compile-time NotSupportedException claiming "net48 has no
// such overload" -- a FALSE message: the overloads exist, the runtime/transpiler
// arms were simply missing.
//
// Shapes pinned here, per family:
//   cap5   : new List<T>(5)  -> Count 0, Capacity 5, Add keeps the values
//   arr    : new List<T>(T[])   -> Count/Capacity == source length, elements equal
//   lst    : new List<T>(List<T>) -> same
//   zero   : new List<T>(0)  -> Count 0, Capacity 0
//   neg    : new List<T>(-1) -> ArgumentOutOfRangeException
//   null   : new List<T>((IEnumerable<T>)null) -> ArgumentNullException
//   emptyarr / emptylst -> Count 0, Capacity 0
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cases
{
    public struct Pt
    {
        public int X;
        public int Y;
        public Pt(int x, int y) { X = x; Y = y; }
    }

    public class RefElem
    {
        public int V;
        public RefElem(int v) { V = v; }
    }

    public static class ProbeListCtor
    {
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string L(long v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string D(double v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string M(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        private static void LongShape()
        {
            List<long> a = new List<long>(5);
            Console.WriteLine("[long_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(11L); a.Add(-12L);
            Console.WriteLine("[long_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + L(a[0]) + "/" + L(a[1]));
            List<long> b = new List<long>(new long[] { 7L, -8L, 9L });
            Console.WriteLine("[long_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + L(b[0]) + "/" + L(b[1]) + "/" + L(b[2]));
            List<long> c = new List<long>(b);
            Console.WriteLine("[long_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + L(c[2]));
            List<long> z = new List<long>(0);
            Console.WriteLine("[long_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<long> ea = new List<long>(new long[0]);
            Console.WriteLine("[long_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<long> el = new List<long>(new List<long>());
            Console.WriteLine("[long_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<long> n = new List<long>(-1); Console.WriteLine("[long_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[long_neg]=aoore"); }
            try { List<long> n = new List<long>((IEnumerable<long>)null); Console.WriteLine("[long_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[long_null]=ane"); }
        }

        private static void DoubleShape()
        {
            List<double> a = new List<double>(5);
            Console.WriteLine("[double_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(1.5); a.Add(-2.25);
            Console.WriteLine("[double_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + D(a[0]) + "/" + D(a[1]));
            List<double> b = new List<double>(new double[] { 1.5, -2.25, 0.5 });
            Console.WriteLine("[double_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + D(b[0]) + "/" + D(b[1]) + "/" + D(b[2]));
            List<double> c = new List<double>(b);
            Console.WriteLine("[double_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + D(c[2]));
            List<double> z = new List<double>(0);
            Console.WriteLine("[double_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<double> ea = new List<double>(new double[0]);
            Console.WriteLine("[double_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<double> el = new List<double>(new List<double>());
            Console.WriteLine("[double_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<double> n = new List<double>(-1); Console.WriteLine("[double_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[double_neg]=aoore"); }
            try { List<double> n = new List<double>((IEnumerable<double>)null); Console.WriteLine("[double_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[double_null]=ane"); }
        }

        private static void FloatShape()
        {
            List<float> a = new List<float>(5);
            Console.WriteLine("[float_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(1.5f); a.Add(-2.25f);
            Console.WriteLine("[float_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + D(a[0]) + "/" + D(a[1]));
            List<float> b = new List<float>(new float[] { 1.5f, -2.25f, 0.5f });
            Console.WriteLine("[float_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + D(b[0]) + "/" + D(b[1]) + "/" + D(b[2]));
            List<float> c = new List<float>(b);
            Console.WriteLine("[float_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + D(c[2]));
            List<float> z = new List<float>(0);
            Console.WriteLine("[float_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<float> ea = new List<float>(new float[0]);
            Console.WriteLine("[float_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<float> el = new List<float>(new List<float>());
            Console.WriteLine("[float_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<float> n = new List<float>(-1); Console.WriteLine("[float_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[float_neg]=aoore"); }
            try { List<float> n = new List<float>((IEnumerable<float>)null); Console.WriteLine("[float_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[float_null]=ane"); }
        }

        private static void BoolShape()
        {
            List<bool> a = new List<bool>(5);
            Console.WriteLine("[bool_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(true); a.Add(false);
            Console.WriteLine("[bool_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + I(a[0] ? 1 : 0) + "/" + I(a[1] ? 1 : 0));
            List<bool> b = new List<bool>(new bool[] { true, false });
            Console.WriteLine("[bool_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + I(b[0] ? 1 : 0) + "/" + I(b[1] ? 1 : 0));
            List<bool> c = new List<bool>(b);
            Console.WriteLine("[bool_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + I(c[0] ? 1 : 0));
            List<bool> z = new List<bool>(0);
            Console.WriteLine("[bool_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<bool> ea = new List<bool>(new bool[0]);
            Console.WriteLine("[bool_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<bool> el = new List<bool>(new List<bool>());
            Console.WriteLine("[bool_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<bool> n = new List<bool>(-1); Console.WriteLine("[bool_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[bool_neg]=aoore"); }
            try { List<bool> n = new List<bool>((IEnumerable<bool>)null); Console.WriteLine("[bool_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[bool_null]=ane"); }
        }

        private static void CharShape()
        {
            List<char> a = new List<char>(5);
            Console.WriteLine("[char_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add('A'); a.Add('z');
            Console.WriteLine("[char_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + I(a[0]) + "/" + I(a[1]));
            List<char> b = new List<char>(new char[] { 'A', 'z' });
            Console.WriteLine("[char_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + I(b[0]) + "/" + I(b[1]));
            List<char> c = new List<char>(b);
            Console.WriteLine("[char_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + I(c[1]));
            List<char> z = new List<char>(0);
            Console.WriteLine("[char_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<char> ea = new List<char>(new char[0]);
            Console.WriteLine("[char_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<char> el = new List<char>(new List<char>());
            Console.WriteLine("[char_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<char> n = new List<char>(-1); Console.WriteLine("[char_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[char_neg]=aoore"); }
            try { List<char> n = new List<char>((IEnumerable<char>)null); Console.WriteLine("[char_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[char_null]=ane"); }
        }

        private static void SbyteShape()
        {
            List<sbyte> a = new List<sbyte>(5);
            Console.WriteLine("[sbyte_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(-5); a.Add(6);
            Console.WriteLine("[sbyte_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + I(a[0]) + "/" + I(a[1]));
            List<sbyte> b = new List<sbyte>(new sbyte[] { -5, 6 });
            Console.WriteLine("[sbyte_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + I(b[0]) + "/" + I(b[1]));
            List<sbyte> c = new List<sbyte>(b);
            Console.WriteLine("[sbyte_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + I(c[1]));
            List<sbyte> z = new List<sbyte>(0);
            Console.WriteLine("[sbyte_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<sbyte> ea = new List<sbyte>(new sbyte[0]);
            Console.WriteLine("[sbyte_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<sbyte> el = new List<sbyte>(new List<sbyte>());
            Console.WriteLine("[sbyte_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<sbyte> n = new List<sbyte>(-1); Console.WriteLine("[sbyte_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[sbyte_neg]=aoore"); }
            try { List<sbyte> n = new List<sbyte>((IEnumerable<sbyte>)null); Console.WriteLine("[sbyte_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[sbyte_null]=ane"); }
        }

        private static void ByteShape()
        {
            List<byte> a = new List<byte>(5);
            Console.WriteLine("[byte_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(5); a.Add(250);
            Console.WriteLine("[byte_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + I(a[0]) + "/" + I(a[1]));
            List<byte> b = new List<byte>(new byte[] { 5, 250 });
            Console.WriteLine("[byte_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + I(b[0]) + "/" + I(b[1]));
            List<byte> c = new List<byte>(b);
            Console.WriteLine("[byte_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + I(c[1]));
            List<byte> z = new List<byte>(0);
            Console.WriteLine("[byte_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<byte> ea = new List<byte>(new byte[0]);
            Console.WriteLine("[byte_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<byte> el = new List<byte>(new List<byte>());
            Console.WriteLine("[byte_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<byte> n = new List<byte>(-1); Console.WriteLine("[byte_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[byte_neg]=aoore"); }
            try { List<byte> n = new List<byte>((IEnumerable<byte>)null); Console.WriteLine("[byte_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[byte_null]=ane"); }
        }

        private static void ShortShape()
        {
            List<short> a = new List<short>(5);
            Console.WriteLine("[short_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(-300); a.Add(301);
            Console.WriteLine("[short_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + I(a[0]) + "/" + I(a[1]));
            List<short> b = new List<short>(new short[] { -300, 301 });
            Console.WriteLine("[short_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + I(b[0]) + "/" + I(b[1]));
            List<short> c = new List<short>(b);
            Console.WriteLine("[short_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + I(c[1]));
            List<short> z = new List<short>(0);
            Console.WriteLine("[short_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<short> ea = new List<short>(new short[0]);
            Console.WriteLine("[short_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<short> el = new List<short>(new List<short>());
            Console.WriteLine("[short_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<short> n = new List<short>(-1); Console.WriteLine("[short_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[short_neg]=aoore"); }
            try { List<short> n = new List<short>((IEnumerable<short>)null); Console.WriteLine("[short_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[short_null]=ane"); }
        }

        private static void UshortShape()
        {
            List<ushort> a = new List<ushort>(5);
            Console.WriteLine("[ushort_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(300); a.Add(65535);
            Console.WriteLine("[ushort_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + I(a[0]) + "/" + I(a[1]));
            List<ushort> b = new List<ushort>(new ushort[] { 300, 65535 });
            Console.WriteLine("[ushort_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + I(b[0]) + "/" + I(b[1]));
            List<ushort> c = new List<ushort>(b);
            Console.WriteLine("[ushort_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + I(c[1]));
            List<ushort> z = new List<ushort>(0);
            Console.WriteLine("[ushort_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<ushort> ea = new List<ushort>(new ushort[0]);
            Console.WriteLine("[ushort_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<ushort> el = new List<ushort>(new List<ushort>());
            Console.WriteLine("[ushort_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<ushort> n = new List<ushort>(-1); Console.WriteLine("[ushort_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[ushort_neg]=aoore"); }
            try { List<ushort> n = new List<ushort>((IEnumerable<ushort>)null); Console.WriteLine("[ushort_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[ushort_null]=ane"); }
        }

        private static void UintShape()
        {
            List<uint> a = new List<uint>(5);
            Console.WriteLine("[uint_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(4000000000u); a.Add(7u);
            Console.WriteLine("[uint_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + a[0].ToString(CultureInfo.InvariantCulture) + "/" + a[1].ToString(CultureInfo.InvariantCulture));
            List<uint> b = new List<uint>(new uint[] { 4000000000u, 7u });
            Console.WriteLine("[uint_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + b[0].ToString(CultureInfo.InvariantCulture) + "/" + b[1].ToString(CultureInfo.InvariantCulture));
            List<uint> c = new List<uint>(b);
            Console.WriteLine("[uint_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + c[1].ToString(CultureInfo.InvariantCulture));
            List<uint> z = new List<uint>(0);
            Console.WriteLine("[uint_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<uint> ea = new List<uint>(new uint[0]);
            Console.WriteLine("[uint_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<uint> el = new List<uint>(new List<uint>());
            Console.WriteLine("[uint_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<uint> n = new List<uint>(-1); Console.WriteLine("[uint_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[uint_neg]=aoore"); }
            try { List<uint> n = new List<uint>((IEnumerable<uint>)null); Console.WriteLine("[uint_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[uint_null]=ane"); }
        }

        private static void DecimalShape()
        {
            List<decimal> a = new List<decimal>(5);
            Console.WriteLine("[dec_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(1.25m); a.Add(-3.5m);
            Console.WriteLine("[dec_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + M(a[0]) + "/" + M(a[1]));
            List<decimal> b = new List<decimal>(new decimal[] { 1.25m, -3.5m });
            Console.WriteLine("[dec_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + M(b[0]) + "/" + M(b[1]));
            List<decimal> c = new List<decimal>(b);
            Console.WriteLine("[dec_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + M(c[1]));
            List<decimal> z = new List<decimal>(0);
            Console.WriteLine("[dec_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<decimal> ea = new List<decimal>(new decimal[0]);
            Console.WriteLine("[dec_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<decimal> el = new List<decimal>(new List<decimal>());
            Console.WriteLine("[dec_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<decimal> n = new List<decimal>(-1); Console.WriteLine("[dec_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[dec_neg]=aoore"); }
            try { List<decimal> n = new List<decimal>((IEnumerable<decimal>)null); Console.WriteLine("[dec_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[dec_null]=ane"); }
        }

        private static void DateTimeShape()
        {
            DateTime d0 = new DateTime(2020, 1, 2, 3, 4, 5);
            DateTime d1 = new DateTime(1999, 12, 31);
            List<DateTime> a = new List<DateTime>(5);
            Console.WriteLine("[dt_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(d0); a.Add(d1);
            Console.WriteLine("[dt_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + L(a[0].Ticks) + "/" + L(a[1].Ticks));
            List<DateTime> b = new List<DateTime>(new DateTime[] { d0, d1 });
            Console.WriteLine("[dt_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + L(b[0].Ticks) + "/" + L(b[1].Ticks));
            List<DateTime> c = new List<DateTime>(b);
            Console.WriteLine("[dt_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + L(c[1].Ticks));
            List<DateTime> z = new List<DateTime>(0);
            Console.WriteLine("[dt_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<DateTime> ea = new List<DateTime>(new DateTime[0]);
            Console.WriteLine("[dt_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<DateTime> el = new List<DateTime>(new List<DateTime>());
            Console.WriteLine("[dt_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<DateTime> n = new List<DateTime>(-1); Console.WriteLine("[dt_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[dt_neg]=aoore"); }
            try { List<DateTime> n = new List<DateTime>((IEnumerable<DateTime>)null); Console.WriteLine("[dt_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[dt_null]=ane"); }
        }

        private static void TimeSpanShape()
        {
            TimeSpan t0 = TimeSpan.FromMinutes(90);
            TimeSpan t1 = TimeSpan.FromSeconds(-5);
            List<TimeSpan> a = new List<TimeSpan>(5);
            Console.WriteLine("[ts_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(t0); a.Add(t1);
            Console.WriteLine("[ts_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + L(a[0].Ticks) + "/" + L(a[1].Ticks));
            List<TimeSpan> b = new List<TimeSpan>(new TimeSpan[] { t0, t1 });
            Console.WriteLine("[ts_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + L(b[0].Ticks) + "/" + L(b[1].Ticks));
            List<TimeSpan> c = new List<TimeSpan>(b);
            Console.WriteLine("[ts_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + L(c[1].Ticks));
            List<TimeSpan> z = new List<TimeSpan>(0);
            Console.WriteLine("[ts_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<TimeSpan> ea = new List<TimeSpan>(new TimeSpan[0]);
            Console.WriteLine("[ts_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<TimeSpan> el = new List<TimeSpan>(new List<TimeSpan>());
            Console.WriteLine("[ts_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<TimeSpan> n = new List<TimeSpan>(-1); Console.WriteLine("[ts_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[ts_neg]=aoore"); }
            try { List<TimeSpan> n = new List<TimeSpan>((IEnumerable<TimeSpan>)null); Console.WriteLine("[ts_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[ts_null]=ane"); }
        }

        private static void GuidShape()
        {
            Guid g0 = new Guid(0x00112233, 0x4455, 0x6677, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff);
            Guid g1 = new Guid(0x00000000, 0x0000, 0x0000, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01);
            List<Guid> a = new List<Guid>(5);
            Console.WriteLine("[guid_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(g0); a.Add(g1);
            Console.WriteLine("[guid_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + a[0].ToString() + "/" + a[1].ToString());
            List<Guid> b = new List<Guid>(new Guid[] { g0, g1 });
            Console.WriteLine("[guid_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + b[0].ToString() + "/" + b[1].ToString());
            List<Guid> c = new List<Guid>(b);
            Console.WriteLine("[guid_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + c[1].ToString());
            List<Guid> z = new List<Guid>(0);
            Console.WriteLine("[guid_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<Guid> ea = new List<Guid>(new Guid[0]);
            Console.WriteLine("[guid_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<Guid> el = new List<Guid>(new List<Guid>());
            Console.WriteLine("[guid_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<Guid> n = new List<Guid>(-1); Console.WriteLine("[guid_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[guid_neg]=aoore"); }
            try { List<Guid> n = new List<Guid>((IEnumerable<Guid>)null); Console.WriteLine("[guid_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[guid_null]=ane"); }
        }

        private static void DateTimeOffsetShape()
        {
            DateTimeOffset o0 = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
            DateTimeOffset o1 = new DateTimeOffset(new DateTime(2000, 2, 29, 12, 0, 0), new TimeSpan(8, 0, 0));
            List<DateTimeOffset> a = new List<DateTimeOffset>(5);
            Console.WriteLine("[dto_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(o0); a.Add(o1);
            Console.WriteLine("[dto_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + L(a[0].Ticks) + "/" + L(a[1].Ticks) + "/" + L(a[1].Offset.Ticks));
            List<DateTimeOffset> b = new List<DateTimeOffset>(new DateTimeOffset[] { o0, o1 });
            Console.WriteLine("[dto_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + L(b[0].Ticks) + "/" + L(b[1].Ticks));
            List<DateTimeOffset> c = new List<DateTimeOffset>(b);
            Console.WriteLine("[dto_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + L(c[1].Ticks));
            List<DateTimeOffset> z = new List<DateTimeOffset>(0);
            Console.WriteLine("[dto_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<DateTimeOffset> ea = new List<DateTimeOffset>(new DateTimeOffset[0]);
            Console.WriteLine("[dto_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<DateTimeOffset> el = new List<DateTimeOffset>(new List<DateTimeOffset>());
            Console.WriteLine("[dto_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<DateTimeOffset> n = new List<DateTimeOffset>(-1); Console.WriteLine("[dto_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[dto_neg]=aoore"); }
            try { List<DateTimeOffset> n = new List<DateTimeOffset>((IEnumerable<DateTimeOffset>)null); Console.WriteLine("[dto_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[dto_null]=ane"); }
        }

        private static void TupleShape()
        {
            List<ValueTuple<int, int>> a = new List<ValueTuple<int, int>>(5);
            Console.WriteLine("[tup_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(new ValueTuple<int, int>(1, 2));
            a.Add(new ValueTuple<int, int>(3, 4));
            Console.WriteLine("[tup_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + I(a[0].Item1) + "/" + I(a[1].Item2));
            List<ValueTuple<int, int>> b = new List<ValueTuple<int, int>>(
                new ValueTuple<int, int>[] { new ValueTuple<int, int>(5, 6), new ValueTuple<int, int>(7, 8) });
            Console.WriteLine("[tup_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + I(b[0].Item1) + "/" + I(b[1].Item2));
            List<ValueTuple<int, int>> c = new List<ValueTuple<int, int>>(b);
            Console.WriteLine("[tup_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + I(c[1].Item1));
            List<ValueTuple<int, int>> z = new List<ValueTuple<int, int>>(0);
            Console.WriteLine("[tup_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<ValueTuple<int, int>> ea = new List<ValueTuple<int, int>>(new ValueTuple<int, int>[0]);
            Console.WriteLine("[tup_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<ValueTuple<int, int>> el = new List<ValueTuple<int, int>>(new List<ValueTuple<int, int>>());
            Console.WriteLine("[tup_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<ValueTuple<int, int>> n = new List<ValueTuple<int, int>>(-1); Console.WriteLine("[tup_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[tup_neg]=aoore"); }
            try { List<ValueTuple<int, int>> n = new List<ValueTuple<int, int>>((IEnumerable<ValueTuple<int, int>>)null); Console.WriteLine("[tup_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[tup_null]=ane"); }
        }

        private static void StructShape()
        {
            List<Pt> a = new List<Pt>(5);
            Console.WriteLine("[pt_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(new Pt(1, 2)); a.Add(new Pt(3, 4));
            Console.WriteLine("[pt_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + I(a[0].X) + "/" + I(a[1].Y));
            List<Pt> b = new List<Pt>(new Pt[] { new Pt(5, 6), new Pt(7, 8) });
            Console.WriteLine("[pt_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + I(b[0].X) + "/" + I(b[1].Y));
            List<Pt> c = new List<Pt>(b);
            Console.WriteLine("[pt_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + I(c[1].X));
            List<Pt> z = new List<Pt>(0);
            Console.WriteLine("[pt_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<Pt> ea = new List<Pt>(new Pt[0]);
            Console.WriteLine("[pt_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<Pt> el = new List<Pt>(new List<Pt>());
            Console.WriteLine("[pt_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<Pt> n = new List<Pt>(-1); Console.WriteLine("[pt_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[pt_neg]=aoore"); }
            try { List<Pt> n = new List<Pt>((IEnumerable<Pt>)null); Console.WriteLine("[pt_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[pt_null]=ane"); }
        }

        private static void StringShape()
        {
            List<string> a = new List<string>(5);
            Console.WriteLine("[str_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add("aa"); a.Add("bb");
            Console.WriteLine("[str_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + a[0] + "/" + a[1]);
            List<string> b = new List<string>(new string[] { "cc", "dd" });
            Console.WriteLine("[str_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + b[0] + "/" + b[1]);
            List<string> c = new List<string>(b);
            Console.WriteLine("[str_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + c[1]);
            List<string> z = new List<string>(0);
            Console.WriteLine("[str_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<string> ea = new List<string>(new string[0]);
            Console.WriteLine("[str_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<string> el = new List<string>(new List<string>());
            Console.WriteLine("[str_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<string> n = new List<string>(-1); Console.WriteLine("[str_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[str_neg]=aoore"); }
            try { List<string> n = new List<string>((IEnumerable<string>)null); Console.WriteLine("[str_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[str_null]=ane"); }
        }

        private static void RefShape()
        {
            List<RefElem> a = new List<RefElem>(5);
            Console.WriteLine("[ref_cap5]=" + I(a.Count) + "/" + I(a.Capacity));
            a.Add(new RefElem(11)); a.Add(new RefElem(22));
            Console.WriteLine("[ref_add]=" + I(a.Count) + "/" + I(a.Capacity) + "/" + I(a[0].V) + "/" + I(a[1].V));
            List<RefElem> b = new List<RefElem>(new RefElem[] { new RefElem(33), new RefElem(44) });
            Console.WriteLine("[ref_arr]=" + I(b.Count) + "/" + I(b.Capacity) + "/" + I(b[0].V) + "/" + I(b[1].V));
            List<RefElem> c = new List<RefElem>(b);
            Console.WriteLine("[ref_lst]=" + I(c.Count) + "/" + I(c.Capacity) + "/" + I(c[1].V));
            List<RefElem> z = new List<RefElem>(0);
            Console.WriteLine("[ref_zero]=" + I(z.Count) + "/" + I(z.Capacity));
            List<RefElem> ea = new List<RefElem>(new RefElem[0]);
            Console.WriteLine("[ref_emptyarr]=" + I(ea.Count) + "/" + I(ea.Capacity));
            List<RefElem> el = new List<RefElem>(new List<RefElem>());
            Console.WriteLine("[ref_emptylst]=" + I(el.Count) + "/" + I(el.Capacity));
            try { List<RefElem> n = new List<RefElem>(-1); Console.WriteLine("[ref_neg]=no_throw/" + I(n.Count)); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("[ref_neg]=aoore"); }
            try { List<RefElem> n = new List<RefElem>((IEnumerable<RefElem>)null); Console.WriteLine("[ref_null]=no_throw"); }
            catch (ArgumentNullException) { Console.WriteLine("[ref_null]=ane"); }
        }

        public static void Main()
        {
            LongShape();
            DoubleShape();
            FloatShape();
            BoolShape();
            CharShape();
            SbyteShape();
            ByteShape();
            ShortShape();
            UshortShape();
            UintShape();
            DecimalShape();
            DateTimeShape();
            TimeSpanShape();
            GuidShape();
            DateTimeOffsetShape();
            TupleShape();
            StructShape();
            StringShape();
            RefShape();
            Console.WriteLine("done");
        }
    }
}
