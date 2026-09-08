using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.IO;
var path = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll";
using var pe = new PEReader(File.OpenRead(path));
var md = pe.GetMetadataReader();
foreach (var h in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(h);
    if (td.IsNested) continue;
    var name = md.GetString(td.Name);
    var ns = md.GetString(td.Namespace);
    var full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    if (full == "Node" || name == "Node")
    {
        Console.WriteLine($"Top-level: {full} namespace='{ns}' name='{name}' token={MetadataTokens.GetToken(h):X8}");
        // List fields/methods
        foreach (var fh in td.GetFields()) Console.WriteLine($"  field {md.GetString(md.GetFieldDefinition(fh).Name)}");
        foreach (var mh in td.GetMethods()) Console.WriteLine($"  method {md.GetString(md.GetMethodDefinition(mh).Name)}");
    }
}
foreach (var h in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(h);
    if (!td.IsNested) continue;
    var name = md.GetString(td.Name);
    if (name == "Node")
    {
        var ns = md.GetString(td.Namespace);
        var decl = td.GetDeclaringType();
        var declName = decl.IsNil ? "?" : md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)decl).Name);
        // Get chain full name via MetadataExtensions
        string full;
        try { full = Il2Cpp.Transpiler.Emit.MetadataExtensions.GetFullName(md, h); } catch { full = "?"; }
        Console.WriteLine($"Nested: {full} name='{name}' ns='{ns}' decl='{declName}' token={MetadataTokens.GetToken(h):X8}");
    }
}
