using System;

//------------------------------------------------------------------------------
// conv-str-parse -- Convert.ToXxx(string) THROWING parse surface.
//
// net48 Convert applies the "null means zero" rule FIRST (null -> 0 / '\0'),
// then the byte/sbyte/short/ushort/uint/ulong/single Parse grammar: ASCII
// whitespace trim, optional sign, decimal digits; malformed text throws
// FormatException and an out-of-range magnitude throws OverflowException.
// This pins the whole surface end to end against the net48 oracle.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class ConvStrParse
    {
        public static void Main()
        {
            // ===== ToChar(string) =====
            Console.WriteLine("charA=" + ((int)Convert.ToChar("A")));
            Console.WriteLine("charnull=" + (Probe(() => (int)Convert.ToChar(null))));
            try { Convert.ToChar(""); Console.WriteLine("charempty=NO"); }
            catch (Exception e) { Console.WriteLine("charempty=" + e.GetType().Name); }
            try { Convert.ToChar("AB"); Console.WriteLine("charAB=NO"); }
            catch (Exception e) { Console.WriteLine("charAB=" + e.GetType().Name); }

            // ===== ToByte(string) =====
            Console.WriteLine("byte42=" + Convert.ToByte("42"));
            Console.WriteLine("byteNull=" + Convert.ToByte(null));
            try { Convert.ToByte(""); Console.WriteLine("byteempty=NO"); }
            catch (Exception e) { Console.WriteLine("byteempty=" + e.GetType().Name); }
            try { Convert.ToByte("300"); Console.WriteLine("byte300=NO"); }
            catch (Exception e) { Console.WriteLine("byte300=" + e.GetType().Name); }
            try { Convert.ToByte("-1"); Console.WriteLine("bytem1=NO"); }
            catch (Exception e) { Console.WriteLine("bytem1=" + e.GetType().Name); }
            try { Convert.ToByte("x"); Console.WriteLine("bytex=NO"); }
            catch (Exception e) { Console.WriteLine("bytex=" + e.GetType().Name); }

            // ===== ToSByte(string) =====
            Console.WriteLine("sbyte42=" + Convert.ToSByte("42"));
            Console.WriteLine("sbyteNull=" + Convert.ToSByte(null));
            Console.WriteLine("sbyte128=" + (Probe(() => Convert.ToSByte("128"))));
            Console.WriteLine("sbytem128=" + (Probe(() => Convert.ToSByte("-128"))));
            Console.WriteLine("sbytem129=" + (Probe(() => Convert.ToSByte("-129"))));

            // ===== ToInt16(string) =====
            Console.WriteLine("i16=" + Convert.ToInt16("1234"));
            Console.WriteLine("i16Null=" + Convert.ToInt16(null));
            Console.WriteLine("i16m=" + (Probe(() => Convert.ToInt16("-32768"))));
            Console.WriteLine("i16p=" + (Probe(() => Convert.ToInt16("32768"))));

            // ===== ToUInt16(string) =====
            Console.WriteLine("u16=" + Convert.ToUInt16("1234"));
            Console.WriteLine("u16Null=" + Convert.ToUInt16(null));
            Console.WriteLine("u16p=" + (Probe(() => Convert.ToUInt16("65536"))));
            Console.WriteLine("u16m=" + (Probe(() => Convert.ToUInt16("-1"))));

            // ===== ToUInt32(string) =====
            Console.WriteLine("u32=" + Convert.ToUInt32("123456"));
            Console.WriteLine("u32Null=" + Convert.ToUInt32(null));
            Console.WriteLine("u32p=" + (Probe(() => Convert.ToUInt32("4294967296"))));
            Console.WriteLine("u32m=" + (Probe(() => Convert.ToUInt32("-1"))));

            // ===== ToUInt64(string) =====
            Console.WriteLine("u64=" + Convert.ToUInt64("1234567890123"));
            Console.WriteLine("u64Null=" + Convert.ToUInt64(null));
            Console.WriteLine("u64max=" + Convert.ToUInt64("18446744073709551615"));
            Console.WriteLine("u64p=" + (Probe(() => Convert.ToUInt64("18446744073709551616"))));
            Console.WriteLine("u64m=" + (Probe(() => Convert.ToUInt64("-1"))));

            // ===== ToSingle(string) =====
            Console.WriteLine("f=" + Convert.ToSingle("1.5"));
            Console.WriteLine("fNull=" + Convert.ToSingle(null));
            Console.WriteLine("fovf=" + (Probe(() => Convert.ToSingle("1e39"))));
            Console.WriteLine("fbad=" + (Probe(() => Convert.ToSingle("x"))));
        }

        private static string Probe(Func<object> f)
        {
            try { return "OK:" + f(); }
            catch (Exception e) { return e.GetType().Name; }
        }
    }
}
