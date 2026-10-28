namespace Il2Cpp.Transpiler.Model;

//------------------------------------------------------------------------------
// TypeDef -- one top-level translated type.
//
// M2 scope: a translated type is a reference type (class) whose instances are
// GC objects. The emitter produces:
//   - a C++ struct embedding the object header as its first member
//     (struct Calc_Point { Il2CppObject _obj; int32_t X; ... }),
//   - a static Il2CppClass descriptor table for the runtime
//     (name / instanceSize / refFieldBitmap / refFieldCount / baseClass).
//
// The instance/static field split mirrors the CLI metadata (FieldAttributes):
// static fields are process-wide storage emitted as file-scope C++ statics;
// instance fields are per-object and laid out by the type emitter.
//------------------------------------------------------------------------------
public sealed class TypeDef
{
    public required string Name { get; init; }
    public required string Namespace { get; init; }
    public string FullName => string.IsNullOrEmpty(Namespace) ? Name : Namespace + "." + Name;

    // Nested-name match: nested TypeDefs carry Namespace="" so FullName is
    // the short name, while metadata token decodes yield the '+' form
    // ("System.Environment+SpecialFolder"). Accept both.
    public bool MatchesFullName(string n) =>
        FullName == n || (IsNested && DeclaringTypeName + "+" + Name == n);

    // Static (class-level) fields: emitted as file-scope C++ statics named
    // {TypeCppName}_sf_{FieldName}. M1 scope: int32 only.
    public List<FieldDef> StaticFields { get; } = new();

    // Instance fields in metadata declaration order. The type emitter lays
    // them out (slot assignment + instanceSize + refFieldBitmap) before
    // emitting the struct; see TypeEmitter.EmitClassStruct.
    public List<FieldDef> InstanceFields { get; } = new();

    public List<MethodDef> Methods { get; } = new();

    // Reflected properties (refl3/m7d): populated by MetadataLoader from the
    // PropertyDefinition table; emitted by TypeEmitter as the typeinfo
    // property sub-table (typeinfo.h Il2CppPropertyInfo -- accessor names
    // resolve through the same methods table). Empty for property-free types
    // (the emitted table stays nullptr/0).
    public List<PropertyDef> Properties { get; } = new();

    // Reflected events (COM-C3 / RF-N1): populated by MetadataLoader from the
    // EventDefinition table (the EventMap); emitted by TypeEmitter as the
    // typeinfo event sub-table (typeinfo.h Il2CppEventInfo -- accessor names
    // resolve through the same methods table, eventIID from the folded [Guid]
    // of the event's COM source interface). Empty for event-free types (the
    // emitted table stays nullptr/0 -- zero delta).
    public List<EventDef> Events { get; } = new();

    public bool IsModuleType { get; init; }
    // Interface type flag (Task 8): interface TypeDefs have no instance fields
    // and their methods are abstract virtuals (dispatch contract only).
    public bool IsInterface { get; init; }

    // Sealed type flag (devirt S1, TypeAttributes.Sealed 0x1): a sealed class
    // cannot be subclassed, so callvirt on a receiver statically typed as this
    // class resolves to ONE statically-known implementation (devirtualization
    // to a direct call). Excluded from IsEffectivelySealed when also abstract
    // (a static class is abstract+sealed -- it has no instances to dispatch).
    public bool IsSealed { get; init; }

    // Abstract type flag (devirt S1, TypeAttributes.Abstract 0x20): abstract
    // types (and static classes) are never effectively sealed -- an abstract
    // class's virtual methods are dispatch contracts, not concrete targets.
    public bool IsAbstract { get; init; }

    // Closed-world derived count (devirt S1, G3): the number of translated
    // types whose base chain passes through this type. DerivedCount == 0 means
    // NO type in the translation set subclasses this one -- in the closed-world
    // single-assembly model (the `types` list IS all types), the type is
    // effectively sealed even without the metadata Sealed bit. Filled by
    // VTablePlanner.PlanAll after the type graph is resolved.
    public int DerivedCount { get; set; }

    // Closed-world interface implementer count (devirt S3): for an INTERFACE
    // type, the number of translated CONCRETE (non-abstract, non-value-type)
    // types that implement it (own or inherited through the base chain -- the
    // InterfaceSlotMaps key set is exactly the full implementation set, audit
    // H7 includes base-chain interfaces). An interface with exactly ONE such
    // implementer has a statically-known implementation for every dispatch:
    // any receiver assigned to the interface must be that single type, so the
    // callvirt can lower to a direct call (devirtualization-plan.md section S3).
    // Filled by VTablePlanner.PlanAll after the interface maps are built.
    public int InterfaceImplementerCount { get; set; }

