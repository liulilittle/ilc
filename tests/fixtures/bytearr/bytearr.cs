using System;

namespace AuditFix
{
    public static class ByteArr
    {
        public static void Main()
        {
            // newarr System.Byte (net-domain T0): the value-array entry
            // (1, false) must reach il2cpp_array_new with the byte stride.
            byte[] a = new byte[4];
            a[0] = 1;
            a[3] = 5;
            int v = a[0] + a[3];
            Console.WriteLine(v); // 6
        }
    }
}
