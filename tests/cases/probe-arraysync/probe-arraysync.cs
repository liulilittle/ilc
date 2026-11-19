using System;

class Program
{
    static void Main()
    {
        // Array.get_SyncRoot: net48 returns an object used to synchronize
        // access. For an array it is the array itself.
        int[] a = { 1, 2, 3 };
        object root = a.SyncRoot;
        Console.WriteLine("root:" + (root != null));
        Console.WriteLine("self:" + ReferenceEquals(root, a));

        // The root must be stable across calls.
        Console.WriteLine("stable:" + ReferenceEquals(a.SyncRoot, a.SyncRoot));

        // A null array must throw NullReferenceException.
        int[] n = null;
        try
        {
            object r = n.SyncRoot;
            Console.WriteLine("null:no");
        }
        catch (NullReferenceException)
        {
            Console.WriteLine("null:ok");
        }
    }
}
