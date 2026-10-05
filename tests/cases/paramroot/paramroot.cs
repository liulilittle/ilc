using System;

public class Node
{
    public int V;
    public Node(int v) { V = v; }
}

namespace Cases
{
    public static class ParamRoot
    {
        public static void Main()
        {
            // Audit C1: reference-typed parameters are GC roots. An object
            // reachable ONLY through the parameter (no locals/fields) must
            // survive the collection pressure below; before the fix the
            // parameter was not registered and the object was collected --
            // reading n.V then returned garbage or crashed.
            Node n = new Node(42);
            Console.WriteLine(KeepAlive(n));       // 42
            Console.WriteLine(KeepAlive(null));    // -1 (null path)
            Node n2 = new Node(7);
            Console.WriteLine(KeepAlive(n2));      // 7
        }

        static int KeepAlive(Node n)
        {
            if (n == null) return -1;
            // Allocation pressure: any collection here must keep 'n' alive
            // through the PARAMETER root only.
            for (int i = 0; i < 5000; i++) { Node t = new Node(i); }
            return n.V;
        }
    }
}
