using System;
namespace Multidim {
    public static class Program {
        public static void Main() {
            var m = new int[2, 3];
            Console.WriteLine(Probe(m));
        }
        static int Probe(int[,] m) => m[1, 2];
        static int Sum(int[] a) => a.Length;
        static int[,] Make(string name) => new int[2, 3];
    }
}
