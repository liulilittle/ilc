using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Xunit;

// B0: "NOT TRANSPILED" stubs must be mechanically detectable.
//
// A method whose body the fold rejects is emitted as a runtime stub
// (il2cpp_throw_new(NotSupportedException)) and the transpile still exits 0.
// These tests run the REAL CLI entry against a SYNTHESIZED assembly (no case
// fixture dependency) that carries one fold-rejected method:
//
//   Cases.Stub::Unsupported  -- body `arglist; ret`; StackFold rejects
//                               `arglist` (varargs have no transpiler model),
//                               so the method becomes a NOT TRANSPILED stub.
//   Cases.Stub::Main         -- calls Unsupported (entry point for the
//                               generated executable).
//
// Contracts under test:
//   1. --stub-report <path>  writes one `Type::Method` line per stub, even
//                            when the run exits 0 (the default).
//   2. --strict-stubs        opts CI into exit 3 and lists every stub on
//                            stderr; the default stays exit 0.
//   3. the stub body prints "il2cpp: not transpiled: T::M" to stderr before
//      throwing, and the transpile emits a stderr summary.
public class StubReportTests
{
    [Fact]
    public void Default_Stub_KeepsExitZero_AndWritesStubReport()
    {
        var run = Run(withStub: true);

        Assert.Equal(0, run.Code);
        Assert.Contains("Cases.Stub::Unsupported", ReportLines(run.Report));
        Assert.Contains("warning: 1 method(s) were not transpiled", run.Log);
    }

    [Fact]
    public void StrictStubs_WithStub_ExitsThree_AndListsStubOnStderr()
    {
        var run = Run(withStub: true, "--strict-stubs");

        Assert.Equal(3, run.Code);
        Assert.Contains("Cases.Stub::Unsupported", run.Log);
        Assert.Contains("not transpiled: Cases.Stub::Unsupported", run.Log);
        // The report is written even on the strict failure path.
        Assert.Contains("Cases.Stub::Unsupported", ReportLines(run.Report));
    }

    [Fact]
    public void StrictStubs_WithoutStub_StaysExitZero_AndPrintsNoSummary()
    {
        var run = Run(withStub: false, "--strict-stubs");

        Assert.Equal(0, run.Code);
        Assert.DoesNotContain("not transpiled", run.Log);
        Assert.DoesNotContain("were not transpiled", run.Log);
        Assert.DoesNotContain("Cases.Stub::Unsupported", ReportLines(run.Report));
    }

    [Fact]
    public void StubBody_PrintsDiagnosticBeforeThrow()
    {
        var run = Run(withStub: true);

        var cpp = File.ReadAllText(Path.Combine(run.GenDir, "src", "Cases_Stub.cpp"));
        var diagnostic = "std::fprintf(stderr, \"il2cpp: not transpiled: Cases.Stub::Unsupported\\n\");";
        Assert.Contains(diagnostic, cpp);
        Assert.Contains("il2cpp_throw_new(", cpp);
        Assert.True(cpp.IndexOf(diagnostic, StringComparison.Ordinal)
                    < cpp.IndexOf("il2cpp_throw_new(", StringComparison.Ordinal),
            "the stderr diagnostic must precede the managed throw");
        Assert.Contains("#include <cstdio>", cpp);
    }

    [Fact]
    public void StubReport_MissingValue_IsParseError()
    {
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "--stub-report" }));
        Assert.Contains("--stub-report requires a file path", ex.Message);
    }

    private static string[] ReportLines(string report) =>
        report.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    private sealed record RunResult(int Code, string Report, string Log, string GenDir, string Root);

    private static RunResult Run(bool withStub, params string[] extra)
    {
        var root = Path.Combine(Path.GetTempPath(), "il2cpp_stub_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dll = Path.Combine(root, "stubcase.dll");
        File.WriteAllBytes(dll, Synthesize(withStub).ToArray());
        var gen = Path.Combine(root, "gen");
        var report = Path.Combine(root, "stubs.txt");
        var log = Path.Combine(root, "run.log");
        var args = new List<string>(extra);
        if (!args.Contains("--stub-report"))
        {
            args.Add("--stub-report");
            args.Add(report);
        }
        args.Add("--log");
        args.Add(log);
        args.Add(dll);
        args.Add(gen);
        try
        {
            var code = Program.RunMain(args.ToArray());
            return new RunResult(code,
                File.Exists(report) ? File.ReadAllText(report) : "",
                File.Exists(log) ? File.ReadAllText(log) : "",
                gen, root);
        }
        finally
        {
            // The caller may still need the generated sources; cleanup is done
            // by the caller through Root. Tests that only inspect strings leave
            // the temp dir behind like the other CLI tests do on success.
        }
    }

    //--------------------------------------------------------------------------
    // Synthesize a minimal PE (same MetadataBuilder/ManagedPEBuilder pattern as
    // ProgramNestedWiringTests) with ONE fold-rejected method:
    //   Cases.Stub::Unsupported  body `arglist; ret` (withStub) or `ret`
    //   Cases.Stub::Main         body `call Unsupported; ret`
    //--------------------------------------------------------------------------
    private static MemoryStream Synthesize(bool withStub)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("stubcase.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var voidSig = mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }); // static, 0 params, void

        var ilStream = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(ilStream);
        var unsupportedBody = AddBody(bodyEncoder, e =>
        {
            if (withStub) e.OpCode(ILOpCode.Arglist); // StackFold LOUD: varargs unsupported
            e.OpCode(ILOpCode.Ret);
        });
        var mainBody = AddBody(bodyEncoder, e =>
        {
            e.Call(MetadataTokens.MethodDefinitionHandle(1)); // Unsupported (row 1)
            e.OpCode(ILOpCode.Ret);
        });

        var unsupported = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Unsupported"), voidSig, unsupportedBody,
            default(ParameterHandle));
        mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"), voidSig, mainBody,
            default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Stub"), objRef,
            MetadataTokens.FieldDefinitionHandle(1), unsupported);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, ilStream, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }

    private static int AddBody(MethodBodyStreamEncoder encoder, Action<InstructionEncoder> build)
    {
        var code = new BlobBuilder();
        var instr = new InstructionEncoder(code);
        build(instr);
        return encoder.AddMethodBody(instr, maxStack: 8);
    }
}
