using System;
using System.Text;
namespace Cases { public static class InsertCharArrCase {
    public static void Main() {
        // Insert(int, char[]) -- array as Il2CppObject* value (misread as string?)
        StringBuilder sb = new StringBuilder("base");
        char[] chars = { 'A', 'B', 'C' };
        sb.Insert(0, chars);
        Console.WriteLine(sb.Length);
        Console.WriteLine(sb.ToString());
        // Insert(int, string, int count) overload
        StringBuilder sb2 = new StringBuilder("base");
        sb2.Insert(0, "xy", 3);
        Console.WriteLine(sb2.Length);
        Console.WriteLine(sb2.ToString());
        // Insert at end with char[]
        StringBuilder sb3 = new StringBuilder("end");
        sb3.Insert(sb3.Length, chars);
        Console.WriteLine(sb3.Length);
        Console.WriteLine(sb3.ToString());
    }
}}