    // Effective sealing (devirt S1): metadata Sealed OR closed-world no-derived
    // (and not abstract/interface). A receiver statically typed as an
    // effectively-sealed class can only ever be that class (or null), so its
    // vtable slots resolve statically.
    public bool IsEffectivelySealed => IsSealed || (DerivedCount == 0 && !IsAbstract && !IsInterface);

    // Nested-type modeling (md1 audit). M2/M3 load TOP-LEVEL types only
    // (MetadataLoader skips IsNested TypeDefs; TypeEmitter.ExpandNestedTypes
    // un-nests nested types into independent top-level entries under their
    // SHORT metadata name). This semantic surface records the nesting when a
    // loader preserves it: IsNested marks a nested type, DeclaringTypeName
    // carries the enclosing type's full name, and NestedTypes lists a type's
    // children in metadata declaration order. DeclaringType mirrors BaseType:
    // a second-pass resolution pointer to the enclosing TypeDef (null when the
    // enclosing type is not translated in this assembly).
    public bool IsNested { get; init; }
    public string DeclaringTypeName { get; init; } = "";    // refl6b (item A): the raw ECMA 3-bit TYPE visibility mask
    // (TypeAttributes & 0x7). For a NESTED type: NestedPublic=0x2,
    // NestedPrivate=0x3, NestedFamily=0x4, NestedAssembly=0x5,
    // NestedFamANDAssem=0x6, NestedFamORAssem=0x7 -- the byte the enclosing
    // typeinfo's nestedVis column carries for the runtime GetNestedType /
    // GetNestedTypes visibility filter (net48: default GetNestedTypes() is
    // Public-only). For a top-level type: NotPublic=0x0 / Public=0x1 (unused
    // by the nested table). Set in ExpandNestedTypes (the metadata TypeDef's
    // raw Attributes).
    public int VisibilityMask { get; init; }
    // refl2 (A): the type's ECMA metadata token (0x02000000 | TypeDef row) for
    // Type.MetadataToken. 0 for a synthesized type (a generic clone has no own
    // TypeDef row -- net48 reports the DEFINITION's token for a constructed
    // instance, which the folded typeof chain answers from the ldtoken).
    public int MetadataToken { get; init; }
    // refl2 (A): the RAW ECMA TypeAttributes value (TypeDef.Attributes) for
    // Type.Attributes and the exact nested-visibility predicates. 0 for a
    // synthesized clone (a constructed instance takes the definition's value
    // through the clone path).
    public int RawAttributes { get; init; }
    public TypeDef? DeclaringType { get; set; }
    public List<TypeDef> NestedTypes { get; } = new();

    // BeforeFieldInit (TypeAttributes.BeforeFieldInit): the .cctor is
    // compiler-generated (static field initializers, no explicit static
    // constructor). ECMA-335 II.7.5.4.1 permits running such initializers at
    // ANY time before the first static-field access -- this is the license
    // for the cctor-guard loop hoisting (generated-code audit #5). Precise-init
    // types (explicit .cctor) keep the lazy per-access guard.
    public bool BeforeFieldInit { get; init; }

    // Generic type definition (M3 Task 7): a generic arity > 0 marks a
    // generic type definition ("Box`1"). M3 emits ONE default instantiation
    // per generic definition with T mapped to int32_t (value-type
    // specialization, iron rule 2g); the instantiation name is
    // {TypeCppName}_int (the .NET type name without the arity suffix).
    // R77: InstantiationSuffix overrides the default "int" suffix. When
    // non-null, it is the lowercased element type name ("int", "object",
    // "byte", "long", ...) of the concrete generic instantiation this
    // TypeDef represents. Used to emit ListBuilder_1_object, Task_1_long, etc.
    public int GenericArity { get; init; }
    public bool IsGenericType => GenericArity > 0;

    // where-clause constraints (GenericParamConstraint table), per generic
    // parameter index. Read for the first time by MetadataLoader -- the
    // transpiler previously never decoded this table at all, which forced every
    // unconstrained T to be treated as int32.
    //
    // Semantics per the project's generics doctrine (see .opencode/memory.md:
    // "generics implementation doctrine"): a `where T : X` constraint is one of three equivalent
    // signals that tell us "T is at least X here" (the other two being an
    // explicit `as X` and a cast `(X)`). So the T-bearing call site is emitted
    // against X. An entry that is absent (or empty) means T is UNCONSTRAINED,
    // i.e. T is exactly System.Object at the IL level (value-type arguments
    // arrive boxed) -- NOT int32.
    //
    // Key = generic parameter index (0-based, matching the VAR index that
    // follows ELEMENT_TYPE_VAR 0x13 in a signature blob).
    // Value = the constraint type's full name, e.g. "System.IDisposable".
    // A parameter may have several constraints (e.g. `where T : A, new()`),
    // hence a list. `new()` (DefaultConstructorConstraint) carries no type and
    // is not represented here.
    public List<string>[]? GenericConstraints { get; init; }

