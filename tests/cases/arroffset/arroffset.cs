using System;
namespace Cases { public static class ArrOffsetCase {
    public static void Main() {
        // The offset-index fast path (batch 159): a[j+1] with a fresh
        // constant-length array must keep EXACT bounds semantics.
        int[] a = new int[12000];
        for (int i = 0; i < 12000; i++) a[i] = i;
        // Boundary reads/writes through j+1 / j-1 offsets.
        int j = 11998;
        a[j + 1] = a[j] + 1;                       // j+1 = 11999: valid
        Console.WriteLine(a[11999] == 11999);
        j = 0;
        try { int x = a[j - 1]; Console.WriteLine("no-throw:" + x); }
        catch (IndexOutOfRangeException) { Console.WriteLine("oob-1"); }
        j = 11999;
        try { int x = a[j + 1]; Console.WriteLine("no-throw:" + x); }
        catch (IndexOutOfRangeException) { Console.WriteLine("oob+1"); }
        j = 0;
        try { a[j - 1] = 5; Console.WriteLine("no-throw-store"); }
        catch (IndexOutOfRangeException) { Console.WriteLine("oob-store"); }
        // Sum through an offset index (the sort shift shape).
        int sum = 0;
        for (int i = 1; i < 12000; i++) sum += a[i - 1] + a[i];
        Console.WriteLine(sum > 0);
    }
}}
