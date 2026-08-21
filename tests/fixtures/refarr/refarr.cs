using System;

public class Node
{
    public int V;
    public Node Next;
    public Node(int v) { V = v; }
}

namespace AuditFix
{
    public static class RefArr
    {
        public static void Main()
        {
            // Reference-element arrays (audit C4): the array must be allocated
            // with elemSize = sizeof(void*) and scanned as references.
            string[] names = new string[2];
            names[0] = "hello";
            names[1] = "world";
            Console.WriteLine(names[0]);           // hello
            Console.WriteLine(names[1]);           // world
            Console.WriteLine(names.Length);       // 2
            int[] xs = new int[3];
            xs[1] = 42;
            Console.WriteLine(xs[0] + xs[1] + xs[2]); // 42 (int arrays keep elemSize 4)
            object[] objs = new object[1];
            objs[0] = 42;                          // box int32 -> stelem_ref
            Console.WriteLine((int)objs[0]);       // 42 (unbox)
            // A method whose parameter is the ONLY reference to an object
            // (audit C1): the parameter must be a GC frame root.
            Node n = new Node(42);
            Console.WriteLine(ReadViaParam(n));    // 42
            Console.WriteLine(ReadViaParam(null)); // -1
        }

        static int ReadViaParam(Node n)
        {
            if (n == null) return -1;
            // Allocation pressure: any collection here must keep 'n' alive
            // through the PARAMETER root (no locals/fields reference it).
            for (int i = 0; i < 5000; i++) { Node t = new Node(i); }
            return n.V;
        }
    }
}
