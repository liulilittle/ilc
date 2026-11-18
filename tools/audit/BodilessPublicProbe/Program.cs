// Finds CoreLib PUBLIC methods that have NO IL body (RVA == 0) and are not
// P/Invoke. Those are the shape that produced the Marshal.GetExceptionForHR
// phantom: a bodiless public wrapper is resolved through MethodRefResolver.Resolve,
// which -- unlike BclResolve -- never sets IsFictionalBclFallback, so the
// phantom guard in StackFold cannot fire and the generated C++ calls a symbol
// nothing defines.
//
// This is a CANDIDATE finder, not a verdict: a bodiless public method is only a
// defect when no arm maps it. The verdict comes from transpiling a call.
//
// usage: BodilessPublicProbe <assembly.dll> <out.tsv>

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace BodilessPublicProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: BodilessPublicProbe <assembly.dll> <out.tsv>");
            return 2;
        }

        using var fs = File.OpenRead(Path.GetFullPath(args[0]));
        using var pe = new PEReader(fs);
        if (!pe.HasMetadata) { Console.Error.WriteLine("no metadata"); return 3; }
        var md = pe.GetMetadataReader();

        var rows = new List<string>();
        int bodiless = 0;
        int bodilessPublic = 0;

        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            if (!IsTypePublic(td.Attributes)) continue;
            var owner = FullName(md, th);

            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                if (m.RelativeVirtualAddress != 0) continue;
                // Abstract members and interface members have no IL body by
                // design -- they are implemented by the callee, not wrapped. Only
                // a CONCRETE bodiless method reaches the emitter's wrapper path.
                if ((m.Attributes & MethodAttributes.Abstract) != 0) continue;
                if ((td.Attributes & TypeAttributes.Interface) != 0) continue;
                bodiless++;
                var access = m.Attributes & MethodAttributes.MemberAccessMask;
                if (access != MethodAttributes.Public && access != MethodAttributes.Family) continue;
                // P/Invoke members legitimately have no IL body; they are handled
                // by the decl/entry path, not by the wrapper path.
                if ((m.Attributes & MethodAttributes.PinvokeImpl) != 0) continue;
                bodilessPublic++;

                var name = md.GetString(m.Name);
                var sig = Signature(md, m);
                var stat = (m.Attributes & MethodAttributes.Static) != 0 ? "static" : "instance";
                rows.Add(string.Join('\t', owner, name, sig,
                    (access == MethodAttributes.Public ? "public" : "family") + "/" + stat));
            }
        }

        rows.Sort(StringComparer.Ordinal);
        File.WriteAllLines(Path.GetFullPath(args[1]), rows, new System.Text.UTF8Encoding(false));
        Console.WriteLine($"bodiless_methods={bodiless}");
        Console.WriteLine($"bodiless_public_nonpinvoke={bodilessPublic}");
        return 0;
    }

    private static bool IsTypePublic(TypeAttributes a) =>
        (a & TypeAttributes.VisibilityMask) is TypeAttributes.Public or TypeAttributes.NestedPublic;

    private static string Signature(MetadataReader md, MethodDefinition m)
    {
        var sig = m.DecodeSignature(new SigProvider(), null);
        var ps = string.Join(", ", sig.ParameterTypes);
        return sig.ReturnType + " (" + ps + ")";
    }

    private sealed class SigProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => FullName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var tr = reader.GetTypeReference(handle);
            var ns = reader.GetString(tr.Namespace);
            var n = reader.GetString(tr.Name);
            return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
        }
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext,
            TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }

    private static string FullName(MetadataReader r, TypeDefinitionHandle h)
    {
        var td = r.GetTypeDefinition(h);
        var name = r.GetString(td.Name);
        var decl = td.GetDeclaringType();
        if (!decl.IsNil) return FullName(r, decl) + "+" + name;
        var ns = r.GetString(td.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
