using System;
using System.Reflection;
class Program {
  static void Main() {
    var asm = Assembly.LoadFile(@"C:\Windows\Microsoft.NET\Framework\v4.0.30319\mscorlib.dll");
    var t = asm.GetType("System.Globalization.CultureData");
    if (t==null) { Console.WriteLine("not found"); return; }
    Console.WriteLine("Type: " + t.FullName + " IsValueType:" + t.IsValueType + " Base:" + t.BaseType);
    var fields = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
    Console.WriteLine("Instance fields count: " + fields.Length);
    foreach(var f in fields) {
      Console.WriteLine("  " + f.Name + " : " + f.FieldType.FullName + " IsValueType:" + f.FieldType.IsValueType + " IsRef:" + (!f.FieldType.IsValueType));
    }
    var staticFields = t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
    Console.WriteLine("Static fields count: " + staticFields.Length);
    foreach(var f in staticFields) {
      Console.WriteLine("  static " + f.Name + " : " + f.FieldType.FullName);
    }
  }
}
