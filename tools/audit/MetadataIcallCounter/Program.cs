// Channel 2 of the net48 mscorlib InternalCall audit: raw ECMA-335 metadata.
//
// Reads mscorlib.dll through System.Reflection.Metadata (PEReader/MetadataReader),
// independent of the runtime reflection surface used by
// tools/audit/enumerate-icall-reflection.ps1, and emits the same TSV shape:
//     declaringType <TAB> methodName <TAB> signature <TAB> visibility <TAB> kind
// where visibility is "typeVis/methodVis/static|instance" and kind is
// "ctor" | "method".
//
// The only source of truth here is the MethodDef.ImplAttributes bit field; no
// attribute decoding, no IL parsing.

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace MetadataIcallCounter;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: MetadataIcallCounter <assembly.dll> <out.tsv>");
            return 2;
        }

        var asmPath = Path.GetFullPath(args[0]);
        var outPath = Path.GetFullPath(args[1]);

        using var fs = File.OpenRead(asmPath);
        using var pe = new PEReader(fs);
        if (!pe.HasMetadata)
        {
            Console.Error.WriteLine("no metadata in " + asmPath);
            return 3;
        }

        var reader = pe.GetMetadataReader();
        var rows = new List<string>();
        var typeCount = 0;

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            typeCount++;
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var declaring = TypeFullName(reader, typeHandle);
            var typeVis = TypeVisibility(typeDef.Attributes);

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.ImplAttributes & MethodImplAttributes.InternalCall) == 0)
                    continue;

                var name = reader.GetString(method.Name);
                var sig = Signature(reader, method);
                var methodVis = MethodVisibility(method.Attributes);
                var stat = (method.Attributes & MethodAttributes.Static) != 0 ? "static" : "instance";
                var kind = name is ".ctor" or ".cctor" ? "ctor" : "method";

                rows.Add(string.Join('\t', declaring, name, sig, typeVis + "/" + methodVis + "/" + stat, kind));
            }
        }

        rows.Sort(StringComparer.Ordinal);
        File.WriteAllLines(outPath, rows, new UTF8Encoding(false));

        Console.WriteLine("types=" + typeCount);
        Console.WriteLine("icalls=" + rows.Count);
        Console.WriteLine("out=" + outPath);
        return 0;
    }

    private static string TypeFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var def = reader.GetTypeDefinition(handle);
        var name = reader.GetString(def.Name);
        var declaring = def.GetDeclaringType();
        if (!declaring.IsNil)
            return TypeFullName(reader, declaring) + "+" + name;

        var ns = reader.GetString(def.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string TypeVisibility(TypeAttributes attrs) => (attrs & TypeAttributes.VisibilityMask) switch
    {
        TypeAttributes.Public or TypeAttributes.NestedPublic => "public",
        TypeAttributes.NestedFamily => "protected",
        TypeAttributes.NestedFamORAssem => "protected internal",
        TypeAttributes.NestedAssembly => "internal",
        TypeAttributes.NestedPrivate => "private",
        _ => "internal",
    };

    private static string MethodVisibility(MethodAttributes attrs) => (attrs & MethodAttributes.MemberAccessMask) switch
    {
        MethodAttributes.Public => "public",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected internal",
        MethodAttributes.Assembly => "internal",
        _ => "private",
    };

    private static string Signature(MetadataReader reader, MethodDefinition method)
    {
        var provider = new IcallAuditShared.SignatureProvider(reader);
        MethodSignature<string> sig;
        try
        {
            sig = method.DecodeSignature(provider, null);
        }
        catch (Exception ex)
        {
            return "<undecodable: " + ex.GetType().Name + ">";
        }

        var sb = new StringBuilder();
        sb.Append(sig.ReturnType).Append(" (");
        for (var i = 0; i < sig.ParameterTypes.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(sig.ParameterTypes[i]);
        }
        sb.Append(')');
        return sb.ToString();
    }
}
