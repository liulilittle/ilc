using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// a5reds: the raudit4 + strsort_ordinal closure tests.
//
//   * System.Array::get_Length receiver cast ("a"): a System.Array-typed
//     receiver arrives as Il2CppObject* (the `(Array)...` path); the inline
//     header helper takes Il2CppArray* -- without the cast the emitted
//     argument is a raw Il2CppObject* (raudit4 C++ error).
//   * System.StringComparer::get_Ordinal / get_OrdinalIgnoreCase: the
//     runtime singleton comparers + tracked pseudo-types.
//   * The FULL transpile of the two cases: the generic Array.Sort<T> with a
//     StringComparer argument must route to the comparer sort (NOT the
//     Comparison<T> delegate sort), no phantom generated-name symbols; the
//     raudit4 enum registration must carry the real underlying TypeCode (11
//     for `enum LE : long` -- the MetadataLoader value__ name fix) and the
//     GetValues .Length read must cast the receiver.
public class MethodMapA5RedsTests
{
    //--------------------------------------------------------------------------
    // get_Length -- the receiver cast is part of the mapping contract.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ArrayGetLength_CastsReceiverToArray()
    {
        var m = ResolveMember(Synthesize("System", "Array", "get_Length", Sig(0x08)), "get_Length");

        Assert.Equal("il2cpp::il2cpp_array_get_length", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.Equal(new[] { "a" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // StringComparer static getters -> the runtime singletons.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_StringComparer_GetOrdinal_MapsToSingleton()
    {
        var m = ResolveMember(Synthesize("System", "StringComparer", "get_Ordinal", Sig(0x1C)), "get_Ordinal");

        Assert.Equal("il2cpp::il2cpp_string_comparer_ordinal", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal("System.StringComparer.Ordinal", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_StringComparer_GetOrdinalIgnoreCase_MapsToSingleton()
    {
        var m = ResolveMember(Synthesize("System", "StringComparer", "get_OrdinalIgnoreCase", Sig(0x1C)), "get_OrdinalIgnoreCase");

        Assert.Equal("il2cpp::il2cpp_string_comparer_ordinal_ignore_case", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal("System.StringComparer.OrdinalIgnoreCase", m.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // strsort_ordinal full transpile: both Sort calls route to the comparer
    // sort; no phantom generated-name symbol remains.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_StrSortOrdinal_RoutesStringComparerToComparerSort()
    {
        var exe = CaseExePath("strsort_ordinal");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Prog.cpp"));
            Assert.Contains("il2cpp::il2cpp_string_comparer_ordinal()", cpp);
            Assert.Contains("il2cpp::il2cpp_string_comparer_ordinal_ignore_case()", cpp);
            Assert.Contains("il2cpp::il2cpp_array_sort_icomparer", cpp);
            // the delegate sort must NOT receive the comparer
            Assert.DoesNotContain("il2cpp_array_sort_ref_comparison", cpp);
            // no phantom generated-name symbol (the pre-fix red)
            Assert.DoesNotContain("System_StringComparer_get_", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // raudit4 full transpile: the enum registration carries the real
    // underlying TypeCode (11 = Int64) and the GetValues .Length read casts
    // the receiver to Il2CppArray*.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_Raudit4_EnumUnderlyingAndLengthCast()
    {
        var exe = CaseExePath("raudit4");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var entry = File.ReadAllText(Path.Combine(dir, "src", "entry.cpp"));
            Assert.Contains("s_Cases_LE_enumInfo = { 11, 2,", entry);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Raudit4.cpp"));
            Assert.Contains("il2cpp_array_get_length(reinterpret_cast<Il2CppArray*>(", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (the MethodMapTests.Array pattern)
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

    private static string CaseExePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "cases", name, "obj", "Release", "net48", name + ".exe");
            if (File.Exists(candidate)) return candidate;
            var outCandidate = Path.Combine(dir.FullName, "tests", "cases", name, "out", name + ".exe");
            if (File.Exists(outCandidate)) return outCandidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"case {name}.exe not found; build tests/cases/{name} first");
    }
}
