using System;

namespace Calc
{
    public static class Program
    {
        static int counter = 10;

        public static void Main()
        {
            int x = Add(3, 4);
            Console.WriteLine(x);
            counter++;
            Console.WriteLine(counter);
        }

        static int Add(int a, int b)
        {
            return a + b;
        }
    }
}
