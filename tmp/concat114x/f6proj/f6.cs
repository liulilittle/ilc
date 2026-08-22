using System;
using System.Text;
namespace Cases { public static class InsertIsolateCase {
    public static void Main() {
        // Isolate: StringBuilder.Insert(int, int) numeric overload
        StringBuilder sb = new StringBuilder();
        sb.Insert(0, 42);
        Console.WriteLine(sb.Length);
        // Insert(int, long)
        StringBuilder sb2 = new StringBuilder();
        sb2.Insert(0, 42L);
        Console.WriteLine(sb2.Length);
        // Insert(int, bool)
        StringBuilder sb3 = new StringBuilder();
        sb3.Insert(0, true);
        Console.WriteLine(sb3.Length);
        // Insert(int, double)
        StringBuilder sb4 = new StringBuilder();
        sb4.Insert(0, 3.5);
        Console.WriteLine(sb4.Length);
        // Insert(int, char)
        StringBuilder sb5 = new StringBuilder();
        sb5.Insert(0, 'z');
        Console.WriteLine(sb5.Length);
        // Insert(int, string) -- the wired form (control)
        StringBuilder sb6 = new StringBuilder();
        sb6.Insert(0, "str");
        Console.WriteLine(sb6.Length);
    }
}}
