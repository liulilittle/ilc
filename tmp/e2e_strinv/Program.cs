using System;

namespace E2eStrInvProbe
{
    public static class Program
    {
        public static int Main()
        {
            int failures = 0;

            // 1. ToUpperInvariant: "abc" -> "ABC"
            string up = "abc".ToUpperInvariant();
            if (up == "ABC") Console.WriteLine("PASS ToUpperInvariant abc->" + up);
            else { Console.WriteLine("FAIL ToUpperInvariant got=" + up); failures++; }

            // 2. ToLowerInvariant: "ABC" -> "abc"
            string low = "ABC".ToLowerInvariant();
            if (low == "abc") Console.WriteLine("PASS ToLowerInvariant ABC->" + low);
            else { Console.WriteLine("FAIL ToLowerInvariant got=" + low); failures++; }

            // 3. Insert/Remove chain:
            //    "abcdef".Insert(2,"XY") = "abXYcdef"; .Remove(1,3) drops "bXY" -> "acdef"
            string s0 = "abcdef";
            string t1 = s0.Insert(2, "XY");
            string t2 = t1.Remove(1, 3);
            if (t1 == "abXYcdef" && t2 == "acdef") Console.WriteLine("PASS InsertRemove chain=" + t2);
            else { Console.WriteLine("FAIL InsertRemove got=" + t1 + "/" + t2); failures++; }

            // 4. Split(new[]{','}) then Join(",") roundtrip
            string csv = "a,b,c";
            string[] parts = csv.Split(new[] { ',' });
            string joined = string.Join(",", parts);
            if (parts.Length == 3 && parts[0] == "a" && parts[1] == "b" && parts[2] == "c" && joined == csv)
                Console.WriteLine("PASS SplitJoin roundtrip=" + joined);
            else { Console.WriteLine("FAIL SplitJoin len=" + parts.Length + " join=" + joined); failures++; }

            // 5. PadLeft / PadRight (pad char + space pad + width<length no-op)
            string pl = "7".PadLeft(3, '0');
            string pr = "7".PadRight(3, '.');
            string plSpace = "abc".PadLeft(5);
            string prShort = "abc".PadRight(2);
            if (pl == "007" && pr == "7.." && plSpace == "  abc" && prShort == "abc")
                Console.WriteLine("PASS Pad left=" + pl + " right=" + pr);
            else { Console.WriteLine("FAIL Pad got=" + pl + "/" + pr + "/" + plSpace + "/" + prShort); failures++; }

            // 6. Contains ordinal (case-sensitive substring test)
            bool c1 = "hello world".Contains("lo w");
            bool c2 = "hello".Contains("HELLO");
            bool c3 = "hello".Contains("ell");
            if (c1 && !c2 && c3)
                Console.WriteLine("PASS Contains ordinal present=" + c1 + " caseSensitive=" + !c2);
            else { Console.WriteLine("FAIL Contains got=" + c1 + "/" + c2 + "/" + c3); failures++; }

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
