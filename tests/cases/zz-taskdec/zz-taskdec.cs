using System;
using System.Threading.Tasks;

namespace Cases
{
    public static class TDec
    {
        static async Task<decimal> AD()
        {
            decimal d = 1.5m;
            d = await Task.FromResult(d + 1m);
            return d;
        }

        public static void Main()
        {
            Task<decimal> t = Task.FromResult(1.5m);
            Console.WriteLine(t.Result);
            Console.WriteLine(t.GetAwaiter().GetResult());
            Console.WriteLine(AD().Result);
            Console.WriteLine(AD().GetAwaiter().GetResult());
        }
    }
}
