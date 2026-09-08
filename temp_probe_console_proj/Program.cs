
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
var path = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\mscorlib.dll";
using var fs = File.OpenRead(path);
using var pe = new PEReader(fs);
var mr = pe.GetMetadataReader();
static string FullName(MetadataReader md, TypeDefinitionHandle h){ var t=md.GetTypeDefinition(h); var n=md.GetString(t.Name); var ns=md.GetString(t.Namespace); return string.IsNullOrEmpty(ns)?n:ns+"."+n; }
foreach(var tdH in mr.TypeDefinitions){
  var owner = FullName(mr, tdH);
  if(owner!="System.Console") continue;
  var td = mr.GetTypeDefinition(tdH);
  foreach(var mh in td.GetMethods()){
    var m = mr.GetMethodDefinition(mh);
    var name = mr.GetString(m.Name);
    if(name!="Write" && name!="WriteLine" && name!="Read" && name!="ReadLine" && name!="SetOut" && name!="SetError" && name!="get_Out" && name!="get_Error" && name!="get_IsInputRedirected" && name!="OpenStandardOutput" && name!="Beep") continue;
    var sig = mr.GetBlobReader(m.Signature);
    var header = sig.ReadByte();
    var pcount = sig.ReadCompressedInteger();
    var ret = sig.ReadCompressedInteger();
    // read params
    var ps = new System.Collections.Generic.List<byte>();
    for(int i=0;i<pcount;i++){
      // need to decode type: if ELEMENT_TYPE (handle?) just read byte
      // For simple case read next byte (works for primitive types)
      var b = sig.ReadByte();
      // handle special: 0x1C=OBJECT, 0x0E=STRING, 0x12=class? Check
      // For generic etc, need compressed? But Console overloads are primitive, so one byte suffices except char[] which is SZARRAY 0x1D
      if(b==0x1D){ // SZARRAY
        var elem = sig.ReadByte();
        ps.Add(b); ps.Add(elem);
      } else if(b==0x12 || b==0x11){
        // TypeDefOrRef encoded? read compressed
        // Actually for System.Decimal etc, it's VALUETYPE 0x11 with TypeDefOrRef
        ps.Add(b);
        // try read compressed integer to consume token
        try{ var tok = sig.ReadCompressedInteger(); ps.Add((byte)tok); }catch{}
      } else {
        ps.Add(b);
      }
    }
    Console.WriteLine($"{name} pcount={pcount} ps={string.Join(\",\", ps.Select(x=> $\"0x{x:X2}\"))} header=0x{header:X2} ret=0x{ret:X2}");
  }
}
