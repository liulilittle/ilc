using System;

namespace Cases
{
    public static class CapW
    {
        public static void Main()
        {
            long lv = 5000000000L;
            double dv = 1.25;
            float fv = 1.25f;
            Func<int> a = () => lv.CompareTo(3L);
            Func<int> b = () => dv.CompareTo(2.5);
            Func<int> c = () => fv.CompareTo(2.5f);
            Func<long> d = () => lv;
            Console.WriteLine("[cap_i8]=" + a());
            Console.WriteLine("[cap_d]=" + b());
            Console.WriteLine("[cap_f]=" + c());
            Console.WriteLine("[cap_val]=" + d());
        }
    }
}
