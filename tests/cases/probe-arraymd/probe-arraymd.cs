using System;

class Program
{
    static void Main()
    {
        // A multi-dimensional array cast: (int[,])o. The TypeSpec is
        // ELEMENT_TYPE_ARRAY (0x14), not SZARRAY (0x1D).
        object o = new int[2, 3];
        int[,] m = (int[,])o;
        Console.WriteLine("md:" + m.GetLength(0) + ":" + m.GetLength(1));

        // `as` form on a matching and a non-matching operand.
        int[,] m2 = o as int[,];
        Console.WriteLine("as:" + (m2 != null));

        object notMd = new int[] { 1, 2 };
        Console.WriteLine("badnull:" + (notMd as int[,] == null));

        // A failed cast must throw.
        try
        {
            int[,] bad = (int[,])notMd;
            Console.WriteLine("bad:" + bad.Length);
        }
        catch (InvalidCastException)
        {
            Console.WriteLine("badthrow:ok");
        }

        // Element access through the multidim array.
        m[1, 2] = 42;
        Console.WriteLine("elem:" + m[1, 2] + ":" + m.Length);

        // RANK IS PART OF THE IDENTITY: net48 keeps int[] and int[,] distinct,
        // so each must REJECT the other. Without this the two ranks could share
        // one descriptor and both casts would silently succeed.
        object mdObj = new int[2, 3];
        try
        {
            int[] wrongRank = (int[])mdObj;
            Console.WriteLine("rank1:" + wrongRank.Length);
        }
        catch (InvalidCastException)
        {
            Console.WriteLine("rank1throw:ok");
        }

        object szObj = new int[] { 1, 2, 3 };
        try
        {
            int[,] wrongRank2 = (int[,])szObj;
            Console.WriteLine("rank2:" + wrongRank2.Length);
        }
        catch (InvalidCastException)
        {
            Console.WriteLine("rank2throw:ok");
        }

        // The `as` form must answer null, not throw.
        Console.WriteLine("rank1null:" + (mdObj as int[] == null));
        Console.WriteLine("rank2null:" + (szObj as int[,] == null));

        // A 3-rank array is a third distinct identity.
        object o3 = new int[2, 2, 2];
        int[,,] m3 = (int[,,])o3;
        Console.WriteLine("md3:" + m3.Length + ":" + (o3 as int[,] == null));
    }
}
