// WinFormsLoaderBlockers -- reproduce, as data, the FIRST hard rejection the
// il2cppc metadata loader hits on System.Windows.Forms/System.Drawing, so the
// audit can say precisely which type/field blocks the assembly from loading.
//
// The loader (src/il2cppc/MetadataLoader.cs:1039, DecodeFieldSignature) throws
// "value-type instance fields not supported in M2" for any CLASS instance
// field whose signature is a VALUETYPE that is not Guid / DateTime / TimeSpan /
// Decimal / DateTimeOffset / an enum. This tool enumerates exactly that set.
//
// usage: WinFormsLoaderBlockers <assembly.dll>...

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace WinFormsLoaderBlockers;

internal static class Program
{
    private static readonly HashSet<string> AllowedValueTypes = new(StringComparer.Ordinal)
    {
        "System.Guid", "System.DateTime", "System.TimeSpan", "System.Decimal", "System.DateTimeOffset",
    };

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: WinFormsLoaderBlockers <assembly.dll>...");
            return 2;
        }

        var sb = new StringBuilder();
        sb.AppendLine("assembly\tnamespace\ttype\tkind\tfield\tfieldType\treason");
        var total = 0;
        var byReason = new Dictionary<string, int>(StringComparer.Ordinal);

        // GLOBAL pre-pass: a value type declared in ANOTHER input assembly is
        // still an enum/struct here (ContentAlignment lives in System.Drawing
        // but is used as a field type in System.Windows.Forms), so the enum
        // and struct sets must span every input before any field is judged.
        var enums = new HashSet<string>(StringComparer.Ordinal);
        var valueTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p0 in args)
        {
            using var fs0 = File.OpenRead(p0);
            using var pe0 = new PEReader(fs0);
            var r0 = pe0.GetMetadataReader();
            foreach (var th0 in r0.TypeDefinitions)
            {
                var name0 = TypeFullName(r0, th0);
                var base0 = BaseName(r0, r0.GetTypeDefinition(th0));
                if (base0 == "System.Enum") enums.Add(name0);
                else if (base0 == "System.ValueType") valueTypes.Add(name0);
            }
        }

        foreach (var path in args)
        {
            var asmName = Path.GetFileNameWithoutExtension(path);
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            var reader = pe.GetMetadataReader();

            foreach (var th in reader.TypeDefinitions)
            {
                var td = reader.GetTypeDefinition(th);
                var typeName = TypeFullName(reader, th);
                if (valueTypes.Contains(typeName) || enums.Contains(typeName)) continue;
                if ((td.Attributes & TypeAttributes.Interface) != 0) continue;
                foreach (var fh in td.GetFields())
                {
                    var fd = reader.GetFieldDefinition(fh);
                    if ((fd.Attributes & FieldAttributes.Static) != 0) continue;
                    var blob = reader.GetBlobReader(fd.Signature);
                    if (blob.ReadByte() != 0x06) continue;
                    var b = SkipModifiers(ref blob, blob.ReadByte());
                    if (b != 0x11) continue;
                    var vtHandle = blob.ReadTypeHandle();
                    var vtName = HandleName(reader, vtHandle);
                    if (enums.Contains(vtName) || AllowedValueTypes.Contains(vtName)) continue;
                    var reason = valueTypes.Contains(vtName) ? "user/BCL-struct-instance-field" : "unknown-valuetype";
                    sb.AppendLine(string.Join("\t", asmName, reader.GetString(td.Namespace), typeName,
                        "class", reader.GetString(fd.Name), vtName, reason));
                    total++;
                    byReason[reason] = byReason.GetValueOrDefault(reason) + 1;
                }
            }
        }

        Console.Write(sb.ToString());
        Console.Error.WriteLine("blockingFields=" + total);
        foreach (var kv in byReason) Console.Error.WriteLine("  " + kv.Key + "=" + kv.Value);
        return 0;
    }

    private static byte SkipModifiers(ref BlobReader blob, byte b)
    {
        while (b is 0x1F or 0x20)
        {
            _ = blob.ReadCompressedInteger();
            b = blob.ReadByte();
        }
        return b;
    }

    private static string BaseName(MetadataReader reader, TypeDefinition td)
    {
        if (td.BaseType.IsNil) return "";
        return td.BaseType.Kind switch
        {
            HandleKind.TypeReference => TypeRefFullName(reader, (TypeReferenceHandle)td.BaseType),
            HandleKind.TypeDefinition => TypeFullName(reader, (TypeDefinitionHandle)td.BaseType),
            _ => "",
        };
    }

    private static string HandleName(MetadataReader reader, EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeReference => TypeRefFullName(reader, (TypeReferenceHandle)h),
        HandleKind.TypeDefinition => TypeFullName(reader, (TypeDefinitionHandle)h),
        _ => h.Kind.ToString(),
    };

    private static string TypeFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var def = reader.GetTypeDefinition(handle);
        var name = reader.GetString(def.Name);
        var decl = def.GetDeclaringType();
        if (!decl.IsNil) return TypeFullName(reader, decl) + "+" + name;
        var ns = reader.GetString(def.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string TypeRefFullName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var tr = reader.GetTypeReference(handle);
        var name = reader.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
            return TypeRefFullName(reader, (TypeReferenceHandle)tr.ResolutionScope) + "+" + name;
        var ns = reader.GetString(tr.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
