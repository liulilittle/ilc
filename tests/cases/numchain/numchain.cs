using System;

// -----------------------------------------------------------------------------
// numchain.cs -- numeric chain deep: int/long/double mixed arithmetic +
// ToString(D/N/X/E/F/G) + Parse/TryParse + Convert + Math + rounding, all
// bool (== known value/string), aligned to the net48 reference run.
//
// Each probe prints the VALUE (diagnostic line) followed by a BOOL
// comparison against a fixed known literal (culture-proof assertion line),
// the fmtchain convention. All formats (D/N/X/E/F/G) render identically in
// zh-CN and invariant cultures.
//
// Wired surface exercised (MethodMap):
//   int32/long/double: ToString D/N/X/E/G  (MethodMap.Format.cs)
//   double:            ToString F          (MethodMap.Format.cs)
//   Int32.Parse/TryParse, Int64.TryParse, Double.Parse/TryParse
//                     (MethodMap.ValueType.cs)
//   Convert.ToInt32/ToInt64/ToDouble/ToString/ToBoolean/ToChar + BitConverter
//                     (MethodMap.Convert.cs)
//   Math Abs/Max/Min/Sign (int/long/double), Sqrt/Pow/Floor/Ceiling/Round/
//   Truncate (double)    (MethodMap.Math.cs)
// DELIBERATELY EXCLUDED (unwired -- the transpiler would LOUD-reject):
//   Int64.Parse                (MethodMap.ValueType.cs:57)
//   Convert.ToInt32(string, base) (MethodMap.Convert.cs:37)
//   Math.DivRem / BigMul       (MethodMap.MathExtra.cs)
//   Math.Round(double, MidpointRounding) (MethodMap.Math.cs:53)
//   ToString(IFormatProvider)  (MethodMap.Format.cs:26)
// -----------------------------------------------------------------------------
class Program
{
    static void Main()
    {
        // ---- mixed int/long/double arithmetic chain ----
        int a = 7;
        int b = 3;
        long la = 123456789012345L;
        double da = 7.5;
        double db = 0.5;
        double d1 = 0.1;
        double d2 = 0.2;
        Console.WriteLine(a + b);
        Console.WriteLine(a + b == 10);
        Console.WriteLine(a - b);
        Console.WriteLine(a - b == 4);
        Console.WriteLine(a * b);
        Console.WriteLine(a * b == 21);
        Console.WriteLine(a / b);
        Console.WriteLine(a / b == 3);
        Console.WriteLine(a % b);
        Console.WriteLine(a % b == 1);
        Console.WriteLine(-a);
        Console.WriteLine(-a == -7);
        Console.WriteLine(a + la);
        Console.WriteLine(a + la == 123456789012352L);
        Console.WriteLine(la * 3);
        Console.WriteLine(la * 3 == 370367367037035L);
        Console.WriteLine(la / 7);
        Console.WriteLine(la / 7 == 17636684144620L);
        Console.WriteLine(la % 10);
        Console.WriteLine(la % 10 == 5);
        Console.WriteLine(da * 2);
        Console.WriteLine(da * 2 == 15.0);
        Console.WriteLine(da / b);
        Console.WriteLine(da / b == 2.5);
        Console.WriteLine(7.0 / 2.0);
        Console.WriteLine(7.0 / 2.0 == 3.5);
        Console.WriteLine(10.0 % 4.0);
        Console.WriteLine(10.0 % 4.0 == 2.0);
        Console.WriteLine(d1 + d2);
        Console.WriteLine(d1 + d2 == 0.30000000000000004);
        Console.WriteLine(d1 + d2 == 0.3);
        Console.WriteLine(-7 / 2);
        Console.WriteLine(-7 / 2 == -3);
        Console.WriteLine(-7.0 / 2.0);
        Console.WriteLine(-7.0 / 2.0 == -3.5);
        Console.WriteLine(a * db);
        Console.WriteLine(a * db == 3.5);
        Console.WriteLine(a + da);
        Console.WriteLine(a + da == 14.5);
        Console.WriteLine(a * 0.1);
        Console.WriteLine(a * 0.1 == 0.7000000000000001);

        // ---- casts (truncation toward zero) ----
        Console.WriteLine((int)3.9);
        Console.WriteLine((int)3.9 == 3);
        Console.WriteLine((int)(-3.9));
        Console.WriteLine((int)(-3.9) == -3);
        Console.WriteLine((int)7.5);
        Console.WriteLine((int)7.5 == 7);
        Console.WriteLine((int)(-7.5));
        Console.WriteLine((int)(-7.5) == -7);
        Console.WriteLine((long)2.7);
        Console.WriteLine((long)2.7 == 2);
        Console.WriteLine((double)a);
        Console.WriteLine((double)a == 7.0);

        // ---- ToString: D chain (int32/int64) ----
        int p = 1234567;
        Console.WriteLine(p.ToString("D"));
        Console.WriteLine(p.ToString("D") == "1234567");
        Console.WriteLine((255).ToString("D8"));
        Console.WriteLine((255).ToString("D8") == "00000255");
        Console.WriteLine((-255).ToString("D"));
        Console.WriteLine((-255).ToString("D") == "-255");
        Console.WriteLine((0).ToString("D"));
        Console.WriteLine((0).ToString("D") == "0");
        Console.WriteLine(int.MaxValue.ToString("D"));
        Console.WriteLine(int.MaxValue.ToString("D") == "2147483647");
        Console.WriteLine(la.ToString("D"));
        Console.WriteLine(la.ToString("D") == "123456789012345");
        Console.WriteLine((-1L).ToString("D20"));
        Console.WriteLine((-1L).ToString("D20") == "-00000000000000000001");
        Console.WriteLine(long.MaxValue.ToString("D"));
        Console.WriteLine(long.MaxValue.ToString("D") == "9223372036854775807");

        // ---- ToString: N chain (int32/int64/double) ----
        Console.WriteLine(p.ToString("N0"));
        Console.WriteLine(p.ToString("N0") == "1,234,567");
        Console.WriteLine((-p).ToString("N0"));
        Console.WriteLine((-p).ToString("N0") == "-1,234,567");
        Console.WriteLine(la.ToString("N0"));
        Console.WriteLine(la.ToString("N0") == "123,456,789,012,345");
        Console.WriteLine((0).ToString("N2"));
        Console.WriteLine((0).ToString("N2") == "0.00");
        Console.WriteLine(da.ToString("N2"));
        Console.WriteLine(da.ToString("N2") == "7.50");

        // ---- ToString: X chain (int32/int64) ----
        Console.WriteLine((255).ToString("X"));
        Console.WriteLine((255).ToString("X") == "FF");
        Console.WriteLine((255).ToString("X2"));
        Console.WriteLine((255).ToString("X2") == "FF");
        Console.WriteLine((255).ToString("X4"));
        Console.WriteLine((255).ToString("X4") == "00FF");
        Console.WriteLine((-1).ToString("X"));
        Console.WriteLine((-1).ToString("X") == "FFFFFFFF");
        Console.WriteLine(int.MaxValue.ToString("X"));
        Console.WriteLine(int.MaxValue.ToString("X") == "7FFFFFFF");
        Console.WriteLine((255L).ToString("X4"));
        Console.WriteLine((255L).ToString("X4") == "00FF");
        Console.WriteLine((-1L).ToString("X"));
        Console.WriteLine((-1L).ToString("X") == "FFFFFFFFFFFFFFFF");
        Console.WriteLine(long.MaxValue.ToString("X"));
        Console.WriteLine(long.MaxValue.ToString("X") == "7FFFFFFFFFFFFFFF");
        Console.WriteLine((0L).ToString("X16"));
        Console.WriteLine((0L).ToString("X16") == "0000000000000000");

        // ---- ToString: E chain (int32/int64/double) ----
        Console.WriteLine(p.ToString("E2"));
        Console.WriteLine(p.ToString("E2") == "1.23E+006");
        Console.WriteLine((12345).ToString("E2"));
        Console.WriteLine((12345).ToString("E2") == "1.23E+004");
        Console.WriteLine(la.ToString("E2"));
        Console.WriteLine(la.ToString("E2") == "1.23E+014");
        Console.WriteLine((0.00123).ToString("E2"));
        Console.WriteLine((0.00123).ToString("E2") == "1.23E-003");
        Console.WriteLine((0.0).ToString("E2"));
        Console.WriteLine((0.0).ToString("E2") == "0.00E+000");

        // ---- ToString: F chain (double) ----
        Console.WriteLine((1234.5).ToString("F2"));
        Console.WriteLine((1234.5).ToString("F2") == "1234.50");
        Console.WriteLine((-1234.5).ToString("F2"));
        Console.WriteLine((-1234.5).ToString("F2") == "-1234.50");
        Console.WriteLine((0.0).ToString("F2"));
        Console.WriteLine((0.0).ToString("F2") == "0.00");
        Console.WriteLine((3.14159).ToString("F3"));
        Console.WriteLine((3.14159).ToString("F3") == "3.142");

        // ---- ToString: G chain (int32/double) ----
        Console.WriteLine(p.ToString("G"));
        Console.WriteLine(p.ToString("G") == "1234567");
        Console.WriteLine(da.ToString("G"));
        Console.WriteLine(da.ToString("G") == "7.5");
        Console.WriteLine((0.00123).ToString("G"));
        Console.WriteLine((0.00123).ToString("G") == "0.00123");
        Console.WriteLine((100000.0).ToString("G"));
        Console.WriteLine((100000.0).ToString("G") == "100000");
        Console.WriteLine((0.0).ToString("G"));
        Console.WriteLine((0.0).ToString("G") == "0");

        // ---- Parse / TryParse chain ----
        Console.WriteLine(int.Parse("123"));
        Console.WriteLine(int.Parse("123") == 123);
        Console.WriteLine(int.Parse("-42"));
        Console.WriteLine(int.Parse("-42") == -42);
        Console.WriteLine(int.Parse("2147483647"));
        Console.WriteLine(int.Parse("2147483647") == int.MaxValue);
        Console.WriteLine(double.Parse("3.5"));
        Console.WriteLine(double.Parse("3.5") == 3.5);
        Console.WriteLine(double.Parse("-0.25"));
        Console.WriteLine(double.Parse("-0.25") == -0.25);
        Console.WriteLine(double.Parse("0.5"));
        Console.WriteLine(double.Parse("0.5") == 0.5);
        Console.WriteLine(int.TryParse("123", out int r1));
        Console.WriteLine(int.TryParse("123", out int r1b) && r1b == 123);
        Console.WriteLine(int.TryParse("-42", out int r2));
        Console.WriteLine(int.TryParse("-42", out int r2b) && r2b == -42);
        Console.WriteLine(int.TryParse("abc", out int r3));
        Console.WriteLine(int.TryParse("abc", out int r3b) == false);
        Console.WriteLine(double.TryParse("2.5", out double rd1));
        Console.WriteLine(double.TryParse("2.5", out double rd1b) && rd1b == 2.5);
        Console.WriteLine(double.TryParse("x", out double rd2));
        Console.WriteLine(double.TryParse("x", out double rd2b) == false);
        Console.WriteLine(long.TryParse("123456789012345", out long rl1));
        Console.WriteLine(long.TryParse("123456789012345", out long rl1b) && rl1b == 123456789012345L);
        Console.WriteLine(long.TryParse("-1", out long rl2));
        Console.WriteLine(long.TryParse("-1", out long rl2b) && rl2b == -1L);
        Console.WriteLine(long.TryParse("abc", out long rl3));
        Console.WriteLine(long.TryParse("abc", out long rl3b) == false);

        // ---- parse into arithmetic / format chains ----
        Console.WriteLine(int.Parse("123") + 1);
        Console.WriteLine(int.Parse("123") + 1 == 124);
        Console.WriteLine(double.Parse("1.5") * 2);
        Console.WriteLine(double.Parse("1.5") * 2 == 3.0);
        Console.WriteLine((int)double.Parse("2.9"));
        Console.WriteLine((int)double.Parse("2.9") == 2);
        Console.WriteLine(int.Parse("7") + int.Parse("8"));
        Console.WriteLine(int.Parse("7") + int.Parse("8") == 15);
        Console.WriteLine(int.Parse("255").ToString("X2"));
        Console.WriteLine(int.Parse("255").ToString("X2") == "FF");

        // ---- Convert family chain ----
        Console.WriteLine(Convert.ToInt32("123"));
        Console.WriteLine(Convert.ToInt32("123") == 123);
        Console.WriteLine(Convert.ToInt32("-99"));
        Console.WriteLine(Convert.ToInt32("-99") == -99);
        Console.WriteLine(Convert.ToInt32(42L));
        Console.WriteLine(Convert.ToInt32(42L) == 42);
        Console.WriteLine(Convert.ToInt32(3.9));
        Console.WriteLine(Convert.ToInt32(3.9) == 4);
        Console.WriteLine(Convert.ToInt32(-3.9));
        Console.WriteLine(Convert.ToInt32(-3.9) == -4);
        Console.WriteLine(Convert.ToInt32(2.5));
        Console.WriteLine(Convert.ToInt32(2.5) == 3);
        Console.WriteLine(Convert.ToInt32('A'));
        Console.WriteLine(Convert.ToInt32('A') == 65);
        Console.WriteLine(Convert.ToInt64("123456789012345"));
        Console.WriteLine(Convert.ToInt64("123456789012345") == 123456789012345L);
        Console.WriteLine(Convert.ToDouble("3.5"));
        Console.WriteLine(Convert.ToDouble("3.5") == 3.5);
        Console.WriteLine(Convert.ToDouble(7));
        Console.WriteLine(Convert.ToDouble(7) == 7.0);
        Console.WriteLine(Convert.ToString(123));
        Console.WriteLine(Convert.ToString(123) == "123");
        Console.WriteLine(Convert.ToString(123L));
        Console.WriteLine(Convert.ToString(123L) == "123");
        Console.WriteLine(Convert.ToString(3.5));
        Console.WriteLine(Convert.ToString(3.5) == "3.5");
        Console.WriteLine(Convert.ToString(true));
        Console.WriteLine(Convert.ToString(true) == "True");
        Console.WriteLine(Convert.ToString('X'));
        Console.WriteLine(Convert.ToString('X') == "X");
        Console.WriteLine(Convert.ToBoolean(0));
        Console.WriteLine(Convert.ToBoolean(0) == false);
        Console.WriteLine(Convert.ToBoolean(1));
        Console.WriteLine(Convert.ToBoolean(1) == true);
        Console.WriteLine(Convert.ToBoolean(5));
        Console.WriteLine(Convert.ToBoolean(5) == true);
        Console.WriteLine(Convert.ToChar(65));
        Console.WriteLine(Convert.ToChar(65) == 'A');
        Console.WriteLine(Convert.ToChar(66));
        Console.WriteLine(Convert.ToChar(66) == 'B');
        Console.WriteLine(BitConverter.DoubleToInt64Bits(1.0));
        Console.WriteLine(BitConverter.DoubleToInt64Bits(1.0) == 4607182418800017408L);
        Console.WriteLine(BitConverter.Int64BitsToDouble(4607182418800017408L));
        Console.WriteLine(BitConverter.Int64BitsToDouble(4607182418800017408L) == 1.0);

        // ---- Math family chain ----
        Console.WriteLine(Math.Abs(-7));
        Console.WriteLine(Math.Abs(-7) == 7);
        Console.WriteLine(Math.Abs(-7L));
        Console.WriteLine(Math.Abs(-7L) == 7L);
        Console.WriteLine(Math.Abs(-3.5));
        Console.WriteLine(Math.Abs(-3.5) == 3.5);
        Console.WriteLine(Math.Abs(0));
        Console.WriteLine(Math.Abs(0) == 0);
        Console.WriteLine(Math.Max(3, 5));
        Console.WriteLine(Math.Max(3, 5) == 5);
        Console.WriteLine(Math.Min(3, 5));
        Console.WriteLine(Math.Min(3, 5) == 3);
        Console.WriteLine(Math.Max(-7L, 2L));
        Console.WriteLine(Math.Max(-7L, 2L) == 2L);
        Console.WriteLine(Math.Min(-7L, 2L));
        Console.WriteLine(Math.Min(-7L, 2L) == -7L);
        Console.WriteLine(Math.Max(1.5, 1.25));
        Console.WriteLine(Math.Max(1.5, 1.25) == 1.5);
        Console.WriteLine(Math.Min(1.5, 1.25));
        Console.WriteLine(Math.Min(1.5, 1.25) == 1.25);
        Console.WriteLine(Math.Sign(-42));
        Console.WriteLine(Math.Sign(-42) == -1);
        Console.WriteLine(Math.Sign(0));
        Console.WriteLine(Math.Sign(0) == 0);
        Console.WriteLine(Math.Sign(7));
        Console.WriteLine(Math.Sign(7) == 1);
        Console.WriteLine(Math.Sign(-42L));
        Console.WriteLine(Math.Sign(-42L) == -1);
        Console.WriteLine(Math.Sqrt(4.0));
        Console.WriteLine(Math.Sqrt(4.0) == 2.0);
        Console.WriteLine(Math.Sqrt(9.0));
        Console.WriteLine(Math.Sqrt(9.0) == 3.0);
        Console.WriteLine(Math.Sqrt(2.0));
        Console.WriteLine(Math.Sqrt(2.0) == 1.4142135623730951);
        Console.WriteLine(Math.Pow(2.0, 10.0));
        Console.WriteLine(Math.Pow(2.0, 10.0) == 1024.0);
        Console.WriteLine(Math.Pow(10.0, 2.0));
        Console.WriteLine(Math.Pow(10.0, 2.0) == 100.0);
        Console.WriteLine(Math.Pow(3.0, 3.0));
        Console.WriteLine(Math.Pow(3.0, 3.0) == 27.0);
        Console.WriteLine(Math.Pow(da, 2.0));
        Console.WriteLine(Math.Pow(da, 2.0) == 56.25);
        Console.WriteLine(Math.Floor(2.9));
        Console.WriteLine(Math.Floor(2.9) == 2.0);
        Console.WriteLine(Math.Floor(-2.1));
        Console.WriteLine(Math.Floor(-2.1) == -3.0);
        Console.WriteLine(Math.Ceiling(2.1));
        Console.WriteLine(Math.Ceiling(2.1) == 3.0);
        Console.WriteLine(Math.Ceiling(-2.9));
        Console.WriteLine(Math.Ceiling(-2.9) == -2.0);
        Console.WriteLine(Math.Truncate(2.9));
        Console.WriteLine(Math.Truncate(2.9) == 2.0);
        Console.WriteLine(Math.Truncate(-2.9));
        Console.WriteLine(Math.Truncate(-2.9) == -2.0);
        Console.WriteLine(Math.Floor(da));
        Console.WriteLine(Math.Floor(da) == 7.0);

        // ---- rounding chain (net48 ToEven semantics) ----
        Console.WriteLine(Math.Round(2.5));
        Console.WriteLine(Math.Round(2.5) == 2.0);
        Console.WriteLine(Math.Round(3.5));
        Console.WriteLine(Math.Round(3.5) == 4.0);
        Console.WriteLine(Math.Round(4.5));
        Console.WriteLine(Math.Round(4.5) == 4.0);
        Console.WriteLine(Math.Round(1.5));
        Console.WriteLine(Math.Round(1.5) == 2.0);
        Console.WriteLine(Math.Round(0.5));
        Console.WriteLine(Math.Round(0.5) == 0.0);
        Console.WriteLine(Math.Round(-2.5));
        Console.WriteLine(Math.Round(-2.5) == -2.0);
        Console.WriteLine(Math.Round(2.4));
        Console.WriteLine(Math.Round(2.4) == 2.0);
        Console.WriteLine(Math.Round(2.6));
        Console.WriteLine(Math.Round(2.6) == 3.0);
        Console.WriteLine(Math.Round(0.0));
        Console.WriteLine(Math.Round(0.0) == 0.0);
        Console.WriteLine(Math.Round(123.456, 2));
        Console.WriteLine(Math.Round(123.456, 2) == 123.46);
        Console.WriteLine(Math.Round(123.454, 2));
        Console.WriteLine(Math.Round(123.454, 2) == 123.45);
        Console.WriteLine(Math.Round(2.5, 0));
        Console.WriteLine(Math.Round(2.5, 0) == 2.0);
        Console.WriteLine(Math.Round(3.5, 0));
        Console.WriteLine(Math.Round(3.5, 0) == 4.0);

        // ---- end-to-end chain (parse -> math -> format -> convert) ----
        int k = int.Parse("123");
        Console.WriteLine(k.ToString("D"));
        Console.WriteLine(k.ToString("D") == "123");
        double half = Math.Round(2.5);
        Console.WriteLine(half);
        Console.WriteLine(half == 2.0);
        Console.WriteLine(half.ToString("F1"));
        Console.WriteLine(half.ToString("F1") == "2.0");
        Console.WriteLine(Math.Round(123.456, 2).ToString("F2"));
        Console.WriteLine(Math.Round(123.456, 2).ToString("F2") == "123.46");
        Console.WriteLine(Convert.ToInt32(Math.Round(2.5)).ToString("X"));
        Console.WriteLine(Convert.ToInt32(Math.Round(2.5)).ToString("X") == "2");
        Console.WriteLine(Convert.ToString(Math.Round(3.5)));
        Console.WriteLine(Convert.ToString(Math.Round(3.5)) == "4");

        // ---- last: empty-string TryParse (net48 == false) ----
        Console.WriteLine(int.TryParse("", out int rE));
        Console.WriteLine(int.TryParse("", out int rEb) == false);
    }
}
