using System;
using System.Reflection;
var t = typeof(System.Enum);
foreach(var m in t.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic)){
  var attrs = m.GetMethodImplementationFlags();
  bool isICall = (attrs & MethodImplAttributes.InternalCall)!=0;
  Console.WriteLine($"{(isICall?"ICALL":"MANAG")} {(m.IsPublic?"pub":"nonpub")} {(m.IsStatic?"static":"inst")} {m.Name}({string.Join(", ", Array.ConvertAll(m.GetParameters(), p=>p.ParameterType.Name + (p.ParameterType.IsByRef?"&":"") + " " + p.Name))}) : {m.ReturnType.Name}");
}
