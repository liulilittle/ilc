using System;
using System.Text;

//------------------------------------------------------------------------------
// interp-reftype -- DefaultInterpolatedStringHandler.AppendFormatted<T> over a
// REFERENCE generic argument (class / object / array / generic instantiation)
// plus String.GetHashCode(StringComparison) (the net core 2.0+ overload).
//
// The reference forms ride the Append(object) ToString dispatch (the value
// slot is already an Il2CppObject*); the alignment/format trios are the
// boxed-object _w/_f/_wf entries.
//------------------------------------------------------------------------------
namespace Cases
{
    public sealed class Widget
    {
        public override string ToString() => "Widget";
    }

    public sealed class Box<T>
    {
        public override string ToString() => "Box";
    }

    public static class InterpRefType
    {
        public static void Main()
        {
            object o = "obj";
            string s = "str";
            Widget w = new Widget();
            StringBuilder sb = new StringBuilder("sb-content");
            Box<int> box = new Box<int>();

            Console.WriteLine($"o=[{o}]");
            Console.WriteLine($"s=[{s}]");
            Console.WriteLine($"w=[{w}]");
            Console.WriteLine($"sb=[{sb}]");
            Console.WriteLine($"box=[{box}]");
            Console.WriteLine($"o.w=[{o,7}]");
            Console.WriteLine($"s.w=[{s,7}]");
            Console.WriteLine($"w.w=[{w,9}]");
            Console.WriteLine($"s.f=[{s,7:X}]");

            Console.WriteLine("hash.ord.self=" + ("Hello".GetHashCode(StringComparison.Ordinal) == "Hello".GetHashCode(StringComparison.Ordinal)));
            Console.WriteLine("hash.ic.eq=" + ("Hello".GetHashCode(StringComparison.OrdinalIgnoreCase) == "hello".GetHashCode(StringComparison.OrdinalIgnoreCase)));
            Console.WriteLine("hash.ord.ne=" + ("Hello".GetHashCode(StringComparison.Ordinal) != "hello".GetHashCode(StringComparison.Ordinal)));
        }
    }
}

