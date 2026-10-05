using System;
public class Box<T> {
    public T item;
    public T Get() { return item; }
    public void Set(T v) { item = v; }
}
namespace Cases { public static class Generics {
    public static void Main() {
        Box<int> bi = new Box<int>();
        bi.Set(42);
        Console.WriteLine(bi.Get());              // 42
        Box<int> bi2 = new Box<int>();
        bi2.Set(7);
        Console.WriteLine(bi2.Get());             // 7
        Console.WriteLine(bi.Get());              // 42 (independent instances)
    }
}}
