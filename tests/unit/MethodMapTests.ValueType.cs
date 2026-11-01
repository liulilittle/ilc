using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.ValueType System.ValueType::GetHashCode dispatch tests -- the
// audit section 5 #5 gap wiring (MethodMap.ValueType.cs
// TryMapValueTypeGetHashCode + the MethodRefResolver.cs "System.ValueType"
// dispatch case).
//
// net48's rule is NOT "hash the first non-static field" and it is NOT process-
// randomized. MEASURED against .NET Framework 4.8 (45+ probe points; the e2e
// lock is tests/cases/structdefaulthash1):
//
//     hash = munge(typeID) ^ fieldImage
//     munge(id) = id * 711650207 + 2506965631U
//
//   typeID     the lazy ValueType typeID (3, then +2), assigned on the FIRST
//              GetHashCode call for that type -- FIRST-HASH order, not
//              metadata declaration order -- and shared with the tuple path.
//   fieldImage the XOR of every complete 4-byte word of the NET48 PACKED field
//              image (a 1..3-byte tail is dropped) when the struct is tightly
//              packed with no GC reference; otherwise the first non-null
//              field's own words.
//
// The generated C++ struct is not that image (short/byte are widened into
// int32 slots), so every user struct carries an Il2CppVtField table and the
// resolver emits icall_user_struct_default_hash(<addr>, <table>, <klass>).
// A receiver with no field model (no instance fields, an untracked receiver,
// or no TypeDef at all) keeps the OfPtr fallback.
//
// The PEs are synthesized with System.Reflection.Metadata's writer: a user
// struct TypeDef (with field rows), a System.ValueType TypeRef MemberRef for
// GetHashCode, resolved via the receiver-type-aware Resolve overload.

public class MethodMapValueTypeTests
{
    //--------------------------------------------------------------------------
    // Every user struct with instance fields routes to the ONE net48 default-
    // hash icall, whatever its first field is: net48 computes
    // munge(typeID) ^ fieldImage over the PACKED field image (measured; see
    // tests/cases/structdefaulthash1), NOT the first field's hash. The runtime
    // reads the per-struct Il2CppVtField table, so the resolver injects the
    // field table and the class descriptor, in that order after the receiver.
    //--------------------------------------------------------------------------
    public static IEnumerable<object[]> UserStructFirstFields() => new[]
    {
        new object[] { "Pt", new byte[] { 0x08, 0x08 } },     // int x, int y
        new object[] { "Flag", new byte[] { 0x02 } },         // bool flag
        new object[] { "Big", new byte[] { 0x0A, 0x08 } },    // long v, int tag
        new object[] { "Pair", new byte[] { 0x0D, 0x08 } },   // double d, int tag
        new object[] { "F", new byte[] { 0x0C } },            // float f
        new object[] { "B", new byte[] { 0x05 } },            // byte b
        new object[] { "S", new byte[] { 0x06 } },            // short s
        new object[] { "Named", new byte[] { 0x0E, 0x08 } },  // string name, int id
    };

