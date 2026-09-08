using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.IO;
using System;
var pe = new PEReader(File.OpenRead(@"C:\Windows\Microsoft.NET\Framework\v4.0.30319\mscorlib.dll"));
var mr = pe.GetMetadataReader();
foreach(var tdH in mr.TypeDefinitions){
  var td=mr.GetTypeDefinition(tdH);
  string ns = td.Namespace.IsNil ? "" : mr.GetString(td.Namespace);
  string nm = mr.GetString(td.Name);
  string owner = string.IsNullOrEmpty(ns) ? nm : ns + "." + nm;
  if(owner!="System.Enum") continue;
  Console.WriteLine("Type " + owner);
  foreach(var mh in td.GetMethods()){
    var m=mr.GetMethodDefinition(mh);
    var name=mr.GetString(m.Name);
    var impl=m.ImplAttributes;
    var attrs=m.Attributes;
    bool icall=((int)impl & 0x1000)!=0;
    bool isPublic=((int)attrs & 0x07)==0x06;
    bool isStatic=((int)attrs & 0x10)!=0;
    var blob=mr.GetBlobBytes(m.Signature);
    string kind=icall?"ICALL":"MANAG";
    string acc=isPublic?"pub":"nonpub";
    string st=isStatic?"static":"inst";
    Console.WriteLine("  "+kind+" "+acc+" "+st+" "+name+" blob="+BitConverter.ToString(blob));
  }
}
