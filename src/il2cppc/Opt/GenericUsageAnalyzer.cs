using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Opt;

//------------------------------------------------------------------------------
// GenericUsageAnalyzer -- compile-pruning decision for generic-type sharing
// (user-mandated architecture, see .opencode/tmp/generic-share/GENERIC-SHARE-DESIGN.md
// section 2 / section 5.4).
//
// For EVERY generic definition the transpiler must decide whether it can be
// emitted as a SINGLE object-boxed shared implementation (closed
// instantiations collapse onto one "object" clone) or must keep per-instance
// specialization.
//
// Multi-type templates `<T1, T2, ...>` are decided PER GENERIC PARAMETER
// (the share mask). The verdict is deliberately CONSERVATIVE:
//   * ANY uncertain case, MVAR (method-level), undecodable signature, generic
//     method call, or a directed constraint marks the whole definition to stay
//     on the per-instance path (today's behavior) -- sharing is ONLY granted
//     when every parameter is provably object-compatible.
//   * A directed use of parameter i marks ONLY that parameter non-shareable;
//     with any directed parameter the definition keeps per-instance semantics
//     in the conservative first phase (per-parameter partial shapes are the
//     later P-G4 optimization).
// This guarantees MULTI-T templates never mis-shrink: a definition is either
// shared as plain object or falls back to exact per-instance emission, never a
// wrong in-between.
//
// The verdict is cached per definition by the caller.
//------------------------------------------------------------------------------
public static class GenericUsageAnalyzer
{
    //--------------------------------------------------------------------------
    // Active-share set (generation-session scope). During code generation this
    // holds the FULL NAMES ("Box`1", "Pair`2") of every definition whose share
    // mask was accepted -- the coding side (MethodRefResolver/StackFold/
    // CodeGenerator GenericInstantiationName) collapses ANY reference to those
    // definitions onto the single "object" instantiation name. Set by
    // CodeGenerator.Generate before emission; null = sharing disabled.
    //--------------------------------------------------------------------------
    public static HashSet<string>? SharedGenericDefs { get; set; }

    //--------------------------------------------------------------------------
    // GN-N4 D5: the FULL NAMES of every USER VALUE TYPE (user struct / user
    // enum -- a TypeDef whose IsValueType is true) in this assembly. Codegen
    // populates it from the loaded type list before emission; the shared
    // collapse consults it so a closed instantiation whose generic argument is
    // a user value type keeps per-instance emission (the shared object clone
    // boxes value args with the primitive int32 helper -- struct/enum payloads
    // would be corrupted). Mirrors SharedGenericDefs' session-scope contract.
    //--------------------------------------------------------------------------
    public static HashSet<string>? UserValueTypeFullNames { get; set; }

    //--------------------------------------------------------------------------
    // GN-N4 stage-2 (by-value MVAR user-struct per-instance): the set of
    // USER VALUE-TYPE generic arguments observed at MethodSpec call sites
    // (a method-generic call `Id<Pt>` -- the user struct is a CONCRETE
    // by-value MVAR). CodeGenerator's pre-emission MethodSpec scan collects
    // these; the pass-through generic-method variant emission keys on them to
    // synthesize {name}_Cases_Pt concrete-struct variants (Id<Pt> byte-exact
    // needs the struct slot). Null/empty = no struct MVAR surface.
    //--------------------------------------------------------------------------
    public static HashSet<string>? MethodSpecUserStructArgs { get; set; }

    //--------------------------------------------------------------------------
    // Collapse a closed instantiation's argument list onto plain "object"
    // when the definition is in the active share set (all parameters are
    // shareable -- the mask is all-true). Mutates args in place; returns true
    // when the instantiation was collapsed (caller emits {def_name}_{object
    // ...}).
    //--------------------------------------------------------------------------
    //--------------------------------------------------------------------------
    // IsArrayArgToken -- whether one encoded generic-argument token denotes an
    // ARRAY argument (G1: element-aware SZARRAY encoding -- the producers emit
    // "[<elementSuffix>]" for simple-element arrays; see the GenericInstantiation
    // Name 0x1D arms). Managed type full names never contain '[' / ']', so the
    // bracket marker is unambiguous.
    //--------------------------------------------------------------------------
    public static bool IsArrayArgToken(string arg) =>
        arg.Length >= 2 && arg[0] == '[' && arg[^1] == ']';

