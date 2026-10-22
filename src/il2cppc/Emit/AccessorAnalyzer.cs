using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// FoldedAccessor -- one recognized simple property accessor.
//   IsGetter      : true = get_X (returns the field), false = set_X (stores)
//   IsStatic      : the accessor is static (static field access)
//   FieldName     : the RAW field name (sanitized at emission)
//   OwnerFullName : the field's declaring type (== the accessor's type; the
//                   cross-type check is part of the analysis)
//   FieldIsRef    : the field holds a GC reference (Il2CppObject*)
//   RefTargetFullName : the ref field's element type (for the FieldAssignStmt
//                   value cast), "" for value fields
//   MethodName    : the accessor's METADATA method name (get_X/set_X -- refl2
//                   S2/G1: TypeEmitter.EmitTypeInfo uses it to emit fnPtr=
//                   nullptr for folded accessors; it is NOT reconstructible
//                   from FieldName -- a get_Value accessor may read _value)
//------------------------------------------------------------------------------
public sealed record FoldedAccessor(
    bool IsGetter, bool IsStatic, string FieldName,
    string OwnerFullName, bool FieldIsRef, string RefTargetFullName, string MethodName);

//------------------------------------------------------------------------------
// AccessorAnalyzer -- property-folding pre-scan pass (iron rule 2j).
//
//   Simple properties (get/set that directly return/store a same-type field)
//   MUST be folded into field accesses instead of function calls. Folding is
//   the ONLY legal accessor elimination under the IL2CPP_NOINLINE contract
//   (see docs/architecture/property-folding-design.md): every call site is rewritten to
//   a FieldExpr/FieldAssignStmt and the accessor FUNCTION is not emitted.
//
//   Recognition (verified by decoding the body -- never guessed):
//     instance getter: [nop*] ldarg.0 | ldarg.s 0; ldfld <token>; ret
//     instance setter: [nop*] ldarg.0 | ldarg.s 0; ldarg.1 | ldarg.s 1;
//                            stfld <token>; ret
//     static  getter:  [nop*] ldsfld <token>; ret
//     static  setter:  [nop*] ldarg.0 | ldarg.s 0; stsfld <token>; ret
//
//   Safety conditions (ALL required, else NO fold -- falls to the normal
//   call path):
//     1. the method is named get_*/set_* (a C# property accessor -- plain
//        methods with the same shape are NOT folded: ldftn/reflection
//        visibility stays conservative),
//     2. NOT virtual/abstract (polymorphism goes through the vtable),
//     3. the field is declared in the SAME type as the accessor,
//     4. the shape matches exactly (nop prefix tolerated).
//------------------------------------------------------------------------------
public static class AccessorAnalyzer
{
    public static Dictionary<int, FoldedAccessor> Scan(
        PEReader pe, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        var result = new Dictionary<int, FoldedAccessor>();
        foreach (var tdh in md.TypeDefinitions)
        {
            var typeFullName = md.GetFullName(tdh);
            // Only translated types are foldable (their structs exist).
            if (!TypeLookup.Any(types, typeFullName)) continue;
            var td = md.GetTypeDefinition(tdh);
            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                if (m.RelativeVirtualAddress == 0) continue; // abstract/PInvoke: no body
                var attrs = m.Attributes;
                // Condition 2: no virtual dispatch, no abstract contract.
                if ((attrs & (MethodAttributes.Virtual | MethodAttributes.Abstract)) != 0) continue;
                var name = md.GetString(m.Name);
                if (!name.StartsWith("get_", StringComparison.Ordinal)
                    && !name.StartsWith("set_", StringComparison.Ordinal)) continue;
                var body = pe.GetMethodBody(m.RelativeVirtualAddress);
                var insns = IlDecoder.DecodeMethod(body.GetILBytes() ?? Array.Empty<byte>(), md);
                var isStatic = (attrs & MethodAttributes.Static) != 0;
                var acc = TryMatch(insns, md, typeFullName, isStatic, name);
                if (acc != null) result[MetadataTokens.GetToken(mh)] = acc;
            }
        }
        return result;
    }

    //--------------------------------------------------------------------------
    // TryMatch -- exact shape recognition (nops tolerated). Returns null when
    // the body is not a simple accessor (conservative: no partial matches).
    //--------------------------------------------------------------------------
    private static FoldedAccessor? TryMatch(
        List<IlInstruction> insns, MetadataReader md, string ownerFullName, bool isStatic, string methodName)
    {
        var seq = new List<IlInstruction>(insns.Count);
        foreach (var i in insns)
            if (i.OpCode != ILOpCode.Nop) seq.Add(i);

        if (isStatic)
        {
            // static getter: ldsfld <token>; ret
            if (seq.Count == 2 && seq[0].OpCode == ILOpCode.Ldsfld && seq[1].OpCode == ILOpCode.Ret)
                return Make(seq[0], md, ownerFullName, isGetter: true, isStatic: true, methodName);
            // static setter: ldarg.0; stsfld <token>; ret  (value comes from the stack)
            if (seq.Count == 3 && IsArg0(seq[0]) && seq[1].OpCode == ILOpCode.Stsfld && seq[2].OpCode == ILOpCode.Ret)
                return Make(seq[1], md, ownerFullName, isGetter: false, isStatic: true, methodName);
            return null;
        }

        // instance getter: ldarg.0; ldfld <token>; ret
        if (seq.Count == 3 && IsArg0(seq[0]) && seq[1].OpCode == ILOpCode.Ldfld && seq[2].OpCode == ILOpCode.Ret)
            return Make(seq[1], md, ownerFullName, isGetter: true, isStatic: false, methodName);
        // instance setter: ldarg.0; ldarg.1; stfld <token>; ret
        if (seq.Count == 4 && IsArg0(seq[0]) && IsArg1(seq[1])
            && seq[2].OpCode == ILOpCode.Stfld && seq[3].OpCode == ILOpCode.Ret)
            return Make(seq[2], md, ownerFullName, isGetter: false, isStatic: false, methodName);
        return null;
    }

    private static bool IsArg0(IlInstruction i) =>
        i.OpCode == ILOpCode.Ldarg_0
        || (i.OpCode is ILOpCode.Ldarg or ILOpCode.Ldarg_s && i.Operand == 0);

    private static bool IsArg1(IlInstruction i) =>
        i.OpCode == ILOpCode.Ldarg_1
        || (i.OpCode is ILOpCode.Ldarg or ILOpCode.Ldarg_s && i.Operand == 1);

    //--------------------------------------------------------------------------
    // Make -- resolve the field token and apply the cross-type condition.
    //--------------------------------------------------------------------------
    private static FoldedAccessor? Make(
        IlInstruction fieldIns, MetadataReader md, string ownerFullName, bool isGetter, bool isStatic, string methodName)
    {
        var handle = MetadataTokens.Handle((int)fieldIns.Operand);
        string fieldName;
        string fieldOwner;
        string signature;
        if (handle.Kind == HandleKind.FieldDefinition)
        {
            var fd = md.GetFieldDefinition((FieldDefinitionHandle)handle);
            fieldName = md.GetString(fd.Name);
            fieldOwner = md.GetFullName(fd.GetDeclaringType());
            signature = md.GetBlobBytes(fd.Signature).Length > 0
                ? string.Join("", md.GetBlobBytes(fd.Signature).Select(b => b.ToString("X2")))
                : "";
        }
        else if (handle.Kind == HandleKind.MemberReference)
        {
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            fieldName = md.GetString(mr.Name);
            fieldOwner = mr.Parent.Kind switch
            {
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
                _ => "?",
            };
            signature = md.GetBlobBytes(mr.Signature).Length > 0
                ? string.Join("", md.GetBlobBytes(mr.Signature).Select(b => b.ToString("X2")))
                : "";
        }
        else
        {
            return null; // unknown field token: no fold (conservative)
        }
        // Condition 3: the field must live in the accessor's own type.
        if (fieldOwner != ownerFullName) return null;

        // Field signature: FIELD header (0x06) + type byte.
        var isRef = false;
        var refTarget = "";
        if (signature.Length >= 4)
        {
            var typeByte = int.Parse(signature.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            isRef = typeByte is 0x0E or 0x12 or 0x1C;
            refTarget = typeByte switch
            {
                0x0E => "System.String",
                0x1C => "System.Object",
                0x12 => ResolveClassRefTarget(md, (EntityHandle)handle),
                _ => "",
            };
        }
        return new FoldedAccessor(isGetter, isStatic, fieldName, ownerFullName, isRef, refTarget, methodName);
    }

    // 0x12 CLASS field: the target type follows the type byte in the field
    // signature (TypeDefOrRef coded index).
    private static string ResolveClassRefTarget(MetadataReader md, EntityHandle fieldHandle)
    {
        BlobHandle sigHandle;
        if (fieldHandle.Kind == HandleKind.FieldDefinition)
            sigHandle = md.GetFieldDefinition((FieldDefinitionHandle)fieldHandle).Signature;
        else
            sigHandle = md.GetMemberReference((MemberReferenceHandle)fieldHandle).Signature;
        var sig = md.GetBlobReader(sigHandle);
        sig.ReadByte(); // FIELD header
        if (sig.ReadByte() != 0x12) return "";
        var target = sig.ReadTypeHandle();
        return target.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)target),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)target),
            _ => "",
        };
    }
}
