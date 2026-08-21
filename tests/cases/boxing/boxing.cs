using System;
public class Animal { public virtual int Legs() => 4; }
public class Dog : Animal { public override int Legs() => 4; }
public class Bird : Animal { public override int Legs() => 2; }
namespace Cases { public static class Boxing {
    public static void Main() {
        // boxing / unboxing int32
        object o = 5;
        Console.WriteLine((int)o);                    // 5
        int x = 42;
        object o2 = x;
        Console.WriteLine((int)o2);                   // 42
        object o3 = 0;
        Console.WriteLine((int)o3);                   // 0
        // isinst / castclass on class hierarchy
        Animal a = new Dog();
        Console.WriteLine(a is Dog ? 1 : 0);          // 1
        Console.WriteLine(a is Bird ? 1 : 0);         // 0
        Console.WriteLine(a is Animal ? 1 : 0);       // 1
        Dog d = a as Dog;
        Console.WriteLine(d == null ? 0 : d.Legs());  // 4
        Bird b = a as Bird;
        Console.WriteLine(b == null ? 0 : 1);         // 0 (as returns null)
        // castclass (audit H4): a failing explicit cast throws
        // InvalidCastException (NOT null) -- and a succeeding cast works.
        try {
            Dog d2 = (Dog)a;
            Console.WriteLine(d2.Legs());             // 4
        } catch (InvalidCastException) {
            Console.WriteLine("ice?");
        }
        try {
            Bird b2 = (Bird)a;
            Console.WriteLine(b2 == null ? 0 : 1);    // never printed
        } catch (InvalidCastException) {
            Console.WriteLine("ice");                 // ice
        }
        // null handling
        object n = null;
        Console.WriteLine(n == null ? 1 : 0);         // 1
        Console.WriteLine(n is Animal ? 1 : 0);       // 0
    }
}}
