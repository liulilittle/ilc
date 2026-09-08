using System;
public class VirtBase<T> { public virtual T Virt(T v)=>v; public virtual string Name()=>"VirtBase"; }
public class VirtStr : VirtBase<string> { public override string Virt(string v)=>v+"_ovr"; public override string Name()=>"VirtStr"; }
public class VirtInt : VirtBase<int> { public override int Virt(int v)=>v*2; }
namespace Cases { public static class GenericVirtStr {
    public static void Main(){
        VirtBase<int> vi=new VirtInt(); Console.WriteLine(vi.Virt(21));
        VirtBase<string> vs=new VirtStr(); Console.WriteLine(vs.Virt("hi"));
        Console.WriteLine(vs.Name());
        VirtBase<string> vs2=new VirtBase<string>(); Console.WriteLine(vs2.Virt("base"));
    }
}}
