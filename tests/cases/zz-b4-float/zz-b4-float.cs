using System;
using System.Linq;
namespace Cases {
  public static class ZZB4Float {
    public static void Main(){
      float[] fs = { 2f, 4f };
      Console.WriteLine("[f_sum]=" + fs.Sum());
      Console.WriteLine("[f_avg]=" + fs.Average());
      Console.WriteLine("[f_min]=" + fs.Min());
      Console.WriteLine("[f_max]=" + fs.Max());
      float f = 2.5f;
      Console.WriteLine("[f_print]=" + f);
      Console.WriteLine("[f_int]=" + (int)f);
      Console.WriteLine("done");
    }
  }
}
