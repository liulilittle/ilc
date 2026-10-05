using System;
public interface IBox<T> { T Get(); void Set(T v); }
public class Box<T> : IBox<T> { public T item; public T Get(){return item;} public void Set(T v){item=v;} }
namespace Cases { public static class GenericIface {
    public static void Main(){
        IBox<int> bi = new Box<int>(); bi.Set(42); Console.WriteLine(bi.Get());
        IBox<string> bs = new Box<string>(); bs.Set("hi"); Console.WriteLine(bs.Get());
        Console.WriteLine(bi.Get()+1);
        Console.WriteLine(bs.Get()+" world");
    }
}}
