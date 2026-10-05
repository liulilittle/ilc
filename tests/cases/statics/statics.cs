using System;
namespace Cases { public static class Statics {
    static int counter = 100;
    static readonly int baseValue = 7;
    public static void Main() {
        for (int i = 0; i < 5; i++) { counter += 2; Console.WriteLine(counter); }
        Console.WriteLine(baseValue * 3);             // 21
        counter -= 50;
        Console.WriteLine(counter);                   // 100+10-50 = 60
    }
}}
