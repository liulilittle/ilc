using System;
using System.Resources;

namespace Resx
{
    public static class Program
    {
        public static void Main()
        {
            var rm = new ResourceManager("resx.Strings", typeof(Program).Assembly);
            Console.WriteLine(rm.GetString("Greeting"));
            Console.WriteLine(rm.GetString("Missing"));
        }
    }
}