    /// Constraint names for one generic parameter index, or null when that
    /// parameter is unconstrained (or constraints were never decoded).
    public List<string>? ConstraintsFor(int genericParamIndex)
        => GenericConstraints is { } all
           && (uint)genericParamIndex < (uint)all.Length
            ? all[genericParamIndex]
            : null;

    /// <summary>
    /// Nested (not a sibling) so it cannot break the enclosing class's braces.
    /// Decodes the GenericParamConstraint table (ECMA-335 II.22.21).
/// This table had never been read anywhere in the transpiler before, which is
/// why every unconstrained `T` was forced into an int32 slot.
/// </summary>
public static class GenericConstraintReader
{
    /// Reads the where-clauses of a generic type/method definition.
    /// Result is indexed by generic parameter index; a null/empty entry means
    /// that parameter is unconstrained (T == System.Object at the IL level).
    // `resolve` is supplied by the caller (MetadataLoader.ResolveTypeName) so this
    // helper stays independent of whatever TypeDefOrRef resolution the loader uses.
    public static List<string>[]? Read(
        System.Reflection.Metadata.MetadataReader md,
        System.Reflection.Metadata.GenericParameterHandleCollection handles,
        System.Func<System.Reflection.Metadata.EntityHandle, string> resolve)
    {
        var count = handles.Count;
        if (count == 0) return null;

        var result = new List<string>[count];
        var any = false;

        foreach (var h in handles)
        {
            var gp = md.GetGenericParameter(h);
            var index = gp.Index;
            if ((uint)index >= (uint)count) continue;

            var names = new List<string>();
            foreach (var ch in gp.GetConstraints())
            {
                // A constraint row is (Owner GenericParam, Type TypeDefOrRef).
                // `new()` (DefaultConstructorConstraint) has no type entry, so it
                // simply contributes nothing here -- that is correct.
                var c = md.GetGenericParameterConstraint(ch);
                var name = resolve(c.Type);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }

            if (names.Count > 0) { result[index] = names; any = true; }
        }

        return any ? result : null;
    }
}

    public string? InstantiationSuffix { get; set; }

    // GN-13 fix4: the PER-ARGUMENT components of InstantiationSuffix for an
    // arity>=2 closed clone (e.g. Pair<int[,],string> -> ["[int2]","string"]).
    // InstantiationSuffix is their "_"-join, but a component may itself
    // contain '_' (nested generic "System.Nullable`1_int", user name
    // "Cases.A_B", array token), so splitting the joined suffix on '_' is
    // ambiguous. Inside the clone's method body an open self/owner TypeSpec
    // (Pair`2<!0,!1>) decodes each VAR (0x13) argument by INDEX; the
    // per-argument array lets the VAR arms resolve parts[idx] instead of
    // echoing the whole suffix once per argument (the GN-13 fix4 doubling).
    // Null for the definition and for arity-1 clones (they keep the legacy
    // single-suffix split semantics). Set by CodeGenerator.EnsureClone.
    public string[]? InstantiationSuffixParts { get; set; }

    // GN-14 (c): whether a VAR (0x13) slot at generic-parameter index
    // `varIndex` is a managed REFERENCE in THIS closed clone. Arity>=2 with a
    // per-argument array resolves the EXACT argument ("Pair<int[,],int>" ->
    // index 0 "[int2]" ref, index 1 "int" value); arity-1 / an invalid index
    // keeps the legacy whole-suffix classifier (suffix != "int"), so
    // Box<long>/struct arity-1 behavior is untouched. The classifier stays
    // `!= "int"` (an array token "[int2]" is a reference; a user struct/full
    // name is likewise non-"int").
    public bool CloneArgSlotIsRef(int varIndex)
    {
        var parts = InstantiationSuffixParts;
        if (parts is { Length: > 1 } && (uint)varIndex < (uint)parts.Length)
            return parts[varIndex] != "int";
        return InstantiationSuffix != null && InstantiationSuffix != "int";
    }

