using System;
using System.Threading.Tasks;
class P { static void Main() {
    Task<int> t = T();
    Console.WriteLine(t.IsCompleted);
    try { int r = t.Result; Console.WriteLine("no-throw"); }
    catch (Exception) { Console.WriteLine("caught-ex"); }
    try { int r = T2().Result; Console.WriteLine("no-throw"); }
    catch (Exception) { Console.WriteLine("caught-ex2"); }
}
static async Task<int> T() {
    int a = 1;
    a = await Task.FromResult(a + 1);
    if (a == 2) throw new Exception("async boom");
    return a;
}
static async Task<int> T2() {
    throw new Exception("immediate boom");
}
}
