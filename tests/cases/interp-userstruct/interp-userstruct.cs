using System;

//------------------------------------------------------------------------------
// interp-userstruct -- batch 7: DefaultInterpolatedStringHandler.AppendFormatted<T>
// over a USER value type (a struct or enum TypeDef in the input assembly).
//
//   * A user struct WITHOUT a ToString override: net48 calls ValueType.ToString
//     -> the type's full name; the format specifier is ignored (the struct is
//     not IFormattable).
//   * A user struct WITH a ToString override: net48 dispatches the override.
//   * A user enum: net48 routes the box through IFormattable.ToString(format),
//     so "G" -> the member name, "D" -> the decimal value, "X" -> hex.
//
// Byte-exact against the net48 oracle.
//------------------------------------------------------------------------------
namespace Cases
{
    public struct Point { public int X; public int Y; }

    public struct Pair
    {
        public int A;
        public int B;
        public override string ToString() { return "P" + A + "," + B; }
    }

    public enum Color { Red = 1, Green = 2, Blue = 4 }

    public static class InterpUserStruct
    {
        public static void Main()
        {
            Point pt = new Point { X = 3, Y = 4 };
            Console.WriteLine($"pt=[{pt}]");
            Console.WriteLine($"pt2=[{pt,8}]");
            Console.WriteLine($"pt3=[{pt,8:X}]");

            Pair pr = new Pair { A = 1, B = 2 };
            Console.WriteLine($"pr=[{pr}]");
            Console.WriteLine($"pr2=[{pr,8}]");

            Color c = Color.Green;
            Console.WriteLine($"c=[{c}]");
            Console.WriteLine($"c2=[{c,8}]");
            Console.WriteLine($"c3=[{c,8:D}]");
            Console.WriteLine($"c4=[{c:D}]");
            Console.WriteLine($"c5=[{c:X}]");
        }
    }
}
