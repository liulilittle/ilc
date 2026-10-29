//------------------------------------------------------------------------------
// probe-arrayagg -- RED/GREEN probe for the ARRAY-domain
// Aggregate(seed, func) (ELEMENT family, ACCUMULATOR family) matrix, plus the
// List<decimal> LOCAL receiver shapes (b5-gaps section 17 items 2 and 6).
//
// The ARRAY Aggregate arm only shipped diagonal/one-family kernels
// (seed_i4/seed_i8/seed_d/seed_f read int32 cells; seed_i8_i8 / seed_d_d read
// their own width). Every NON-DIAGONAL (element, accumulator) pair therefore
// read the wrong number of bytes out of the cell and/or called the lambda
// through a mismatched fn-pointer:
//   * long[]   + int accumulator read the low 32 bits of each 8-byte cell;
//   * double[] + int/long accumulator read the low 32 bits;
//   * float[]  + int/long accumulator read the float BITS as int32;
//   * float[]  + double accumulator paired two 4-byte float cells as one
//                double (and misaligned the tail);
//   * byte[]/short[] (1/2-byte cells) were read as int32;
//   * string[] + a numeric accumulator read a POINTER's low 32 bits.
// Every cell of the matrix is printed; the net48 oracle is the byte reference,
// so a width/ABI misread is visible in the value (and in the float/double raw
// bit patterns).
//
// The List<decimal> LOCAL section covers a decimal List that is NOT the
// immediate ToList() result: a stored local, a captured local (closure display
// class field), a static field and a method parameter. A `new List<decimal>()`
// receiver is NOT probed -- its allocation entry is a separate CodeGenerator
// surface (outside this seat).
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeArrayAgg
    {
        private static List<decimal> s_decField =
            new decimal[] { 4.5m, -1.25m }.ToList();

        private static string FB(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture) + "/" +
                   BitConverter.ToUInt32(BitConverter.GetBytes(v), 0).ToString(CultureInfo.InvariantCulture);
        }

        private static string DB(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture) + "/" +
                   BitConverter.DoubleToInt64Bits(v).ToString(CultureInfo.InvariantCulture);
        }

        private static string S(string v) { return v ?? "<null>"; }

        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        // ---- element = int32 (4-byte cells) ----
        private static int EI4(int[] a) { return a.Aggregate(0, (x, y) => x + y); }
        private static long EI4I8(int[] a) { return a.Aggregate(0L, (x, y) => x + y); }
        private static float EI4F(int[] a) { return a.Aggregate(0f, (x, y) => x + y); }
        private static double EI4D(int[] a) { return a.Aggregate(0.0, (x, y) => x + y); }
        private static decimal EI4Dec(int[] a) { return a.Aggregate(0m, (x, y) => x + y); }
        private static string EI4Ref(int[] a) { return a.Aggregate("s", (x, y) => x + y.ToString(CultureInfo.InvariantCulture)); }

        // ---- element = int64 (8-byte cells) ----
        private static int EI8(long[] a) { return a.Aggregate(1000, (x, y) => x + (int)(y % 1000)); }
        private static long EI8I8(long[] a) { return a.Aggregate(0L, (x, y) => x + y); }
        private static float EI8F(long[] a) { return a.Aggregate(0f, (x, y) => x + y); }
        private static double EI8D(long[] a) { return a.Aggregate(0.0, (x, y) => x + y); }
        private static decimal EI8Dec(long[] a) { return a.Aggregate(0m, (x, y) => x + y); }
        private static string EI8Ref(long[] a) { return a.Aggregate("s", (x, y) => x + y.ToString(CultureInfo.InvariantCulture)); }

        // ---- element = float (4-byte cells) ----
        private static int EF(float[] a) { return a.Aggregate(0, (x, y) => x + (int)y); }
        private static long EFI8(float[] a) { return a.Aggregate(0L, (x, y) => x + (long)y); }
        private static float EFF(float[] a) { return a.Aggregate(0f, (x, y) => x + y); }
        private static double EFD(float[] a) { return a.Aggregate(0.5, (x, y) => x + y); }
        private static decimal EFDec(float[] a) { return a.Aggregate(0m, (x, y) => x + (decimal)(int)y); }
        private static string EFRef(float[] a) { return a.Aggregate("s", (x, y) => x + ((int)y).ToString(CultureInfo.InvariantCulture)); }

        // ---- element = double (8-byte cells) ----
        private static int ED(double[] a) { return a.Aggregate(0, (x, y) => x + (int)y); }
        private static long EDI8(double[] a) { return a.Aggregate(0L, (x, y) => x + (long)y); }
        private static float EDF(double[] a) { return a.Aggregate(0f, (x, y) => x + (float)y); }
        private static double EDD(double[] a) { return a.Aggregate(0.0, (x, y) => x + y); }
        private static decimal EDDec(double[] a) { return a.Aggregate(0m, (x, y) => x + (decimal)(int)y); }
        private static string EDRef(double[] a) { return a.Aggregate("s", (x, y) => x + ((int)y).ToString(CultureInfo.InvariantCulture)); }

        // ---- element = byte (1-byte cells) ----
        private static int EBy(byte[] a) { return a.Aggregate(0, (x, y) => x + y); }
        private static long EByI8(byte[] a) { return a.Aggregate(0L, (x, y) => x + y); }
        private static float EByF(byte[] a) { return a.Aggregate(0f, (x, y) => x + y); }
        private static double EByD(byte[] a) { return a.Aggregate(0.0, (x, y) => x + y); }
        private static string EByRef(byte[] a) { return a.Aggregate("s", (x, y) => x + y.ToString(CultureInfo.InvariantCulture)); }

        // ---- element = short (2-byte cells) ----
        private static int ESh(short[] a) { return a.Aggregate(0, (x, y) => x + y); }
        private static long EShI8(short[] a) { return a.Aggregate(0L, (x, y) => x + y); }
        private static float EShF(short[] a) { return a.Aggregate(0f, (x, y) => x + y); }
        private static double EShD(short[] a) { return a.Aggregate(0.0, (x, y) => x + y); }

        // ---- element = reference ----
        private static int EStr(string[] a) { return a.Aggregate(0, (x, y) => x + y.Length); }
        private static long EStrI8(string[] a) { return a.Aggregate(0L, (x, y) => x + (long)y.Length); }
        private static float EStrF(string[] a) { return a.Aggregate(0f, (x, y) => x + y.Length); }
        private static double EStrD(string[] a) { return a.Aggregate(0.0, (x, y) => x + y.Length); }
        private static string EStrRef(string[] a) { return a.Aggregate("s", (x, y) => x + y); }

        // ---- element = decimal (16-byte cells) ----
        private static decimal EDecDec(decimal[] a) { return a.Aggregate(0m, (x, y) => x + y); }

        // ---- empty sources return the seed unchanged ----
        private static int EmptyI8I4(long[] a) { return a.Aggregate(42, (x, y) => x + (int)y); }
        private static double EmptyFD(float[] a) { return a.Aggregate(1.5, (x, y) => x + y); }
        private static string EmptyStrRef(string[] a) { return a.Aggregate("seed", (x, y) => x + y); }
        private static long EmptyDI8(double[] a) { return a.Aggregate(7L, (x, y) => x + (long)y); }

        // ---- List<decimal> LOCAL shapes (not the immediate ToList result) ----
        private static decimal ListParam(List<decimal> l)
        {
            return l.Aggregate(0m, (x, y) => x + y);
        }

        public static void Main()
        {
            int[] ai = { 1, 2, 3, 4 };
            long[] al = { 4294967298L, 3000000000L, -7L };
            float[] af = { 0.1f, 0.1f, 0.1f, 16777216.0f, 1.0f };
            double[] ad = { 0.1, 0.1, 16777216.0, 1.0 };
            byte[] aby = { 200, 100, 55 };
            short[] ash = { -30000, 5, 1234 };
            string[] astr = { "a", "bb", "ccc" };
            decimal[] adec = { 1.5m, 2.25m, -0.75m };

            // element int32 x accumulator {i4,i8,f,d,dec,ref}
            Console.WriteLine("[i4_i4]=" + EI4(ai).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i4_i8]=" + EI4I8(ai).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i4_f]=" + FB(EI4F(ai)));
            Console.WriteLine("[i4_d]=" + DB(EI4D(ai)));
            Console.WriteLine("[i4_dec]=" + D(EI4Dec(ai)));
            Console.WriteLine("[i4_ref]=" + S(EI4Ref(ai)));

            // element int64 x accumulator {i4,i8,f,d,dec,ref}
            Console.WriteLine("[i8_i4]=" + EI8(al).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i8_i8]=" + EI8I8(al).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i8_f]=" + FB(EI8F(al)));
            Console.WriteLine("[i8_d]=" + DB(EI8D(al)));
            Console.WriteLine("[i8_dec]=" + D(EI8Dec(al)));
            Console.WriteLine("[i8_ref]=" + S(EI8Ref(al)));

            // element float x accumulator {i4,i8,f,d,dec,ref}
            Console.WriteLine("[f_i4]=" + EF(af).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[f_i8]=" + EFI8(af).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[f_f]=" + FB(EFF(af)));
            Console.WriteLine("[f_d]=" + DB(EFD(af)));
            Console.WriteLine("[f_dec]=" + D(EFDec(af)));
            Console.WriteLine("[f_ref]=" + S(EFRef(af)));

            // element double x accumulator {i4,i8,f,d,dec,ref}
            Console.WriteLine("[d_i4]=" + ED(ad).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[d_i8]=" + EDI8(ad).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[d_f]=" + FB(EDF(ad)));
            Console.WriteLine("[d_d]=" + DB(EDD(ad)));
            Console.WriteLine("[d_dec]=" + D(EDDec(ad)));
            Console.WriteLine("[d_ref]=" + S(EDRef(ad)));

            // element byte (1-byte cells) x accumulator {i4,i8,f,d,ref}
            Console.WriteLine("[by_i4]=" + EBy(aby).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[by_i8]=" + EByI8(aby).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[by_f]=" + FB(EByF(aby)));
            Console.WriteLine("[by_d]=" + DB(EByD(aby)));
            Console.WriteLine("[by_ref]=" + S(EByRef(aby)));

            // element short (2-byte cells) x accumulator {i4,i8,f,d}
            Console.WriteLine("[sh_i4]=" + ESh(ash).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[sh_i8]=" + EShI8(ash).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[sh_f]=" + FB(EShF(ash)));
            Console.WriteLine("[sh_d]=" + DB(EShD(ash)));

            // element reference x accumulator {i4,i8,f,d,ref}
            Console.WriteLine("[str_i4]=" + EStr(astr).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[str_i8]=" + EStrI8(astr).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[str_f]=" + FB(EStrF(astr)));
            Console.WriteLine("[str_d]=" + DB(EStrD(astr)));
            Console.WriteLine("[str_ref]=" + S(EStrRef(astr)));

            // element decimal (16-byte cells) x decimal accumulator
            Console.WriteLine("[dec_dec]=" + D(EDecDec(adec)));

            // empty sources: the seed comes back unchanged
            Console.WriteLine("[empty_i8_i4]=" + EmptyI8I4(new long[0]).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[empty_f_d]=" + DB(EmptyFD(new float[0])));
            Console.WriteLine("[empty_str_ref]=" + S(EmptyStrRef(new string[0])));
            Console.WriteLine("[empty_d_i8]=" + EmptyDI8(new double[0]).ToString(CultureInfo.InvariantCulture));

            // List<decimal> LOCAL: stored ToList result
            List<decimal> ldec = adec.ToList();
            Console.WriteLine("[list_local_agg]=" + D(ldec.Aggregate(0m, (x, y) => x + y)));

            // List<decimal> LOCAL: static field
            List<decimal> lfld = s_decField;
            Console.WriteLine("[list_field_agg]=" + D(lfld.Aggregate(0m, (x, y) => x + y)));

            // List<decimal> LOCAL: method parameter
            Console.WriteLine("[list_param_agg]=" + D(ListParam(ldec)));

            // List<decimal> LOCAL: captured by a lambda (display-class field)
            Func<decimal> cap = () => ldec.Aggregate(0m, (x, y) => x + y);
            Console.WriteLine("[list_capture_agg]=" + D(cap()));

            Console.WriteLine("done");
        }
    }
}
