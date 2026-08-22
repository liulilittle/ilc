using System;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Model;

class Probe {
    static void Main(string[] args) {
        using var pe = new PEReader(System.IO.File.OpenRead(args[0]));
        var md = pe.GetMetadataReader();
        foreach (var th in md.TypeDefinitions) {
            var td = md.GetTypeDefinition(th);
            var name = md.GetString(td.Name);
            if (!name.Contains("d__")) continue;
            foreach (var mh in td.GetMethods()) {
                var m = md.GetMethodDefinition(mh);
                var mn = md.GetString(m.Name);
                if (mn != "MoveNext") continue;
                var t = new TypeDef { Name = name, Namespace = "" };
                var mi = MethodBodyReader.Read(pe, md, t, new MethodDef { Name = mn, Owner = t, IsStatic = false, ParamCount = 0, ParamTypes = "", ReturnType = SignatureTypeCode.Void, ReturnTypeName = "" });
                Console.WriteLine($"{name}::{mn}: kinds = [{string.Join(", ", mi.LocalKinds)}] names = [{string.Join(", ", mi.LocNames)}]");
            }
        }
    }
}
