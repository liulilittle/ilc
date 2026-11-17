using System;
using System.Runtime.InteropServices;

[ComVisible(true)]
[Guid("6B29FC40-CA47-1067-B31D-00DD010662DA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProbeComSlot
{
    void MethodA();
    void MethodB();
}

[ComVisible(true)]
[Guid("11111111-2222-3333-4444-555555555555")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDerivedComSlot : IProbeComSlot
{
    void MethodC();
    void MethodD();
    void MethodE();
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
[Guid("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE")]
public sealed class ProbeComSlotClass : IDerivedComSlot
{
    public void MethodA() { }
    public void MethodB() { }
    public void MethodC() { }
    public void MethodD() { }
    public void MethodE() { }
}

// [ComVisible] defaults to true for public types, so this one IS visible.
public sealed class NoAttributeComSlotClass
{
    public void Nothing() { }
}

// Explicitly hidden: IsTypeVisibleFromCom must answer False.
[ComVisible(false)]
public sealed class HiddenComSlotClass
{
    public void Nothing() { }
}

// A class (not an interface): GetStartComSlot/GetEndComSlot answer -1.
[ComVisible(true)]
public sealed class NotAnInterfaceComSlotClass
{
    public void Nothing() { }
}

public static class Program
{
    public static void Main()
    {
        // Every typeof is a compile-time constant here: the slot markers need the
        // Type resolved to metadata at transpile time.
        Console.WriteLine("start=" + Marshal.GetStartComSlot(typeof(IDerivedComSlot)));
        Console.WriteLine("end=" + Marshal.GetEndComSlot(typeof(IDerivedComSlot)));
        Console.WriteLine("base.start=" + Marshal.GetStartComSlot(typeof(IProbeComSlot)));
        Console.WriteLine("base.end=" + Marshal.GetEndComSlot(typeof(IProbeComSlot)));

        Console.WriteLine("visible=" + Marshal.IsTypeVisibleFromCom(typeof(IDerivedComSlot)));
        Console.WriteLine("noattr=" + Marshal.IsTypeVisibleFromCom(typeof(NoAttributeComSlotClass)));
        Console.WriteLine("hidden=" + Marshal.IsTypeVisibleFromCom(typeof(HiddenComSlotClass)));

        // Non-interface: -1 on both ends.
        Console.WriteLine("class.start=" + Marshal.GetStartComSlot(typeof(NotAnInterfaceComSlotClass)));
        Console.WriteLine("class.end=" + Marshal.GetEndComSlot(typeof(NotAnInterfaceComSlotClass)));
    }
}
