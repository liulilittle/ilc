using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Dictionary dispatch tests: prove the System.Collections.Generic.
// Dictionary`2 domain (MethodMap.Dictionary.cs -> TryMapDictionary) maps the
// Dictionary<string,int> expansion to the runtime GC-object-wrapper icalls
// declared in src/runtime/include/internal/dictsi_obj.h / dictsi.h
// (namespace il2cpp::runtime):
//
//   .ctor()                    -> icall_dict_si_ctor(self, 0)  (AppendZeroArg)
//   .ctor(int32)               -> icall_dict_si_ctor(self, capacity)
//   Add(TKey,TValue)           -> icall_dict_si_add
//   set_Item(TKey,TValue)      -> icall_dict_si_set   (insert-or-replace)
//   get_Item(TKey)             -> icall_dict_si_get   (KNF on a miss)
//   TryGetValue(TKey,out TVal) -> icall_dict_si_try_get_value
//   ContainsKey(TKey)          -> icall_dict_si_contains_key
//   Remove(TKey)               -> icall_dict_si_remove
//   get_Count()                -> icall_dict_si_get_count
//   Clear()                    -> icall_dict_si_clear
//
// The MemberRef signatures are the GENERIC DEFINITION signatures (TKey/TValue
// arrive as ELEMENT_TYPE_VAR 0x13 with a compressed index, consumed by
// ReadParamByte) -- the real net48 dictcase.exe blobs, probe-verified:
//   .ctor       20 00 01
//   Add         20 02 01 13 00 13 01      -> ps [13,13]
//   get_Count   20 00 08
//   TryGetValue 20 02 02 13 00 10 13 01   -> ps [13,13] (BYREF consumes elem)
//   ContainsKey 20 01 02 13 00            -> ps [13]
//   get_Item    20 01 13 01 13 00         -> ps [0x01]! the RETURN is TValue
//               (0x13), and RawSignature does not consume a VAR-return index,
//               so the index byte leaks into ps[0] -- get_Item guards on
//               COUNT ONLY (the List get_Item precedent).
//   set_Item    20 02 01 13 00 13 01      -> ps [13,13]
//   Remove      20 01 02 13 00            -> ps [13]
//   Clear       20 00 01
//
// Receiver handling: the Dictionary<string,int> instance rides in a managed
// reference slot (Il2CppObject*), so the instance receiver cast is ""
// (pass-through); the int32 value casts "i"; the TryGetValue out int is the
// ldloca ADDRESS ("" passthrough -- the icall writes through the pointer).
//
// Instantiation guard: a folded receiver instance name other than
// "System.Collections.Generic.Dictionary`2_string_int" is a known-unwireable
// instantiation and must fail LOUDLY (NotSupportedException), never silently
// fall to the generated-name path. A T-shaped parameter that is not VAR
// (0x13) is not the Dictionary`2 definition signature at all and must also
// be LOUD.
public class MethodMapTestsDictionary
{
    //--------------------------------------------------------------------------
    // Dictionary<string,int> -- instance members map to icall_dict_si_*
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Dictionary_CtorParameterless_AppendsZeroArgument()
    {
        // real dictcase blob: 20 00 01 (HAS_THIS, 0 params, void)
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x00, 0x01)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_dict_si_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
        Assert.False(resolved.PrependZeroArg);
        Assert.True(resolved.AppendZeroArg);
    }

    [Fact]
    public void Resolve_Dictionary_CtorCapacity_MapsToCtor()
    {
        // capacity ctor: the int32 param is the ONE concrete byte (0x08)
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x01, 0x01, 0x08)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_dict_si_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_Add_MapsToDictSiAdd()
    {
        // instance void Add(TKey,TValue): real blob 20 02 01 13 00 13 01
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add");

        Assert.Equal("il2cpp::runtime::icall_dict_si_add", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_SetItem_MapsToDictSiSet()
    {
        // indexer SET: same VAR [13,13] shape as Add (insert-or-replace)
        var resolved = ResolveMember(Synthesize("set_Item", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "set_Item");

        Assert.Equal("il2cpp::runtime::icall_dict_si_set", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_GetItem_MapsToDictSiGet()
    {
        // indexer GET: real blob 20 01 13 01 13 00. The RETURN is TValue
        // (0x13 0x01) -- RawSignature does not consume a VAR-return index, so
        // the leaked index byte occupies ps[0]; the mapping guards on COUNT
        // ONLY (List get_Item precedent).
        var resolved = ResolveMember(Synthesize("get_Item", Blob(0x20, 0x01, 0x13, 0x01, 0x13, 0x00)), "get_Item");

        Assert.Equal("il2cpp::runtime::icall_dict_si_get", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_TryGetValue_MapsToDictSiTryGetValue()
    {
        // bool TryGetValue(TKey, out TValue): real blob 20 02 02 13 00 10 13 01
        // -- the BYREF (0x10) consumes its element (VAR 0x13), so ps is
        // [13,13] like Add; the out param is the ldloca ADDRESS ("").
        var resolved = ResolveMember(Synthesize("TryGetValue", Blob(0x20, 0x02, 0x02, 0x13, 0x00, 0x10, 0x13, 0x01)), "TryGetValue");

        Assert.Equal("il2cpp::runtime::icall_dict_si_try_get_value", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData("ContainsKey", "il2cpp::runtime::icall_dict_si_contains_key")]
    [InlineData("Remove", "il2cpp::runtime::icall_dict_si_remove")]
    public void Resolve_Dictionary_OneVarParamMembers_MapToBoolIcalls(string name, string expectedIcall)
    {
        // bool ContainsKey(TKey) / bool Remove(TKey): real blob 20 01 02 13 00
        var resolved = ResolveMember(Synthesize(name, Blob(0x20, 0x01, 0x02, 0x13, 0x00)), name);

        Assert.Equal(expectedIcall, resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_GetCount_MapsToDictSiGetCount()
    {
        // instance int32 get_Count(): real blob 20 00 08
        var resolved = ResolveMember(Synthesize("get_Count", Blob(0x20, 0x00, 0x08)), "get_Count");

        Assert.Equal("il2cpp::runtime::icall_dict_si_get_count", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Dictionary_Clear_MapsToDictSiClear()
    {
        // instance void Clear(): real blob 20 00 01
        var resolved = ResolveMember(Synthesize("Clear", Blob(0x20, 0x00, 0x01)), "Clear");

        Assert.Equal("il2cpp::runtime::icall_dict_si_clear", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Instantiation guard -- a non-int32 (non-string->int32) instantiation is
    // LOUD, not silent
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Dictionary_Add_TrackedNonIntReceiver_ThrowsLoud()
    {
        // A folded Dictionary`2_string_string receiver is a known-unwireable
        // instantiation (only string->int32 is wired) -- LOUD.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
                receiverType: "System.Collections.Generic.Dictionary`2_string_string"));

        Assert.Contains("System.Collections.Generic.Dictionary`2::Add", ex.Message);
        Assert.Contains("dictsi", ex.Message);
    }

    [Fact]
    public void Resolve_Dictionary_GetCount_TrackedNonIntReceiver_ThrowsLoud()
    {
        // get_Count has no T-bearing parameter, so the guard falls back to the
        // folded receiver instance name: a Dictionary`2_string_string receiver
        // is LOUD.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("get_Count", Blob(0x20, 0x00, 0x08)), "get_Count",
                receiverType: "System.Collections.Generic.Dictionary`2_string_string"));

        Assert.Contains("System.Collections.Generic.Dictionary`2::get_Count", ex.Message);
    }

    [Fact]
    public void Resolve_Dictionary_Add_ConcreteShape_ThrowsLoud()
    {
        // A CONCRETE string/int-shaped parameter list (0x0E 0x08) is NOT the
        // Dictionary`2 definition signature (TKey/TValue always arrive as VAR
        // 0x13) -- LOUD, not mapped.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x0E, 0x08)), "Add"));

        Assert.Contains("System.Collections.Generic.Dictionary`2::Add", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Negative: unwired members fall back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Dictionary_UnwiredMember_FallsBackToGeneratedName()
    {
        // GetHashCode (inherited from Object) is NOT in the wired surface -- it
        // must fall to the generated-name path, not be mis-mapped.
        var resolved = ResolveMember(Synthesize("GetHashCode", Blob(0x20, 0x00, 0x08)), "GetHashCode");

        Assert.StartsWith("System_Collections_Generic_Dictionary_2_GetHashCode_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the resolver)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null, null);
    }

    // The raw MemberRef signature blob, verbatim (the real net48 dictcase.exe
    // blobs, probe-verified): header 0x20 (HAS_THIS), param count, return
    // byte, then the parameter types with their trailing data.
    private static byte[] Blob(params byte[] bytes) => bytes;

    private static MemoryStream Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("Dictionary`2"));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    private static MemoryStream Serialize(MetadataBuilder mdb)
    {
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
