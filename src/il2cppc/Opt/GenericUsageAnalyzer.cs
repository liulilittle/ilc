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
// 搂2/搂5.4).
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
        if (def.GenericArity <= 0) return null;
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

        // Any generic METHOD on the definition needs method-level sharing
        // (P-G3); keep those definitions on the per-instance path for now.
        if (def.Methods.Any(m => m.GenericArity > 0)) return null;

        // (B) Body scan: mark parameters used directionally anywhere.
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
