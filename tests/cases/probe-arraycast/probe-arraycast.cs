using System;

class Program
{
    static void Main()
    {
        // Each element type gets its own array class; the cast must hold
        // regardless of whether an array of that type was allocated first.
        object i8 = new long[] { 9000000000L, 7000000000L };
        long[] a8 = (long[])i8;
        Console.WriteLine("i8:" + a8.Length + ":" + a8[0]);

        object f8 = new double[] { 1.5, 2.5 };
        double[] af8 = (double[])f8;
        Console.WriteLine("f8:" + af8.Length + ":" + af8[0]);

        object r = new string[] { "x", "y" };
        string[] ar = (string[])r;
        Console.WriteLine("ref:" + ar.Length + ":" + ar[0]);

        object i4 = new int[] { 1, 2, 3 };
        int[] ai4 = (int[])i4;
        Console.WriteLine("i4:" + ai4.Length + ":" + ai4[1]);

        object i2 = new short[] { 7, 8 };
        short[] ai2 = (short[])i2;
        Console.WriteLine("i2:" + ai2.Length + ":" + ai2[0]);

        object u1 = new byte[] { 5, 6, 7 };
        byte[] au1 = (byte[])u1;
        Console.WriteLine("u1:" + au1.Length + ":" + au1[2]);

        // A failed cast must throw, not silently produce a wrong array.
        object wrong = new int[] { 1 };
        try
        {
            long[] bad = (long[])wrong;
            Console.WriteLine("bad:" + bad.Length);
        }
        catch (InvalidCastException)
        {
            Console.WriteLine("badthrow:ok");
        }
    }
}
