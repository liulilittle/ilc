using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
var pe = new PEReader(File.OpenRead(@"C:\Windows\Microsoft.NET\Framework\v4.0.30319\mscorlib.dll"));
var mr = pe.GetMetadataReader();
foreach(var tdH in mr.TypeDefinitions){
  var td=mr.GetTypeDefinition(tdH);
  var owner=mr.GetFullName(tdH);
  if(owner!="System.Enum") continue;
  Console.WriteLine($"Type {owner}");
  foreach(var mh in td.GetMethods()){
    var m=mr.GetMethodDefinition(mh);
    var name=mr.GetString(m.Name);
    var impl=m.ImplAttributes;
    var attrs=m.Attributes;
    bool pub=(attrs & MethodAttributes.Public)!=0;
    bool icall=(impl & MethodImplAttributes.InternalCall)!=0;
    var sig=m.Signature;
    var blob=mr.GetBlobBytes(sig);
    Console.WriteLine($"  {(icall?"ICALL":"MANAG")} {(pub?"pub":"nonpub")} {(m.Attributes & MethodAttributes.Static)!=0?"static":"inst"} {name} blob={BitConverter.ToString(blob)}");
  }
}
