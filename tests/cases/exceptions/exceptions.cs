using System;
namespace Cases { public static class Exceptions {
    public static void Main() {
        // try/catch: divide by zero
        int r = TryDiv(10, 0);
        Console.WriteLine(r);                        // -1 (caught)
        Console.WriteLine(TryDiv(10, 2));            // 5
        // try/finally: finally runs on the normal path
        Console.WriteLine(WithFinally(7));           // 70
        // explicit throw + catch
        try {
            throw new Exception("boom");
        } catch (Exception) {
            Console.WriteLine("caught");             // caught
        }
        // nested: inner throw, outer catch (finally between)
        try {
            NestedThrow();
        } catch (Exception) {
            Console.WriteLine("outer");              // outer
        }
        // audit H8: a throw of a DERIVED exception type must be catchable by
        // its own type (the throw site must carry the real type, not a fixed
        // System.Exception).
        try {
            throw new InvalidOperationException("nope");
        } catch (InvalidOperationException) {
            Console.WriteLine("caught ioe");        // caught ioe
        }
        // ...and by a base type (chain matching).
        try {
            throw new ArgumentOutOfRangeException();
        } catch (Exception) {
            Console.WriteLine("caught base");       // caught base
        }
        // User-defined exception (translated type): own type + base catch.
        try {
            throw new MyEx();
        } catch (MyEx) {
            Console.WriteLine("caught myex");       // caught myex
        }
        try {
            throw new MyEx();
        } catch (Exception) {
            Console.WriteLine("caught myex base");  // caught myex base
        }
        // A thrown type that does NOT match must propagate to the outer catch.
        try {
            try {
                throw new InvalidOperationException("inner");
            } catch (ArgumentOutOfRangeException) {
                Console.WriteLine("wrong catch");   // never printed
            }
        } catch (InvalidOperationException) {
            Console.WriteLine("outer ioe");         // outer ioe
        }
    }
    class MyEx : Exception {
        public MyEx() { }
    }
    static int TryDiv(int x, int y) {
        int result = 0;
        try { result = x / y; }
        catch (Exception) { result = -1; }
        return result;
    }
    static int WithFinally(int v) {
        int result = 0;
        try { result = v * 10; }
        finally { result = result + 1; }             // runs on the normal path
        return result;
    }
    static void NestedThrow() {
        try {
            throw new Exception("inner");
        } finally {
            Console.WriteLine("fin");                // fin (finally runs during unwinding)
        }
    }
}}
