using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Reflect dispatch tests: prove the reflection base domain
// (MethodMap.Reflect.cs -> TryMapReflect) maps Activator.CreateInstance
// (Type) and Type.GetType(string) to the reflect_base.h icalls, that the
// Type.GetType result is tracked as "System.Type" (the fold linkage), that
// the System.Type route still falls back to the Type domain (TryMapType --
// GetTypeFromHandle/GetProperty etc. keep resolving), that unwired arities
// fail LOUD (NotSupportedException with the "{owner}::{name}" prefix -- see
// BclResolve's wrap), and that unmapped members fall back to the
// generated-name path.
//
// GAP-REFL-3 (API surface, net48-verified): Activator.CreateInstance(string)
// -- the single-arg string form previously wired -- DOES NOT EXIST in .NET
// Framework 4.8 (mscorlib v4.0.30319 reflection dump: the string overloads
// are (string assemblyName, string typeName) and its (params object[]) /
// (bool, BindingFlags, ...) extensions), so it can never be reached from a
// net48 input assembly. The mapping was corrected to the reachable
// CreateInstance(Type) overload -- its argument is a Type VALUE (the type's
// full-name string in the simplified Type model), the exact shape the
// 1-param icall consumes; the (string) arity now fails LOUD naming the
// net48 actual signatures.
//
// Harness: same as MethodMapTests.Type.cs -- a synthesized ECMA-335 PE
// whose only MemberReference is the member under test, resolved through the
// same public Resolve entry point the transpiler uses. Signature blobs are
// the raw bytes RawSignature consumes; a 0x11/0x12 (valuetype/class) byte
// consumes a trailing TypeDefOrRef coded index. The synthesized PE holds
// exactly ONE TypeReference (the owner, row 1), so the coded index is
// (1<<2)|1 = 0x05 after each such byte.
public class MethodMapTestsReflect
{
    //--------------------------------------------------------------------------
    // Activator.CreateInstance(Type) -- static: typeinfo registry lookup +
    // instantiation of the registered class descriptor; returns the new
    // object itself (no Type tracking -- later member calls route by their
    // declaring owner like any other instance). GAP-REFL-3: this is the
    // reachable net48 single-param overload (the Type VALUE is the type's
    // full-name string, the shape the 1-param icall consumes).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Activator_CreateInstanceType_MapsToReflectIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Activator", "CreateInstance",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x12, 0x05 }), "CreateInstance"); // (Type)

        Assert.Equal("il2cpp::il2cpp_activator_create_instance", resolved.CppName);
        Assert.True(resolved.ReturnsRef);   // the created instance
        Assert.False(resolved.IsInstance);  // static
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Activator.CreateInstance(string) -- the single-arg string overload
    // DOES NOT EXIST in .NET Framework 4.8 (GAP-REFL-3; net48 string forms
    // are (string, string) / (string, string, params object[]) / ...). The
    // old M5.5 mapping was e2e-unreachable from any net48 assembly -- must
    // fail LOUD naming the net48 actual signatures.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Activator_CreateInstanceString_ThrowsLoudNet48()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Activator", "CreateInstance",
                new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "CreateInstance")); // (string)

        Assert.Contains("System.Activator::CreateInstance", ex.Message);
        Assert.Contains("does not exist in .NET Framework 4.8", ex.Message);
        Assert.Contains("CreateInstance(string assemblyName, string typeName)", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Activator.CreateInstance(string, string) -- net48-REAL (assemblyName,
    // typeName), but the runtime icall is single-param (a Type VALUE / one
    // name string) -- no runtime support -- must fail LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Activator_CreateInstanceStringString_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Activator", "CreateInstance",
                new byte[] { 0x00, 0x02, 0x12, 0x05, 0x0E, 0x0E }), "CreateInstance")); // (string, string)

        Assert.Contains("System.Activator::CreateInstance", ex.Message);
        Assert.Contains("net48 mscorlib actual overloads", ex.Message);
        Assert.Contains("only (Type) maps to il2cpp_activator_create_instance", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Type.GetType(string) -- static: registry lookup, returns the type's
    // full-name string (the simplified Type model) or null; the result is
    // tracked as "System.Type" so .Name/.GetTypeCode/.Equals/.IsAssignable-
    // From/.GetProperty on it resolve (the GetTypeFromHandle linkage).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetTypeString_MapsToReflectIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetType",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "GetType"); // (string)

        Assert.Equal("il2cpp::il2cpp_type_get_type", resolved.CppName);
        Assert.True(resolved.ReturnsRef);   // the name string
        Assert.Equal("System.Type", resolved.ReturnTypeName); // fold linkage
        Assert.False(resolved.IsInstance);  // static
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Type.GetType(string, bool) -- static: the throwOnError overload maps to
    // the 3-param runtime wrapper il2cpp_type_get_type_ex (the wrapper appends
    // ignoreCase = 0; the 2-arg overload has no case-insensitive form). The
    // bool rides the int32 carrier; the result is tracked as "System.Type"
    // (the fold linkage). RF-REFL (e26d9b56): aligned with the net48 oracle.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetTypeTwoParams_MapsToReflectIcallEx()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetType",
            new byte[] { 0x00, 0x02, 0x12, 0x05, 0x0E, 0x02 }), "GetType"); // (string, bool)

        Assert.Equal("il2cpp::il2cpp_type_get_type_ex", resolved.CppName);
        Assert.True(resolved.ReturnsRef);   // the name string
        Assert.Equal("System.Type", resolved.ReturnTypeName); // fold linkage
        Assert.False(resolved.IsInstance);  // static
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Type.GetType with an arity the runtime wrapper does not cover -- only
    // the 1/2/3-param (string[, bool[, bool]]) overloads are wired, so any
    // other shape stays LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetTypeUnwiredArity_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Type", "GetType",
                new byte[] { 0x00, 0x04, 0x12, 0x05, 0x0E, 0x02, 0x02, 0x02 }), "GetType")); // (string, bool, bool, bool)

        Assert.Contains("System.Type::GetType", ex.Message);
    }

    //--------------------------------------------------------------------------
    // The System.Type route now tries TryMapReflect FIRST, then falls back
    // to the Type domain (TryMapType) -- existing Type mappings (e.g.
    // GetTypeFromHandle / GetProperty(string)) must keep resolving.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetTypeFromHandle_StillResolvesThroughTypeDomain()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetTypeFromHandle",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x11, 0x05 }), "GetTypeFromHandle"); // RuntimeTypeHandle

        // p-refl2-gapmatrix (E01/E02): the handle VALUE rides as an intptr_t
        // slot, so the Type-domain icall is the intptr-carrying variant.
        Assert.Equal("il2cpp::runtime::il2cpp_type_get_type_from_handle_value", resolved.CppName);
        Assert.Equal("System.Type", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Type_GetPropertyString_StillResolvesThroughTypeDomain()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetProperty",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "GetProperty"); // (string)

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_property", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Dispatch miss -- an unwired Activator member falls back to the
    // generated-name path, proving the domain returns null instead of
    // misrouting.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Activator_UnmappedMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System", "Activator", "GetObject",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetObject"); // ()

        Assert.StartsWith("System_Activator_GetObject_", resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Harness -- same synthetic-PE pipeline as MethodMapTests.Type.cs.
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, false, null, null);
    }

    private static MemoryStream Synthesize(string ns, string typeName, string memberName, byte[] signature)
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
}
