using System;

namespace Cases
{
    public static class MathRound
    {
        public static void Main()
        {
            // default (ToEven) banker's rounding
            Console.WriteLine(Math.Round(2.5));   // 2
            Console.WriteLine(Math.Round(3.5));   // 4
            Console.WriteLine(Math.Round(4.5));   // 4
            Console.WriteLine(Math.Round(-2.5));  // -2
            Console.WriteLine(Math.Round(1.4));   // 1
            // explicit ToEven
            Console.WriteLine(Math.Round(2.5, MidpointRounding.ToEven));   // 2
            Console.WriteLine(Math.Round(-3.5, MidpointRounding.ToEven));  // -4
            // AwayFromZero
            Console.WriteLine(Math.Round(2.5, MidpointRounding.AwayFromZero));   // 3
            Console.WriteLine(Math.Round(-2.5, MidpointRounding.AwayFromZero));  // -3
            Console.WriteLine(Math.Round(3.5, MidpointRounding.AwayFromZero));   // 4
            Console.WriteLine(Math.Round(1.4, MidpointRounding.AwayFromZero));   // 1
            // digit-scaled modes
            Console.WriteLine(Math.Round(2.45, 1, MidpointRounding.AwayFromZero)); // 2.5
            Console.WriteLine(Math.Round(2.44, 1, MidpointRounding.AwayFromZero)); // 2.4
            Console.WriteLine(Math.Round(2.45, 1, MidpointRounding.ToEven));       // 2.4
            Console.WriteLine(Math.Round(2.35, 1, MidpointRounding.ToEven));       // 2.4
            Console.WriteLine(Math.Round(-2.45, 1, MidpointRounding.AwayFromZero)); // -2.5
            Console.WriteLine(Math.Round(3.14159, 3, MidpointRounding.AwayFromZero)); // 3.142
            Console.WriteLine(Math.Round(3.14159, 3, MidpointRounding.ToEven));       // 3.142
            // Math.BigMul: exact 64-bit product (no overflow)
            Console.WriteLine(Math.BigMul(3, 5));
            Console.WriteLine(Math.BigMul(100000, 100000));
            Console.WriteLine(Math.BigMul(-7, 8));
            Console.WriteLine(Math.BigMul(int.MaxValue, 2));
            Console.WriteLine(Math.BigMul(int.MinValue, -1));
        }
    }
}
