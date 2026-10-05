using System;
namespace Cases { public static class TypeChain {
    public static void Main() {
        // Type reflection chain: typeof keyword -> Type object -> .Name short name,
        // GetTypeCode folding, GetType().Name instance chain, typeof on custom
        // classes, reference identity comparison. All probes are bool
        // (culture-independent, per e2e convention).
        // --- typeof .Name short names ---
        Console.WriteLine(typeof(int).Name == "Int32");                     // True
        Console.WriteLine(typeof(string).Name == "String");                 // True
        Console.WriteLine(typeof(bool).Name == "Boolean");                  // True
        Console.WriteLine(typeof(double).Name == "Double");                 // True
        Console.WriteLine(typeof(object).Name == "Object");                 // True
        // --- Type.GetTypeCode folding (net48: no instance GetTypeCode() on Type,
        // static Type.GetTypeCode(typeof(T)) only) ---
        Console.WriteLine(Type.GetTypeCode(typeof(int)) == TypeCode.Int32); // True
        Console.WriteLine((int)Type.GetTypeCode(typeof(int)) == 9);         // True
        Console.WriteLine(Type.GetTypeCode(typeof(string)) == TypeCode.String); // True
        Console.WriteLine(Type.GetTypeCode(typeof(bool)) == TypeCode.Boolean);  // True
        Console.WriteLine(Type.GetTypeCode(typeof(double)) == TypeCode.Double); // True
        Console.WriteLine((int)Type.GetTypeCode(typeof(object)) == 1);      // True (Object=1)
        Console.WriteLine(Type.GetTypeCode(typeof(ChainA)) == TypeCode.Object); // True
        // --- GetType().Name instance chain (box + runtime type fetch) ---
        Console.WriteLine("hi".GetType().Name == "String");                 // True
        Console.WriteLine((42).GetType().Name == "Int32");                  // True
        Console.WriteLine((3.14).GetType().Name == "Double");               // True
        Console.WriteLine((true).GetType().Name == "Boolean");              // True
        // --- typeof on custom classes ---
        Console.WriteLine(typeof(ChainA).Name == "ChainA");                 // True
        Console.WriteLine(typeof(ChainB).Name == "ChainB");                 // True
        Console.WriteLine(typeof(ChainA).ToString() == "ChainA");           // True
        // --- typeof reference identity ---
        Console.WriteLine(typeof(int) == typeof(int));                      // True
        Console.WriteLine(typeof(string) == typeof(string));                // True
        Console.WriteLine(typeof(int) == typeof(string));                   // False
        Console.WriteLine(typeof(ChainA) == typeof(ChainB));                // False
        Console.WriteLine(typeof(int) == typeof(ChainA));                   // False
        // --- instance GetType() vs typeof identity (chain closure) ---
        Console.WriteLine("hi".GetType() == typeof(string));                // True
        Console.WriteLine(new ChainB().GetType() == typeof(ChainB));        // True
        Console.WriteLine(new ChainA().GetType() == typeof(ChainB));        // False
    }
}}

class ChainA { }
class ChainB { }
