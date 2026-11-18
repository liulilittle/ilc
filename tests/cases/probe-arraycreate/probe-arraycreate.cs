using System;

struct Pt { public int X; public int Y; public int Z; }

enum Color { Red, Green, Blue }
enum Big : long { A = 1, B = 2 }

class Program
{
    static void Main()
    {
        Array a1 = Array.CreateInstance(typeof(int), 3);
        Console.WriteLine("1d:" + a1.Length + ":" + a1.Rank + ":" + Buffer.ByteLength(a1));

        Array a2 = Array.CreateInstance(typeof(int), 2, 3);
        Console.WriteLine("2d:" + a2.Length + ":" + a2.Rank + ":" + a2.GetLength(0) + ":" + a2.GetLength(1) + ":" + Buffer.ByteLength(a2));

        Array a3 = Array.CreateInstance(typeof(int), 2, 3, 4);
        Console.WriteLine("3d:" + a3.Length + ":" + a3.Rank + ":" + Buffer.ByteLength(a3));

        Array a4 = Array.CreateInstance(typeof(int), new int[] { 2, 3 });
        Console.WriteLine("dims:" + a4.Length + ":" + a4.Rank + ":" + Buffer.ByteLength(a4));

        Array a5 = Array.CreateInstance(typeof(string), 4);
        Console.WriteLine("ref:" + a5.Length + ":" + a5.GetType().Name);

        Array a6 = Array.CreateInstance(typeof(int), new long[] { 2, 3 });
        Console.WriteLine("longdims:" + a6.Length + ":" + a6.Rank + ":" + Buffer.ByteLength(a6));

        Array a7 = Array.CreateInstance(typeof(long), 3);
        a7.SetValue(9000000000L, 0);
        a7.SetValue(7000000000L, 1);
        a7.SetValue(5000000000L, 2);
        Console.WriteLine("i8:" + a7.Length + ":" + a7.GetValue(0) + ":" + a7.GetValue(1) + ":" + a7.GetValue(2) + ":" + Buffer.ByteLength(a7));

        Array a8 = Array.CreateInstance(typeof(double), 2);
        a8.SetValue(1.5, 0);
        a8.SetValue(2.5, 1);
        Console.WriteLine("f8:" + a8.Length + ":" + a8.GetValue(0) + ":" + a8.GetValue(1) + ":" + Buffer.ByteLength(a8));

        Array a9 = Array.CreateInstance(typeof(Pt), 2);
        Console.WriteLine("struct:" + a9.Length + ":" + a9.GetType().Name);

        // An enum element strides by its UNDERLYING primitive, not by the
        // enum descriptor instanceSize (which is the box size, 16).
        Array e1 = Array.CreateInstance(typeof(Color), 3);
        Console.WriteLine("enum:" + e1.Length + ":" + e1.GetType().Name + ":" + Buffer.ByteLength(e1));

        Array e2 = Array.CreateInstance(typeof(Big), 2);
        Console.WriteLine("enum8:" + e2.Length + ":" + e2.GetType().Name + ":" + Buffer.ByteLength(e2));

        // The narrow primitives: a wrong stride shows up here too.
        Array e3 = Array.CreateInstance(typeof(byte), 5);
        Console.WriteLine("u1:" + e3.Length + ":" + Buffer.ByteLength(e3));

        Array e4 = Array.CreateInstance(typeof(short), 4);
        Console.WriteLine("i2:" + e4.Length + ":" + Buffer.ByteLength(e4));

        Array e5 = Array.CreateInstance(typeof(bool), 3);
        Console.WriteLine("bool:" + e5.Length + ":" + Buffer.ByteLength(e5));

        Array e6 = Array.CreateInstance(typeof(char), 3);
        Console.WriteLine("char:" + e6.Length + ":" + Buffer.ByteLength(e6));

        Array e7 = Array.CreateInstance(typeof(float), 2);
        Console.WriteLine("f4:" + e7.Length + ":" + Buffer.ByteLength(e7));
    }
}
