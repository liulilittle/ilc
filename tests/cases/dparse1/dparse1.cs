using System; using System.Globalization;
namespace Cases { public static class DParse1 {
  static void W(string k, string v){ Console.WriteLine(k+"=["+v+"]"); }
  public static void Main(){
    var inv = CultureInfo.InvariantCulture;
    W("p1", decimal.Parse("1.5", inv).ToString(inv));
    W("p2", decimal.Parse("-2.25", inv).ToString(inv));
    W("p3", decimal.Parse("1,234.5", inv).ToString(inv));
    W("p4", decimal.Parse("  3.5  ", inv).ToString(inv));
    W("p5", decimal.Parse("1.50", inv).ToString(inv));
    decimal d;
    W("p6", decimal.TryParse("2.75", NumberStyles.Number, inv, out d).ToString()+","+d.ToString(inv));
    W("p7", decimal.TryParse("xx", NumberStyles.Number, inv, out d).ToString()+","+d.ToString(inv));
  } } }