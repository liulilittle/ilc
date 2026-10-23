using System; using System.Globalization;
namespace Cases { public static class DParse2 {
  static void W(string k, string v){ Console.WriteLine(k+"=["+v+"]"); }
  public static void Main(){
    var inv = CultureInfo.InvariantCulture;
    try { decimal.Parse("abc", inv); W("e1","nothrow"); }
    catch(FormatException) { W("e1","FormatException"); }
    try { decimal.Parse("", inv); W("e2","nothrow"); }
    catch(FormatException) { W("e2","FormatException"); }
    try { decimal.Parse((string)null, inv); W("e3","nothrow"); }
    catch(ArgumentNullException) { W("e3","ArgumentNullException"); }
    try { decimal.Parse("1e40", NumberStyles.Float, inv); W("e4","nothrow"); }
    catch(OverflowException) { W("e4","OverflowException"); }
  } } }