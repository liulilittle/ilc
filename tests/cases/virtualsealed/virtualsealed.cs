using System;
public class Shape {
    public virtual int Area() => 0;
    public virtual int Sides() => 0;
}
public class Rect : Shape {
    public int W;
    public int H;
    public Rect(int w, int h) { W = w; H = h; }
    public override int Area() => W * H;
}
public sealed class Square : Rect {
    public Square(int w) : base(w, w) { }
    public override int Area() => W * W;
    public override int Sides() => 4;
}
public sealed class Tri : Shape {
    public int B;
    public int H;
    public Tri(int b, int h) { B = b; H = h; }
    public override int Area() => (B * H) / 2;
    public override int Sides() => 3;
}
namespace Cases { public static class VirtualSealed {
    public static void Main() {
        Shape s1 = new Rect(3, 4);
        Console.WriteLine(s1.Area());                 // 12 (Rect NOT sealed: vtable dispatch)
        Square sq = new Square(5);
        Console.WriteLine(sq.Area());                 // 25 (sealed receiver: direct call)
        Console.WriteLine(sq.Sides());                // 4
        Shape s2 = new Square(6);
        Console.WriteLine(s2.Area());                 // 36 (sealed static type via Shape ref: direct call)
        Tri tr = new Tri(6, 7);
        Console.WriteLine(tr.Area());                 // 21 (sealed receiver: direct call)
        Console.WriteLine(tr.Sides());                // 3
        Rect r = new Square(4);
        Console.WriteLine(r.Area());                  // 16 (receiver tracked as Square: direct call)
        Console.WriteLine(r.Sides());                 // 4
        Shape s3 = new Shape();
        Console.WriteLine(s3.Area());                 // 0 (Shape HAS subclasses: vtable dispatch)
    }
}}
