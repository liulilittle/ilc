using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.ValueType System.ValueType::GetHashCode dispatch tests -- the
// audit §5 #5 gap wiring (MethodMap.ValueType.cs TryMapValueTypeGetHashCode
// + the MethodRefResolver.cs "System.ValueType" dispatch case).
//
// net48 DOCUMENTED rule (netcore ValueType.cs:89-98): hash of the FIRST
// non-static instance field; no instance fields -> the type hash. (The
// probed net48 ACTUAL behavior is a process-randomized whole-struct hash --
// not reproducible for AOT -- so the resolver implements the documented
// rule deterministically.)
//
// The receiver kind is read from the constrained value-type's TypeDef
// metadata (user structs only -- BCL structs override GetHashCode and never
// route through this owner), and the per-kind runtime target is selected
// because the emitter can inject only literal 0 / ParamCount, not the
// typeHash/fieldKind constants icall_value_type_get_hash_code needs:
//
//   first field kind                 runtime target + receiver cast
//   --------------------------------------------------------------------
//   4-byte value (i4/u4/bool/char/   icall_int32_get_hash_code + deref:i
//     i1/u1/i2/u2/nested-struct)
//   8-byte value (i8/u8)             icall_int64_get_hash_code + deref:l
//   float/double (r4/r8)             icall_double_get_hash_code + deref:d
//   GC reference (string/class/      icall_value_type_get_hash_code_ref + ""
//     object/array)
//   no instance fields               icall_value_type_get_hash_code + the two
//                                    prepend markers -> (0, 0, data) -> 0
//
// The PEs are synthesized with System.Reflection.Metadata's writer: a user
// struct TypeDef (with field rows), a System.ValueType TypeRef MemberRef for
// GetHashCode, resolved via the receiver-type-aware Resolve overload.
public class MethodMapValueTypeTests
{
    //--------------------------------------------------------------------------
    // 4-byte value first field -> the Int32 identity hash on the deref'd slot.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_Int32FirstField_UsesInt32HashDeref()
    {
        var resolved = ResolveStructGetHashCode("Calc", "Pt",
            new[] { FieldSig(0x08), FieldSig(0x08) }); // int x, int y

        Assert.Equal("il2cpp::runtime::icall_int32_get_hash_code", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:i" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { false }, resolved.ParamIsRef);
    }

    //--------------------------------------------------------------------------
    // Bool/char first fields are 4-byte slots too.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_BoolFirstField_UsesInt32HashDeref()
    {
        var resolved = ResolveStructGetHashCode("Calc", "Flag",
            new[] { FieldSig(0x02) }); // bool flag

        Assert.Equal("il2cpp::runtime::icall_int32_get_hash_code", resolved.CppName);
        Assert.Equal(new[] { "deref:i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // 8-byte value first field -> the Int64 low^high hash on the deref'd slot.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_Int64FirstField_UsesInt64HashDeref()
    {
        var resolved = ResolveStructGetHashCode("Calc", "Big",
            new[] { FieldSig(0x0A), FieldSig(0x08) }); // long v, int tag

        Assert.Equal("il2cpp::runtime::icall_int64_get_hash_code", resolved.CppName);
        Assert.Equal(new[] { "deref:l" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Float/double first field -> the Double bit-pattern hash on the deref'd
    // slot (Single values live in double slots -- the icall_double precedent).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_DoubleFirstField_UsesDoubleHashDeref()
    {
        var resolved = ResolveStructGetHashCode("Calc", "Pair",
            new[] { FieldSig(0x0D), FieldSig(0x08) }); // double d, int tag

        Assert.Equal("il2cpp::runtime::icall_double_get_hash_code", resolved.CppName);
        Assert.Equal(new[] { "deref:d" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ValueType_GetHashCode_SingleFirstField_UsesDoubleHashDeref()
    {
        var resolved = ResolveStructGetHashCode("Calc", "F", new[] { FieldSig(0x0C) });

        Assert.Equal("il2cpp::runtime::icall_double_get_hash_code", resolved.CppName);
        Assert.Equal(new[] { "deref:d" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // GC-reference first field (string) -> the reference-arm icall (null -> 0
    // at runtime); the receiver address passes through uncast.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_StringFirstField_UsesReferenceArm()
    {
        var resolved = ResolveStructGetHashCode("Calc", "Named",
            new[] { FieldSig(0x0E), FieldSig(0x08) }); // string name, int id

        Assert.Equal("il2cpp::runtime::icall_value_type_get_hash_code_ref", resolved.CppName);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { true }, resolved.ParamIsRef);
    }

    //--------------------------------------------------------------------------
    // Class (0x12 + coded index) first field -> reference arm (ReadParamByte
    // consumes the TypeDefOrRef coded index byte in the field signature).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_ClassFirstField_UsesReferenceArm()
    {
        var resolved = ResolveStructGetHashCode("Calc", "Holder",
            new[] { FieldSigClass(0x12, 0x05), FieldSig(0x08) }); // object field (TypeRef row 1), int tag

        Assert.Equal("il2cpp::runtime::icall_value_type_get_hash_code_ref", resolved.CppName);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // No instance fields -> the type-hash arm: the existing 3-arg icall with
    // BOTH prepend markers injects (0, 0) before the receiver address, so the
    // runtime's fieldKind 0 path returns typeHash 0 (the per-type constant is
    // not injectable -- documented divergence).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_NoFields_UsesTypeHashArmWithBothPrepends()
    {
        var resolved = ResolveStructGetHashCode("Calc", "Empty", Array.Empty<(byte[], bool)>());

        Assert.Equal("il2cpp::runtime::icall_value_type_get_hash_code", resolved.CppName);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
        Assert.True(resolved.PrependCountArg);
        Assert.True(resolved.PrependZeroArg);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Static-only fields count as "no instance fields" (the documented rule
    // skips static members -- a static field of the same type would loop).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_StaticOnlyFields_UsesTypeHashArm()
    {
        var resolved = ResolveStructGetHashCode("Calc", "Const", new[] { FieldSig(0x08, isStatic: true) });

        Assert.Equal("il2cpp::runtime::icall_value_type_get_hash_code", resolved.CppName);
        Assert.True(resolved.PrependCountArg);
        Assert.True(resolved.PrependZeroArg);
    }

    //--------------------------------------------------------------------------
    // An untracked receiver routes to the OfPtr icall -- the fold resolves
    // the callvirt TWICE (hint-less first pass, then re-resolve with the
    // constrained receiver type), so the hint-less pass must not throw.
    // The OfPtr icall (stubs.h/cpp) hashes by pointer instead of the
    // generated-name fallback (a self-identifying C++ link error).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_UntrackedReceiver_DefersToGeneratedName()
    {
        var resolved = ResolveStructGetHashCode("Calc", "Pt", new[] { FieldSig(0x08) }, receiverType: null);

        Assert.Equal("il2cpp::runtime::icall_value_type_get_hash_code_of_ptr", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // A receiver type with no TypeDef in the metadata (e.g. a BCL struct that
    // would never route here) -- LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_UnknownReceiverType_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveStructGetHashCode("Calc", "Pt", new[] { FieldSig(0x08) }, receiverType: "System.Guid"));

        Assert.Contains("no TypeDef found", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction: a user struct TypeDef with field
    // rows, the System.ValueType::GetHashCode MemberRef, resolved with the
    // receiver-type-aware Resolve overload.
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveStructGetHashCode(string structNs, string structName,
        (byte[] Sig, bool IsStatic)[] fieldSignatures, string? receiverType = "receiver")
    {
        using var reader = new PEReader(SynthesizeStruct(structNs, structName, fieldSignatures));
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == "GetHashCode");
        var recv = receiverType == "receiver" ? structNs + "." + structName : receiverType;
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            recv, true, null, null);
    }

    // A FIELD signature: 0x06 marker + element byte (+ optional trailing coded
    // index for 0x12/0x11). isStatic marks the field static (skipped by the
    // classifier -- the documented rule ignores static fields).
    private static (byte[] Sig, bool IsStatic) FieldSig(byte elem, bool isStatic = false)
    {
        return (new[] { (byte)0x06, elem }, isStatic);
    }

    // A CLASS/VALUETYPE field signature with a trailing TypeDefOrRef coded
    // index (ReadParamByte must consume it; a TypeRef at row 1 encodes 0x05).
    private static (byte[] Sig, bool IsStatic) FieldSigClass(byte elem, byte codedIndex)
    {
        return (new[] { (byte)0x06, elem, codedIndex }, false);
    }

    private static MemoryStream SynthesizeStruct(string structNs, string structName,
        (byte[] Sig, bool IsStatic)[] fieldSignatures)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var vtRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("ValueType"));
        // System.ValueType::GetHashCode -- instance int32 GetHashCode()
        // signature: header 0x00 (DEFAULT) + 0 params + int32 return (the
        // BCL mapping carries the instance shape, so the header bit is
        // irrelevant here).
        mdb.AddMemberReference(vtRef, mdb.GetOrAddString("GetHashCode"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x08 }));

        // Field rows (threaded into the struct TypeDef's fieldList -- the
        // TypeDef references the FIRST row it owns; ECMA-335 II.22.37).
        var firstField = default(FieldDefinitionHandle);
        for (var i = 0; i < fieldSignatures.Length; i++)
        {
            var attrs = fieldSignatures[i].IsStatic
                ? FieldAttributes.Public | FieldAttributes.Static
                : FieldAttributes.Public;
            var fh = mdb.AddFieldDefinition(attrs,
                mdb.GetOrAddString("f" + i), mdb.GetOrAddBlob(fieldSignatures[i].Sig));
            if (i == 0) firstField = fh;
        }
        mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.SequentialLayout,
            mdb.GetOrAddString(structNs), mdb.GetOrAddString(structName),
            vtRef, firstField, default(MethodDefinitionHandle));

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
