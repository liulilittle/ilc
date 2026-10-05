//------------------------------------------------------------------------------
// arraybounds.cs -- end-to-end case for array bounds checks mixed with
// managed exceptions (net48 reference run).
//
// Sections, in order:
//   1. normal in-range access (first/last element, Length);
//   2. positive out-of-range index -> IndexOutOfRangeException;
//   3. negative index (a variable: a constant -1 would emit CS0251);
//   4. multi-dimensional arrays -- SKIPPED: the transpiler rejects them
//      (MethodBodyReader rejects multidim locals; the multidim newobj ctor
//      is unwired), so "simple operations (if supported)" does not apply;
//   5. Array.Copy out-of-bounds: dst too short / src too short
//      (ArgumentException) and negative index (ArgumentOutOfRangeException)
//      -- the runtime arrayext domain throws catchable typed exceptions;
//   6. foreach over int[] (the C# compiler lowers it to a Length + indexer
//      loop, no GetEnumerator wiring needed).
//
// Every assertion prints a bool (True when the exception was caught);
// every line of output is deterministic.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class ArrayBounds {
    public static void Main() {
        // --- 1. normal in-range access ---
        int[] a = new int[5];
        for (int i = 0; i < 5; i++) a[i] = (i + 1) * 10;
        Console.WriteLine("boundary");
        Console.WriteLine(a[0]);      // 10
        Console.WriteLine(a[4]);      // 50
        Console.WriteLine(a.Length);  // 5

        // --- 2. positive out-of-range index ---
        int big = 7;
        try { int v = a[big]; Console.WriteLine(v); }
        catch (IndexOutOfRangeException) { Console.WriteLine("oob+"); Console.WriteLine(true); }

        // --- 3. negative index ---
        int neg = -1;
        try { int v = a[neg]; Console.WriteLine(v); }
        catch (IndexOutOfRangeException) { Console.WriteLine("oob-"); Console.WriteLine(true); }

        // --- 4. multi-dimensional: not supported by the transpiler, skipped ---

        // --- 5. Array.Copy out-of-bounds ---
        // NOTE: no `{ 1, 2, 3 }` array-initializer shorthand here -- the C#
        // compiler lowers constant initializers to ldtoken +
        // RuntimeHelpers.InitializeArray, which the transpiler does not fold.
        int[] src = new int[3];
        src[0] = 1; src[1] = 2; src[2] = 3;
        int[] dst = new int[3];
        try { Array.Copy(src, 1, dst, 0, 5); Console.WriteLine("copy dst"); Console.WriteLine(false); }
        catch (ArgumentException) { Console.WriteLine("copy dst"); Console.WriteLine(true); }
        try { Array.Copy(src, 2, dst, 0, 3); Console.WriteLine("copy src"); Console.WriteLine(false); }
        catch (ArgumentException) { Console.WriteLine("copy src"); Console.WriteLine(true); }
        try { Array.Copy(src, -1, dst, 0, 2); Console.WriteLine("copy neg"); Console.WriteLine(false); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine("copy neg"); Console.WriteLine(true); }

        // --- 6. foreach full traversal ---
        int sum = 0;
        foreach (int v in a) sum += v;
        Console.WriteLine("foreach sum");
        Console.WriteLine(sum);       // 150
    }
}}
