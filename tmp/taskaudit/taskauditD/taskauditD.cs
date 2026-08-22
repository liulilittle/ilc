using System;
using System.Threading.Tasks;
namespace Cases.TaskAudit {
    public static class TaskAuditD {
        public static void Main() {
            Point p = F().Result;
            Console.WriteLine(p.X);
            Console.WriteLine(p.GetType().Name);
        }
        static async Task<Point> F() {
            Point v = new Point(3, 4);
            v = await Task.FromResult(v);
            return v;
        }
    }
    public class Point {
        public int X;
        public int Y;
        public Point(int x, int y) { X = x; Y = y; }
    }
}
