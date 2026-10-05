using System;
public class Node {
    public int V;
    public Node Next;
    public Node(int v) { V = v; }
}
namespace Cases { public static class GcStress {
    public static void Main() {
        // Allocation pressure: build a 2000-node chain (each newobj may trigger
        // collection as the heap fills), then walk it. Frame-root protocol must
        // keep 'head'/'cur' alive across the pressure allocations.
        Node head = new Node(0);
        Node cur = head;
        for (int i = 1; i < 2000; i++) {
            cur.Next = new Node(i);
            cur = cur.Next;
        }
        // Walk the chain and verify every value survived the collections.
        int sum = 0;
        cur = head;
        while (cur != null) { sum += cur.V; cur = cur.Next; }
        Console.WriteLine(sum);                      // 1999000
        Console.WriteLine(head.V);                   // 0
        Console.WriteLine(head.Next.Next.V);         // 2
        // Second churn: allocate and discard many objects, keep a rooted value.
        Node keep = new Node(777);
        for (int i = 0; i < 5000; i++) {
            Node temp = new Node(i);
            temp.Next = keep;
        }
        Console.WriteLine(keep.V);                   // 777
        Console.WriteLine(keep.Next == null ? 1 : 0);// 1
    }
}}
