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
    // callvirt can lower to a direct call (devirtualization-plan.md §S3).
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
    public string DeclaringTypeName { get; init; } = "";
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
    public string? InstantiationSuffix { get; set; }

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
    public uint RefFieldBitmap { get; set; } = 0; // bit i <=> slot i is a GC reference
    public int RefFieldCount { get; set; } = 0;   // number of reference-typed fields
}