    [Theory]
    [MemberData(nameof(UserStructFirstFields))]
    public void Resolve_ValueType_GetHashCode_UserStruct_UsesNet48DefaultHash(string structName, byte[] firstSigs)
    {
        var fields = new List<(byte[] Sig, bool IsStatic)>();
        foreach (var sig in firstSigs) fields.Add(FieldSig(sig));

        var resolved = ResolveStructGetHashCode("Calc", structName, fields.ToArray());

        Assert.Equal("il2cpp::runtime::icall_user_struct_default_hash", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
        // The receiver address, then the field table, then the descriptor.
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
        Assert.NotNull(resolved.AppendLiterals);
        Assert.Equal($"Calc_{structName}_vtfields", resolved.AppendLiterals![0]);
        Assert.Equal($"&Calc_{structName}_class", resolved.AppendLiterals![1]);
    }

    //--------------------------------------------------------------------------
    // A user struct with a CLASS-typed field takes the same path (the reference
    // field only forces net48's REGULAR path inside the runtime; it does not
    // change the dispatch).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_ClassFirstField_UsesNet48DefaultHash()
    {
        var fields = new (byte[] Sig, bool IsStatic)[]
        {
            FieldSigClass(0x12, 0x05),
            FieldSig(0x08),
        };
        var resolved = ResolveStructGetHashCode("Calc", "Holder", fields);

        Assert.Equal("il2cpp::runtime::icall_user_struct_default_hash", resolved.CppName);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // A VAR first field (a GENERIC struct's T field) takes the same path too:
    // the closed argument is unknown at the open TypeDef, so the runtime hashes
    // the widened slot through the field table like any other field.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_VarFirstField_UsesNet48DefaultHash()
    {
        var fields = new (byte[] Sig, bool IsStatic)[]
        {
            FieldSigVar(0x13, 0x00),
            FieldSig(0x08),
        };
        var resolved = ResolveStructGetHashCode("Calc", "GBox", fields);

        Assert.Equal("il2cpp::runtime::icall_user_struct_default_hash", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // No instance fields (and static-only fields, which do not count): net48
    // hashes the typeID munge alone, so the runtime needs no field table and
    // the resolver keeps the OfPtr fallback for this shape.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_NoInstanceFields_UsesOfPtrFallback()
    {
        var resolved = ResolveStructGetHashCode("Calc", "Empty", Array.Empty<(byte[] Sig, bool IsStatic)>());
        Assert.Equal("il2cpp::runtime::icall_value_type_get_hash_code_of_ptr", resolved.CppName);
    }

    [Fact]
    public void Resolve_ValueType_GetHashCode_StaticOnlyFields_UsesOfPtrFallback()
    {
        var fields = new[] { FieldSig(0x08, isStatic: true) };
        var resolved = ResolveStructGetHashCode("Calc", "Const", fields);
        Assert.Equal("il2cpp::runtime::icall_value_type_get_hash_code_of_ptr", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // An untracked receiver routes to the OfPtr icall -- the fold resolves
    // the callvirt TWICE (hint-less first pass, then re-resolve with the
    // constrained receiver type), so the hint-less pass must not throw.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_UntrackedReceiver_DefersToGeneratedName()
    {
        var fields = new[] { FieldSig(0x08) };
        var resolved = ResolveStructGetHashCode("Calc", "Pt", fields, receiverType: null);

        Assert.Equal("il2cpp::runtime::icall_value_type_get_hash_code_of_ptr", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // A receiver type with no TypeDef in the metadata (a BCL struct that would
    // never route here): no field model, so the OfPtr fallback -- never a
    // silent wrong hash.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ValueType_GetHashCode_UnknownReceiverType_FallsBackToOfPtr()
    {
        var fields = new[] { FieldSig(0x08) };
        var resolved = ResolveStructGetHashCode("Calc", "Pt", fields, receiverType: "System.Guid");

        Assert.Equal("il2cpp::runtime::icall_value_type_get_hash_code_of_ptr", resolved.CppName);
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

    //--------------------------------------------------------------------------
    // binrw3 residual: Byte/Char ToString on an ADDRESS receiver must read
    // the NATIVE cell width (ldelema of byte[]/char[] points at a 1/2-byte
    // cell; deref:i reads neighbor garbage -- b4=12345). An int32-slot byte
    // local keeps high bytes zero, so the narrow read is exact in both
    // shapes.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Byte_ToStringAddressReceiver_UsesNarrowDeref()
    {
        var resolved = ResolveMemberWithAddress("System", "Byte", "ToString",
            new byte[] { 0x00, 0x00, 0x0E });

        Assert.Equal("il2cpp::runtime::icall_int32_to_string", resolved.CppName);
        Assert.Equal(new[] { "deref:u1" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Char_ToStringAddressReceiver_UsesNarrowDeref()
    {
        var resolved = ResolveMemberWithAddress("System", "Char", "ToString",
            new byte[] { 0x00, 0x00, 0x0E });

        Assert.Equal("il2cpp::runtime::icall_char_to_string", resolved.CppName);
        Assert.Equal(new[] { "deref:u2" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // P0-narrow: SByte/Int16/UInt16 parameterless ToString() had NO wiring in
    // MethodMap.ValueType.cs (only Byte did), so the resolver fell through to
    // the generated-name path and the emitted C++ called a nonexistent symbol
    // (System_SByte_ToString_..., System_Int16_ToString_...,
    // System_UInt16_ToString_...). The ADDRESS receiver must read the NATIVE
    // SIGNED/UNSIGNED cell width: deref:i1 / deref:i2 sign-extend (so -3 and
    // -32768 print correctly), deref:u2 zero-extends (65535 stays 65535).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SByte_ToStringAddressReceiver_UsesSignedNarrowDeref()
    {
        var resolved = ResolveMemberWithAddress("System", "SByte", "ToString",
            new byte[] { 0x00, 0x00, 0x0E });

        Assert.Equal("il2cpp::runtime::icall_int32_to_string", resolved.CppName);
        Assert.Equal(new[] { "deref:i1" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Int16_ToStringAddressReceiver_UsesSignedNarrowDeref()
    {
        var resolved = ResolveMemberWithAddress("System", "Int16", "ToString",
            new byte[] { 0x00, 0x00, 0x0E });

        Assert.Equal("il2cpp::runtime::icall_int32_to_string", resolved.CppName);
        Assert.Equal(new[] { "deref:i2" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_UInt16_ToStringAddressReceiver_UsesUnsignedNarrowDeref()
    {
        var resolved = ResolveMemberWithAddress("System", "UInt16", "ToString",
            new byte[] { 0x00, 0x00, 0x0E });

        Assert.Equal("il2cpp::runtime::icall_int32_to_string", resolved.CppName);
        Assert.Equal(new[] { "deref:u2" }, resolved.ParamTypeNames);
    }

    private static ResolvedMethod ResolveMemberWithAddress(string ns, string typeName,
        string memberName, byte[] signature)
    {
        using var reader = new PEReader(SynthesizeMember(ns, typeName, memberName, signature));
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, true, null, null);
    }

    private static MemoryStream SynthesizeMember(string ns, string typeName,
        string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
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

    // A VAR/MVAR field signature: FIELD marker + 0x13/0x1E + the generic index.
    private static (byte[] Sig, bool IsStatic) FieldSigVar(byte elem, byte index)
    {
        return (new[] { (byte)0x06, elem, index }, false);
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
