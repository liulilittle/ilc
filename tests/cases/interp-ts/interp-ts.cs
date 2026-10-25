using System;

//------------------------------------------------------------------------------
// interp-ts -- DefaultInterpolatedStringHandler.AppendFormatted<T> over the
// TimeSpan value type (a BCL struct beyond decimal/DateTime/Guid): the 1/2/3-
// argument forms (plain, alignment, format string). Byte-exact against the
// net48 oracle.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class InterpTs
    {
        public static void Main()
        {
            TimeSpan ts = new TimeSpan(1, 2, 3, 4); // 1.02:03:04
            Console.WriteLine($"ts1=[{ts}]");
            Console.WriteLine($"ts2=[{ts,8}]");
            Console.WriteLine($"ts3=[{ts,8:c}]");
            Console.WriteLine($"ts4=[{ts:c}]");

            // mixed with a primitive on the same line
            int x = 5;
            Console.WriteLine($"mix=[{x:D3}|{ts:c}]");
        }
    }
}
