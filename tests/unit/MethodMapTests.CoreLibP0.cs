using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// CoreLib P0 dispatch tests -- the members that exist in BOTH shipped
// System.Private.CoreLib 8.0.31 and 9.0.20 (docs/audit/corelib-p0-reachability.md).
//
// Every row asserted here was read out of the SHIPPED assembly with
// System.Reflection.Metadata (probe: .opencode/tmp/impl-corelib-p0/probe), so
// the signatures below are the measured ones -- not the CoreCLR main-branch
// reference text. That distinction matters: e.g. shipped `GC._Collect` takes
// (int, int) while the main branch has grown a third bool.
//
// Same synthetic-PE pipeline as MethodMapTests.Interlocked / .RuntimeHelpers:
// one MemberReference resolved through the public Resolve entry point.
public class MethodMapTestsCoreLibP0
{
    //==========================================================================
    // System.GC -- the QCall/FCall bodies behind the public GC API.
    //==========================================================================
    [Theory]
    [InlineData("_Collect", 2, new byte[] { 0x08, 0x08 }, 0x01,
        "il2cpp::runtime::icall_gc_collect", false, 2, null)]
    [InlineData("_CollectionCount", 2, new byte[] { 0x08, 0x08 }, 0x08,
        "il2cpp::il2cpp_gc_get_collection_count", true, 2, "l")]
    [InlineData("_GetTotalPauseDuration", 0, new byte[0], 0x0A,
        "il2cpp::il2cpp_gc_get_total_pause_ticks", true, 0, "l")]
    [InlineData("_RegisterForFullGCNotification", 2, new byte[] { 0x08, 0x08 }, 0x02,
        "il2cpp::il2cpp_gc_register_for_full_gc_notification", true, 2, null)]
    [InlineData("_CancelFullGCNotification", 0, new byte[0], 0x02,
        "il2cpp::il2cpp_gc_cancel_full_gc_notification", true, 0, null)]
    [InlineData("_AddMemoryPressure", 1, new byte[] { 0x0B }, 0x01,
        "il2cpp::runtime::icall_gc_add_memory_pressure", false, 1, null)]
    [InlineData("_RemoveMemoryPressure", 1, new byte[] { 0x0B }, 0x01,
        "il2cpp::runtime::icall_gc_remove_memory_pressure", false, 1, null)]
    [InlineData("_WaitForPendingFinalizers", 0, new byte[0], 0x01,
        "il2cpp::runtime::icall_gc_finalize_trigger", false, 0, null)]
    [InlineData("_EndNoGCRegion", 0, new byte[0], 0x08,
        "il2cpp::runtime::icall_gc_end_no_gc_region", true, 0, null)]
    [InlineData("_GetGenerationBudget", 1, new byte[] { 0x08 }, 0x0A,
        "il2cpp::il2cpp_gc_get_generation_budget", true, 1, "l")]
    [InlineData("_UnregisterFrozenSegment", 1, new byte[] { 0x18 }, 0x01,
        "il2cpp::runtime::icall_gc_unregister_frozen_segment", false, 1, null)]
    public void Resolve_GcInternalBodies_MapToRuntimeEntries(string memberName, int count, byte[] paramBytes,
        byte ret, string cpp, bool returnsValue, int expectedParamCount, string expectedResultKind)
    {
        var resolved = ResolveMember("System", "GC", Synthesize("System", "GC", memberName, Sig(count, ret, paramBytes)),
            memberName);

        Assert.Equal(cpp, resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(returnsValue, resolved.ReturnsValue);
        Assert.Equal(expectedParamCount, resolved.ParamCount);
        Assert.Equal(expectedResultKind, resolved.ResultKind);
    }

    // _RegisterFrozenSegment(IntPtr, nint) -> IntPtr: the handle rides the
    // intptr_t carrier (ResultKind "ptr").
    [Fact]
    public void Resolve_GcRegisterFrozenSegment_MapsToFrozenSegmentEntry()
    {
        var resolved = ResolveMember("System", "GC",
            Synthesize("System", "GC", "_RegisterFrozenSegment", Sig(2, 0x18, 0x18, 0x18)),
            "_RegisterFrozenSegment");

        Assert.Equal("il2cpp::runtime::icall_gc_register_frozen_segment", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("ptr", resolved.ResultKind);
    }

    // _EnableNoGCRegionCallback(workItem*, long) -> status: no region is ever
    // granted, so both arguments are dropped at the call site.
    [Fact]
    public void Resolve_GcEnableNoGCRegionCallback_DropsBothArguments()
    {
        var resolved = ResolveMember("System", "GC",
            Synthesize("System", "GC", "_EnableNoGCRegionCallback", Sig(2, 0x08, 0x0F, 0x1C, 0x0A)),
            "_EnableNoGCRegionCallback");

        Assert.Equal("il2cpp::runtime::icall_gc_enable_no_gc_region_callback", resolved.CppName);
        Assert.Equal(new[] { "drop", "drop" }, resolved.ParamTypeNames);
    }

    // _EnumerateConfigurationValues(void*, callback*): no configuration values
    // exist, so the callback is never invoked -> both arguments dropped.
    [Fact]
    public void Resolve_GcEnumerateConfigurationValues_DropsBothArguments()
    {
        var resolved = ResolveMember("System", "GC",
            Synthesize("System", "GC", "_EnumerateConfigurationValues", Sig(2, 0x01, 0x0F, 0x01, 0x18)),
            "_EnumerateConfigurationValues");

        Assert.Equal("il2cpp::runtime::icall_gc_enumerate_configuration_values", resolved.CppName);
        Assert.Equal(new[] { "drop", "drop" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // The PUBLIC wrappers over those QCall bodies. A net8/net9-compiled caller
    // binds these (the wrapper IL lives in CoreLib and is never transpiled), so
    // they need their own arms.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("EndNoGCRegion", 0, new byte[0], 0x01,
        "il2cpp::runtime::icall_gc_end_no_gc_region_public", 0, null)]
    [InlineData("RefreshMemoryLimit", 0, new byte[0], 0x01,
        "il2cpp::runtime::icall_gc_refresh_memory_limit", 0, null)]
    [InlineData("GetTotalPauseDuration", 0, new byte[] { 0x05 }, 0x11,
        "il2cpp::runtime::icall_gc_get_total_pause_duration", 0, "ts")]
    public void Resolve_GcPublicWrappers_MapToRuntimeEntries(string memberName, int count, byte[] paramBytes,
        byte ret, string cpp, int expectedParamCount, string expectedResultKind)
    {
        var resolved = ResolveMember("System", "GC", Synthesize("System", "GC", memberName, Sig(count, ret, paramBytes)),
            memberName);

        Assert.Equal(cpp, resolved.CppName);
        Assert.Equal(expectedParamCount, resolved.ParamCount);
        Assert.Equal(expectedResultKind, resolved.ResultKind);
    }

    // RegisterNoGCRegionCallback(long totalSize, Action callback): argument
    // order follows the IL, and the callback slot is a managed reference.
    [Fact]
    public void Resolve_GcRegisterNoGCRegionCallback_KeepsIlArgumentOrder()
    {
        var resolved = ResolveMember("System", "GC",
            Synthesize("System", "GC", "RegisterNoGCRegionCallback", Sig(2, 0x01, 0x0A, 0x1C)),
            "RegisterNoGCRegionCallback");

        Assert.Equal("il2cpp::runtime::icall_gc_register_no_gc_region_callback_public", resolved.CppName);
        Assert.Equal(new[] { "l", "" }, resolved.ParamTypeNames);
    }

    // GetTotalPauseDuration answers a TimeSpan, so a chained member call
    // (`.Ticks`, `.TotalMilliseconds`) resolves with a System.TimeSpan receiver.
    [Fact]
    public void Resolve_GcGetTotalPauseDuration_DeclaresTimeSpanResult()
    {
        var resolved = ResolveMember("System", "GC",
            Synthesize("System", "GC", "GetTotalPauseDuration", Sig(0, 0x11, 0x05)),
            "GetTotalPauseDuration");

        Assert.Equal("System.TimeSpan", resolved.ReturnTypeName);
        Assert.True(resolved.ReturnsValue);
    }

    //==========================================================================
    // System.Exception -- the CoreLib-internal FCalls.
    //==========================================================================
    [Fact]
    public void Resolve_ExceptionIsImmutableAgile_MapsToRuntimeEntry()
    {
        var resolved = ResolveMember("System", "Exception",
            Synthesize("System", "Exception", "IsImmutableAgileException", Sig(1, 0x02, 0x1C)),
            "IsImmutableAgileException");

        Assert.Equal("il2cpp::runtime::icall_exception_is_immutable_agile_exception", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ExceptionPrepareForForeignExceptionRaise_MapsToRuntimeEntry()
    {
        var resolved = ResolveMember("System", "Exception",
            Synthesize("System", "Exception", "PrepareForForeignExceptionRaise", Sig(0, 0x01)),
            "PrepareForForeignExceptionRaise");

        Assert.Equal("il2cpp::runtime::icall_exception_prepare_for_foreign_exception_raise", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    // GetMessageFromNativeResources is a registered GAP: its out channel is a
    // CoreLib-internal StringHandleOnStack struct with no C++ value model. The
    // rejection must be LOUD and self-identifying (never a silent phantom).
    [Fact]
    public void Resolve_ExceptionGetMessageFromNativeResources_IsLoudGap()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember("System", "Exception",
                Synthesize("System", "Exception", "GetMessageFromNativeResources", Sig(2, 0x01, 0x11, 0x05, 0x1C)),
                "GetMessageFromNativeResources"));

        Assert.Contains("System.Exception::GetMessageFromNativeResources", ex.Message);
        Assert.Contains("b5-gaps.md", ex.Message);
    }

    //==========================================================================
    // System.Math.ModF -- the measured FCall is (double, double*) -> double,
    // exactly the runtime's icall_math_modf_d.
    //==========================================================================
    [Fact]
    public void Resolve_MathModF_MapsToModfIcall()
    {
        var resolved = ResolveMember("System", "Math",
            Synthesize("System", "Math", "ModF", Sig(2, 0x0D, 0x0D, 0x0F, 0x0D)),
            "ModF");

        Assert.Equal("il2cpp::runtime::icall_math_modf_d", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("d", resolved.ResultKind);
        // The integral part is written through the caller's address slot.
        Assert.Equal(new[] { "d", "addr:d" }, resolved.ParamTypeNames);
    }

    // net48's private SplitFractionDouble has a DIFFERENT contract (single
    // in/out pointer) and no runtime symbol -- it stays LOUD.
    [Fact]
    public void Resolve_MathSplitFractionDouble_IsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember("System", "Math",
                Synthesize("System", "Math", "SplitFractionDouble", Sig(1, 0x0D, 0x0F, 0x0D)),
                "SplitFractionDouble"));

        Assert.Contains("System.Math::SplitFractionDouble", ex.Message);
    }

    //==========================================================================
    // System.Runtime.CompilerServices.RuntimeHelpers.
    //==========================================================================
    [Fact]
    public void Resolve_RuntimeHelpersTryGetHashCode_MapsToIdentityHash()
    {
        var resolved = ResolveMember("System.Runtime.CompilerServices", "RuntimeHelpers",
            Synthesize("System.Runtime.CompilerServices", "RuntimeHelpers", "TryGetHashCode", Sig(1, 0x08, 0x1C)),
            "TryGetHashCode");

        Assert.Equal("il2cpp::runtime::icall_object_get_hash_code", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_RuntimeHelpersTryEnsureSufficientExecutionStack_MapsToRuntimeEntry()
    {
        var resolved = ResolveMember("System.Runtime.CompilerServices", "RuntimeHelpers",
            Synthesize("System.Runtime.CompilerServices", "RuntimeHelpers", "TryEnsureSufficientExecutionStack",
                Sig(0, 0x02)),
            "TryEnsureSufficientExecutionStack");

        Assert.Equal("il2cpp::runtime::icall_runtime_helpers_try_ensure_sufficient_execution_stack",
            resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_RuntimeHelpersCompileMethod_MapsToNoOpEntry()
    {
        var resolved = ResolveMember("System.Runtime.CompilerServices", "RuntimeHelpers",
            Synthesize("System.Runtime.CompilerServices", "RuntimeHelpers", "CompileMethod", Sig(1, 0x01, 0x11, 0x05)),
            "CompileMethod");

        Assert.Equal("il2cpp::runtime::icall_runtime_helpers_compile_method", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
    }

    [Theory]
    [InlineData(1, new byte[] { 0x11, 0x05 })]
    [InlineData(2, new byte[] { 0x11, 0x05, 0x1D, 0x11, 0x05 })]
    [InlineData(3, new byte[] { 0x11, 0x05, 0x0F, 0x18, 0x08 })]
    public void Resolve_RuntimeHelpersPrepareMethod_AllArityShapesMap(int count, byte[] paramBytes)
    {
        var resolved = ResolveMember("System.Runtime.CompilerServices", "RuntimeHelpers",
            Synthesize("System.Runtime.CompilerServices", "RuntimeHelpers", "PrepareMethod", Sig(count, 0x01, paramBytes)),
            "PrepareMethod");

        Assert.Equal("il2cpp::runtime::icall_runtime_helpers_prepare_method", resolved.CppName);
        Assert.Equal(count, resolved.ParamCount);
        // Only the handle is forwarded; the instantiation selector is dropped.
        for (var i = 1; i < resolved.ParamTypeNames!.Length; i++)
            Assert.Equal("drop", resolved.ParamTypeNames[i]);
    }

    [Fact]
    public void Resolve_RuntimeHelpersAllocateTypeAssociatedMemory_MapsToRuntimeEntry()
    {
        var resolved = ResolveMember("System.Runtime.CompilerServices", "RuntimeHelpers",
            Synthesize("System.Runtime.CompilerServices", "RuntimeHelpers", "AllocateTypeAssociatedMemory",
                Sig(2, 0x18, 0x12, 0x05, 0x08)),
            "AllocateTypeAssociatedMemory");

        Assert.Equal("il2cpp::runtime::icall_runtime_helpers_allocate_type_associated_memory", resolved.CppName);
        Assert.Equal("ptr", resolved.ResultKind);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    // The QCall twin's first parameter is the CoreLib-internal QCallTypeHandle
    // struct (VALUETYPE, not a Type reference) -- it cannot be built outside
    // CoreLib and is a registered gap.
    [Fact]
    public void Resolve_RuntimeHelpersAllocateTypeAssociatedMemory_HandleShapeIsLoudGap()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember("System.Runtime.CompilerServices", "RuntimeHelpers",
                Synthesize("System.Runtime.CompilerServices", "RuntimeHelpers", "AllocateTypeAssociatedMemory",
                    Sig(2, 0x18, 0x11, 0x05, 0x09)),
                "AllocateTypeAssociatedMemory"));

        Assert.Contains("QCallTypeHandle", ex.Message);
    }

    // RunClassConstructor / RunModuleConstructor stay LOUD: an Il2CppClass
    // descriptor carries no .cctor pointer, and type-init is driven by the
    // generated ensure_cctor guards.
    [Theory]
    [InlineData("RunClassConstructor")]
    [InlineData("_RunClassConstructor")]
    [InlineData("RunModuleConstructor")]
    [InlineData("_RunModuleConstructor")]
    public void Resolve_RuntimeHelpersTypeInitDrivers_AreLoudGaps(string memberName)
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember("System.Runtime.CompilerServices", "RuntimeHelpers",
                Synthesize("System.Runtime.CompilerServices", "RuntimeHelpers", memberName, Sig(1, 0x01, 0x11, 0x05)),
                memberName));

        Assert.Contains("b5-gaps.md", ex.Message);
    }

    // GetTailCallInfo reads the JIT's tailcall TLS buffer -- an AOT image has
    // none, so it is a registered gap rather than a fabricated pointer.
    [Fact]
    public void Resolve_RuntimeHelpersGetTailCallInfo_IsLoudGap()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember("System.Runtime.CompilerServices", "RuntimeHelpers",
                Synthesize("System.Runtime.CompilerServices", "RuntimeHelpers", "GetTailCallInfo",
                    Sig(2, 0x18, 0x1C, 0x18, 0x0F, 0x18)),
                "GetTailCallInfo"));

        Assert.Contains("b5-gaps.md", ex.Message);
    }

    //==========================================================================
    // System.Threading.Thread -- the CoreLib-internal helpers.
    //==========================================================================
    // InternalFinalize is the Thread object's finalizer hook; Threads own no
    // native handle needing a finalizer here, so it is the empty receiver
    // no-op (the instance form of Object.Finalize).
    [Fact]
    public void Resolve_ThreadInternalFinalize_MapsToReceiverNoOp()
    {
        var resolved = ResolveMember("System.Threading", "Thread",
            Synthesize("System.Threading", "Thread", "InternalFinalize", Sig(0, 0x01)),
            "InternalFinalize");

        Assert.Equal("il2cpp::runtime::il2cpp_object_ctor_nop", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_ThreadGetCurrentOSThreadId_MapsToWidenedId()
    {
        var resolved = ResolveMember("System.Threading", "Thread",
            Synthesize("System.Threading", "Thread", "GetCurrentOSThreadId", Sig(0, 0x0B)),
            "GetCurrentOSThreadId");

        Assert.Equal("il2cpp::runtime::icall_thread_get_current_os_thread_id", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
    }

    // YieldInternal carries the result the net48-void entry drops.
    [Fact]
    public void Resolve_ThreadYieldInternal_MapsToBoolYield()
    {
        var resolved = ResolveMember("System.Threading", "Thread",
            Synthesize("System.Threading", "Thread", "YieldInternal", Sig(0, 0x11, 0x05)),
            "YieldInternal");

        Assert.Equal("il2cpp::runtime::icall_thread_yield_bool", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    // StartInternal takes the CoreLib-internal ThreadHandle wrapper struct, so
    // the public Start surface is mapped instead and this is a registered gap.
    [Fact]
    public void Resolve_ThreadStartInternal_IsLoudGap()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember("System.Threading", "Thread",
                Synthesize("System.Threading", "Thread", "StartInternal", Sig(4, 0x01, 0x11, 0x05, 0x08, 0x08, 0x0F, 0x03)),
                "StartInternal"));

        Assert.Contains("ThreadHandle", ex.Message);
    }

    //==========================================================================
    // System.Threading.Interlocked._MemoryBarrierProcessWide.
    //==========================================================================
    [Theory]
    [InlineData("MemoryBarrierProcessWide")]
    [InlineData("_MemoryBarrierProcessWide")]
    public void Resolve_InterlockedProcessWideBarrier_MapsToSeqCstFence(string memberName)
    {
        var resolved = ResolveMember("System.Threading", "Interlocked",
            Synthesize("System.Threading", "Interlocked", memberName, Sig(0, 0x01)),
            memberName);

        // All atomics here are seq_cst over one shared heap, so the process-wide
        // barrier and the ordinary memory barrier are the same primitive.
        Assert.Equal("il2cpp::runtime::icall_thread_memory_barrier", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //==========================================================================
    // Helpers -- synthetic PE construction (the Interlocked/RuntimeHelpers
    // pipeline).
    //==========================================================================
    private static ResolvedMethod ResolveMember(string ownerNamespace, string ownerType, MemoryStream pe,
        string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Signature blob: header 0x00 (DEFAULT, non-generic), MANAGED param count,
    // return byte, then the raw parameter bytes. count is explicit because
    // RawSignature's ReadParamByte consumes a BYREF/PTR element byte, so the
    // byte count is not the parameter count.
    private static byte[] Sig(int count, byte ret, params byte[] paramBytes)
    {
        var bytes = new byte[3 + paramBytes.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)count;
        bytes[2] = ret;
        Array.Copy(paramBytes, 0, bytes, 3, paramBytes.Length);
        return bytes;
    }

    private static MemoryStream Synthesize(string ownerNamespace, string ownerType, string memberName,
        byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ownerNamespace),
            mdb.GetOrAddString(ownerType));
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
