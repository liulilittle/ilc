using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
class P {
    static void Main() {
        string path = @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll";
        var asm = AssemblyDefinition.ReadAssembly(path);
        var ic = MethodImplAttributes.InternalCall;
        var outp = new System.Collections.Generic.SortedSet<string>();
        foreach (var t in asm.Modules.SelectMany(m => m.Types)) {
            foreach (var m in t.Methods) {
                if ((m.ImplAttributes & ic) == ic) {
                    var owner = t.FullName;
                    outp.Add(owner + "::" + m.Name);
                }
            }
        }
        File.WriteAllLines(@"E:\il2cpp\tmp\icalls_net48_mscorlib.txt", outp);
        Console.WriteLine("TOTAL InternalCall in net48 mscorlib: " + outp.Count);
    }
}
