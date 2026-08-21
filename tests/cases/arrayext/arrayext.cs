using System;
namespace Cases { public static class ArrayExt {
    private static void Print(int[] a) {
        for (int i = 0; i < a.Length; i++) Console.WriteLine(a[i]);
    }
    public static void Main() {
        int[] a = new int[5];
        a[0] = 3; a[1] = 1; a[2] = 4; a[3] = 1; a[4] = 5;
        Array.Sort(a);
        Console.WriteLine("sorted");
        Print(a);                              // 1 1 3 4 5
        Array.Reverse(a);
        Console.WriteLine("reversed");
        Print(a);                              // 5 4 3 1 1
        int[] src = new int[5];
        src[0] = 10; src[1] = 20; src[2] = 30; src[3] = 40; src[4] = 50;
        int[] dst = new int[5];
        Array.Copy(src, 1, dst, 0, 3);
        Console.WriteLine("copied");
        Print(dst);                            // 20 30 40 0 0
        int[] shift = new int[5];
        shift[0] = 1; shift[1] = 2; shift[2] = 3; shift[3] = 4; shift[4] = 5;
        Array.Copy(shift, 0, shift, 1, 4);
        Console.WriteLine("shifted");
        Print(shift);                          // 1 1 2 3 4
        Console.WriteLine(Array.IndexOf(a, 4)); // 1
        Console.WriteLine(Array.IndexOf(a, 99)); // -1
        Console.WriteLine(a.GetLength(0));      // 5
        Console.WriteLine(a.GetUpperBound(0));  // 4
    }
}}
