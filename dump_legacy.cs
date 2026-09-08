using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
var path = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\mscorlib.dll";
using var pe = new PEReader(File.OpenRead(path));
var md = pe.GetMetadataReader();
foreach(var fh in md.FieldDefinitions){
  var fd = md.GetFieldDefinition(fh);
  var name = md.GetString(fd.Name);
  if(name == "_legacyMode" || name == "_legacyConfigChecked"){
    var owner = md.GetTypeDefinition(fd.GetDeclaringType());
    var typeName = md.GetString(owner.Name);
    if(typeName == "TimeSpan"){
      var sig = md.GetBlobReader(fd.Signature);
      var b0 = sig.ReadByte();
      var b1 = sig.ReadByte();
      Console.WriteLine($"{name} b0={b0:X2} b1={b1:X2} rem={sig.RemainingBytes}");
      while(sig.RemainingBytes>0) Console.Write($"{sig.ReadByte():X2} ");
      Console.WriteLine();
    }
  }
}
