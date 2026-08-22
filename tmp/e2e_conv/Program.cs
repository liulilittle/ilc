using System;

namespace E2eConvProbe
{
    public static class Program
    {
        private static int Check(bool ok, string name, string detail)
        {
            if (ok) Console.WriteLine("PASS " + name + "=" + detail);
            else Console.WriteLine("FAIL " + name + "=" + detail);
            return ok ? 0 : 1;
        }

        public static int Main()
        {
            int failures = 0;

            // --- ToInt32(string) ---
            int a = Convert.ToInt32("123");
            failures += Check(a == 123, "ToInt32(string)", a.ToString());
            int an = Convert.ToInt32("-456");
            failures += Check(an == -456, "ToInt32(string-neg)", an.ToString());

            // --- ToInt32(int) identity ---
            int b = Convert.ToInt32(2147483647);
            failures += Check(b == 2147483647, "ToInt32(int)", b.ToString());
            int bn = Convert.ToInt32(-2147483648);
            failures += Check(bn == -2147483648, "ToInt32(int-min)", bn.ToString());

            // --- ToInt32(bool): true->1 false->0 ---
            int c = Convert.ToInt32(true);
            int d = Convert.ToInt32(false);
            failures += Check(c == 1 && d == 0, "ToInt32(bool)", c + "/" + d);

            // --- ToInt32(double): round-half-to-even ---
            int e = Convert.ToInt32(3.7);   // -> 4
            int f = Convert.ToInt32(2.5);   // -> 2 (half-to-even)
            int g = Convert.ToInt32(3.5);   // -> 4 (half-to-even)
            int gn = Convert.ToInt32(-2.5); // -> -2 (half-to-even)
            failures += Check(e == 4 && f == 2 && g == 4 && gn == -2,
                "ToInt32(double)", e + "/" + f + "/" + g + "/" + gn);

            // --- ToBoolean(int): nonzero true, zero false ---
            bool h = Convert.ToBoolean(0);
            bool i = Convert.ToBoolean(1);
            bool j = Convert.ToBoolean(42);
            bool k = Convert.ToBoolean(-1);
            failures += Check(!h && i && j && k, "ToBoolean(int)",
                (h ? "T" : "F") + "/" + (i ? "T" : "F") + "/" + (j ? "T" : "F") + "/" + (k ? "T" : "F"));

            // --- ToString(int) ---
            string s1 = Convert.ToString(789);
            string s1n = Convert.ToString(-42);
            failures += Check(s1 == "789" && s1n == "-42", "ToString(int)", s1 + "/" + s1n);

            // --- ToString(long) ---
            string s2 = Convert.ToString(9223372036854775807L);
            string s2n = Convert.ToString(-9034567890123456789L);
            failures += Check(s2 == "9223372036854775807" && s2n == "-9034567890123456789",
                "ToString(long)", s2 + "/" + s2n);

            // --- ToString(double) ---
            string s3 = Convert.ToString(2.5);
            string s3b = Convert.ToString(0.1);
            string s3c = Convert.ToString(123.456);
            failures += Check(s3 == "2.5" && s3b == "0.1" && s3c == "123.456",
                "ToString(double)", s3 + "/" + s3b + "/" + s3c);

            // --- ToString(bool) ---
            string s4 = Convert.ToString(true);
            string s5 = Convert.ToString(false);
            failures += Check(s4 == "True" && s5 == "False", "ToString(bool)", s4 + "/" + s5);

            // --- ToInt64(string) ---
            long l = Convert.ToInt64("9223372036854775806");
            long ln = Convert.ToInt64("-123456789012");
            failures += Check(l == 9223372036854775806L && ln == -123456789012L,
                "ToInt64(string)", l.ToString() + "/" + ln.ToString());

            // --- ToDouble(string) ---
            double db = Convert.ToDouble("3.14");
            double dbn = Convert.ToDouble("-0.5");
            failures += Check(db == 3.14 && dbn == -0.5, "ToDouble(string)",
                db.ToString() + "/" + dbn.ToString());

            // --- Roundtrips ---
            // int: value -> ToString -> ToInt32
            int rv = 123456789;
            int rvi = Convert.ToInt32(Convert.ToString(rv));
            failures += Check(rvi == rv, "roundtrip int", rvi.ToString());

            // long: value -> ToString -> ToInt64
            long rl = -9034567890123456789L;
            long rll = Convert.ToInt64(Convert.ToString(rl));
            failures += Check(rll == rl, "roundtrip long", rll.ToString());

            // double: value -> ToString -> ToDouble
            double rd = 123.456;
            double rdd = Convert.ToDouble(Convert.ToString(rd));
            failures += Check(rdd == rd, "roundtrip double", rdd.ToString());

            // bool: value -> ToInt32 -> ToBoolean
            bool rb = Convert.ToBoolean(Convert.ToInt32(true));
            bool rb2 = Convert.ToBoolean(Convert.ToInt32(false)) == false;
            failures += Check(rb && rb2, "roundtrip bool", (rb ? "T" : "F") + "/" + (rb2 ? "T" : "F"));

            if (failures == 0)
            {
                Console.WriteLine("ALL-PASS");
                return 0;
            }
            Console.WriteLine("ALL-FAIL failures=" + failures);
            return 1;
        }
    }
}
