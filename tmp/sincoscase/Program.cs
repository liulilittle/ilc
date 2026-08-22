using System;
class Program {
    static void Main() {
        var r = Math.SinCos(0.0);
        Console.WriteLine("sin0=" + r.Item1 + " cos0=" + r.Item2);
        var r2 = Math.SinCos(Math.PI / 2.0);
        Console.WriteLine("sinp2=" + r2.Item1 + " cosp2=" + r2.Item2);
        var rf = MathF.SinCos(0.0f);
        Console.WriteLine("fsin0=" + rf.Item1 + " fcos0=" + rf.Item2);
    }
}
