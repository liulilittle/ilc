using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.List W5 value-type expansion (T2): List<float/bool/char/sbyte/
// byte/short/ushort/uint> ride the listobj storage through per-kind box/unbox
// forwarders (listobj.h), mirroring the G4 long/double precedent:
//
//   float -> icall_box_float / icall_unbox_float (double carrier "d")
//   bool/char/sbyte/byte/short/ushort/uint -> the boxing_ext/ext2 int32-slot
//     pair (int32 carrier "i")
//
// T-free members (.ctor/get_Count/Clear/RemoveAt/Sort) reuse the shared
// object-domain entries. CopyTo stays obj/i4-only (a typed value-array
// CopyTo needs dedicated runtime overloads -- out of the minimal W5 scope,
// still LOUD).
public class MethodMapTestsListVal
{
    public static IEnumerable<object[]> ValueKinds => new List<object[]>
    {
        new object[] { "float", "d" },
        new object[] { "bool", "i" },
        new object[] { "char", "i" },
        new object[] { "sbyte", "i" },
        new object[] { "byte", "i" },
        new object[] { "short", "i" },
        new object[] { "ushort", "i" },
        new object[] { "uint", "i" },
    };

    [Theory]
    [MemberData(nameof(ValueKinds))]
    public void Resolve_ListVal_Add_MapsToKindAdd(string kind, string cast)
    {
        // instance void Add(T): real blob 20 01 01 13 00 (param VAR 0)
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Add", Sig(0x20, 0x01, 0x01, 0x13, 0x00)),
            "Add", receiverType: $"System.Collections.Generic.List`1_{kind}");

        Assert.Equal($"il2cpp::il2cpp_list_obj_add_{kind}", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(new[] { "", cast }, resolved.ParamTypeNames);
    }

    [Theory]
    [MemberData(nameof(ValueKinds))]
    public void Resolve_ListVal_GetItem_MapsToKindGetItem(string kind, string cast)
    {
        // instance T get_Item(int32): real blob 20 01 13 00 08
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "get_Item", Sig(0x20, 0x01, 0x13, 0x00, 0x08)),
            "get_Item", receiverType: $"System.Collections.Generic.List`1_{kind}");

        Assert.Equal($"il2cpp::il2cpp_list_obj_get_item_{kind}", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_ListVal_GetItem_Float_ResultKindDouble()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "get_Item", Sig(0x20, 0x01, 0x13, 0x00, 0x08)),
            "get_Item", receiverType: "System.Collections.Generic.List`1_float");

        Assert.Equal("d", resolved.ResultKind);
    }

    [Theory]
    [MemberData(nameof(ValueKinds))]
    public void Resolve_ListVal_GetCount_MapsToObjGetCount(string kind, string _)
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "get_Count", Sig(0x20, 0x00, 0x08)),
            "get_Count", receiverType: $"System.Collections.Generic.List`1_{kind}");

        Assert.Equal("il2cpp::il2cpp_list_obj_get_count", resolved.CppName);
    }

    [Theory]
    [MemberData(nameof(ValueKinds))]
    public void Resolve_ListVal_Ctor_MapsToObjCreate(string kind, string _)
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", ".ctor", Sig(0x20, 0x00, 0x01)),
            ".ctor", receiverType: $"System.Collections.Generic.List`1_{kind}");

        Assert.Equal("il2cpp::il2cpp_list_obj_create", resolved.CppName);
    }

    [Theory]
    [MemberData(nameof(ValueKinds))]
    public void Resolve_ListVal_IndexOf_MapsToKindIndexOf(string kind, string cast)
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "IndexOf", Sig(0x20, 0x01, 0x08, 0x13, 0x00)),
            "IndexOf", receiverType: $"System.Collections.Generic.List`1_{kind}");

        Assert.Equal($"il2cpp::il2cpp_list_obj_index_of_{kind}", resolved.CppName);
        Assert.Equal(new[] { "", cast }, resolved.ParamTypeNames);
    }

    [Theory]
    [MemberData(nameof(ValueKinds))]
    public void Resolve_ListVal_Contains_MapsToKindContains(string kind, string cast)
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Contains", Sig(0x20, 0x01, 0x02, 0x13, 0x00)),
            "Contains", receiverType: $"System.Collections.Generic.List`1_{kind}");

        Assert.Equal($"il2cpp::il2cpp_list_obj_contains_{kind}", resolved.CppName);
        Assert.Equal(new[] { "", cast }, resolved.ParamTypeNames);
    }

    [Theory]
    [MemberData(nameof(ValueKinds))]
    public void Resolve_ListVal_ToArray_MapsToKindToArray(string kind, string _)
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "ToArray", Sig(0x20, 0x00, 0x1D, 0x13, 0x00)),
            "ToArray", receiverType: $"System.Collections.Generic.List`1_{kind}");

        Assert.Equal($"il2cpp::il2cpp_list_obj_to_array_{kind}", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
    }

    [Fact]
    public void Resolve_ListEnumerator_FloatReceiver_GetCurrent_MapsToFloat()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1+Enumerator", "get_Current", Sig(0x20, 0x00, 0x13, 0x00)),
            "get_Current", receiverType: "System.Collections.Generic.List`1.Enumerator_float");

        Assert.Equal("il2cpp::il2cpp_list_obj_enumerator_get_current_float", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_ListEnumerator_UintReceiver_GetCurrent_MapsToUint()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1+Enumerator", "get_Current", Sig(0x20, 0x00, 0x13, 0x00)),
            "get_Current", receiverType: "System.Collections.Generic.List`1.Enumerator_uint");

        Assert.Equal("il2cpp::il2cpp_list_obj_enumerator_get_current_uint", resolved.CppName);
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

    private static byte[] Sig(params byte[] bytes) => bytes;

    private static MemoryStream Synthesize(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
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
