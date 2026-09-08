using System;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.IO;
using System.Linq;
using Il2Cpp.Transpiler.Emit;

class Dump {
 static void Main(string[] args) {
   var dll = args.Length>0? args[0] : @"E:\il2cpp\tmp\net910\net10\shared\Microsoft.NETCore.App\10.0.11\System.Private.CoreLib.dll";
   // try mscorlib from .NET Framework? Look for anonymous type in that dll? Use the reference assembly that generated gen?
   // Find the dll that was used for generation: likely E:\il2cpp\reference or E:\il2cpp\out's source? Check transpiler config.
   // Instead brute search.
   if (!File.Exists(dll)) {
     var candidates = Directory.GetFiles(@"E:\il2cpp\tmp", "mscorlib.dll", SearchOption.AllDirectories);
     foreach(var c in candidates) Console.WriteLine(c);
     return;
   }
   using var pe = new PEReader(File.OpenRead(dll));
   var md = pe.GetMetadataReader();
    foreach(var tdh in md.TypeDefinitions) {
      var td = md.GetTypeDefinition(tdh);
      var name = md.GetString(td.Name);
      if (name.Contains("AnonymousType")) {
        Console.WriteLine($"Type {md.GetFullName(tdh)} token {MetadataTokens.GetToken(tdh):X8} generic {td.GetGenericParameters().Count}");
        foreach(var mh in td.GetMethods()) {
          var m = md.GetMethodDefinition(mh);
          var mName = md.GetString(m.Name);
          Console.WriteLine($"  Method {mName} token {MetadataTokens.GetToken(mh):X8} RVA {m.RelativeVirtualAddress:X}");
          try {
            var body = pe.GetMethodBody(m.RelativeVirtualAddress);
            var il = body.GetILBytes();
            Console.WriteLine($"    IL bytes {il.Length}: {BitConverter.ToString(il).Replace("-"," ")}");
            // decode locals?
            if (!body.LocalSignature.IsNil) {
              var sig = md.GetBlobReader(md.GetStandaloneSignature(body.LocalSignature).Signature);
              Console.WriteLine($"    Local sig bytes: {BitConverter.ToString(sig.ReadBytes(sig.RemainingBytes)).Replace("-"," ")}");
            }
          } catch(Exception ex){ Console.WriteLine($"    err {ex.Message}");}
        }
        // dump TypeSpecs
        foreach(var tsh in md.TypeSpecifications) {
          var ts = md.GetTypeSpecification((TypeSpecificationHandle)tsh);
          var br = md.GetBlobReader(ts.Signature);
          var bytes = br.ReadBytes(br.RemainingBytes);
          Console.WriteLine($"  TypeSpec token {MetadataTokens.GetToken((EntityHandle)tsh):X8} bytes {BitConverter.ToString(bytes).Replace("-"," ")}");
        }
      }
    }
 }
}
