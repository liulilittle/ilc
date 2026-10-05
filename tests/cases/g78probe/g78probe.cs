// g78probe -- G7/G8 e2e probe (NON-POOL): `new IntPtr(x)` (ldloca-address ctor)
// must keep the full 64-bit carrier, IntPtr/UIntPtr explicit operators must
// resolve (no `System_*Ptr_op_Explicit_*` phantoms), and the checked
// conversions must throw System.OverflowException exactly like net48
// ((int)IntPtr, (uint)UIntPtr, UIntPtr.ToUInt32 for out-of-range values;
// small values pass). Oracle = net48 run of this case (287 B raw EQ on
// mingw + msvc in the A6 scratch verification).
using System;

namespace Cases
{
    public static class G78Probe
    {
        public static void Main()
        {
            int i = 12345;
            long l = 0x1234567890ABCDEFL;

            IntPtr p1 = new IntPtr(i);
            IntPtr p2 = new IntPtr(l);
            IntPtr p3 = (IntPtr)l;
            IntPtr p4 = (IntPtr)i;
            long pl = (long)p2;
            int pi = (int)p1;
            Console.WriteLine("p1=" + p1.ToInt64() + " p2=" + p2.ToInt64() + " p3=" + p3.ToInt64() + " p4=" + p4.ToInt64());
            Console.WriteLine("pl=" + pl + " pi=" + pi);
            try
            {
                int ovfP = (int)p2;
                Console.WriteLine("p2->int = " + ovfP);
            }
            catch (OverflowException)
            {
                Console.WriteLine("p2->int OVERFLOW");
            }

            UIntPtr u1 = (UIntPtr)(ulong)l;
            UIntPtr u2 = (UIntPtr)42u;
            UIntPtr u3 = new UIntPtr(7);
            Console.WriteLine("u1=" + u1.ToUInt64() + " u2=" + u2.ToUInt64() + " u3=" + u3.ToUInt64());

            ulong back1 = (ulong)u1;
            uint back2 = (uint)u2;
            long back3 = (long)u1;
            Console.WriteLine("back1=" + back1 + " back2=" + back2 + " back3=" + back3);

            try
            {
                uint ovf = (uint)u1;
                Console.WriteLine("u1->uint = " + ovf);
            }
            catch (OverflowException)
            {
                Console.WriteLine("u1->uint OVERFLOW");
            }

            try
            {
                uint ovf2 = u1.ToUInt32();
                Console.WriteLine("u1.ToUInt32 = " + ovf2);
            }
            catch (OverflowException)
            {
                Console.WriteLine("u1.ToUInt32 OVERFLOW");
            }

            IntPtr zero = IntPtr.Zero;
            UIntPtr uzero = UIntPtr.Zero;
            Console.WriteLine("zero=" + (zero == IntPtr.Zero) + " uzero=" + (uzero == UIntPtr.Zero) + " uneq=" + (uzero != UIntPtr.Zero));
        }
    }
}
