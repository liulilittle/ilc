using System;
using System.Threading.Tasks;
namespace Cases { public static class DelayProbe2 {
    public static void Main() {
        Console.WriteLine(Task.Delay(0).IsCompleted);
        Console.WriteLine(Task.Delay(5).IsCompleted);
    }
}}
