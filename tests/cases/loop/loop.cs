using System;
namespace Cases { public static class Loop {
    public static void Main() {
        int sum = 0;
        for (int i = 1; i <= 100; i++) sum += i;
        Console.WriteLine(sum);            // 5050
        int n = 10;
        while (n > 0) { Console.Write(n); n--; }
        Console.WriteLine();
        int m = 3;
        do { Console.Write(m); m++; } while (m < 6);
        Console.WriteLine();
        int c = 0;
        for (int i = 0; i < 10; i++) { if (i == 4) continue; c += i; if (i == 7) break; }
        Console.WriteLine(c);              // 0+1+2+3+5+6+7 = 24
    }
}}
