using System;
namespace Cases { public static class Arrays {
    public static void Main() {
        int[] a = new int[5];
        for (int i = 0; i < 5; i++) a[i] = i * i;
        Console.WriteLine(a[0] + a[4]);       // 16
        Console.WriteLine(a.Length);          // 5
        int[] b = new int[5];
        b[0] = 3; b[1] = 1; b[2] = 4; b[3] = 1; b[4] = 5;
        Console.WriteLine(b[2] + b[4]);       // 9
        int[] c = new int[0];
        Console.WriteLine(c.Length);          // 0
        int[] d = new int[3];
        d[1] = 7;
        Console.WriteLine(d[0] + d[1] + d[2]); // 7
        int sum = 0;
        for (int i = 0; i < b.Length; i++) sum += b[i];
        Console.WriteLine(sum);               // 14
        // Reference-element arrays (audit C4): allocation must use element
        // size sizeof(void*) and the payload must be scanned as references.
        string[] names = new string[2];
        names[0] = "hello";
        names[1] = "world";
        Console.WriteLine(names[0]);          // hello
        Console.WriteLine(names[1]);          // world
        Console.WriteLine(names.Length);      // 2
        object[] objs = new object[1];
        objs[0] = 42;                         // box int32 -> stelem_ref
        Console.WriteLine((int)objs[0]);      // 42 (unbox back)
    }
}}
