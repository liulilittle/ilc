using System;
using System.Runtime.InteropServices;
namespace Cases { public static class Pinvoke {
    [DllImport("mylib", EntryPoint = "add_ints")]
    public static extern int add_ints(int a, int b);
    [DllImport("mylib", EntryPoint = "strlen_wrap", CharSet = CharSet.Ansi)]
    public static extern int strlen_wrap(string s);
    [DllImport("mylib", EntryPoint = "fail_with_error", SetLastError = true)]
    public static extern int fail_with_error(int code);
    [DllImport("mylib", EntryPoint = "concat_len", CharSet = CharSet.Ansi)]
    public static extern int concat_len(string a, string b);
    public static void Main() {
        Console.WriteLine(add_ints(20, 22));       // 42
        Console.WriteLine(strlen_wrap("hello"));   // 5
        Console.WriteLine(strlen_wrap(""));        // 0
        Console.WriteLine(strlen_wrap("abc"));     // 3
        int r = fail_with_error(123);
        Console.WriteLine(r);                      // -1
        Console.WriteLine(Marshal.GetLastWin32Error()); // 123
        // audit H5: two string parameters must each get their OWN marshal
        // buffer (the runtime's rotating static buffer would alias them --
        // both pointers would end up naming the same buffer with the last
        // string's content: 10 or 2 instead of 6 here).
        Console.WriteLine(concat_len("hello", "x"));  // 6
        Console.WriteLine(concat_len("", "abcdef"));  // 6
    }
}}
