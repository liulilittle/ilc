using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace QCallProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: QCallProbe <assembly.dll> <out.tsv>");
            return 2;
        }

        var asmPath = Path.GetFullPath(args[0]);
        using var fs = File.OpenRead(asmPath);
        using var pe = new PEReader(fs);
        if (!pe.HasMetadata) { Console.Error.WriteLine("no metadata"); return 3; }
        var r = pe.GetMetadataReader();

        var moduleNames = new Dictionary<ModuleReferenceHandle, string>();
        int modRefCount = r.GetTableRowCount(TableIndex.ModuleRef);
        for (int i = 1; i <= modRefCount; i++)
        {
            var h = MetadataTokens.ModuleReferenceHandle(i);
            moduleNames[h] = r.GetString(r.GetModuleReference(h).Name);
        }

        var rows = new List<string>();
        int types = 0;
        foreach (var th in r.TypeDefinitions)
        {
            types++;
            var td = r.GetTypeDefinition(th);
            var declaring = TypeFullName(r, th);
            var tvis = TypeVisibility(td.Attributes);

            foreach (var mh in td.GetMethods())
            {
                var md = r.GetMethodDefinition(mh);
                if ((md.Attributes & MethodAttributes.PinvokeImpl) == 0) continue;
                var imp = md.GetImport();
                if (imp.Module.IsNil) continue;
                var modName = moduleNames.TryGetValue(imp.Module, out var mn) ? mn : "";
                if (!string.Equals(modName, "QCall", StringComparison.Ordinal)) continue;

                var name = r.GetString(md.Name);
                var entry = r.GetString(imp.Name);
                var mvis = MethodVisibility(md.Attributes);
                var stat = (md.Attributes & MethodAttributes.Static) != 0 ? "static" : "instance";
                rows.Add(string.Join('\t', declaring, name, entry, tvis + "/" + mvis + "/" + stat));
            }
        }

        rows.Sort(StringComparer.Ordinal);
        File.WriteAllLines(Path.GetFullPath(args[1]), rows);
        Console.WriteLine($"types={types}");
        Console.WriteLine($"qcalls={rows.Count}");
        return 0;
    }

    private static string TypeFullName(MetadataReader r, TypeDefinitionHandle h)
    {
        var td = r.GetTypeDefinition(h);
        var name = r.GetString(td.Name);
        var ns = r.GetString(td.Namespace);
        var decl = td.GetDeclaringType();
        if (!decl.IsNil) return TypeFullName(r, decl) + "+" + name;
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string TypeVisibility(TypeAttributes a) =>
        (a & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public => "public",
            TypeAttributes.NestedPublic => "nestedPublic",
            TypeAttributes.NestedPrivate => "nestedPrivate",
            TypeAttributes.NestedFamily => "nestedFamily",
            TypeAttributes.NestedAssembly => "nestedAssembly",
            TypeAttributes.NestedFamORAssem => "nestedFamORAssem",
            TypeAttributes.NestedFamANDAssem => "nestedFamANDAssem",
            _ => "internal",
        };

    private static string MethodVisibility(MethodAttributes a) =>
        (a & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => "public",
            MethodAttributes.Private => "private",
            MethodAttributes.Family => "family",
            MethodAttributes.Assembly => "assembly",
            MethodAttributes.FamORAssem => "famORassem",
            MethodAttributes.FamANDAssem => "famANDAssem",
            _ => "private",
        };
}
