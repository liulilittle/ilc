using System;

namespace Cases
{
    public static class ProbeIntPtrApi
    {
        static string Try(Func<string> f)
        {
            try { return f(); }
            catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        public static unsafe void Main()
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

            Console.WriteLine("[a.X]=" + a.ToString("X"));
            Console.WriteLine("[a.x]=" + a.ToString("x"));
            Console.WriteLine("[a.D]=" + a.ToString("D"));
            Console.WriteLine("[a.G]=" + a.ToString("G"));
            Console.WriteLine("[a.X8]=" + a.ToString("X8"));
            Console.WriteLine("[a.X20]=" + a.ToString("X20"));
            Console.WriteLine("[a.D10]=" + a.ToString("D10"));
            Console.WriteLine("[b.X]=" + b.ToString("X"));
            Console.WriteLine("[big.X]=" + big.ToString("X"));
            Console.WriteLine("[big.X16]=" + big.ToString("X16"));
            Console.WriteLine("[zero.X]=" + zero.ToString("X"));
            Console.WriteLine("[zero.D5]=" + zero.ToString("D5"));
            Console.WriteLine("[a.nullfmt]=" + a.ToString((string)null));
            Console.WriteLine("[a.emptyfmt]=" + a.ToString(""));
            Console.WriteLine("[a.badfmt]=" + Try(() => a.ToString("Q")));

            Console.WriteLine("[a.eqobjstr]=" + a.Equals((object)"255"));
            Console.WriteLine("[a.eqobjint]=" + a.Equals((object)255));
            Console.WriteLine("[a.eqstr]=" + a.Equals("255"));
            Console.WriteLine("[a.eqnull]=" + a.Equals(null));
            Console.WriteLine("[a.ptr.nonnull]=" + (a.ToPointer() != null));
            Console.WriteLine("[b.ptr.nonnull]=" + (b.ToPointer() != null));
            Console.WriteLine("[zero.ptr.isnull]=" + (zero.ToPointer() == null));

            Console.WriteLine("[add]=" + ((IntPtr)10 + 5).ToString());
            Console.WriteLine("[sub]=" + ((IntPtr)10 - 5).ToString());
            Console.WriteLine("[addneg]=" + ((IntPtr)10 + (-5)).ToString());

            UIntPtr ua = new UIntPtr(255u);
            UIntPtr ubig = new UIntPtr(ulong.MaxValue);
            UIntPtr uzero = UIntPtr.Zero;

            Console.WriteLine("[u.ToString]=" + ua.ToString());
            Console.WriteLine("[u.big]=" + ubig.ToString());
            Console.WriteLine("[u.zero]=" + uzero.ToString());
            Console.WriteLine("[u.hash]=" + ua.GetHashCode());
            Console.WriteLine("[u.bighash]=" + ubig.GetHashCode());
            Console.WriteLine("[u.zerohash]=" + uzero.GetHashCode());
            Console.WriteLine("[u.eqobjstr]=" + ua.Equals((object)"255"));
            Console.WriteLine("[u.eqstr]=" + ua.Equals("255"));
            Console.WriteLine("[u.eqnull]=" + ua.Equals(null));
            Console.WriteLine("[u.ptr.nonnull]=" + (ua.ToPointer() != null));
            Console.WriteLine("[u.zero.ptr.isnull]=" + (uzero.ToPointer() == null));
            Console.WriteLine("[u.add]=" + (new UIntPtr(10u) + 5).ToString());
            Console.WriteLine("[u.sub]=" + (new UIntPtr(10u) - 5).ToString());
        }
    }
}
