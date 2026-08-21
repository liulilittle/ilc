using System;
using System.Text;

namespace Cases { public static class EncodingCase {
    public static void Main() {
        // Encoding.UTF8.GetBytes("\u4F60\u597D") -> E4 BD A0 E5 A5 BD
        byte[] u8 = Encoding.UTF8.GetBytes("\u4F60\u597D");
        Console.WriteLine(u8.Length == 6);
        Console.WriteLine(u8[0] == 0xE4);
        Console.WriteLine(u8[1] == 0xBD);
        Console.WriteLine(u8[2] == 0xA0);
        Console.WriteLine(u8[3] == 0xE5);
        Console.WriteLine(u8[4] == 0xA5);
        Console.WriteLine(u8[5] == 0xBD);
        // GetString round-trip
        string round = Encoding.UTF8.GetString(u8);
        Console.WriteLine(round == "\u4F60\u597D");
        // ASCII.GetBytes("ABC")
        byte[] ascii = Encoding.ASCII.GetBytes("ABC");
        Console.WriteLine(ascii.Length == 3);
        Console.WriteLine(ascii[0] == 0x41 && ascii[1] == 0x42 && ascii[2] == 0x43);
        // GetChars round-trip (ASCII)
        char[] chars = Encoding.ASCII.GetChars(ascii);
        Console.WriteLine(chars.Length == 3);
        Console.WriteLine(chars[0] == 'A' && chars[1] == 'B' && chars[2] == 'C');
        Console.WriteLine(new string(chars) == "ABC");
        // GetChars round-trip (UTF8, 2 chars)
        char[] u8chars = Encoding.UTF8.GetChars(u8);
        Console.WriteLine(u8chars.Length == 2);
        Console.WriteLine(u8chars[0] == '\u4F60' && u8chars[1] == '\u597D');
    }
}}
