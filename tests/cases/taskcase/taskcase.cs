using System;
using System.Threading.Tasks;
namespace Cases { public static class TaskCase {
    public static void Main() {
        Task<int> t = Task.FromResult(42);
        Console.WriteLine(t.Result);
        Console.WriteLine(t.IsCompleted);
        Task<int> t2 = t;
        Console.WriteLine(t == t2);
        Task<int> t3 = Task.FromResult(7);
        Console.WriteLine(t == t3);
    }
}}
