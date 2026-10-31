using System;

namespace Cases
{
    public static class ProbeIntPtrApi
    {
        public static void Main()
        {
            IntPtr a = (IntPtr)255;
            IntPtr b = (IntPtr)(-1);
            IntPtr big = (IntPtr)0x1234567890L;
            IntPtr zero = IntPtr.Zero;

            Console.WriteLine("[a.ToString]=" + a.ToString());
            Console.WriteLine("[b.ToString]=" + b.ToString());
            Console.WriteLine("[big.ToString]=" + big.ToString());
            Console.WriteLine("[zero.ToString]=" + zero.ToString());
            Console.WriteLine("[a.hash]=" + a.GetHashCode());
            Console.WriteLine("[b.hash]=" + b.GetHashCode());
            Console.WriteLine("[big.hash]=" + big.GetHashCode());
            Console.WriteLine("[zero.hash]=" + zero.GetHashCode());

            UIntPtr ua = new UIntPtr(255u);
            UIntPtr ubig = new UIntPtr(ulong.MaxValue);
            UIntPtr uzero = UIntPtr.Zero;

            Console.WriteLine("[u.ToString]=" + ua.ToString());
            Console.WriteLine("[u.big]=" + ubig.ToString());
            Console.WriteLine("[u.zero]=" + uzero.ToString());
            Console.WriteLine("[u.hash]=" + ua.GetHashCode());
            Console.WriteLine("[u.bighash]=" + ubig.GetHashCode());
            Console.WriteLine("[u.zerohash]=" + uzero.GetHashCode());
        }
    }
}
