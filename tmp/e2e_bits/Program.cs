using System;

namespace E2eBitsProbe
{
    public static class Program
    {
        public static int Main()
        {
            int failures = 0;

            // 1. GetBytes(int)/ToInt32 roundtrip: 258 = 0x00000102 (multi-byte
            //    little-endian layout; roundtrip must be the identity).
            byte[] b258 = BitConverter.GetBytes(258);
            if (b258 != null && b258.Length == 4 && BitConverter.ToInt32(b258, 0) == 258)
                Console.WriteLine("PASS GetBytes/ToInt32 roundtrip 258");
            else { Console.WriteLine("FAIL GetBytes/ToInt32 roundtrip 258"); failures++; }

            // 2. GetBytes(int)/ToInt32 roundtrip: -1 (all-ones pattern,
            //    sign-preservation check).
            byte[] bm1 = BitConverter.GetBytes(-1);
            if (bm1 != null && bm1.Length == 4 && BitConverter.ToInt32(bm1, 0) == -1)
                Console.WriteLine("PASS GetBytes/ToInt32 roundtrip -1");
            else { Console.WriteLine("FAIL GetBytes/ToInt32 roundtrip -1"); failures++; }

            // 3. GetBytes(double)/ToDouble roundtrip: 3.14 (IEEE-754 double,
            //    bit-exact identity required).
            double v = 3.14;
            byte[] bd = BitConverter.GetBytes(v);
            if (bd != null && bd.Length == 8 && BitConverter.ToDouble(bd, 0) == v)
                Console.WriteLine("PASS GetBytes/ToDouble roundtrip 3.14");
            else { Console.WriteLine("FAIL GetBytes/ToDouble roundtrip 3.14"); failures++; }

            // 4. ToString(byte[]) hex form -- wired: the single byte[] arity
            //    maps to il2cpp_bitconverter_to_hex_string
            //    (src/il2cppc/Emit/MethodMap.BitConverter.cs ToString case).
            //    net48 canonical form: uppercase hex pairs, '-' separated.
            string hex = BitConverter.ToString(new byte[] { 0x02, 0x01, 0xFF });
            if (hex == "02-01-FF")
                Console.WriteLine("PASS ToString hex 02-01-FF");
            else { Console.WriteLine("FAIL ToString got " + hex); failures++; }

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
