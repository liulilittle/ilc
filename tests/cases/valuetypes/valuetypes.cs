using System;
public struct Point {
    public int X; public int Y;
    public Point(int x, int y) { X = x; Y = y; }
}
namespace Cases { public static class ValueTypes {
    public static void Main() {
        Point p = new Point(3, 4);
        Console.WriteLine(p.X + p.Y);      // 7
        p.X = 10;
        Console.WriteLine(p.X);            // 10
        Point q = p;                       // value copy
        q.X = 100;
        Console.WriteLine(p.X + q.X);      // 110
        Point r = new Point(p.Y, q.Y);
        Console.WriteLine(r.X + r.Y);      // 4 + 4 = 8
    }
}}
