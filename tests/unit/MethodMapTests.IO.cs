using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.IO dispatch tests -- Path.Combine arity dispatch (work order
// #24 + nb2 IO-PATH2): the runtime icall takes exactly (string, string)
// (il2cpp_io.h has no multi-arg combine), so:
//   * 2-arg Combine maps to icall_path_combine
//   * 3-arg AND 4-arg Combine (the params-array forms the compiler emits
//     inline) emit the NestedCombineMarker and StackFold expands them into
//     nested icall_path_combine(combine(a,b),c) calls at fold time
//   * 1-arg Combine(string[]) -- the explicit params-array overload -- maps
//     to the runtime icall_path_combine_many (nb2)
//   * the other Path members keep their existing mappings (dispatch
//     shape proof), now including the nb2 IO-PATH2 extension arms
//     (GetTempPath/GetFullPath/HasExtension/GetDirectoryName/
//     GetFileNameWithoutExtension) and the DIR-FILES Directory enumeration
//     arms (GetFiles/GetDirectories/GetCurrentDirectory)
//
// The PEs are synthesized with System.Reflection.Metadata's own writer
// (same pipeline as MethodMapTests.StreamIO); string params/returns are
// 0x0E, void 0x01, bool 0x02, SZARRAY 0x1D + element byte, VALUETYPE 0x11 +
// coded index.
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
    public void Resolve_Path_Combine_4Args_MapsToNestedCombineMarker()
    {
        // nb2 IO-PATH2: the 4-arg form (previously LOUD) now emits the same
        // NestedCombineMarker; StackFold's left-fold expands any N-ary call
        // into nested icall_path_combine calls (p-nb2-iopath [io.cmb4]).
        var resolved = ResolveMember(Synthesize("System.IO", "Path", "Combine",
            Sig(0x0E, 0x0E, 0x0E, 0x0E, 0x0E)), "Combine");

        Assert.Equal(ResolvedMethod.NestedCombineMarker, resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(4, resolved.ParamCount);
        Assert.False(resolved.IsInstance);
    }

    [Fact]
    public void Resolve_Path_Combine_ParamsArray_MapsToCombineManyIcall()
    {
        // Combine(string[]) -- the explicit params-array overload the caller
        // reaches with `Combine(new[]{"a","b","c"})`: one SZARRAY-of-string
        // param, mapped to the runtime icall_path_combine_many.
        var resolved = ResolveMember(Synthesize("System.IO", "Path", "Combine",
            new byte[] { 0x00, 0x01, 0x0E, 0x1D, 0x0E }), "Combine");

        Assert.Equal("il2cpp::runtime::icall_path_combine_many", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Path_Combine_1Arg_ThrowsLoud()
    {
        // A plain 1-arg Combine(string) is not a .NET overload (the params
        // form is the SZARRAY case above); keep it loud, not a silent drop.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "Path", "Combine", Sig(0x0E, 0x0E)), "Combine"));

        Assert.Contains("Path.Combine with 1 arg(s)", ex.Message);
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

    //--------------------------------------------------------------------------
    // nb2 IO-PATH2 -- the pathproc.h extension arms (the runtime functions
    // existed; the MethodMap arms were missing -> phantom symbols, red-boxed
    // by p-nb2-iopath) plus the two NET-NEW pathproc extensions.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Path_GetTempPath_MapsToTempPathIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Path", "GetTempPath", Sig(0x0E)), "GetTempPath");

        Assert.Equal("il2cpp::runtime::il2cpp_path_get_temp_path", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Path_GetFullPath_MapsToFullPathIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Path", "GetFullPath", Sig(0x0E, 0x0E)), "GetFullPath");

        Assert.Equal("il2cpp::runtime::il2cpp_path_get_full_path", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Path_HasExtension_MapsToHasExtensionIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Path", "HasExtension", Sig(0x02, 0x0E)), "HasExtension");

        Assert.Equal("il2cpp::runtime::il2cpp_path_has_extension", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Path_GetDirectoryName_MapsToDirectoryNameIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Path", "GetDirectoryName", Sig(0x0E, 0x0E)),
            "GetDirectoryName");

        Assert.Equal("il2cpp::runtime::il2cpp_path_get_directory_name", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Path_GetFileNameWithoutExtension_MapsToIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Path", "GetFileNameWithoutExtension", Sig(0x0E, 0x0E)),
            "GetFileNameWithoutExtension");

        Assert.Equal("il2cpp::runtime::il2cpp_path_get_file_name_without_extension", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // nb2 DIR-FILES -- Directory.GetFiles / GetDirectories (string[] return =
    // SZARRAY 0x1D + string element 0x0E; SearchOption is a VALUETYPE 0x11 +
    // coded index) and GetCurrentDirectory (string, 0 params).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Directory_GetFiles_1Arg_MapsToFilesIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Directory", "GetFiles",
            new byte[] { 0x00, 0x01, 0x1D, 0x0E, 0x0E }), "GetFiles");

        Assert.Equal("il2cpp::runtime::icall_directory_get_files", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Directory_GetFiles_3Args_MapsToFilesIcallWithSearchOptionCast()
    {
        // GetFiles(path, pattern, SearchOption) -- the enum (VALUETYPE 0x11)
        // slot travels as the "i" int32 carrier.
        var resolved = ResolveMember(Synthesize("System.IO", "Directory", "GetFiles",
            new byte[] { 0x00, 0x03, 0x1D, 0x0E, 0x0E, 0x0E, 0x11, 0x05 }), "GetFiles");

        Assert.Equal("il2cpp::runtime::icall_directory_get_files", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Directory_GetDirectories_1Arg_MapsToDirectoriesIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Directory", "GetDirectories",
            new byte[] { 0x00, 0x01, 0x1D, 0x0E, 0x0E }), "GetDirectories");

        Assert.Equal("il2cpp::runtime::icall_directory_get_directories", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Directory_GetCurrentDirectory_MapsToEnvironmentIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "Directory", "GetCurrentDirectory", Sig(0x0E)),
            "GetCurrentDirectory");

        Assert.Equal("il2cpp::runtime::icall_environment_get_current_directory", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
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
