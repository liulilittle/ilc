//------------------------------------------------------------------------------
// probe-linqagg2 -- RED/GREEN probe for the List<T>.Aggregate(seed, func)
// (ELEMENT family, ACCUMULATOR family) matrix.
//
// floatfix (7b0ee161) split the list seed fold by (element K, accumulator):
// K4+d (seed_d_d), K2+i8 (seed_i8_i8), K4+f (seed_f_d) and K3+f (seed_f),
// but every NON-DIAGONAL pair still bound the base entries whose element
// reader is fixed at int32 (seed_i4/seed_i8/seed_d/seed_f). MEASURED classes:
//   * List<long> with an int32 accumulator read the LOW 32 bits of each
//     8-byte cell and called the lambda through an int32 element slot;
//   * List<float> with a double accumulator paired TWO 4-byte float cells as
//     one double (and misaligned the tail);
//   * List<float> with a float accumulator read the float BITS as int32;
//   * List<double> with an int32/int64 accumulator read the low 32 bits;
//   * List<byte>/List<short> (1/2-byte cells) were read as int32;
//   * List<string> with a numeric accumulator read a POINTER's low 32 bits;
//   * List<decimal> (16-byte cells) fell into the reference path.
// Every cell of the matrix is printed; the net48 oracle is the byte reference,
// so a width/ABI misread is visible in the value (and in the float/double
// raw bit patterns).
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeLinqAgg2
    {
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

        // ---- element = int32 (4-byte cells) ----
        private static int EI4(List<int> l) { return l.Aggregate(0, (a, x) => a + x); }
        private static long EI4I8(List<int> l) { return l.Aggregate(0L, (a, x) => a + x); }
        private static float EI4F(List<int> l) { return l.Aggregate(0f, (a, x) => a + x); }
        private static double EI4D(List<int> l) { return l.Aggregate(0.0, (a, x) => a + x); }
        private static decimal EI4Dec(List<int> l) { return l.Aggregate(0m, (a, x) => a + x); }
        private static string EI4Ref(List<int> l) { return l.Aggregate("s", (a, x) => a + x.ToString(CultureInfo.InvariantCulture)); }

        // ---- element = int64 (8-byte cells) ----
        private static int EI8(List<long> l) { return l.Aggregate(1000, (a, x) => a + (int)(x % 1000)); }
        private static long EI8I8(List<long> l) { return l.Aggregate(0L, (a, x) => a + x); }
        private static float EI8F(List<long> l) { return l.Aggregate(0f, (a, x) => a + x); }
        private static double EI8D(List<long> l) { return l.Aggregate(0.0, (a, x) => a + x); }
        private static decimal EI8Dec(List<long> l) { return l.Aggregate(0m, (a, x) => a + x); }
        private static string EI8Ref(List<long> l) { return l.Aggregate("s", (a, x) => a + x.ToString(CultureInfo.InvariantCulture)); }

        // ---- element = float (4-byte cells) ----
        private static int EF(List<float> l) { return l.Aggregate(0, (a, x) => a + (int)x); }
        private static long EFI8(List<float> l) { return l.Aggregate(0L, (a, x) => a + (long)x); }
        private static float EFF(List<float> l) { return l.Aggregate(0f, (a, x) => a + x); }
        private static double EFD(List<float> l) { return l.Aggregate(0.5, (a, x) => a + x); }
        private static decimal EFDec(List<float> l) { return l.Aggregate(0m, (a, x) => a + (decimal)(int)x); }
        private static string EFRef(List<float> l) { return l.Aggregate("s", (a, x) => a + ((int)x).ToString(CultureInfo.InvariantCulture)); }

        // ---- element = double (8-byte cells) ----
        private static int ED(List<double> l) { return l.Aggregate(0, (a, x) => a + (int)x); }
        private static long EDI8(List<double> l) { return l.Aggregate(0L, (a, x) => a + (long)x); }
        private static float EDF(List<double> l) { return l.Aggregate(0f, (a, x) => a + (float)x); }
        private static double EDD(List<double> l) { return l.Aggregate(0.0, (a, x) => a + x); }
        private static decimal EDDec(List<double> l) { return l.Aggregate(0m, (a, x) => a + (decimal)(int)x); }
        private static string EDRef(List<double> l) { return l.Aggregate("s", (a, x) => a + ((int)x).ToString(CultureInfo.InvariantCulture)); }

        // ---- element = byte (1-byte cells) ----
        private static int EBy(List<byte> l) { return l.Aggregate(0, (a, x) => a + x); }
        private static long EByI8(List<byte> l) { return l.Aggregate(0L, (a, x) => a + x); }
        private static float EByF(List<byte> l) { return l.Aggregate(0f, (a, x) => a + x); }
        private static double EByD(List<byte> l) { return l.Aggregate(0.0, (a, x) => a + x); }
        private static string EByRef(List<byte> l) { return l.Aggregate("s", (a, x) => a + x.ToString(CultureInfo.InvariantCulture)); }

        // ---- element = short (2-byte cells) ----
        private static int ESh(List<short> l) { return l.Aggregate(0, (a, x) => a + x); }
        private static long EShI8(List<short> l) { return l.Aggregate(0L, (a, x) => a + x); }
        private static float EShF(List<short> l) { return l.Aggregate(0f, (a, x) => a + x); }
        private static double EShD(List<short> l) { return l.Aggregate(0.0, (a, x) => a + x); }

        // ---- element = reference ----
        private static int EStr(List<string> l) { return l.Aggregate(0, (a, x) => a + x.Length); }
        private static long EStrI8(List<string> l) { return l.Aggregate(0L, (a, x) => a + (long)x.Length); }
        private static float EStrF(List<string> l) { return l.Aggregate(0f, (a, x) => a + x.Length); }
        private static double EStrD(List<string> l) { return l.Aggregate(0.0, (a, x) => a + x.Length); }
        private static string EStrRef(List<string> l) { return l.Aggregate("s", (a, x) => a + x); }

        // ---- element = decimal (16-byte cells) ----
        // NOT probed: a List<decimal> receiver is not tracked as a List (the
        // stack-kind vocabulary has no decimal tag), so the call falls into the
        // ARRAY aggregate arm with a List object cast to Il2CppArray*. That
        // pre-existing gap is registered in docs/audit/b5-gaps.md.

        // ---- empty sources return the seed unchanged ----
        private static int EmptyI8I4(List<long> l) { return l.Aggregate(42, (a, x) => a + (int)x); }
        private static double EmptyFD(List<float> l) { return l.Aggregate(1.5, (a, x) => a + x); }
        private static string EmptyStrRef(List<string> l) { return l.Aggregate("seed", (a, x) => a + x); }

        public static void Main()
        {
            List<int> li = new List<int> { 1, 2, 3, 4 };
            List<long> ll = new List<long> { 4294967298L, 3000000000L, -7L };
            List<float> lf = new List<float> { 0.1f, 0.1f, 0.1f, 16777216.0f, 1.0f };
            List<double> ld = new List<double> { 0.1, 0.1, 16777216.0, 1.0 };
            List<byte> lb = new List<byte> { 200, 100, 55 };
            List<short> ls = new List<short> { -30000, 5, 1234 };
            List<string> lstr = new List<string> { "a", "bb", "ccc" };

            // element int32 x accumulator {i4,i8,f,d,dec,ref}
            Console.WriteLine("[i4_i4]=" + EI4(li).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i4_i8]=" + EI4I8(li).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i4_f]=" + FB(EI4F(li)));
            Console.WriteLine("[i4_d]=" + DB(EI4D(li)));
            Console.WriteLine("[i4_dec]=" + EI4Dec(li).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i4_ref]=" + S(EI4Ref(li)));

            // element int64 x accumulator {i4,i8,f,d,dec,ref}
            Console.WriteLine("[i8_i4]=" + EI8(ll).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i8_i8]=" + EI8I8(ll).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i8_f]=" + FB(EI8F(ll)));
            Console.WriteLine("[i8_d]=" + DB(EI8D(ll)));
            Console.WriteLine("[i8_dec]=" + EI8Dec(ll).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i8_ref]=" + S(EI8Ref(ll)));

            // element float x accumulator {i4,i8,f,d,dec,ref}
            Console.WriteLine("[f_i4]=" + EF(lf).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[f_i8]=" + EFI8(lf).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[f_f]=" + FB(EFF(lf)));
            Console.WriteLine("[f_d]=" + DB(EFD(lf)));
            Console.WriteLine("[f_dec]=" + EFDec(lf).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[f_ref]=" + S(EFRef(lf)));

            // element double x accumulator {i4,i8,f,d,dec,ref}
            Console.WriteLine("[d_i4]=" + ED(ld).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[d_i8]=" + EDI8(ld).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[d_f]=" + FB(EDF(ld)));
            Console.WriteLine("[d_d]=" + DB(EDD(ld)));
            Console.WriteLine("[d_dec]=" + EDDec(ld).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[d_ref]=" + S(EDRef(ld)));

            // element byte (1-byte cells) x accumulator {i4,i8,f,d,ref}
            Console.WriteLine("[by_i4]=" + EBy(lb).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[by_i8]=" + EByI8(lb).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[by_f]=" + FB(EByF(lb)));
            Console.WriteLine("[by_d]=" + DB(EByD(lb)));
            Console.WriteLine("[by_ref]=" + S(EByRef(lb)));

            // element short (2-byte cells) x accumulator {i4,i8,f,d}
            Console.WriteLine("[sh_i4]=" + ESh(ls).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[sh_i8]=" + EShI8(ls).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[sh_f]=" + FB(EShF(ls)));
            Console.WriteLine("[sh_d]=" + DB(EShD(ls)));

            // element reference x accumulator {i4,i8,f,d,ref}
            Console.WriteLine("[str_i4]=" + EStr(lstr).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[str_i8]=" + EStrI8(lstr).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[str_f]=" + FB(EStrF(lstr)));
            Console.WriteLine("[str_d]=" + DB(EStrD(lstr)));
            Console.WriteLine("[str_ref]=" + S(EStrRef(lstr)));

            // element decimal (16-byte cells): only reachable as a CHAINED
            // ToList() receiver (a List<decimal> LOCAL is not tracked as a list
            // -- see the note above and docs/audit/b5-gaps.md).
            Console.WriteLine("[dec_chain_dec]=" + new decimal[] { 1.5m, 2.25m, -0.75m }.ToList().Aggregate(0m, (a, x) => a + x).ToString(CultureInfo.InvariantCulture));

            // empty sources: the seed comes back unchanged
            Console.WriteLine("[empty_i8_i4]=" + EmptyI8I4(new List<long>()).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[empty_f_d]=" + DB(EmptyFD(new List<float>())));
            Console.WriteLine("[empty_str_ref]=" + S(EmptyStrRef(new List<string>())));

            Console.WriteLine("done");
        }
    }
}
