using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// Queue/Stack canonical-shared dispatch (A4 virtual-dispatch rework + the no-repeated-generic-specialization rule):
//   * GetEnumerator: all instantiations go through a single common arm (same icall; token mechanically computed by a unified function),
//     eliminating per-int/long/double/string/object/class/dotted arms;
//   * get_Current: unknown/unconstrained enumerator token falls to the shared object arm
//     (il2cpp_*_enumerator_get_current tracks System.Object); no silent fallback to
//     int unboxing (Queue old default) or mistakenly tracking string (Stack old default);
//   * container members of unknown value-type instantiations (float/bool/...) stay LOUD -- no generic box emission,
//     must not wrongly go through the shared object arm (would produce type-incorrect C++).
public class MethodMapTestsQueueCanonical
{
    //--------------------------------------------------------------------------
    // Queue GetEnumerator: int token stays on the common arm (same icall+token before/after collapse).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Queue_GetEnumerator_IntToken_ViaUniversalArm()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1", "GetEnumerator",
            new byte[] { 0x20, 0x00, 0x12, 0x05 }), "GetEnumerator",
            receiverType: "System.Collections.Generic.Queue`1_int");

        Assert.Equal("il2cpp::il2cpp_queue_get_enumerator", resolved.CppName);
        Assert.Equal("System.Collections.Generic.Queue`1.Enumerator_int", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Queue get_Current: known int token keeps the value-type fast path (unboxing icall).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Queue_GetCurrent_IntToken_KeepsIntUnbox()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1+Enumerator", "get_Current",
            new byte[] { 0x00, 0x00, 0x13, 0x00 }), "get_Current",
            receiverType: "System.Collections.Generic.Queue`1.Enumerator_int");

        Assert.Equal("il2cpp::runtime::icall_queue_int_enumerator_get_current", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Queue get_Current: unknown token falls to the shared object arm (today red: silent int unboxing).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Queue_GetCurrent_UnknownToken_MapsToSharedObjectArm()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1+Enumerator", "get_Current",
            new byte[] { 0x00, 0x00, 0x13, 0x00 }), "get_Current",
            receiverType: "System.Collections.Generic.Queue`1.Enumerator_float");

        Assert.Equal("il2cpp::il2cpp_queue_enumerator_get_current", resolved.CppName);
        Assert.Equal("System.Object", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Queue container: unknown value-type instantiations stay LOUD (must not wrongly take the object arm).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Queue_GetEnumerator_FloatTag_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "Queue`1", "GetEnumerator",
                new byte[] { 0x20, 0x00, 0x12, 0x05 }), "GetEnumerator",
                receiverType: "System.Collections.Generic.Queue`1_float"));

        Assert.Contains("System.Collections.Generic.Queue`1::GetEnumerator", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Stack GetEnumerator: int token stays on the common arm.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Stack_GetEnumerator_IntToken_ViaUniversalArm()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Stack`1", "GetEnumerator",
            new byte[] { 0x20, 0x00, 0x12, 0x05 }), "GetEnumerator",
            receiverType: "System.Collections.Generic.Stack`1_int");

        Assert.Equal("il2cpp::il2cpp_stack_get_enumerator", resolved.CppName);
        Assert.Equal("System.Collections.Generic.Stack`1.Enumerator_int", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Stack get_Current: known int token keeps the value-type fast path.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Stack_GetCurrent_IntToken_KeepsIntUnbox()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Stack`1+Enumerator", "get_Current",
            new byte[] { 0x00, 0x00, 0x13, 0x00 }), "get_Current",
            receiverType: "System.Collections.Generic.Stack`1.Enumerator_int");

        Assert.Equal("il2cpp::runtime::icall_stack_int_enumerator_get_current", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Stack get_Current: unknown token falls to the shared object arm (today red: mistakenly tracks string).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Stack_GetCurrent_UnknownToken_MapsToSharedObjectArm()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Stack`1+Enumerator", "get_Current",
            new byte[] { 0x00, 0x00, 0x13, 0x00 }), "get_Current",
            receiverType: "System.Collections.Generic.Stack`1.Enumerator_float");

        Assert.Equal("il2cpp::il2cpp_stack_enumerator_get_current", resolved.CppName);
        Assert.Equal("System.Object", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Stack container: unknown value-type instantiations stay LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Stack_GetEnumerator_FloatTag_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "Stack`1", "GetEnumerator",
                new byte[] { 0x20, 0x00, 0x12, 0x05 }), "GetEnumerator",
                receiverType: "System.Collections.Generic.Stack`1_float"));

        Assert.Contains("System.Collections.Generic.Stack`1::GetEnumerator", ex.Message);
    }

    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null, null);
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
