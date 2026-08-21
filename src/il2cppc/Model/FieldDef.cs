namespace Il2Cpp.Transpiler.Model;

//------------------------------------------------------------------------------
// FieldDef -- one metadata field of a translated type.
//
// M2 scope: a field is either a 32-bit integer value field (TypeKind.Int32)
// or a managed reference field (TypeKind.Reference: a GC pointer to a
// translated class instance, emitted as Il2CppObject* in the generated C++).
// The reference/value distinction is what drives the reference-field bitmap
// of the type's Il2CppClass descriptor (bit i set <=> slot i is a GC ref),
// which the mark-sweep collector scans.
//
// M3 extends TypeKind to the full ECMA type set (all primitive widths,
// floats, strings, arrays, value types) and to layout-correct offsets;
// the M2 layout rules (one 8-byte slot per field, in declaration order)
// are documented in the type emitter.
//------------------------------------------------------------------------------
public sealed class FieldDef
{
    public required string Name { get; init; }
    public required TypeDef Owner { get; init; }
    public bool IsStatic { get; init; }
    public bool IsLiteral { get; init; }   // const field
    public string? ConstantValue { get; init; }

    // M2 field type classification (see TypeKind above):
    //   - Int32     : 32-bit signed value field (int32_t in generated C++)
    //   - Reference : managed reference field (Il2CppObject* in generated C++)
    // Unknown/unsupported signatures throw at metadata load time so the
    // transpiler fails loudly on input it cannot represent faithfully.
    public FieldTypeKind TypeKind { get; init; } = FieldTypeKind.Int32;

    // Full ECMA type name for Reference fields (e.g. "Calc.Point",
    // "System.Object", "System.String"). Used by the type emitter to
    // (a) declare the field with the CONCRETE generated struct type where
    //     possible, and
    // (b) keep the descriptor's name field in sync with the metadata.
    // Value fields carry the empty string.
    //
    // STATIC fields (md1 audit): the model carries this same semantic surface
    // for static fields -- a static reference field is represented by
    // { TypeKind = Reference, FieldTypeName = "..." } and emits Il2CppObject*
    // storage. MetadataLoader currently decodes INSTANCE signatures only
    // (static fields are added type-less, MetadataLoader.cs) -- emit-time
    // workarounds re-decode the metadata signature (CodeGenerator.
    // StaticFieldIsReference, TypeEmitter.StaticFieldSemantic); a loader-side
    // fix that fills TypeKind/FieldTypeName for static fields needs no model
    // change -- this FieldDef already carries it.
    public string FieldTypeName { get; init; } = "";

    // IsReference (computed): the field is a managed GC reference
    // (TypeKind == Reference). Convenience mirror of TypeKind -- the reference/
    // value distinction drives the class descriptor's refFieldBitmap and the
    // static-field extern declaration (Il2CppObject* vs int32_t).
    public bool IsReference => TypeKind == FieldTypeKind.Reference;

    // Slot index assigned by the layout pass: slot i means the field lives
    // at byte offset i*sizeof(void*) inside the instance and is represented
    // by bit i of the class's refFieldBitmap. Slot 0 is the object header
    // (the klass pointer), so instance fields occupy slots 1..N.
    public int SlotIndex { get; set; } = -1;
}

// M2 field type classification (see FieldDef.TypeKind for the contract).
public enum FieldTypeKind
{
    Int32,    // 32-bit signed value
    NativeInt, // native int (IntPtr/UIntPtr -- 0x18/0x19 ELEMENT_TYPE_I/U): full-width pointer carrier
    Reference // managed GC reference
}