    public static bool CollapseInstantiation(string defName, string[] args)
    {
        if (SharedGenericDefs == null || !SharedGenericDefs.Contains(defName)) return false;
        // The all-"int" instantiation is the M3 definition default itself
        // (TypeCppName "_int"); it is NOT collapsed -- today's per-instance
        // path stays for it (EnsureClone's gSuffix=="int" early return is the
        // mirror rule). Every OTHER closed instantiation collapses: all
        // parameters become "object" (the single shared clone).
        if (args.All(a => a == "int")) return false;
        // G1 (C1): ARRAY arguments keep per-instance element-aware emission.
        // The array element type is observable (distinct closed types:
        // Foo<int[]> != Foo<string[]>), so an array argument must NOT collapse
        // onto the shared object clone -- it gets its own element-aware clone.
        if (args.Any(IsArrayArgToken)) return false;
        // GN-N4 D5: a USER VALUE TYPE argument (user struct / user enum -- a
        // TypeDef in this assembly with IsValueType) keeps per-instance. The
        // shared object clone stores T slots as Il2CppObject* and boxes value
        // args with the PRIMITIVE int32 box helper -- a struct payload would
        // be corrupted (or fail to compile). Mirroring the array rule, the
        // struct/enum instantiation gets its own concrete {def}_<StructCppName>
        // clone (the GN-N4 stage-2 per-instance variant) instead of collapsing.
        // User REFERENCE-typed args still collapse (Il2CppObject* is correct).
        // The value-type set is populated by CodeGenerator before emission
        // (session scope, exactly like SharedGenericDefs).
        if (UserValueTypeFullNames != null)
        {
            foreach (var a in args)
            {
                if (UserValueTypeFullNames.Contains(a)) return false;
            }
        }
        for (var i = 0; i < args.Length; i++) args[i] = "object";
        return true;
    }

    //--------------------------------------------------------------------------
    // GN-N6 obs2: does a '-' joined generic-instance SUFFIX token contain a
    // USER VALUE TYPE argument as a whole arg? Mirrors CollapseInstantiation's
    // per-arg full-name match so EnsureClone (clone-side suffix scan) and the
    // call-side collapse decide identically. Matching is against the FULL NAME
    // ("Cases.A_B" -- a type name may itself contain '_', GN-N5), so the
    // suffix must NOT be naively Split('_') (that would chop "Cases.A_B" into
    // "Cases"/"A"/"B" and miss the set). Checks each full name as a bounded
    // '_'-delimited arg segment.
    //--------------------------------------------------------------------------
    public static bool SuffixContainsUserValueType(string suffix)
    {
        if (UserValueTypeFullNames == null || suffix == null) return false;
        if (suffix.Length == 0) return false;
        foreach (var u in UserValueTypeFullNames)
        {
            if (u.Length == 0) continue;
            // Whole suffix == one arg.
            if (suffix == u) return true;
            // Leading, trailing, or embedded bounded arg segment.
            if (suffix.StartsWith(u + "_", StringComparison.Ordinal)) return true;
            if (suffix.EndsWith("_" + u, StringComparison.Ordinal)) return true;
            if (suffix.IndexOf("_" + u + "_", StringComparison.Ordinal) >= 0) return true;
        }
        return false;
    }

    // Object members a shared clone can still call directly (the slot is a
    // real object -- these dispatch through object semantics, no exact type).
    private static readonly HashSet<string> s_objectMembers = new(StringComparer.Ordinal)
    {
        "Equals", "GetHashCode", "ToString", "GetType", "MemberwiseClone",
        "get_Type", "ReferenceEquals",
    };

