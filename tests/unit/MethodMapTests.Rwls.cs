using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Rwls dispatch tests (nb5 RWLS-LOCK): the
// System.Threading.ReaderWriterLockSlim arms in MethodMap.Rwls.cs route each
// member to the rwls.cpp icall:
//
//   ReaderWriterLockSlim()                  -> icall_rwls_ctor_default
//   ReaderWriterLockSlim(LockRecursionPolicy) -> icall_rwls_ctor_policy
//   Enter{Read,Write,UpgradeableRead}Lock    -> icall_rwls_enter_*
//   TryEnterWriteLock(int)                   -> icall_rwls_try_enter_write
//   IsReadLockHeld / RecursionPolicy         -> icall_rwls_get_*
//
// Signature blobs are the raw bytes RawSignature consumes (0x08=i4, 0x02=bool,
// 0x01=void); the LockRecursionPolicy enum is a VALUETYPE (0x11 + coded index).
public class MethodMapTestsRwls
{
    [Fact]
    public void Resolve_Rwls_DefaultCtor_MapsToDefaultIcall()
    {
        var resolved = ResolveMember("System.Threading", "ReaderWriterLockSlim", ".ctor",
            Sig(0x01), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_rwls_ctor_default", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Rwls_PolicyCtor_MapsToPolicyIcall()
    {
        // ReaderWriterLockSlim(LockRecursionPolicy): the enum is a valuetype.
        var resolved = ResolveMember("System.Threading", "ReaderWriterLockSlim", ".ctor",
            Sig(0x01, B(0x11, 0x05)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_rwls_ctor_policy", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Rwls_EnterReadLock_MapsToEnterReadIcall()
    {
        var resolved = ResolveMember("System.Threading", "ReaderWriterLockSlim", "EnterReadLock",
            Sig(0x01), "EnterReadLock");

        Assert.Equal("il2cpp::runtime::icall_rwls_enter_read", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_Rwls_TryEnterWriteLockInt_MapsToTryEnterWriteIcall()
    {
        var resolved = ResolveMember("System.Threading", "ReaderWriterLockSlim", "TryEnterWriteLock",
            Sig(0x02, B(0x08)), "TryEnterWriteLock");

        Assert.Equal("il2cpp::runtime::icall_rwls_try_enter_write", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Rwls_IsReadLockHeld_MapsToGetterIcall()
    {
        var resolved = ResolveMember("System.Threading", "ReaderWriterLockSlim", "get_IsReadLockHeld",
            Sig(0x02), "get_IsReadLockHeld");

        Assert.Equal("il2cpp::runtime::icall_rwls_get_is_read_lock_held", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
    }

    [Fact]
    public void Resolve_Rwls_RecursionPolicy_MapsToGetterIcall()
    {
        var resolved = ResolveMember("System.Threading", "ReaderWriterLockSlim", "get_RecursionPolicy",
            Sig(0x08), "get_RecursionPolicy");

        Assert.Equal("il2cpp::runtime::icall_rwls_get_recursion_policy", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.WaitHandle.cs)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(string ns, string typeName, string memberName, byte[] signature,
        string resolveName)
    {
        using var reader = new PEReader(Synthesize(ns, typeName, memberName, signature));
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == resolveName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, false, null, null);
    }

    private static byte[] B(params byte[] bytes) => bytes;

    private static byte[] Sig(byte ret, params byte[][] ps)
    {
        var bytes = new byte[3 + ps.Sum(p => p.Length)];
        bytes[0] = 0x00;
        bytes[1] = (byte)ps.Length;
        bytes[2] = ret;
        var at = 3;
        foreach (var p in ps)
        {
            Array.Copy(p, 0, bytes, at, p.Length);
            at += p.Length;
        }
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
