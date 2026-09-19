using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Queue / MethodMap.Stack dispatch tests (stackqueue.h icalls):
// prove the BclResolveCore owner->domain switch routes System.Collections
// .Queue / .Stack members to TryMapQueue / TryMapStack with the runtime
// icall names, arity and ref classifications.
//
// NAMESPACE: the stackqueue.h/.cpp icalls live in PLAIN namespace il2cpp
// (unlike most BCL icall families in il2cpp::runtime -- interlocked,
// hashtable, ...), so the CppNames are "il2cpp::il2cpp_queue_*" /
// "il2cpp::il2cpp_stack_*" -- mirroring the runtime's actual declarations.
//
// Boxing semantics covered by the wiring (documented in the two MethodMap
// files): Enqueue/Push arguments are Il2CppObject* slots -- the IL `box`
// opcode boxes value arguments BEFORE the call (box wiring), so no cast
// code is applied; Dequeue/Pop/Peek results stay Il2CppObject* -- the
// CALLER's castclass/unbox turns them back into values.
//
// .ctor: the runtime create icalls (il2cpp_queue_create /
// il2cpp_stack_create) take NO self and return the new object; the
// fold-supplied newobj temp must be DROPPED at emission ("drop" cast) to
// keep the call arity correct. Capacity ctors have no runtime icall and
// are rejected LOUD (NotSupportedException with the "{owner}::{name}"
// prefix -- see BclResolve's wrap).
//
// The PEs are SYNTHESIZED with System.Reflection.Metadata's own writer
// (MetadataBuilder + ManagedPEBuilder -- net8.0 shared framework, no new
// packages), the same reader pipeline the transpiler uses; signature blobs
// are the raw bytes RawSignature consumes (0x08=i4, 0x1C=object, 0x01=void).
public class MethodMapQueueStackTests
{
    //--------------------------------------------------------------------------
    // Queue domain (MethodMap.Queue.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Queue_EnqueueObject_MapsToEnqueueIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Queue", "Enqueue", Sig(0x01, 0x1C)), "Enqueue");