    //--------------------------------------------------------------------------
    // Public verdict: the per-generic-parameter share mask for 'def'.
    //   result[i] == true  -> parameter i may collapse onto an object slot;
    //   result[i] == false -> parameter i is used directionally and must
    //                         keep concrete per-instance emission.
    // Returns null when the WHOLE definition must stay on the per-instance
    // path (interface/value/constraint/method-generic/undecodable body).
    //--------------------------------------------------------------------------
    public static bool[]? GetShareMask(
        MetadataReader md, TypeDef def, Func<MethodDef, MethodBodyInfo?> bodyReader)
    {
        // P-G3: a non-generic class whose METHODS are generic (SwapRef<T> --
        // method-level sharing, GENERIC-SHARE-DESIGN.md section 5) is a sharing
        // candidate too; a class with no generics at all never shares.
        var hasGenericMethod = def.Methods.Any(m => m.GenericArity > 0);
        if (def.GenericArity <= 0 && !hasGenericMethod) return null;
        if (def.IsInterface || def.IsValueType) return null;

        // (A) Constraints: a directed constraint kills THAT parameter.
        var kindPerParam = FindGenericParameterKinds(md, def);
        var mask = new bool[def.GenericArity];
        var anyDirected = false;
        for (var i = 0; i < def.GenericArity; i++)
        {
            var directed = false;
            if (kindPerParam != null && i < kindPerParam.Length
                && (kindPerParam[i] & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                directed = true; // `where T : struct`
            var cons = def.ConstraintsFor(i);
            if (cons != null && cons.Count > 0 &&
                cons.Any(n => !string.IsNullOrEmpty(n) && n != "System.Object"))
                directed = true; // `where T : SomeType/IFace` -> directed
            mask[i] = !directed;
            anyDirected |= directed;
        }

        // (B) Method-level MVAR scan (P-G3): a generic METHOD is shareable only
        // when every MVAR parameter is a byref T slot (the object ABI renders
        // them Il2CppObject**) and the body only uses the MVAR in pure slot
        // moves (ldobj/stobj on the byref slot). Any by-value MVAR param, MVAR
        // return, or directed MVAR use keeps the whole definition per-instance.
        foreach (var m in def.Methods)
        {
            if (m.GenericArity <= 0 || m.IsPInvoke || m.IsAbstract) continue;
            var bio = bodyReader(m);
            if (bio == null) return null;   // undecodable -> conservative
            if (!MethodMvarShareable(md, m, bio)) return null;
        }

        // (C) Class-level body scan: mark parameters used directionally anywhere.
        // Only arity>0 defs have class-level VARs to track; an arity-0 def's
        // body carries ONLY MVAR TypeSpecs (its generic methods), which the
        // method-level scan above already judged.
        if (def.GenericArity > 0)
        {
            foreach (var m in def.Methods)
            {
                if (m.IsPInvoke || m.IsAbstract) continue;
                var bio = bodyReader(m);
                if (bio == null) return null;           // undecodable -> conservative
                var directed = ScanBodyDirectedParams(md, bio.IlBytes);
                if (directed == null) return null;      // MVAR / generic-method call
                foreach (var idx in directed)
                {
                    if ((uint)idx < (uint)mask.Length) { mask[idx] = false; anyDirected = true; }
                }
            }
        }
        // Conservative first phase: any directed parameter keeps the WHOLE
        // definition on the per-instance path (all-or-nothing sharing).
        return anyDirected ? null : mask;
    }

    // Whole-definition verdict (P-G1 short form): shareable iff EVERY parameter
    // is object-compatible.
    public static bool IsObjectShareable(
        MetadataReader md, TypeDef def, Func<MethodDef, MethodBodyInfo?> bodyReader)
        => GetShareMask(md, def, bodyReader) is { } m && m.All(x => x);

    private static GenericParameterAttributes[]? FindGenericParameterKinds(
        MetadataReader md, TypeDef def)
    {
        foreach (var tdh in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(tdh);
            if (md.GetString(td.Namespace) != def.Namespace
                || md.GetString(td.Name) != def.Name) continue;
            var gps = td.GetGenericParameters();
            if (gps.Count == 0) return null;
            var arr = new GenericParameterAttributes[gps.Count];
            foreach (var h in gps)
            {
                var gp = md.GetGenericParameter(h);
                if ((uint)gp.Index < (uint)arr.Length) arr[gp.Index] = gp.Attributes;
            }
            return arr;
        }
        return null;
    }

    //--------------------------------------------------------------------------
    // Method-level MVAR compatibility scan (P-G3, GENERIC-SHARE-DESIGN.md section 5):
    // a generic METHOD (`SwapRef<T>`) is shareable under the object ABI only
    // when:
    //   (1) every MVAR parameter is a BYREF T slot (0x10 BYREF wrapping 0x1E) --
    //      the shared clone renders those as Il2CppObject** object slots;
    //   (2) the method has no MVAR return (a shared MVAR return needs the
    //      return-slot unbox bridge -- future work, D9);
    //   (3) every body IL use of an MVAR TypeSpec is a pure slot move
    //      (ldobj/stobj/cpobj/initobj on the byref slot) -- arithmetic, member
    //      calls, casts, newarr/sizeof on T are directed and keep the
    //      definition per-instance (conservative: never mis-share).
    //--------------------------------------------------------------------------
    private static bool MethodMvarShareable(MetadataReader md, MethodDef m, MethodBodyInfo bio)
    {
        var (returnIsVar, paramIsVar, paramIsByrefVar) =
            MethodSignatureCoder.DecodeVarFlags(md, md.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.Handle(bio.MethodToken)).Signature);
        // (1) D5: an MVAR return no longer blocks method-level sharing -- the
        // shared object ABI (GENERIC-SHARE-DESIGN.md section 5) renders the return
        // slot as the boxed T object (Il2CppObject*), and the caller unboxes by
        // the MethodSpec concrete argument (D5 return-unbox bridge). The
        // conservatism is preserved by rules (2)/(3) below: byref-only MVAR
        // params AND a body whose only MVAR uses are pure slot moves.
        // (2) EVERY MVAR parameter must be BYREF-wrapped (else by-value MVAR
        // slots / arithmetic bodies like Add<T> would be mis-shared).
        for (var i = 0; i < paramIsVar.Length; i++)
        {
            if (paramIsVar[i] && !paramIsByrefVar[i]) return false;
        }
        // (3) Body: MVAR TypeSpec uses must be pure slot moves, and a generic
        // METHOD call (MethodSpec) / field access may not pass this method's
        // own MVAR onward (P-G4 F3 -- a shared body calling a per-instance
        // generic method with T as its type argument would need the exact
        // type, so those uses are directed).
        foreach (var ins in IlDecoder.DecodeMethod(bio.IlBytes, md))
        {
            switch (ins.OperandKind)
            {
                case IlOperandKind.TypeToken:
                    if (!TypeSpecCarriesMvar(md, (int)ins.Operand)) continue;
                    switch (ins.OpCode)
                    {
                        case ILOpCode.Ldobj:
                        case ILOpCode.Stobj:
                        case ILOpCode.Cpobj:
                        case ILOpCode.Initobj:
                            continue; // byref-slot move: object-compatible
                        default:
                            return false; // box/unbox/castclass/newarr/sizeof/...: directed
                    }
                case IlOperandKind.MethodToken:
                    // P-G4 F3 / P-G5 FINDING-1: a generic-method call whose
                    // MethodSpec genArgs carry this method's MVAR (0x1E), OR an
                    // instance constructed/`new`ed through a MemberRef whose
                    // Parent TypeSpec carries the MVAR (new Box2`1<!!0>(), an
                    // instance call on it), passes the MVAR onward -> directed,
                    // not shareable.
                    if (MethodTokenCarriesMvar(md, (int)ins.Operand)) return false;
                    continue;
                case IlOperandKind.FieldToken:
                    // P-G4 F3: a field whose DECLARED TYPE carries this
                    // method's MVAR (e.g. `T[] field` / `Holder`1<!!0> field`)
                    // is a concrete-slot access -> directed, not shareable.
                    if (FieldTokenDeclaredTypeCarriesMvar(md, (int)ins.Operand)) return false;
                    continue;
            }
        }
        return true;
    }

    // P-G4 F3 / P-G5 FINDING-1: whether a MethodToken operand carries a
    // METHOD-level MVAR (0x1E) in a shape that would require the exact type
    // inside a shared method body. Two shapes:
    //   * MethodSpecification: the GENERICINST blob (0x0A) -- a generic-method
    //     call such as `Wrapper.Pass<!!0>(...)` passes this method's MVAR as
    //     the exact type argument -> directed;
    //   * MemberReference whose PARENT is a TypeSpecification carrying the
    //     MVAR (P-G5 FINDING-1) -- `new Box2`1<!!0>()` / an instance call on
    //     `Box2`1<!!0>` instantiates the receiver with this method's MVAR ->
    //     directed. Mirrors the FieldToken branch's parent-TypeSpec scan
    //     (FieldTokenDeclaredTypeCarriesMvar).
    // Any other shape (plain method, concrete receiver) returns false (no MVAR
    // carried); undecodable -> false (conservative scan side).
    private static bool MethodTokenCarriesMvar(MetadataReader md, int token)
    {
        var h = MetadataTokens.Handle(token);
        try
        {
            switch (h.Kind)
            {
                case HandleKind.MethodSpecification:
                {
                    var genSig = md.GetBlobReader(md.GetMethodSpecification((MethodSpecificationHandle)h).Signature);
                    if (genSig.ReadByte() != 0x0A) return false; // not GENERICINST
                    var argc = genSig.ReadCompressedInteger();
                    for (var i = 0; i < argc; i++)
                    {
                        if (TypeSigMvarScanCore(md, ref genSig)) return true;
                    }
                    return false;
                }
                case HandleKind.MemberReference:
                {
                    var mr = md.GetMemberReference((MemberReferenceHandle)h);
                    // Parent TypeSpec instantiation (Box2`1<!!0>::.ctor)
                    // instantiates the receiver with this method's own MVAR.
                    if (mr.Parent.Kind == HandleKind.TypeSpecification)
                    {
                        var parentSig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)mr.Parent).Signature);
                        if (TypeSigMvarScanCore(md, ref parentSig)) return true;
                    }
                    return false;
                }
                default:
                    return false;
            }
        }
        catch (BadImageFormatException)
        {
            return false; // undecodable -> treat as no MVAR (conservative scan side)
        }
    }

    // P-G4 F3: whether a FieldToken operand's field carries a METHOD-level
    // MVAR (0x1E) in either the field's DECLARED TYPE (its FieldSig type --
    // `T[] cache`, `Holder`1<!!0> slot`) or, for a MemberReference field, the
    // DECLARING TYPE's instantiation (the Parent TypeSpec -- `Box`1<!!0>.field`
    // in a generic-method body carries the MVAR as the instance arg). Either
    // shape is a concrete-slot access in a shared body -> the definition must
    // stay per-instance.
    private static bool FieldTokenDeclaredTypeCarriesMvar(MetadataReader md, int token)
    {
        var h = MetadataTokens.Handle(token);
        try
        {
            switch (h.Kind)
            {
                case HandleKind.FieldDefinition:
                {
                    var fdSig = md.GetBlobReader(md.GetFieldDefinition((FieldDefinitionHandle)h).Signature);
                    if (fdSig.ReadByte() != 0x06) return false; // FieldSig header
                    return TypeSigMvarScanCore(md, ref fdSig);
                }
                case HandleKind.MemberReference:
                {
                    var mr = md.GetMemberReference((MemberReferenceHandle)h);
                    // Parent TypeSpec instantiation (Box`1<!!0>) carries the MVAR.
                    if (mr.Parent.Kind == HandleKind.TypeSpecification)
                    {
                        var parentSig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)mr.Parent).Signature);
                        if (TypeSigMvarScanCore(md, ref parentSig)) return true;
                    }
                    var mrSig = md.GetBlobReader(mr.Signature);
                    if (mrSig.ReadByte() != 0x06) return false; // FieldSig header
                    return TypeSigMvarScanCore(md, ref mrSig);
                }
                default:
                    return false;
            }
        }
        catch (BadImageFormatException)
        {
            return false; // undecodable -> treat as no MVAR (conservative scan side)
        }
    }

    // Whether a TypeSpec token's signature blob references a METHOD-level MVAR
    // (0x1E) anywhere (the definition-body shape for a byref T ldobj/stobj).
    private static bool TypeSpecCarriesMvar(MetadataReader md, int token)
    {
        var h = MetadataTokens.Handle(token);
        if (h.Kind != HandleKind.TypeSpecification) return false;
        var sig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)h).Signature);
        try
        {
            return TypeSigMvarScanCore(md, ref sig);
        }
        catch (BadImageFormatException)
        {
            return false; // undecodable -> treat as no MVAR (conservative scan side)
        }
    }

    private static bool TypeSigMvarScanCore(MetadataReader md, ref BlobReader sig)
    {
        var b = sig.ReadByte();
        switch (b)
        {
            case 0x13: // VAR -- class-level generic parameter
                _ = sig.ReadCompressedInteger();
                return false;
            case 0x1E: // MVAR -- method-level generic parameter
                _ = sig.ReadCompressedInteger();
                return true;
            case 0x11: // VALUETYPE
            case 0x12: // CLASS
            case 0x40: // nested type-handle
                _ = sig.ReadTypeHandle();
                return false;
            case 0x15: // GenericInst
            {
                _ = sig.ReadByte();
                _ = sig.ReadTypeHandle();
                var n = sig.ReadCompressedInteger();
                for (var i = 0; i < n; i++)
                    if (TypeSigMvarScanCore(md, ref sig)) return true;
                return false;
            }
            case 0x1B:
                return TypeSigMvarScanCore(md, ref sig);
            case 0x1D:
            case 0x14:
                return TypeSigMvarScanCore(md, ref sig);
            case 0x1F or 0x20:
                _ = sig.ReadCompressedInteger();
                return TypeSigMvarScanCore(md, ref sig);
            default:
                return false;
        }
    }

    //--------------------------------------------------------------------------
    // Body scan: the set of generic-parameter indexes used in a DIRECTED way
    // anywhere in this body (empty set = fully object-compatible).
    // Returns null when the WHOLE definition must stay per-instance (method-level
    // MVAR, generic-method call, or an undecodable signature -- conservative).
    //--------------------------------------------------------------------------
    private static HashSet<int>? ScanBodyDirectedParams(MetadataReader md, byte[] il)
    {
        var directed = new HashSet<int>();
        foreach (var ins in IlDecoder.DecodeMethod(il, md))
        {
            switch (ins.OperandKind)
            {
                case IlOperandKind.TypeToken:
                {
                    var idx = TypeTokenVarIndexes(md, (int)ins.Operand);
                    if (idx == null) return null;   // MVAR / undecodable
                    directed.UnionWith(idx);
                    break;
                }
                case IlOperandKind.MethodToken:
                {
                    var idx = MethodTokenVarIndexes(md, (int)ins.Operand);
                    if (idx == null) return null;   // generic-method call
                    directed.UnionWith(idx);
                    break;
                }
                case IlOperandKind.FieldToken:
                {
                    // ldfld/stfld/ldsfld*/ldflda = compatible slot moves; but
                    // ldtoken (typeof(T)) reuses this kind and may carry a VAR
                    // TypeSpec -- directed until the exact-type argument route lands.
                    var idx = TokenVarIndexes(md, (int)ins.Operand);
                    if (idx == null) return null;
                    directed.UnionWith(idx);
                    break;
                }
            }
        }
        return directed;
    }

    // A TypeToken operand (box/unbox/castclass/isinst/newarr/ldelem/stelem/...):
    //   * a VAR-bearing TypeSpec marks those parameters directed;
    //   * a CONCRETE (non-System.Object) target also blocks the whole
    //     definition (conservative: a cast against a non-object type cannot be
    //     proven object-compatible by this pass, so sharing is refused rather
    //     than risk a wrong object-slot semantic);
    //   * System.Object targets are object-compatible (empty set).
    private static HashSet<int>? TypeTokenVarIndexes(MetadataReader md, int token)
    {
        var h = MetadataTokens.Handle(token);
        switch (h.Kind)
        {
            case HandleKind.TypeSpecification:
            {
                var idx = TypeSigVarIndexes(md, md.GetTypeSpecification((TypeSpecificationHandle)h).Signature);
                if (idx == null) return null;
                return idx.Count > 0 ? idx : null; // concrete object type == conservative block
            }
            case HandleKind.TypeDefinition:
            case HandleKind.TypeReference:
                return IsSystemObjectTarget(md, h) ? new HashSet<int>() : null;
            default:
                return new HashSet<int>();
        }
    }

    private static bool IsSystemObjectTarget(MetadataReader md, Handle h)
    {
        try
        {
            switch (h.Kind)
            {
                case HandleKind.TypeDefinition:
                    return md.GetFullName((TypeDefinitionHandle)h) == "System.Object";
                case HandleKind.TypeReference:
                    return md.GetFullName((TypeReferenceHandle)h) == "System.Object";
                default:
                    return false;
            }
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    // A method token whose receiver lives on T (a VAR-bearing declared type)
    // marks those parameters directed, EXCEPT the System.Object member family
    // (safe on the object slot: Equals/GetHashCode/ToString/GetType/...).
    // A generic-method call (MethodSpecification) blocks the whole definition.
    private static HashSet<int>? MethodTokenVarIndexes(MetadataReader md, int token)
    {
        var h = MetadataTokens.Handle(token);
        if (h.Kind == HandleKind.MethodDefinition) return new HashSet<int>();
        if (h.Kind == HandleKind.MethodSpecification) return null; // P-G3
        if (h.Kind != HandleKind.MemberReference) return new HashSet<int>();
        var mr = md.GetMemberReference((MemberReferenceHandle)h);
        if (mr.Parent.Kind != HandleKind.TypeSpecification) return new HashSet<int>();
        var idx = TypeSigVarIndexes(md, md.GetTypeSpecification((TypeSpecificationHandle)mr.Parent).Signature);
        if (idx == null) return null;
        if (idx.Count == 0) return idx;
        if (s_objectMembers.Contains(md.GetString(mr.Name)))
            return new HashSet<int>(); // object-member call is object-compatible
        return idx;
    }

    // FieldToken operands: ldfld/stfld are compatible; ldtoken (typeof(T))
    // reuses this kind and may carry a VAR TypeSpec.
    private static HashSet<int>? TokenVarIndexes(MetadataReader md, int token)
    {
        var h = MetadataTokens.Handle(token);
        if (h.Kind != HandleKind.TypeSpecification) return new HashSet<int>();
        return TypeSigVarIndexes(md, md.GetTypeSpecification((TypeSpecificationHandle)h).Signature);
    }

    //--------------------------------------------------------------------------
    // TypeSigVarIndexes -- collect the generic-parameter indexes referenced by a
    // type signature blob (VAR 0x13 + compressed index; nested GenericInst args
    // included). Returns null when the signature carries a METHOD-level MVAR
    // (0x1E) or cannot be decoded -- that blocks the whole definition.
    //--------------------------------------------------------------------------
    private static HashSet<int>? TypeSigVarIndexes(MetadataReader md, in BlobHandle bh)
    {
        var sig = md.GetBlobReader(bh);
        try
        {
            return TypeSigVarIndexesCore(md, ref sig);
        }
        catch (BadImageFormatException)
        {
            return null; // undecodable -> conservative
        }
    }

    private static HashSet<int>? TypeSigVarIndexesCore(MetadataReader md, ref BlobReader sig)
    {
        var b = sig.ReadByte();
        switch (b)
        {
            case 0x13: // VAR -- class-level generic parameter
            {
                var index = sig.ReadCompressedInteger();
                var set = new HashSet<int> { index };
                return set;
            }
            case 0x1E: // MVAR -- method-level generic parameter (P-G3)
                return null;
            case 0x11: // VALUETYPE
            case 0x12: // CLASS
            case 0x40: // nested type-handle
                _ = sig.ReadTypeHandle();
                return new HashSet<int>();
            case 0x15: // GenericInst: marker + def + arg count + args (VAR may nest)
            {
                _ = sig.ReadByte(); // CLASS/VALUETYPE marker (def side)
                _ = sig.ReadTypeHandle();
                var n = sig.ReadCompressedInteger();
                var set = new HashSet<int>();
                for (var i = 0; i < n; i++)
                {
                    var arg = TypeSigVarIndexesCore(md, ref sig);
                    if (arg == null) return null;
                    set.UnionWith(arg);
                }
                return set;
            }
            case 0x1B: // GenericInst (knot marker variant)
                return TypeSigVarIndexesCore(md, ref sig);
            case 0x1D: // SZARRAY
            case 0x14: // ARRAY: element + rank + bounds(skippable)
            {
                var idx = TypeSigVarIndexesCore(md, ref sig);
                if (idx == null) return null;
                if (b == 0x14)
                {
                    _ = sig.ReadCompressedInteger(); // rank
                    var nSizes = sig.ReadCompressedInteger();
                    for (var i = 0; i < nSizes; i++) _ = sig.ReadCompressedInteger();
                    var nLo = sig.ReadCompressedInteger();
                    for (var i = 0; i < nLo; i++) _ = sig.ReadCompressedInteger();
                }
                return idx;
            }
            case 0x1F: // CMOD_OPT / CMOD_REQD: skip the mod (may recurse)
                return TypeSigVarIndexesCore(md, ref sig);
            default:
                return new HashSet<int>(); // primitives / string / object / fnptr / typedref
        }
    }
}
