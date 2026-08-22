using System;
using System.Threading.Tasks;
namespace Cases { public static class DelayProbe {
    public static void Main() {
        Console.WriteLine(Task.Delay(0).IsCompleted);
        Console.WriteLine(Task.Run(() => 42).Result);
    }
}}
