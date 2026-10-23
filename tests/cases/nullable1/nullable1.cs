//------------------------------------------------------------------------------
// nullable1.cs -- Nullable<int> member surface (single topic: int?).
// Covers the boxed-object Nullable representation:
//   * ctor (both `int? nv = 5` and `new int?(7)`) -> boxed int
//   * get_HasValue / get_Value (has-value + no-value paths)
//   * ToString()  -- net48: "" when no value (NOT null, NOT "null")
//   * GetValueOrDefault() / GetValueOrDefault(T)
//   * Value on a no-value instance -> InvalidOperationException
//------------------------------------------------------------------------------

using System;

class Nullable1
{
    static void Main()
    {
        // ---- 1. implicit ctor: int? nv = 5 ----
        int? nv = 5;
        Console.WriteLine("has true: " + nv.HasValue);
        Console.WriteLine("value: " + nv.Value);
        Console.WriteLine("tostring: " + nv.ToString());
        Console.WriteLine("gvod: " + nv.GetValueOrDefault());

        // ---- 2. explicit ctor: new int?(7) ----
        int? nn = new int?(7);
        Console.WriteLine("new ctor has: " + nn.HasValue);
        Console.WriteLine("new ctor value: " + nn.Value);

        // ---- 3. no-value path ----
        int? nz = null;
        Console.WriteLine("has false: " + nz.HasValue);
        Console.WriteLine("tostring empty: [" + nz.ToString() + "]");
        Console.WriteLine("tostring len: " + nz.ToString().Length);
        Console.WriteLine("gvod default: " + nz.GetValueOrDefault());
        Console.WriteLine("gvod arg: " + nz.GetValueOrDefault(42));

        // ---- 4. Value on no-value -> InvalidOperationException ----
        try
        {
            int x = nz.Value;
            Console.WriteLine("no throw: " + x);
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine("throws: " + e.GetType().FullName);
        }
    }
}
