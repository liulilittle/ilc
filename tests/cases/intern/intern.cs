using System;
namespace Cases { public static class Intern {
    // Same literal in DIFFERENT methods -- .NET interning must make them the
    // SAME instance across method boundaries (not just within one method).
    static string PickA() { return "abc"; }
    static string PickB() { return "abc"; }
    static string PickC() { return "xyz"; }
    static string PickEmpty() { return ""; }
    // Reference equality: (object) cast disables string == value semantics.
    static int RefEq(string a, string b) { return (object)a == (object)b ? 1 : 0; }
    public static void Main() {
        // Literal vs literal (same content) -- net48 merges at compile time,
        // ILC interns at runtime: both give the SAME instance.
        Console.WriteLine(RefEq("abc", "abc"));     // 1
        // Literal vs literal (different content) -- different instances.
        Console.WriteLine(RefEq("abc", "xyz"));     // 0
        // Same literal returned from two methods -- interned across methods.
        Console.WriteLine(RefEq(PickA(), PickB())); // 1
        // Method result vs literal -- the same pool entry.
        Console.WriteLine(RefEq(PickA(), "abc"));   // 1
        // Different content from a method vs literal.
        Console.WriteLine(RefEq(PickC(), "abc"));   // 0
        // Method vs method, different content.
        Console.WriteLine(RefEq(PickA(), PickC())); // 0
        // The empty literal is interned exactly once.
        Console.WriteLine(RefEq(PickEmpty(), ""));  // 1
        Console.WriteLine(RefEq(PickEmpty(), "x")); // 0
        // A local holding a literal -- same instance on re-lookup.
        string s = PickB();
        Console.WriteLine(RefEq(s, PickA()));       // 1
        Console.WriteLine(RefEq(s, "abc"));         // 1
        // Content prints normally (interned strings are ordinary strings).
        Console.WriteLine(PickC());                 // xyz
    }
}}
