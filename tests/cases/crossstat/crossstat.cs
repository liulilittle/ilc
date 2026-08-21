using System;
namespace Cases {
    public static class Holder {
        public static int value;
        public static int scale;
    }
    public static class CrossStat {
        public static void Main() {
            Holder.value = 7;
            Holder.scale = 3;
            Console.WriteLine(Holder.value + Holder.scale);  // 10
            Holder.value = Holder.value * Holder.scale;      // 21
            Console.WriteLine(Holder.value);                 // 21
        }
    }
}
