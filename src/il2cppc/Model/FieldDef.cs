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
    // p-refl2-gapmatrix (B17): FieldAttributes.InitOnly (0x20) -- net48
    // FieldInfo.IsInitOnly. A readonly instance field answers True; the
    // runtime typeinfo field flags bake it as the dedicated bit 0x80 (the
    // literal const flag is the existing bit2).
    public bool IsInitOnly { get; init; }
    // net48 reflection visibility: GetField/GetFields bind Public by default;
    // the runtime typeinfo table encodes this as il2cpp field flags bit0
    // (raudit3/proprefl).
    public bool IsPublic { get; init; }
    // refl5 (item 1): PRIVATE visibility distinction. net48 member ENUMERATION
    // inherits public + protected + internal base members but NEVER private
    // base members (probe-pinned on p-refl5-1flags: a base private field/
    // method/property is absent from every (BindingFlags) combination, even
    // NonPublic|Instance). The runtime typeinfo flags encode this as bit 0x10
    // so the flags-aware enumerators can skip private base-chain entries while
    // still listing the queried type's OWN private members.
    public bool IsPrivate { get; init; }
    // refl6b (item C): the raw ECMA 3-bit MEMBER ACCESS mask
    // (FieldAttributes & 0x7: Public=0x6, Private=0x1, Family=0x4,
    // Assembly=0x3, FamORAssem=0x5, FamANDAssem=0x2). The runtime typeinfo
    // flags bake the two exact-info states as dedicated bits (Internal -> 0x20,
    // FamORAssem -> 0x40) for the precise member predicates (IsAssembly=
    // Internal-only, IsFamilyOrAssembly=FamORAssem-only -- net48 oracle6d).
    // Zero-delta for the existing low-bit decoders (public/static/private/literal).
    public int AccessMask { get; init; }
    // p-refl2-gapmatrix (B05/E04): the field's ECMA FieldDef token
    // (0x04000000 | FieldDef row) for MemberInfo.MetadataToken on a member
    // VALUE. 0 for a synthesized field (documented boundary).
    public int MetadataToken { get; init; }
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

    // P/Invoke fixed-buffer marshalling ([MarshalAs] decode). net48 lays these
    // fields INLINE in the value type -- a ByValTStr field is a WCHAR[SizeConst]
    // buffer inside the struct (measured: Marshal.SizeOf(LOGFONTW{3*int32,
    // ByValTStr(32)}) == 76, face offset 12), NOT a GC reference. The emitted
    // C++ member is therefore a fixed char16_t array, the field is EXCLUDED
    // from the refFieldBitmap, and every store/read goes through the inline
    // buffer copy helpers. Kind None = an ordinary field with normal semantics.
    public FieldMarshalKind MarshalKind { get; init; } = FieldMarshalKind.None;
    // The [MarshalAs(SizeConst = N)] unit count (0 when the attribute is absent
    // or has no SizeConst).
    public int MarshalSizeConst { get; init; }
    // Bytes per character of a ByValTStr buffer: 2 for CharSet.Unicode / Auto
    // (WCHAR -- the only encoding this model implements), 1 for Ansi/None.
    public int MarshalCharSize { get; init; } = 1;
    // ByValArray: the unmanaged element kind (ArraySubType) when declared, else
    // empty. The layout/emission of fixed array fields is not implemented yet
    // (the emitter rejects them LOUDLY -- see TypeEmitter.LayoutValueTypeFields).
    public string MarshalElementTypeName { get; init; } = "";

    // R2b P2 (parallel VAR channel): the field signature's type byte is a
    // generic parameter (0x13 VAR / 0x1E MVAR). TypeKind/FieldTypeName keep
    // the M3 default (Int32/"") -- this flag lets a closed generic clone
    // render T as its concrete C++ storage (Il2CppObject* for reference
    // instantiations) without disturbing the definition's default-int path.
    public bool IsGenericVar { get; init; }

    // GN-14 (c): the VAR (0x13) class generic-parameter index of a
    // IsGenericVar field, or -1 when the signature is not a bare VAR (MVAR
    // 0x1E / non-generic). A closed arity>=2 clone uses this index to resolve
    // its OWN per-argument slot (CloneArgSlotIsRef) instead of collapsing every
    // VAR slot to a reference on the whole suffix.
    public int GenericVarIndex { get; init; } = -1;

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

    // Byte offset assigned by the layout pass. For a REFERENCE type this is
    // SlotIndex * PointerSize (the whole-pointer slot grid the collector
    // scans). For a VALUE type it is the net48 sequential offset the field's
    // own width/alignment implies (a struct field's real C++ member offset),
    // which is what a native P/Invoke callee reads and writes through -- the
    // slot grid alone would report an 8-byte stride for a run of int32 fields
    // (GN-20.4 F1). -1 before LayoutInstanceFields runs.
    public int ByteOffset { get; set; } = -1;

    // Value-struct capture support (user struct / Nullable<T> fields on the
    // closure display-class path).
    //
    // UserStructType is the resolved TypeDef of a FieldTypeKind.UserStruct
    // field -- filled by the layout pass (it is the authority for the struct's
    // real InstanceSize/InstanceAlignment/RefFieldBitmap). Null for every other
    // kind.
    public TypeDef? UserStructType { get; set; }

    // Nullable<T> expansion: the underlying T full name (e.g. "System.DateTime"
    // or "CapturedPt"). The member pair is derived from it:
    //   <member>__value    : the underlying T's real value shape
    //   <member>__hasValue : a bool flag (its own whole pointer slot)
    // The value occupies ceil(sizeof(T)/PointerSize) slots and the flag the
    // next one; NullableHasValueOffset is the flag's byte offset (filled by the
    // layout pass, -1 before).
    public string NullableUnderlyingName { get; init; } = "";
    public int NullableHasValueOffset { get; set; } = -1;
}

// M2 field type classification (see FieldDef.TypeKind for the contract).
public enum FieldTypeKind
{
    Int32,    // 32-bit signed value
    Int64,    // 64-bit signed value
    UInt64,   // 64-bit unsigned value
    Float,    // 32-bit floating point
    Double,   // 64-bit floating point
    NativeInt, // native int (IntPtr/UIntPtr -- 0x18/0x19 ELEMENT_TYPE_I/U): full-width pointer carrier
    Guid,      // 16-byte System.Guid value (guid.h Il2CppGuid; value semantics, never a GC reference)
    DateTime,     // 16-byte System.DateTime value (il2cpp_time.h Il2CppDateTime)
    TimeSpan,     // 8-byte System.TimeSpan value (il2cpp_time.h Il2CppTimeSpan)
    Decimal,      // 16-byte System.Decimal value (decimal.h Il2CppDecimal)
    DateTimeOffset, // 16-byte System.DateTimeOffset value (il2cpp_dtoffset.h Il2CppDateTimeOffset)
    UserStruct, // a translated user value type -- embedded as the generated C++ struct value
    Nullable,   // System.Nullable`1<T> -- expanded to a T value slot + a hasValue(bool) slot
    Reference // managed GC reference
}

// P/Invoke fixed-buffer field marshalling ([MarshalAs] decode, see
// FieldDef.MarshalKind). Values are the UnmanagedType enum ordinals:
// 23 = ByValTStr, 30 = ByValArray.
public enum FieldMarshalKind
{
    None,      // ordinary field (no [MarshalAs] fixed buffer)
    ByValTStr, // inline WCHAR[SizeConst] buffer (CharSet.Unicode/Auto)
    ByValArray // inline T[SizeConst] fixed array (decode only; emission LOUD)
}
