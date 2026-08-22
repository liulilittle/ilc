using System;

namespace E2eCharProbe
{
    public static class Program
    {
        public static int Main()
        {
            int failures = 0;

            // 1. char.IsDigit('5')
            if (char.IsDigit('5')) Console.WriteLine("PASS IsDigit('5')");
            else { Console.WriteLine("FAIL IsDigit('5')"); failures++; }

            // 2. char.IsLetter('a')
            if (char.IsLetter('a')) Console.WriteLine("PASS IsLetter('a')");
            else { Console.WriteLine("FAIL IsLetter('a')"); failures++; }

            // 3. char.IsWhiteSpace(' ')
            if (char.IsWhiteSpace(' ')) Console.WriteLine("PASS IsWhiteSpace(' ')");
            else { Console.WriteLine("FAIL IsWhiteSpace(' ')"); failures++; }

            // 4. char.ToUpper('a') == 'A'
            char up = char.ToUpper('a');
            if (up == 'A') Console.WriteLine("PASS ToUpper('a')=='A");
            else { Console.WriteLine("FAIL ToUpper('a') got " + up); failures++; }

            // 5. char.ToLower('Z') == 'z'
            char low = char.ToLower('Z');
            if (low == 'z') Console.WriteLine("PASS ToLower('Z')=='z'");
            else { Console.WriteLine("FAIL ToLower('Z') got " + low); failures++; }

            // 6. char arithmetic: ('a' + 1) int-add then cast back to char == 'b'
            char next = (char)('a' + 1);
            if (next == 'b') Console.WriteLine("PASS ('a'+1)=='b'");
            else { Console.WriteLine("FAIL ('a'+1) got " + next); failures++; }

            if (failures == 0)
            {
                Console.WriteLine("ALL-PASS");
                return 0;
            }
            Console.WriteLine("ALL-FAIL failures=" + failures);
            return 1;
        }
    }
}
