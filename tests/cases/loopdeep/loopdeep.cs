using System;
namespace Cases { public static class LoopDeep {
    public static void Main() {
        int s = 0, i = 1;
        while (i <= 100) { s += i; i++; }
        Console.WriteLine(s == 5050);        // while accumulating 1..100

        int j = 0;
        while (j < 0) j++;
        Console.WriteLine(j == 0);           // while boundary: condition false on first probe, body never runs

        int d = 0, k = 5;
        do { d += k; } while (k < 5);
        Console.WriteLine(d == 5);           // do-while runs at least once (condition false on first probe still runs)

        int t = 0;
        for (int a = 1; a <= 3; a++)
            for (int b = 1; b <= 3; b++)
                t += a * b;
        Console.WriteLine(t == 36);          // 2-level nested for: (1+2+3)*(1+2+3)

        int bsum = 0;
        for (int x = 0; x < 10; x++) { if (x == 4) break; bsum += x; }
        Console.WriteLine(bsum == 6);        // break: 0+1+2+3

        int csum = 0;
        for (int x = 0; x < 10; x++) { if (x % 2 == 0) continue; csum += x; }
        Console.WriteLine(csum == 25);       // continue: 1+3+5+7+9

        int cnt = 0, m = 0;
        while (m < 20) {
            m++;
            if (m % 3 == 0) continue;
            if (m > 15) break;
            cnt++;
        }
        Console.WriteLine(cnt == 10);        // mixed continue+break

        int se = 0, so = 0;
        for (int n = 1; n <= 20; n++) { if (n % 2 == 0) se += n; else so += n; }
        Console.WriteLine(se == 110);        // in-loop condition: evens 2+..+20
        Console.WriteLine(so == 100);        // odds 1+..+19

        int hits = 0, p = 0;
        while (p < 4) {
            int q = 0;
            while (q < 4) { if (p + q >= 4) hits++; q++; }
            p++;
        }
        Console.WriteLine(hits == 6);        // double while + in-loop condition

        int total = 0, row = 0;
        do {
            for (int col = 0; col < 5; col++) {
                if (col == 2) break;
                total += row + col;
            }
            row++;
        } while (row < 3);
        Console.WriteLine(total == 9);       // do-while around for, inner break only exits inner

        int[] arr = { 3, 1, 4, 1, 5, 9, 2, 6 };
        int asum = 0;
        for (int x = 0; x < arr.Length; x++) asum += arr[x];
        Console.WriteLine(asum == 31);       // array loop accumulation (bounds-check surface)

        int[] g = new int[4];
        for (int x = 0; x < g.Length; x++) g[x] = x * x;
        Console.WriteLine(g[0] == 0 && g[1] == 1 && g[2] == 4 && g[3] == 9);  // int[] write-back and read
    }
}}
