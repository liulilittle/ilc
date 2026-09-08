using System.Reflection.Metadata;

namespace Il2Cpp.Transpiler.Emit;

public static class MetadataExtensions
{
    public static string GetFullName(this MetadataReader md, TypeReferenceHandle h)
    {
        var t = md.GetTypeReference(h);
        var name = md.GetString(t.Name);
        var ns = md.GetString(t.Namespace);
        var parent = t.ResolutionScope;
        if (parent.Kind == HandleKind.TypeReference)
            return md.GetFullName((TypeReferenceHandle)parent) + "+" + name;
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public static string GetFullName(this MetadataReader md, TypeDefinitionHandle h)
    {
        var t = md.GetTypeDefinition(h);
        var name = md.GetString(t.Name);
        var ns = md.GetString(t.Namespace);
        // Nested types carry an empty namespace -- walk the declaring-type
        // chain like the TypeReference overload, else nested names leak bare
        // (e.g. "Filter" instead of "System.RuntimeType+Filter").
        if (t.IsNested)
            return md.GetFullName(t.GetDeclaringType()) + "+" + name;
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
