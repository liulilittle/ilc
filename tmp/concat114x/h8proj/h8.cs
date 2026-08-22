using System;
using System.Text;
namespace Cases { public static class InsertCharArrOnlyCase {
    public static void Main() {
        StringBuilder sb = new StringBuilder("base");
        char[] chars = { 'A', 'B', 'C' };
        sb.Insert(0, chars);
        Console.WriteLine(sb.Length);
        Console.WriteLine(sb.ToString());
        StringBuilder sb2 = new StringBuilder("mid");
        char[] chars2 = { 'X', 'Y' };
        sb2.Insert(2, chars2);
        Console.WriteLine(sb2.Length);
        Console.WriteLine(sb2.ToString());
        StringBuilder sb3 = new StringBuilder("end");
        char[] chars3 = { '1', '2', '3', '4' };
        sb3.Insert(sb3.Length, chars3);
        Console.WriteLine(sb3.Length);
        Console.WriteLine(sb3.ToString());
    }
}}
