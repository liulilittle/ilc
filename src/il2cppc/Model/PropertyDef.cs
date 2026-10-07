namespace Il2Cpp.Transpiler.Model;

//------------------------------------------------------------------------------
// PropertyDef -- one metadata property of a translated type (refl2 plan D8).
//
// The Property table row (ECMA-335 II.22.16) carries the property name and a
// PropertySig blob; the METHOD SEMANTICS table associates the accessor methods
// (getter 0x54 / setter 0x55) with the property. Loaded from the PropertyMap
// table (ECMA-335 II.22.15: per-type property ranges) by MetadataLoader and
// emitted into the type's Il2CppTypeInfo property sub-table.
//
// Runtime contract (typeinfo.h / reflection-runtime-design.md section 2.1):
//   * getter/setter are stored as METHOD NAMES (const char*), NOT function
//     pointers -- "single source of truth": the runtime resolves the accessor
//     by name through the SAME methods table (never a second fnPtr copy).
//   * indexParamCount = the indexer parameter count (0 = non-indexed
//     property); the PropertySig ParamCount field is exactly this.
//------------------------------------------------------------------------------
public sealed class PropertyDef
{
    // Property name (metadata Property.Name, e.g. "PropA").
    public required string Name { get; init; }

    // Declaring type (the TypeDef whose PropertyMap range the row belongs to).
    public required TypeDef Owner { get; init; }

    // Full ECMA type name of the property type (decoded from the PropertySig
    // return type), e.g. "System.Int32", "Calc.Node", "System.String".
    // Matches the field-table typeName convention (typeinfo.h Il2CppFieldInfo
    // typeName).
    public required string TypeName { get; init; }

    // Accessor METHOD NAME, null when the property has no getter (write-only).
    public string? GetterName { get; init; }

    // Accessor METHOD NAME, null when the property has no setter (read-only).
    public string? SetterName { get; init; }

    // Indexer parameter count (PropertySig ParamCount): 0 = non-indexed.
    public int IndexParamCount { get; init; }
}
