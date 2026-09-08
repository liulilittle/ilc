using System;
public class VirtBase<T> { public virtual T Virt(T v)=>v; public virtual string Name()=>"VirtBase"; }
public class VirtInt : VirtBase<int> { public override int Virt(int v)=>v*2; public override string Name()=>"VirtInt"; }
namespace Cases { public static class GenericVirtDeep {
    public static void Main(){
        VirtBase<int> vb=new VirtInt(); Console.WriteLine(vb.Virt(21)); Console.WriteLine(vb.Name());
        Box<int> gbi=new Box<int>(); gbi.Set(42); Console.WriteLine(gbi.Get());
        var bi=new Box<int>(); bi.Set(10); Console.WriteLine(bi.Get()*2);
        var b2=new Box<int>(); b2.Set(7); Console.WriteLine(b2.Get()+gbi.Get());
        var bs=new Box<string>(); bs.Set("hi"); Console.WriteLine(bs.Get());
        var bs2=new Box<string>(); bs2.Set("world"); Console.WriteLine(bs2.Get()+bs.Get());
    }
}}
public class Box<T>{ public T item; public T Get(){return item;} public void Set(T v){item=v;}}
