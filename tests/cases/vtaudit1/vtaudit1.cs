using System;

namespace Cases { public struct SNo { public int X; public SNo(int x){X=x;} }
public struct SOver { public int X; public SOver(int x){X=x;}
  public override string ToString(){ return "SOver(" + X + ")"; }
  public override int GetHashCode(){ return X * 7 + 1; }
  public override bool Equals(object o){ return o is SOver && ((SOver)o).X == X; } }
public class CPlain { public int X; public CPlain(int x){X=x;} }
public static class VtAudit1 {
  public static void Main() {
    var s0 = (new SNo(5), 3);
    Console.WriteLine("sno.to=" + s0.ToString());
    Console.WriteLine("sno.hash=" + s0.GetHashCode());
    var s1 = (new SOver(5), 3);
    Console.WriteLine("sover.to=" + s1.ToString());
    Console.WriteLine("sover.hash=" + s1.GetHashCode());
    var c1 = (new CPlain(9), 3);
    Console.WriteLine("cplain.to=" + c1.ToString());
    var cn = ((CPlain)null, 3);
    Console.WriteLine("cnull.to=" + cn.ToString());
    var m = (1.5m, DateTime.MinValue, Guid.Empty, (int?)null);
    Console.WriteLine("misc.to=" + m.ToString());
    var d2 = (1.5m, 2.5m);
    Console.WriteLine("dec2.to=" + d2.ToString());
    var dt2 = (new DateTime(2020,1,2,3,4,5), 1);
    Console.WriteLine("dt2.to=" + dt2.ToString());
    var g2 = (new Guid("00112233-4455-6677-8899-aabbccddeeff"), 1);
    Console.WriteLine("g2.to=" + g2.ToString());
    var n2 = ((int?)5, 1);
    Console.WriteLine("n2.to=" + n2.ToString());
    var n3 = ((int?)null, 1);
    Console.WriteLine("n3.to=" + n3.ToString());
    var f2 = (1.5f, 2.5f);
    Console.WriteLine("f2.to=" + f2.ToString());
    Console.WriteLine("f2.hash=" + f2.GetHashCode());
    Console.WriteLine("dec2.hash=" + d2.GetHashCode());
    Console.WriteLine("dt2.hash=" + dt2.GetHashCode());
    Console.WriteLine("g2.hash=" + g2.GetHashCode());
    Console.WriteLine("n2.hash=" + n2.GetHashCode());
    Console.WriteLine("n3.hash=" + n3.GetHashCode());
  }
}}