using System;
using System.Threading.Tasks;
class P { static void Main() {
    Console.WriteLine(B().Result);
    Console.WriteLine(C().Result);
    Console.WriteLine(C().Result == 'x');
}
static async Task<bool> B() {
    bool b = true;
    b = await Task.FromResult(!b);
    return b;
}
static async Task<char> C() {
    char c = 'a';
    c = await Task.FromResult((char)(c + 23));
    return c;
}
}
