using System;
using System.Threading.Tasks;
class P { static void Main() {
    Console.WriteLine(D().Result);
    Console.WriteLine(D().Result == 3.5);
    Console.WriteLine(S().Result);
    Console.WriteLine(S().Result == "hello");
}
static async Task<double> D() {
    double d = 1.5;
    d = await Task.FromResult(d + 2.0);
    return d;
}
static async Task<string> S() {
    string s = "hel";
    s = await Task.FromResult(s + "lo");
    return s;
}
}