    // GN-14 (c): the SAME rule as an instance method, usable on the CALL side
    // where only the decoded InstantiationSuffix (+ per-argument parts) is at
    // hand (no TypeDef clone). See CloneArgSlotIsRef(int).
    public static bool CloneArgSlotIsRef(string? suffix, string[]? parts, int varIndex)
    {
        if (parts is { Length: > 1 } && (uint)varIndex < (uint)parts.Length)
            return parts[varIndex] != "int";
        return suffix != null && suffix != "int";
    }

    // R2b (R77 clone identity): a synthetic closed instantiation shares the
    // DEFINITION's FullName (the C++ symbol/hash channels key on the
    // definition name -- see R1/R2b design), so FullName cannot serve as the
    // planner's dedup key. InstantiationKey disambiguates: the default
    // (definition / int) instantiation is FullName, a closed clone is
    // FullName + "|" + suffix.
    public string InstantiationKey => InstantiationSuffix == null ? FullName : FullName + "|" + InstantiationSuffix;

    // [Flags] attribute (flagsenum): the REAL [Flags] bit from the metadata
    // CustomAttribute table (System.FlagsAttribute on the TypeDef), NOT a
    // guess from the member shape. net48 semantics: [Flags] is an attribute
    // -- an enum that declares composite members (AB = A|B, All = A|B|C) is
    // still a flags enum, and a plain enum whose members happen to be
    // {Zero=0, One=1, Two=2} is NOT one. Emitted as the isFlags column of the
    // runtime Il2CppEnumInfo (enumext.h) so the ToString decomposition keys
    // on the attribute instead of the shape heuristic (the silent-wrong-value
    // defect this field closes).
    public bool IsFlags { get; init; }

    // Value type (M3 Task 11): a struct whose instances are plain C++ values
    // (NO object header, NO GC allocation). Local variables hold the value
    // inline; field access is '.', not '->'; newobj initializes in place.
    // Value-type descriptors are NOT emitted (they are not GC classes).
    public bool IsValueType { get; init; }

    // Full ECMA name of the base type ("System.Object" for roots; M2 has no
    // user-declared inheritance yet -- Tasks 7+ add class hierarchies, and
    // this property becomes the descriptor's baseClass slot).
    public string BaseTypeName { get; init; } = "System.Object";

    // Resolved base type reference (Task 7 type graph): the TypeDef whose
    // FullName == BaseTypeName, or null when the base is a runtime/BCL type
    // (System.Object). Filled by MetadataLoader's second pass after all
    // TypeDefs are built -- the vtable planner walks this chain.
    public TypeDef? BaseType { get; set; }

    // Interfaces implemented by this type (Task 8): full ECMA names, in
    // metadata InterfaceImpl order. Resolved to TypeDef objects (when the
    // interface is translated in this assembly) by the second pass.
    public List<string> InterfaceNames { get; } = new();
    public List<TypeDef> InterfaceTypes { get; } = new();

    // Virtual-slot table (Task 7): assigned by VTablePlanner; index 0..N are
    // the slots inherited/appended in base-first order. Mirrored into the
    // Il2CppClass descriptor as the vtable pointer.
    public List<MethodDef> VirtualMethods { get; } = new();

    // Interface dispatch maps (Task 8): for each implemented interface, the
    // array mapping interface-slot -> this type's vtable-slot. Filled by
    // VTablePlanner.BuildInterfaceMaps. Emitted as static tables referenced
    // from the type's Il2CppClass descriptor.
    public Dictionary<TypeDef, int[]> InterfaceSlotMaps { get; } = new();

    // Layout result (filled by TypeEmitter.LayoutInstanceFields):
    public int InstanceSize { get; set; } = 0;   // bytes per instance, >= sizeof(Il2CppObject)
    // Natural alignment of a VALUE type's sequential layout (the largest member
    // alignment). The repeated field of a user struct embedded in a reference
    // type (closure display class) must start on this boundary. 1 for a
    // reference type / fieldless struct.
    public int InstanceAlignment { get; set; } = 1;
    public uint RefFieldBitmap { get; set; } = 0; // bit i <=> slot i is a GC reference
    public int RefFieldCount { get; set; } = 0;   // number of reference-typed fields

    // G1 (cross-platform arch): the target pointer width this type's layout was
    // computed for (--arch x86/arm32 -> 4, x64/arm64 -> 8). Defaults to the HOST
    // width (IntPtr.Size) so direct emitter callers (unit tests) keep the
    // historical x64 layout; CodeGenerator.Generate stamps every type with the
    // run's --arch pointer size before any layout/offset/bitmap emission.
    public int PointerSize { get; set; } = IntPtr.Size;
}
