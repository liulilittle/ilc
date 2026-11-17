using System;

namespace Cases
{
    public static class W11ProbeGetters
    {
        public static void Main()
        {
            Console.WriteLine("before");
            Console.WriteLine(Console.Title);
            Console.WriteLine((int)Console.ForegroundColor);
            Console.WriteLine((int)Console.BackgroundColor);
            Console.WriteLine(Console.CursorLeft);
            Console.WriteLine(Console.CursorTop);
            Console.WriteLine(Console.WindowWidth);
            Console.WriteLine(Console.WindowHeight);
            Console.WriteLine(Console.BufferWidth);
            Console.WriteLine(Console.BufferHeight);
            Console.WriteLine(Console.CursorVisible);
            Console.SetCursorPosition(1, 2);
            Console.ResetColor();
            Console.Beep(440, 100);
            Console.WriteLine("after");
        }
    }
}
