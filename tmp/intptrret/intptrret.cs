using System;

namespace Cases
{
    public static class IntPtrRet
    {
        public static void Main()
        {
            IntPtr p = GetPtr();
            Console.WriteLine(p.ToInt64());
            Console.WriteLine(GetPtr().ToInt64());
            Console.WriteLine(GetPtr() == (IntPtr)0x123456789L);
        }

        static IntPtr GetPtr()
        {
            return (IntPtr)0x123456789L;
        }
    }
}
