using System;
var t = typeof(TimeSpan);
foreach(var f in t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)){
  if(f.Name.Contains("legacy")){
    Console.WriteLine($"{f.Name} {f.FieldType} {f.FieldType.IsArray} {f.FieldType.FullName}");
  }
}
