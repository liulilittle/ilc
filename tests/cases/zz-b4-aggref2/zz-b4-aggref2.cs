using System;
using System.Linq;
namespace Cases {
  public static class ZZB4Aggref2 {
    public static void Main(){
      string[] d = { "a", "b" };
      Console.Error.WriteLine("m0");
      try { var r = d.Aggregate("z", (acc, x) => acc); Console.Error.WriteLine("id=" + r); } catch (Exception e) { Console.Error.WriteLine("id=" + e.GetType().Name); }
      Console.Error.WriteLine("m1");
      try { var r = d.Aggregate("z", (acc, x) => x); Console.Error.WriteLine("x=" + r); } catch (Exception e) { Console.Error.WriteLine("x=" + e.GetType().Name); }
      Console.Error.WriteLine("m2");
      try { var r = d.Aggregate("z", (acc, x) => acc + "!"); Console.Error.WriteLine("cat=" + r); } catch (Exception e) { Console.Error.WriteLine("cat=" + e.GetType().Name); }
      Console.Error.WriteLine("m3");
      try { var r = d.Aggregate("z", (acc, x) => acc + x); Console.Error.WriteLine("catx=" + r); } catch (Exception e) { Console.Error.WriteLine("catx=" + e.GetType().Name); }
      Console.Error.WriteLine("done");
    }
  }
}
