using System;

namespace Cases
{
    public static class W11ProbeSetters
    {
        public static void Main()
        {
            Console.WriteLine("before");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.Title = "t";
            Console.CursorLeft = 1;
            Console.CursorTop = 2;
            Console.CursorVisible = false;
            Console.WriteLine("after");
        }
    }
}
