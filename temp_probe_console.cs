using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
var path = \"C:\\Windows\\Microsoft.NET\\Framework\\v4.0.30319\\mscorlib.dll\";
using var fs = File.OpenRead(path);
using var pe = new PEReader(fs);
var mr = pe.GetMetadataReader();
int total=0;
foreach(var tdH in mr.TypeDefinitions){
  var owner = mr.GetFullName(tdH);
  if(owner!=\"System.Console\") continue;
  var td = mr.GetTypeDefinition(tdH);
  foreach(var mh in td.GetMethods()){
    var m = mr.GetMethodDefinition(mh);
    var name = mr.GetString(m.Name);
    bool isPublic = (m.Attributes & System.Reflection.MethodAttributes.Public)!=0;
    bool isInternalCall = (m.ImplAttributes & System.Reflection.MethodImplAttributes.InternalCall)!=0;
    Console.WriteLine($\"{name} public={isPublic} icall={isInternalCall} attrs={(int)m.Attributes:X4} impl={(int)m.ImplAttributes:X4}\");
    if(isInternalCall) total++;
  }
}
Console.WriteLine(\"total icall for System.Console=\"+total);
