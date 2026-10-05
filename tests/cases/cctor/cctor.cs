using System;
namespace Cases {
    // beforefieldinit type (implicit .cctor from field initializers)
    public static class Gadget {
        public static int Base = 10;
        public static int Multiplier = 3;
    }
    // explicit static constructor (non-beforefieldinit)
    public class Widget {
        public static int Count;
        public int Id;
        static Widget() { Count = 42; }
        public Widget(int id) { Id = id; }
    }
    public static class Cctor {
        public static void Main() {
            Console.WriteLine(Gadget.Base);           // 10  (lazy .cctor on static-field access)
            Console.WriteLine(Gadget.Base * Gadget.Multiplier); // 30
            var w = new Widget(5);                    // Widget .cctor runs on newobj
            Console.WriteLine(Widget.Count);          // 42
            Console.WriteLine(w.Id);                  // 5
        }
    }
}
