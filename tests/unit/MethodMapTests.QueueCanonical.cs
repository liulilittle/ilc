using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// Queue/Stack canonical-shared dispatch (A4 虚派发改造 + 泛型禁重复特例化铁律):
//   * GetEnumerator: 全实例化走单一通用臂 (同一 icall, token 由统一函数机械计算),
//     消灭按 int/long/double/string/object/class/dotted 分臂;
//   * get_Current: 未知/无约束 enumerator token 落 object 共享臂
//     (il2cpp_*_enumerator_get_current 跟踪 System.Object), 禁止静默回退到
//     int 拆箱 (Queue 旧缺省) 或误跟踪 string (Stack 旧缺省);
//   * 未知值型实例化 (float/bool/...) 的容器成员保持 LOUD -- 无泛型 box 发射,
//     不得误走 object 共享臂 (会产生类型错误的 C++).
public class MethodMapTestsQueueCanonical
{
    //--------------------------------------------------------------------------
    // Queue GetEnumerator: int token 经通用臂保持 (collapse 前后同 icall+token).
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
    // Queue get_Current: 已知 int token 保持值型快路径 (拆箱 icall).
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
    // Queue get_Current: 未知 token 落 object 共享臂 (今日红: 静默 int 拆箱).
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
    // Queue 容器: 未知值型实例化保持 LOUD (不得误走 object 臂).
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
    // Stack GetEnumerator: int token 经通用臂保持.
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
    // Stack get_Current: 已知 int token 保持值型快路径.
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
    // Stack get_Current: 未知 token 落 object 共享臂 (今日红: 误跟踪 string).
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
    // Stack 容器: 未知值型实例化保持 LOUD.
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
