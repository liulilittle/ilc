using System;
namespace Cases { public static class Objects {
    public static void Main() {
        var p = new Point(3, 4);
        Console.WriteLine(p.X + p.Y);          // 7
        p.X = 10;
        Console.WriteLine(p.X);                // 10
        var q = new PointRef();
        q.ref1 = new Point(1, 2);
        Console.WriteLine(q.ref1.X);           // 1
        Console.WriteLine(q.ref1.X + q.ref1.Y); // 3
        var n = new Node(5);
        n.Next = new Node(6);
        Console.WriteLine(n.V + n.Next.V);     // 11
    }
}}
public class Point { public int X; public int Y; public Point(int x, int y) { X = x; Y = y; } }
public class PointRef { public Point ref1; public PointRef() { } }
public class Node { public int V; public Node Next; public Node(int v) { V = v; } }
