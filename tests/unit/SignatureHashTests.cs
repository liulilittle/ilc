using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Model;
using Xunit;

public class SignatureHashTests
{
    // The two Add overloads encode different parameter-type strings (int32→i,
    // comma-separated) → the hash input (including param types) differs
    [Fact]
    public void MethodSignatureCoder_Overloads_Differ()
    {
        using var pe = new PEReader(File.OpenRead(FixturePath("overloads")));
        var md = pe.GetMetadataReader();
        var encodings = md.MethodDefinitions
            .Select(h => md.GetMethodDefinition(h))
            .Where(m => md.GetString(m.Name) == "Add")
            .Select(m => MethodSignatureCoder.Decode(md, m.Signature).ParamTypes)
            .ToList();
        Assert.Equal(2, encodings.Count);
        Assert.Equal("i,i", encodings[0]);
        Assert.Equal("i,i,i", encodings[1]);
    }

    // Resolve both overload MethodDefs → hash includes the param signature →
    // CppName differs (overloads do not collide)
    [Fact]
    public void Resolve_Overloads_ProduceDistinctCppNames()
    {
        using var pe = new PEReader(File.OpenRead(FixturePath("overloads")));
        var md = pe.GetMetadataReader();
        var names = new List<string>();
        foreach (var h in md.MethodDefinitions)
        {
            var m = md.GetMethodDefinition(h);
            if (md.GetString(m.Name) != "Add") continue;
            names.Add(MethodRefResolver.Resolve(md, MetadataTokens.GetToken(h), Array.Empty<TypeDef>()).CppName);
        }
        Assert.Equal(2, names.Count);
        Assert.NotEqual(names[0], names[1]);
    }

    // Method bodies match overloads by signature (param count + encoded param
    // types): each resolves to the correct ArgCount
    [Fact]
    public void MethodBodyReader_MatchesOverloadBySignature()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("overloads"));
        using var pe = new PEReader(File.OpenRead(FixturePath("overloads")));
        var md = pe.GetMetadataReader();
        var program = types.First(t => t.Name == "Program");
        var add2 = program.Methods.First(m => m.Name == "Add" && m.ParamCount == 2);
        var add3 = program.Methods.First(m => m.Name == "Add" && m.ParamCount == 3);
        var body2 = MethodBodyReader.Read(pe, md, program, add2);
        var body3 = MethodBodyReader.Read(pe, md, program, add3);
        Assert.Equal(2, body2.ArgCount);
        Assert.Equal(3, body3.ArgCount);
    }

    // multidim-probe D wall (2026-08-06): int[,] param signature byte 0x14
    // (ELEMENT_TYPE_ARRAY) — consumes element type + rank + sizes/lobounds counts,
    // encoded as r:i[2] (distinct from the 1D r:i[] form); the transpile must no
    // longer reject it in MethodSignatureCoder (the StackFold newobj wall B is
    // independent of signature decoding).
    [Fact]
    public void MethodSignatureCoder_MultidimArrayParam_Decodes()
    {
        using var pe = new PEReader(File.OpenRead(FixturePath("multidim")));
        var md = pe.GetMetadataReader();
        var probe = md.MethodDefinitions
            .Select(h => md.GetMethodDefinition(h))
            .First(m => md.GetString(m.Name) == "Probe");
        var (paramCount, _, paramTypes, _) = MethodSignatureCoder.Decode(md, probe.Signature);
        Assert.Equal(1, paramCount);
        Assert.Equal("r:i[2]", paramTypes);
    }

    // 1D control (probe-A domain regression guard): SZARRAY 0x1D encodes r:i[]
    // and must not be broken by the 0x14 branch
    [Fact]
    public void MethodSignatureCoder_SzArrayControl_StillDecodes()
    {
        using var pe = new PEReader(File.OpenRead(FixturePath("multidim")));
        var md = pe.GetMetadataReader();
        var sum = md.MethodDefinitions
            .Select(h => md.GetMethodDefinition(h))
            .First(m => md.GetString(m.Name) == "Sum");
        var (paramCount, _, paramTypes, _) = MethodSignatureCoder.Decode(md, sum.Signature);
        Assert.Equal(1, paramCount);
        Assert.Equal("r:i[]", paramTypes);
    }

    // The return-type path also carries 0x14 (int[,] return): the return block must
    // consume element+rank+bounds, otherwise the param loop misaligns (Make(string)
    // would decode "i" instead of "r:System.String")
    [Fact]
    public void MethodSignatureCoder_MultidimArrayReturn_ConsumesBytes()
    {
        using var pe = new PEReader(File.OpenRead(FixturePath("multidim")));
        var md = pe.GetMetadataReader();
        var make = md.MethodDefinitions
            .Select(h => md.GetMethodDefinition(h))
            .First(m => md.GetString(m.Name) == "Make");
        var (paramCount, returnType, paramTypes, _) = MethodSignatureCoder.Decode(md, make.Signature);
        Assert.Equal(1, paramCount);
        Assert.Equal(SignatureTypeCode.Array, returnType);
        Assert.Equal("r:System.String", paramTypes);
    }

    // Method-name hash input format: {owner}.{name}({paramTypes}) — param types
    // are part of the hash input
    [Fact]
    public void SigHashInput_IncludesParamTypes()
    {
        Assert.Equal(SigHash.Hash("Calc.Program.Add(i,i)"), SigHash.Hash("Calc.Program.Add(i,i)"));
        Assert.NotEqual(SigHash.Hash("Calc.Program.Add(i,i)"), SigHash.Hash("Calc.Program.Add(i,i,i)"));
    }

    // The test host's working directory is the bin output directory, so relative
    // paths cannot resolve fixtures directly.
    private static string FixturePath(string name = "calc")
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "out", name + ".dll");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"fixture {name}.dll not found; run scripts/build-fixtures.ps1 first (expects repo root/tests/fixtures/out/{name}.dll)");
    }
}