        Assert.Equal("il2cpp::il2cpp_queue_enqueue", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Queue_Dequeue_MapsToDequeueIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Queue", "Dequeue", Sig(0x1C)), "Dequeue");

        Assert.Equal("il2cpp::il2cpp_queue_dequeue", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Queue_Peek_MapsToPeekIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Queue", "Peek", Sig(0x1C)), "Peek");

        Assert.Equal("il2cpp::il2cpp_queue_peek", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Queue_GetCount_MapsToCountIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Queue", "get_Count", Sig(0x08)), "get_Count");

        Assert.Equal("il2cpp::il2cpp_queue_get_count", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Queue_Clear_MapsToClearIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Queue", "Clear", Sig(0x01)), "Clear");

        Assert.Equal("il2cpp::il2cpp_queue_clear", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Queue_CtorParameterless_DropsReceiverOnCreateIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Queue", ".ctor", Sig(0x01)), ".ctor");

        Assert.Equal("il2cpp::il2cpp_queue_create", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        // The create icall takes NO self -- the newobj temp must be dropped.
        Assert.Equal("drop", resolved.ParamTypeNames![0]);
    }

    [Fact]
    public void Resolve_Queue_CtorCapacity_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections", "Queue", ".ctor", Sig(0x01, 0x08)), ".ctor"));

        Assert.Contains("System.Collections.Queue::.ctor", ex.Message);
    }

    [Fact]
    public void Resolve_Queue_ToArray_MapsToToArrayIcall()
    {
        // M7 stkq: Queue.ToArray -> the runtime il2cpp_queue_to_array returns
        // an EXACT-LENGTH object[] in HEAD-FIRST ring-aware order. Return
        // 0x1D SZARRAY + element 0x1C (object).
        var resolved = ResolveMember(Synthesize("System.Collections", "Queue", "ToArray",
            new byte[] { 0x00, 0x00, 0x1D, 0x1C }), "ToArray");

        Assert.Equal("il2cpp::il2cpp_queue_to_array", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Stack domain (MethodMap.Stack.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Stack_PushObject_MapsToPushIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Stack", "Push", Sig(0x01, 0x1C)), "Push");

        Assert.Equal("il2cpp::il2cpp_stack_push", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stack_Pop_MapsToPopIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Stack", "Pop", Sig(0x1C)), "Pop");

        Assert.Equal("il2cpp::il2cpp_stack_pop", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stack_Peek_MapsToPeekIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Stack", "Peek", Sig(0x1C)), "Peek");

        Assert.Equal("il2cpp::il2cpp_stack_peek", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stack_GetCount_MapsToCountIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Stack", "get_Count", Sig(0x08)), "get_Count");

        Assert.Equal("il2cpp::il2cpp_stack_get_count", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stack_Clear_MapsToClearIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Stack", "Clear", Sig(0x01)), "Clear");

        Assert.Equal("il2cpp::il2cpp_stack_clear", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Stack_CtorParameterless_DropsReceiverOnCreateIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Stack", ".ctor", Sig(0x01)), ".ctor");

        Assert.Equal("il2cpp::il2cpp_stack_create", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        // The create icall takes NO self -- the newobj temp must be dropped.
        Assert.Equal("drop", resolved.ParamTypeNames![0]);
    }

    [Fact]
    public void Resolve_Stack_CtorCapacity_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections", "Stack", ".ctor", Sig(0x01, 0x08)), ".ctor"));

        Assert.Contains("System.Collections.Stack::.ctor", ex.Message);
    }

    [Fact]
    public void Resolve_Stack_ToArray_MapsToToArrayIcall()
    {
        // M7 stkq: Stack.ToArray -> the runtime il2cpp_stack_to_array returns
        // an EXACT-LENGTH object[] in TOP-OF-STACK order. Return 0x1D SZARRAY
        // + element 0x1C (object).
        var resolved = ResolveMember(Synthesize("System.Collections", "Stack", "ToArray",
            new byte[] { 0x00, 0x00, 0x1D, 0x1C }), "ToArray");

        Assert.Equal("il2cpp::il2cpp_stack_to_array", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodRefResolverDispatchTests).
    //--------------------------------------------------------------------------
    //--------------------------------------------------------------------------
    // G4: the generic Queue`1<int> / Stack`1<string> expansions
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Queue1Int_Enqueue_MapsToIntEnqueue()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1", "Enqueue", new byte[] { 0x00, 0x01, 0x01, 0x13, 0x00 }), "Enqueue",
            receiverType: "System.Collections.Generic.Queue`1_int");

        Assert.Equal("il2cpp::runtime::icall_queue_int_enqueue", resolved.CppName);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Queue1Int_Dequeue_ResultKindInt()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1", "Dequeue", new byte[] { 0x00, 0x00, 0x13, 0x00 }), "Dequeue",
            receiverType: "System.Collections.Generic.Queue`1_int");

        Assert.Equal("il2cpp::runtime::icall_queue_int_dequeue", resolved.CppName);
        Assert.Equal("i", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Queue1Int_ToArray_TracksInt32Array()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1", "ToArray", new byte[] { 0x00, 0x00, 0x1D, 0x13, 0x00 }), "ToArray",
            receiverType: "System.Collections.Generic.Queue`1_int");

        Assert.Equal("il2cpp::runtime::icall_queue_int_to_array", resolved.CppName);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Queue1Int_Contains_MapsToIntContains()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1", "Contains", new byte[] { 0x00, 0x01, 0x02, 0x13, 0x00 }), "Contains",
            receiverType: "System.Collections.Generic.Queue`1_int");

        Assert.Equal("il2cpp::runtime::icall_queue_int_contains", resolved.CppName);
    }

    [Fact]
    public void Resolve_Stack1String_Push_MapsToPushIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Stack`1", "Push", new byte[] { 0x00, 0x01, 0x01, 0x13, 0x00 }), "Push",
            receiverType: "System.Collections.Generic.Stack`1_string");

        Assert.Equal("il2cpp::il2cpp_stack_push", resolved.CppName);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Stack1String_Contains_MapsToStackContainsIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Stack`1", "Contains", new byte[] { 0x00, 0x01, 0x02, 0x13, 0x00 }), "Contains",
            receiverType: "System.Collections.Generic.Stack`1_string");

        Assert.Equal("il2cpp::il2cpp_stack_contains", resolved.CppName);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Stack1String_ToArray_TracksStringArray()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Stack`1", "ToArray", new byte[] { 0x00, 0x00, 0x1D, 0x13, 0x00 }), "ToArray",
            receiverType: "System.Collections.Generic.Stack`1_string");

        Assert.Equal("il2cpp::il2cpp_stack_to_array", resolved.CppName);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // T2 W1: the reference-element Queue`1/Stack`1 expansions (string/object/
    // class). These ride the non-generic Il2CppObject* slots unchanged, but
    // (a) ToArray tracks the element array (string[]/object[]; the `_class`
    // suffix collapses the concrete element name so it stays LOUD) and
    // (b) the enumerator tracks a per-element token so get_Current returns the
    // reference element instead of the int32 unbox.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Queue1String_Enqueue_MapsToQueueEnqueue()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1", "Enqueue", new byte[] { 0x00, 0x01, 0x01, 0x13, 0x00 }), "Enqueue",
            receiverType: "System.Collections.Generic.Queue`1_string");

        Assert.Equal("il2cpp::il2cpp_queue_enqueue", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Queue1String_ToArray_TracksStringArray()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1", "ToArray", new byte[] { 0x00, 0x00, 0x1D, 0x13, 0x00 }), "ToArray",
            receiverType: "System.Collections.Generic.Queue`1_string");

        Assert.Equal("il2cpp::il2cpp_queue_to_array", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Queue1Object_ToArray_TracksObjectArray()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1", "ToArray", new byte[] { 0x00, 0x00, 0x1D, 0x13, 0x00 }), "ToArray",
            receiverType: "System.Collections.Generic.Queue`1_object");

        Assert.Equal("il2cpp::il2cpp_queue_to_array", resolved.CppName);
        Assert.Equal("System.Object[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Queue1Class_ToArray_ThrowsLoud()
    {
        // The `_class` suffix collapses the concrete element name, so the
        // element-array tracking is not recoverable -> LOUD (deferred).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "Queue`1", "ToArray", new byte[] { 0x00, 0x00, 0x1D, 0x13, 0x00 }), "ToArray",
                receiverType: "System.Collections.Generic.Queue`1_class"));

        Assert.Contains("System.Collections.Generic.Queue`1::ToArray", ex.Message);
    }

    [Fact]
    public void Resolve_Queue1String_GetEnumerator_TracksRefEnumerator()
    {
        // Return 0x1C (System.Object) -- a self-contained reference byte; the
        // resolver's GetEnumerator arm ignores the decoded return type (it
        // dispatches on the receiver), so the blob only needs to PARSE. (A
        // 0x11 VALUETYPE return would demand a TypeDefOrRef handle; a bare
        // 0x00 resolves to ModuleDefinition, which M2 rejects.)
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1", "GetEnumerator", new byte[] { 0x00, 0x00, 0x1C }), "GetEnumerator",
            receiverType: "System.Collections.Generic.Queue`1_string");

        Assert.Equal("il2cpp::il2cpp_queue_get_enumerator", resolved.CppName);
        Assert.Equal("System.Collections.Generic.Queue`1.Enumerator_string", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Queue1StringEnumerator_GetCurrent_ReturnsRef()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1+Enumerator", "get_Current", new byte[] { 0x00, 0x00, 0x13, 0x00 }), "get_Current",
            receiverType: "System.Collections.Generic.Queue`1.Enumerator_string");

        Assert.Equal("il2cpp::il2cpp_queue_enumerator_get_current", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Queue1ObjectEnumerator_GetCurrent_ReturnsObjectRef()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Queue`1+Enumerator", "get_Current", new byte[] { 0x00, 0x00, 0x13, 0x00 }), "get_Current",
            receiverType: "System.Collections.Generic.Queue`1.Enumerator_object");

        Assert.Equal("il2cpp::il2cpp_queue_enumerator_get_current", resolved.CppName);
        Assert.Equal("System.Object", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Stack1Object_Push_MapsToStackPush()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Stack`1", "Push", new byte[] { 0x00, 0x01, 0x01, 0x13, 0x00 }), "Push",
            receiverType: "System.Collections.Generic.Stack`1_object");

        Assert.Equal("il2cpp::il2cpp_stack_push", resolved.CppName);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Stack1Object_ToArray_TracksObjectArray()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Stack`1", "ToArray", new byte[] { 0x00, 0x00, 0x1D, 0x13, 0x00 }), "ToArray",
            receiverType: "System.Collections.Generic.Stack`1_object");

        Assert.Equal("il2cpp::il2cpp_stack_to_array", resolved.CppName);
        Assert.Equal("System.Object[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Stack1Class_ToArray_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "Stack`1", "ToArray", new byte[] { 0x00, 0x00, 0x1D, 0x13, 0x00 }), "ToArray",
                receiverType: "System.Collections.Generic.Stack`1_class"));

        Assert.Contains("System.Collections.Generic.Stack`1::ToArray", ex.Message);
    }

    [Fact]
    public void Resolve_Stack1ObjectEnumerator_GetCurrent_ReturnsObjectRef()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Stack`1+Enumerator", "get_Current", new byte[] { 0x00, 0x00, 0x13, 0x00 }), "get_Current",
            receiverType: "System.Collections.Generic.Stack`1.Enumerator_object");

        Assert.Equal("il2cpp::il2cpp_stack_enumerator_get_current", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Object", resolved.ReturnTypeName);
    }

    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null, null);
    }

    // Signature blob: header 0x00 (DEFAULT, non-generic), param count, return
    // byte, then one byte per parameter (the raw bytes RawSignature decodes).
    private static byte[] Sig(byte ret, params byte[] ps)
    {
        var bytes = new byte[3 + ps.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)ps.Length;
        bytes[2] = ret;
        Array.Copy(ps, 0, bytes, 3, ps.Length);
        return bytes;
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
