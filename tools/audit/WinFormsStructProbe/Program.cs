// WinFormsStructProbe -- layout/blittability evidence for the STRUCT types that
// appear in System.Windows.Forms / System.Drawing P/Invoke signatures.
//
// The emitter passes an unregistered struct BY ADDRESS and relies on the C++
// struct it generates being byte-identical to the native layout
// (CodeGenerator.cs:9045-9051). That holds only for blittable fields. A field
// that is a managed reference (string / class / array / delegate) is emitted as
// a GC pointer (Il2CppObject*) and the native layout is then WRONG -- silently
// (no LOUD rejection, no compile error). This tool enumerates exactly those
// structs so the audit can size the gap.
//
// usage: WinFormsStructProbe <pinvokes.tsv> <assembly.dll>... > structs.tsv
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace WinFormsStructProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var lines = File.ReadAllLines(args[0]);
        var header = lines[0].Split('\t');
        var paramCol = Array.IndexOf(header, "params");
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var f = lines[i].Split('\t');
            foreach (var p in f[paramCol].Split(';'))
            {
                if (p.Length == 0) continue;
                var t = p.TrimEnd('&');
                if (!t.EndsWith("[]", StringComparison.Ordinal) && t.Contains('.') && !t.StartsWith("System.", StringComparison.Ordinal))
                    used.Add(t);
                else if (t.StartsWith("System.Windows.Forms.", StringComparison.Ordinal)
                         || t.StartsWith("System.Drawing.", StringComparison.Ordinal))
                    used.Add(t);
            }
        }

        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in args.Skip(1))
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            var reader = pe.GetMetadataReader();
            foreach (var th in reader.TypeDefinitions)
            {
                var td = reader.GetTypeDefinition(th);
                var name = TypeFullName(reader, th);
                if (!used.Contains(name)) continue;
                if (rows.ContainsKey(name)) continue;
                var refFields = new List<string>();
                var arrayFields = new List<string>();
                var stringFields = new List<string>();
                var marshalAs = 0;
                foreach (var fh in td.GetFields())
                {
                    var fd = reader.GetFieldDefinition(fh);
                    if ((fd.Attributes & FieldAttributes.Static) != 0) continue;
                    var fn = reader.GetString(fd.Name);
                    var blob = reader.GetBlobReader(fd.Signature);
                    if (blob.ReadByte() != 0x06) continue;
                    var b = blob.ReadByte();
                    while (b is 0x1F or 0x20) { _ = blob.ReadCompressedInteger(); b = blob.ReadByte(); }
                    switch (b)
                    {
                        case 0x0E: stringFields.Add(fn); refFields.Add(fn); break;
                        case 0x12:
                        case 0x1C:
                            refFields.Add(fn);
                            break;
                        case 0x1D:
                        case 0x14:
                            arrayFields.Add(fn);
                            refFields.Add(fn);
                            break;
                    }
                    foreach (var ch in fd.GetCustomAttributes())
                    {
                        var ca = reader.GetCustomAttribute(ch);
                        var nm = AttrName(reader, ca);
                        if (nm == "System.Runtime.InteropServices.MarshalAsAttribute") marshalAs++;
                    }
                }
                var layout = (td.Attributes & TypeAttributes.LayoutMask);
                var pack = td.GetLayout();
                rows[name] = string.Join('\t', name, layout.ToString(),
                    pack.PackingSize.ToString(), refFields.Count.ToString(),
                    stringFields.Count.ToString(), arrayFields.Count.ToString(),
                    marshalAs.ToString(),
                    refFields.Count > 0 ? "NON-BLITTABLE" : "blittable");
            }
        }

        Console.WriteLine("struct\tlayout\tpack\trefFields\tstringFields\tarrayFields\tmarshalAs\tverdict");
        foreach (var kv in rows.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            Console.WriteLine(kv.Value);
        Console.Error.WriteLine("structs=" + rows.Count);
        return 0;
    }

    private static string AttrName(MetadataReader reader, CustomAttribute ca)
    {
        if (ca.Constructor.Kind == HandleKind.MemberReference)
        {
            var mr = reader.GetMemberReference((MemberReferenceHandle)ca.Constructor);
            if (mr.Parent.Kind == HandleKind.TypeReference)
                return TypeRefName(reader, (TypeReferenceHandle)mr.Parent);
        }
        return "";
    }

    private static string TypeRefName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var tr = reader.GetTypeReference(handle);
        var name = reader.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
            return TypeRefName(reader, (TypeReferenceHandle)tr.ResolutionScope) + "+" + name;
        var ns = reader.GetString(tr.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string TypeFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var def = reader.GetTypeDefinition(handle);
        var name = reader.GetString(def.Name);
        var decl = def.GetDeclaringType();
        if (!decl.IsNil) return TypeFullName(reader, decl) + "+" + name;
        var ns = reader.GetString(def.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
