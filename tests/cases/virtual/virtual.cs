using System;
public class Shape {
    public virtual int Area() => 0;
    public virtual int Sides() => 0;
    public int BaseOnly() => 1;
}
public class Rect : Shape {
    public int W;
    public int H;
    public Rect(int w, int h) { W = w; H = h; }
    public override int Area() => W * H;
}
public class Square : Rect {
    public Square(int w) : base(w, w) { }
    public override int Area() => W * W;
    public override int Sides() => 4;
}
public class Tri : Shape {
    public int B;
    public int H;
    public Tri(int b, int h) { B = b; H = h; }
    public override int Area() => (B * H) / 2;
    public override int Sides() => 3;
}
namespace Cases { public static class Virtual {
    public static void Main() {
        Shape s1 = new Rect(3, 4);
        Console.WriteLine(s1.Area());                 // 12 (virtual dispatch)
        Shape s2 = new Square(5);
        Console.WriteLine(s2.Area());                 // 25 (Square impl)
        Console.WriteLine(s2.Sides());                // 4
        Shape s3 = new Tri(6, 7);
        Console.WriteLine(s3.Area());                 // 21
        Shape s4 = new Shape();
        Console.WriteLine(s4.Area());                 // 0 (base impl)
        Console.WriteLine(s4.Sides());                // 0
        Rect r = new Square(4);
        Console.WriteLine(r.Area());                  // 16 (Square impl via Rect ref)
        Console.WriteLine(r.Sides());                 // 4
    }
}}
