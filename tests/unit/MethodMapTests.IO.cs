using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.IO dispatch tests -- Path.Combine arity dispatch (work order
// #24): the runtime icall takes exactly (string, string)
// (il2cpp_io.h has no multi-arg combine), so:
//   * 2-arg Combine maps to icall_path_combine (the only wired form)
//   * 3-arg Combine (the params-array form the compiler emits inline --
//     e.g. Path.Combine(a, b, c)) rejects LOUDLY with a precise message
//     naming the planned nested-folding fix -- it must NOT fall back to
//     the generated-name path (that would surface as a confusing C++
//     link error, the same reasoning as StreamIO.ReadByte)
//   * 1-arg and 4+-arg Combine reject LOUDLY with the generic arity
//     message
//   * the other Path members keep their existing mappings (dispatch
//     shape proof)
//
// The PEs are synthesized with System.Reflection.Metadata's own writer
// (same pipeline as MethodMapTests.StreamIO); string params/returns are
// 0x0E, void 0x01.
public class MethodMapIOTests
{
    [Fact]
    public void Resolve_Path_Combine_2Args_MapsToCombineIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Path", "Combine", Sig(0x0E, 0x0E, 0x0E)), "Combine");

        Assert.Equal("il2cpp::runtime::icall_path_combine", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.False(resolved.IsInstance);
    }

    [Fact]
    public void Resolve_Path_Combine_3Args_MapsToNestedCombineMarker()
    {
        // Path.Combine(dir, "nested", "deep") -- the params-array form the
        // iocase reference case exercises at line 69. Work order #24: the
        // resolver cannot express two calls in one ResolvedMethod, so the
        // 3-arg form emits the NestedCombineMarker and StackFold expands it
        // into nested icall_path_combine(combine(a,b),c) calls at fold time.
        var resolved = ResolveMember(Synthesize("System.IO", "Path", "Combine", Sig(0x0E, 0x0E, 0x0E, 0x0E)), "Combine");

        Assert.Equal(ResolvedMethod.NestedCombineMarker, resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(3, resolved.ParamCount);
        Assert.False(resolved.IsInstance);
    }

    [Fact]
    public void Resolve_Path_Combine_1Arg_ThrowsLoud()
    {
        // .NET has Path.Combine(string) (identity), but the runtime icall
        // takes exactly (string, string) -- loud, not a silent drop.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "Path", "Combine", Sig(0x0E, 0x0E)), "Combine"));

        Assert.Contains("Path.Combine with 1 arg(s)", ex.Message);
    }

    [Fact]
    public void Resolve_Path_Combine_4Args_ThrowsLoud()
    {
        // 4-arg is the params-array form too (net48 has up to 4 explicit
        // overloads); only 2-arg and 3-arg are wired -- loud, not silent.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "Path", "Combine",
                Sig(0x0E, 0x0E, 0x0E, 0x0E, 0x0E)), "Combine"));

        Assert.Contains("Path.Combine with 4 arg(s)", ex.Message);
    }

    [Fact]
    public void Resolve_Path_GetFileName_MapsToGetFileNameIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Path", "GetFileName", Sig(0x0E, 0x0E)), "GetFileName");

        Assert.Equal("il2cpp::runtime::icall_path_get_file_name", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Path_GetExtension_MapsToGetExtensionIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Path", "GetExtension", Sig(0x0E, 0x0E)), "GetExtension");

        Assert.Equal("il2cpp::runtime::icall_path_get_extension", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_FileInfo_Ctor_ThrowsLoud()
    {
        // FileInfo ctor is only wired for exactly (string); other shapes stay
        // loud (the runtime object icall takes the path GC-ref slot).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "FileInfo", ".ctor", Sig(0x01, 0x0E, 0x08)), ".ctor"));

        Assert.Contains("FileInfo ctor with 2 param(s)", ex.Message);
    }

    //--------------------------------------------------------------------------
    // M7 iocase (2026-08-08/09, rtdel-b23): Directory.Delete(path, bool
    // recursive) maps to the recursive icall icall_directory_delete_recursive
    // (il2cpp_io.h) -- "s" casts the path to Il2CppString*, the bool rides
    // the "" int32 carrier (icall_file_copy_overwrite convention). The 1-arg
    // Delete(path) form maps to the non-recursive icall.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Directory_DeleteRecursive_MapsToRecursiveIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Directory", "Delete", Sig(0x01, 0x0E, 0x02)), "Delete");

        Assert.Equal("il2cpp::runtime::icall_directory_delete_recursive", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Directory_DeleteSingleArg_MapsToDeleteIcall()
    {
        // The 1-arg Delete(path) form is wired: maps to the non-recursive
        // icall (a missing path is a status error -- M3 convention).
        var resolved = ResolveMember(Synthesize("System.IO", "Directory", "Delete", Sig(0x01, 0x0E)), "Delete");

        Assert.Equal("il2cpp::runtime::icall_directory_delete", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // M7 io3 (2026-08-08): the File full-API gap closures -- Copy/Move/
    // GetAttributes/SetAttributes/GetLastWriteTime map to the io.cpp icalls
    // (0 success / non-zero failure status; the .NET void status ints are
    // dropped). GetLastWriteTime returns a DateTime -> "dt" result kind.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_File_Copy2Arg_MapsToCopyIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "File", "Copy", Sig(0x01, 0x0E, 0x0E)), "Copy");

        Assert.Equal("il2cpp::runtime::icall_file_copy", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_File_Copy3Arg_MapsToCopyOverwriteIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "File", "Copy", Sig(0x01, 0x0E, 0x0E, 0x02)), "Copy");

        Assert.Equal("il2cpp::runtime::icall_file_copy_overwrite", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(3, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_File_Move_MapsToMoveIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "File", "Move", Sig(0x01, 0x0E, 0x0E)), "Move");

        Assert.Equal("il2cpp::runtime::icall_file_move", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_File_GetAttributes_MapsToGetAttributesIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "File", "GetAttributes", Sig(0x08, 0x0E)), "GetAttributes");

        Assert.Equal("il2cpp::runtime::icall_file_get_attributes", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_File_SetAttributes_MapsToSetAttributesIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "File", "SetAttributes", Sig(0x01, 0x0E, 0x08)),
            "SetAttributes");

        Assert.Equal("il2cpp::runtime::icall_file_set_attributes", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_File_GetLastWriteTime_MapsToLastWriteTimeIcall()
    {
        // Return byte 0x0A (int64 carrier) -- a VALUETYPE return would need a
        // trailing TypeHandle in the raw blob and misparse the 1-param shape;
        // the mapping sets the "dt" ResultKind itself (same convention as the
        // TimeZone DateTime converters).
        var resolved = ResolveMember(Synthesize("System.IO", "File", "GetLastWriteTime", Sig(0x0A, 0x0E)),
            "GetLastWriteTime");

        Assert.Equal("il2cpp::runtime::icall_file_get_last_write_time", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("dt", resolved.ResultKind);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.StreamIO)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
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
