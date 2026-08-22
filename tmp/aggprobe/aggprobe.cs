using System;
using System.Threading.Tasks;
class P { static void Main() {
    // .Result on a faulted task -> AggregateException (net48).
    try { int r = F().Result; Console.WriteLine("no-throw:" + r); }
    catch (Exception ex) { Console.WriteLine("result-type:" + ex.GetType().Name); }
    // GetAwaiter().GetResult() on a faulted task -> the ORIGINAL exception.
    try { int r = F().GetAwaiter().GetResult(); Console.WriteLine("no-throw:" + r); }
    catch (Exception ex) { Console.WriteLine("awaiter-type:" + ex.GetType().Name); }
    // AggregateException message (zh-CN host).
    try { int r = F().Result; }
    catch (Exception ex) { Console.WriteLine("msg:" + ex.Message); }
}
static async Task<int> F() {
    await Task.FromResult(1);
    throw new InvalidOperationException("boom");
}
}
