using System;
namespace Overloads {
    public static class Program {
        public static void Main() {
            Console.WriteLine(Add(1, 2));        // 3
            Console.WriteLine(Add(1, 2, 3));     // 6
        }
        static int Add(int a, int b) => a + b;
        static int Add(int a, int b, int c) => a + b + c;
    }
}
