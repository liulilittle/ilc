using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Legacy dispatch tests (MethodMap.Legacy.cs -> TryMapLegacy,
// reached through the Remoting/AppDomain/ArgIterator/ReaderWriterLock case
// labels): the net48 audit §3/§6 LOUD-rejection families. Every member of
// these owners is rejected as a family -- without the cases they silently
// fell to the generated-name path (undefined C++ symbol at link).
//
// Same synthetic-PE pipeline as MethodMapTests.Interlocked.
public class MethodMapTestsLegacy
{
    //--------------------------------------------------------------------------
    // System.Runtime.Remoting.* -- the 6 public-face RemotingServices gaps
    // (net48 audit §3 row 979: "legacy technique, LOUD rejection recommended"). The family has no
    // runtime infrastructure (proxies/ObjRef/channels/contexts).
    //--------------------------------------------------------------------------
    // p0c D31-D35: the five public-face state accessors (GetProxiedType /
    // GetTransparentProxy / GetStubData / SetStubData / IsTransparentProxy /
    // GetRealProxy) and the RealProxy ctors are now WIRED; the rest of the
    // family still has no runtime infrastructure and stays LOUD.
    [Theory]
    [InlineData("System.Runtime.Remoting", "RemotingServices", "CreateTransparentProxy")]
    [InlineData("System.Runtime.Remoting", "RemotingServices", "AllocateUninitializedObject")]
    [InlineData("System.Runtime.Remoting", "RemotingServices", "AllocateInitializedObject")]
    [InlineData("System.Runtime.Remoting", "RemotingServices", "CallDefaultCtor")]
    [InlineData("System.Runtime.Remoting.Proxies", "RealProxy", "GetStub")]
    public void Resolve_RemotingFamily_ThrowsLoud(string ns, string type, string memberName)
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize(ns, type, memberName, Sig(0x1C, 0x1C)), memberName));

        Assert.Contains($"System.Runtime.Remoting", ex.Message);
    }

    //--------------------------------------------------------------------------
    // AppDomain -- adm1 (AppDomain minimal object closure): the public default-
    // domain surface now MAPS to the appdomain.cpp icalls (it was a family LOUD
    // rejection before). Every listed member resolves to its runtime icall --
    // including the six LOUD strains (ExecuteAssembly / PermissionSet / Evidence
    // / ApplicationTrust / GetAssemblies / ToString), which are mapped to icalls
    // that THROW NotSupportedException at RUNTIME (single-source difftest
    // mapping, appdomain.h). Members still outside the closure (e.g.
    // ShadowCopyFiles/CreateDomain) stay LOUD at the resolver.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("get_CurrentDomain", 0x1C, "il2cpp::runtime::icall_appdomain_get_current_domain")]
    [InlineData("get_Id", 0x08, "il2cpp::runtime::icall_appdomain_get_id_public")]
    [InlineData("get_FriendlyName", 0x0E, "il2cpp::runtime::icall_appdomain_get_friendly_name")]
    [InlineData("get_BaseDirectory", 0x0E, "il2cpp::runtime::icall_appdomain_get_base_directory")]
    [InlineData("get_RelativeSearchPath", 0x0E, "il2cpp::runtime::icall_appdomain_get_relative_search_path")]
    [InlineData("get_DomainManager", 0x1C, "il2cpp::runtime::icall_appdomain_get_domain_manager")]
    [InlineData("get_SetupInformation", 0x1C, "il2cpp::runtime::icall_appdomain_get_setup_information")]
    [InlineData("get_IsFullyTrusted", 0x02, "il2cpp::runtime::icall_appdomain_is_fully_trusted")]
    [InlineData("get_IsHomogenous", 0x02, "il2cpp::runtime::icall_appdomain_is_homogenous")]
    [InlineData("IsDefaultAppDomain", 0x02, "il2cpp::runtime::icall_appdomain_is_default_app_domain")]
    [InlineData("GetType", 0x1C, "il2cpp::runtime::icall_object_get_type")]
    public void Resolve_AppDomain_Getter_MapsToIcall(string member, int ret, string cpp)
    {
        var resolved = ResolveMember(Synthesize("System", "AppDomain", member, Sig((byte)ret)), member);
        Assert.Equal(cpp, resolved.CppName);
    }

    [Theory]
    [InlineData("GetData", 0x1C, 0x0E)]
    [InlineData("SetData", 0x00, 0x0E)]
    [InlineData("CreateInstance", 0x1C, 0x0E)]
    [InlineData("CreateInstanceAndUnwrap", 0x1C, 0x0E)]
    public void Resolve_AppDomain_Method_MapsToIcall(string member, int ret, int ps)
    {
        // GetData(string) / SetData(string, object) / CreateInstance[string,
        // string] -- the 2-parameter forms carry [0x0E, 0x1C]/[0x0E, 0x0E].
        var sig = member == "GetData"
            ? Sig((byte)ret, (byte)ps)
            : Sig((byte)ret, 0x0E, member == "SetData" ? (byte)0x1C : (byte)0x0E);
        var resolved = ResolveMember(Synthesize("System", "AppDomain", member, sig), member);
        Assert.StartsWith("il2cpp::runtime::icall_appdomain_", resolved.CppName);
    }

    [Theory]
    [InlineData("add_UnhandledException", 0x1C)]
    [InlineData("remove_UnhandledException", 0x1C)]
    public void Resolve_AppDomain_EventAccessor_MapsToIcall(string member, int paramType)
    {
        // The accessor mapping gates on count==1 only, so the synthetic PE uses
        // a simple 0x1C-object param (the real UnhandledExceptionEventHandler
        // 0x12-class element needs a type handle the synthetic builder lacks).
        var resolved = ResolveMember(Synthesize("System", "AppDomain", member, Sig(0x00, (byte)paramType)), member);
        Assert.StartsWith("il2cpp::runtime::icall_appdomain_", resolved.CppName);
    }

    // The six LOUD strains map to runtime-throwing icalls (not a resolver LOUD):
    // the single-source difftest PROBE must compile+link (the runtime then
    // throws NotSupportedException for each).
    [Theory]
    [InlineData("ExecuteAssembly", 0x08, 0x0E, "il2cpp::runtime::icall_appdomain_execute_assembly")]
    [InlineData("get_PermissionSet", 0x1C, -1, "il2cpp::runtime::icall_appdomain_get_permission_set")]
    [InlineData("get_Evidence", 0x1C, -1, "il2cpp::runtime::icall_appdomain_get_evidence")]
    [InlineData("get_ApplicationTrust", 0x1C, -1, "il2cpp::runtime::icall_appdomain_get_application_trust")]
    [InlineData("GetAssemblies", 0x1C, -1, "il2cpp::runtime::icall_appdomain_get_assemblies")]
    [InlineData("ToString", 0x0E, -1, "il2cpp::runtime::icall_appdomain_to_string")]
    public void Resolve_AppDomain_LoudStrain_MapsToRuntimeThrow(string member, int ret, int param, string cpp)
    {
        // The mapping gates on count (and for ExecuteAssembly the string
        // param), never on the return element: GetAssemblies' real Assembly[]
        // (0x1D) return cannot be encoded by the synthetic PE, so it uses a
        // plain object (0x1C) return here.
        var resolved = param < 0
            ? ResolveMember(Synthesize("System", "AppDomain", member, Sig((byte)ret)), member)
            : ResolveMember(Synthesize("System", "AppDomain", member, Sig((byte)ret, (byte)param)), member);
        Assert.Equal(cpp, resolved.CppName);
    }

    // Members still outside the adm1 closure stay a resolver LOUD rejection.
    [Fact]
    public void Resolve_AppDomain_UnclosedMember_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "AppDomain", "get_ShadowCopyFiles", Sig(0x02)), "get_ShadowCopyFiles"));

        Assert.Contains("System.AppDomain", ex.Message);
    }

    // The SetupInformation carrier (AppDomainSetup) and the CreateInstance
    // wrapper (ObjectHandle) map to their adm1 icalls.
    [Fact]
    public void Resolve_AppDomainSetup_MapsGetApplicationBase()
    {
        var resolved = ResolveMember(Synthesize("System", "AppDomainSetup", "get_ApplicationBase", Sig(0x0E)), "get_ApplicationBase");
        Assert.Equal("il2cpp::runtime::icall_appdomain_setup_get_application_base", resolved.CppName);
    }

    [Fact]
    public void Resolve_ObjectHandle_MapsUnwrap()
    {
        var resolved = ResolveMember(Synthesize("System.Runtime.Remoting", "ObjectHandle", "Unwrap", Sig(0x1C)), "Unwrap");
        Assert.Equal("il2cpp::runtime::icall_objecthandle_unwrap", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // p0c D31-D35: the wired remoting state accessors resolve to the
    // il2cpp_remoting.h icalls (remoting.cpp side table).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("RemotingServices", "IsTransparentProxy", 0x02, 0x1C,
        "il2cpp::runtime::icall_remoting_services_is_transparent_proxy")]
    [InlineData("RemotingServices", "GetRealProxy", 0x1C, 0x1C,
        "il2cpp::runtime::icall_remoting_services_get_real_proxy")]
    public void Resolve_RemotingServices_Accessor_MapsToIcall(string type, string member, int ret, int ps, string cpp)
    {
        var resolved = ResolveMember(
            Synthesize("System.Runtime.Remoting", type, member, Sig((byte)ret, (byte)ps)), member);
        Assert.Equal(cpp, resolved.CppName);
    }

    [Theory]
    [InlineData("GetProxiedType", 0x1C, "il2cpp::runtime::icall_realproxy_get_proxied_type")]
    [InlineData("GetTransparentProxy", 0x1C, "il2cpp::runtime::icall_realproxy_get_transparent_proxy")]
    [InlineData("GetStubData", 0x1C, "il2cpp::runtime::icall_realproxy_get_stub_data")]
    public void Resolve_RealProxy_Accessor_MapsToIcall(string member, int ret, string cpp)
    {
        // GetStubData is static in net48: (RealProxy) -> object. The synthetic
        // PE passes a single 0x1C parameter for it and none for the two
        // instance getters.
        var sig = member == "GetStubData" ? Sig((byte)ret, 0x1C) : Sig((byte)ret);
        var resolved = ResolveMember(
            Synthesize("System.Runtime.Remoting.Proxies", "RealProxy", member, sig), member);
        Assert.Equal(cpp, resolved.CppName);
    }

    [Fact]
    public void Resolve_RealProxy_SetStubData_MapsToIcall()
    {
        var resolved = ResolveMember(
            Synthesize("System.Runtime.Remoting.Proxies", "RealProxy", "SetStubData", Sig(0x00, 0x1C, 0x1C)),
            "SetStubData");
        Assert.Equal("il2cpp::runtime::icall_realproxy_set_stub_data", resolved.CppName);
    }

    [Fact]
    public void Resolve_RealProxy_Ctor_MapsToIcall()
    {
        // RealProxy(Type classToProxy) -- the ctor is MANAGED in net48 but its
        // body stores mscorlib-only fields, so the runtime side table replaces
        // it (otherwise the accessors would read uninitialized state).
        var resolved = ResolveMember(
            Synthesize("System.Runtime.Remoting.Proxies", "RealProxy", ".ctor", Sig(0x01, 0x1C)),
            ".ctor");
        Assert.Equal("il2cpp::runtime::icall_realproxy_ctor_type", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // p0c D11: ExtensibleClassFactory.RegisterObjectCreationCallback is wired;
    // its only reachable net48 outcomes are reproduced at runtime.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ExtensibleClassFactory_RegisterObjectCreationCallback_MapsToIcall()
    {
        var resolved = ResolveMember(
            Synthesize("System.Runtime.InteropServices", "ExtensibleClassFactory",
                "RegisterObjectCreationCallback", Sig(0x00, 0x1C)),
            "RegisterObjectCreationCallback");
        Assert.Equal("il2cpp::runtime::icall_extensible_class_factory_register", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // ArgIterator -- varargs (__arglist) consumption. The public surface is a
    // managed wrapper over internal icalls in net48; mscorlib is never
    // translated, so these public members are the resolution targets
    // (runtime model: il2cpp_argiterator.h).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("GetRemainingCount", 0x08, "il2cpp::runtime::icall_argiterator_get_remaining_count")]
    [InlineData("GetNextArgType", 0x06, "il2cpp::runtime::icall_argiterator_get_next_arg_type")]
    [InlineData("End", 0x01, "il2cpp::runtime::icall_argiterator_end")]
    [InlineData("GetHashCode", 0x08, "il2cpp::runtime::icall_argiterator_get_hash_code")]
    [InlineData("GetNextArg", 0x08, "il2cpp::runtime::icall_argiterator_get_next_arg")]
    public void Resolve_ArgIterator_Surface_MapsToIcalls(string member, int ret, string cpp)
    {
        var resolved = ResolveMember(
            Synthesize("System", "ArgIterator", member, Sig((byte)ret)),
            member);
        Assert.Equal(cpp, resolved.CppName);
    }

    [Fact]
    public void Resolve_ArgIterator_Ctor_MapsToIcall()
    {
        // ArgIterator(RuntimeArgumentHandle): the frame handle rides the
        // native-int carrier (ELEMENT_TYPE_I 0x18) -- the exact shape net48's
        // public ctor wrapper hands to the internal `.ctor(native int)` icall.
        var resolved = ResolveMember(
            Synthesize("System", "ArgIterator", ".ctor", Sig(0x01, 0x18)),
            ".ctor");
        Assert.Equal("il2cpp::runtime::icall_argiterator_ctor", resolved.CppName);
    }

    [Fact]
    public void Resolve_ArgIterator_UnknownMember_ThrowsLoud()
    {
        // Members outside the varargs-consumption surface must never fall to a
        // generated-name symbol.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "ArgIterator", "GetNextArgTypeHandle", Sig(0x08)), "GetNextArgTypeHandle"));

        Assert.Contains("System.ArgIterator", ex.Message);
    }

    //--------------------------------------------------------------------------
    // ReaderWriterLock -- p0c D37: the lock object model is wired; only the
    // LockCookie members stay LOUD (no System.Threading.LockCookie value-type
    // model in this transpiler).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("AcquireReaderLock", 0x01, "il2cpp::runtime::icall_rwl_acquire_reader")]
    [InlineData("AcquireWriterLock", 0x01, "il2cpp::runtime::icall_rwl_acquire_writer")]
    [InlineData("ReleaseReaderLock", 0x00, "il2cpp::runtime::icall_rwl_release_reader")]
    [InlineData("ReleaseWriterLock", 0x00, "il2cpp::runtime::icall_rwl_release_writer")]
    [InlineData("AnyWritersSince", 0x02, "il2cpp::runtime::icall_rwl_any_writers_since")]
    public void Resolve_ReaderWriterLock_MapsToIcall(string member, int ret, string cpp)
    {
        var sig = member is "ReleaseReaderLock" or "ReleaseWriterLock"
            ? Sig((byte)ret)
            : Sig((byte)ret, 0x08);
        var resolved = ResolveMember(Synthesize("System.Threading", "ReaderWriterLock", member, sig), member);
        Assert.Equal(cpp, resolved.CppName);
    }

    [Theory]
    [InlineData("get_IsReaderLockHeld", "il2cpp::runtime::icall_rwl_get_is_reader_lock_held")]
    [InlineData("get_IsWriterLockHeld", "il2cpp::runtime::icall_rwl_get_is_writer_lock_held")]
    [InlineData("get_WriterSeqNum", "il2cpp::runtime::icall_rwl_get_writer_seq_num")]
    public void Resolve_ReaderWriterLock_Getter_MapsToIcall(string member, string cpp)
    {
        var resolved = ResolveMember(Synthesize("System.Threading", "ReaderWriterLock", member, Sig(0x02)), member);
        Assert.Equal(cpp, resolved.CppName);
    }

    [Fact]
    public void Resolve_ReaderWriterLock_LockCookieMember_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            // The mapping gates on the NAME only, so the synthetic PE uses an
            // object return (a real LockCookie 0x11 value type would need a
            // TypeDefOrRef coded index the synthetic builder lacks).
            ResolveMember(Synthesize("System.Threading", "ReaderWriterLock", "UpgradeToWriterLock", Sig(0x1C, 0x08)),
                "UpgradeToWriterLock"));

        Assert.Contains("System.Threading.ReaderWriterLock", ex.Message);
        Assert.Contains("LockCookie", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (the Interlocked-test pipeline).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

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
