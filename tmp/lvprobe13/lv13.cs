using System;
using System.Reflection;
using System.Runtime.InteropServices;
namespace Cases { public static class LvProbe13 {
    public static void Main() {
        // Reflect the actual System.Windows.Forms LVITEM field layout
        var asm = Assembly.Load("System.Windows.Forms");
        var t = asm.GetType("System.Windows.Forms.ListView+LVITEM", false);
        if (t == null) { Console.WriteLine("LVITEM type not found"); return; }
        Console.WriteLine("found type, Size=" + Marshal.SizeOf(t));
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
            Console.WriteLine(f.Name + " @" + Marshal.OffsetOf(t, f.Name).ToInt64() + " type=" + f.FieldType.Name);
        }
    }
}}
