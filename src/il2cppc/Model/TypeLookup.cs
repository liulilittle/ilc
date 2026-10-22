namespace Il2Cpp.Transpiler.Model;

//------------------------------------------------------------------------------
// TypeLookup -- single authority for "find the TypeDef a METADATA full name
// refers to".
//
// Metadata full names come from MetadataExtensions.GetFullName, which walks the
// declaring-type chain with '+' for nested types ("Cases.Outer+Inner"), while a
// nested TypeDef carries Namespace="" so TypeDef.FullName is the BARE short
// name ("Inner"). A plain `t.FullName == name` therefore NEVER matches a nested
// type -- the callvirt owner stayed unresolved, OwnerIsTranslated said false and
// the emitter fell to the generated-name path (a direct call to the BASE
// implementation: silent wrong values; abstract/interface targets: undefined
// symbols).
//
// Matching order is deliberately two-pass: an EXACT FullName match wins so the
// historical bare-name precedence is preserved when a top-level type and a
// nested type share a short name (ExpandNestedTypes' byName indexes the same
// way), then the nested '+' form (TypeDef.MatchesFullName) is accepted.
//------------------------------------------------------------------------------
public static class TypeLookup
{
    public static TypeDef? Find(IReadOnlyList<TypeDef> types, string? metadataFullName)
    {
        if (string.IsNullOrEmpty(metadataFullName)) return null;
        foreach (var t in types)
            if (t.FullName == metadataFullName) return t;
        foreach (var t in types)
            if (t.MatchesFullName(metadataFullName)) return t;
        return null;
    }

    public static bool Any(IReadOnlyList<TypeDef> types, string? metadataFullName)
        => Find(types, metadataFullName) != null;

    // Does this type answer to the given METADATA full name? The reflection key
    // (nested = DeclaringTypeName + "+" + Name) is the form metadata produces;
    // FullName keeps the historical bare-name behaviour for top-level types.
    public static bool Matches(TypeDef t, string? metadataFullName)
        => metadataFullName != null
           && (t.FullName == metadataFullName
               || (t.IsNested && t.DeclaringTypeName.Length > 0
                   && t.DeclaringTypeName + "+" + t.Name == metadataFullName));
}
