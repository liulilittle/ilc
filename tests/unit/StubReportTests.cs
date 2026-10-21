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
// fixture dependency):
//
//   Cases.Stub::Unsupported  -- body `arglist; ret`; StackFold rejects
//                               `arglist` (varargs have no transpiler model),
//                               so the method becomes a NOT TRANSPILED stub.
//   Cases.Stub::Main         -- calls Unsupported (entry point).
//   Cases.Ov::U(int) / U(int,int) -- two fold-rejected OVERLOADS sharing the
//                               managed name: they must produce TWO report
//                               lines (deduped by metadata token, not text).
//
// Contracts under test:
//   1. --stub-report <path>  writes one identity line per stub, even when the
//                            run exits 0 (the default), and overwrites any
//                            stale content from a previous run.
//   2. --strict-stubs        opts CI into exit 3 and lists every stub on
//                            stderr; the default stays exit 0.
//   3. the stub body prints "il2cpp: not transpiled: T::M" to stderr before
//      throwing, and the transpile emits a stderr summary.
//   4. a report that cannot be written (or a failed transpile) never leaves a
//      success-shaped report behind: the run exits non-zero / writes
//      "status=error", so the acceptance gate (RC==0 && file exists && zero
//      non-empty lines) cannot be fooled by stale evidence.
// Serialized with the rest of the suite: these tests pass --log, which swaps
// the process-wide Console.Out/Error (TeeTextWriter). Running in parallel with
// CliTests.Log_WritesFile tears down the other test's writer mid-run.
[Collection("TypeEmitterSerial")]
public class StubReportTests
{
    [Fact]
    public void Default_Stub_KeepsExitZero_AndWritesStubReport()
    {
        var run = Run(Shape.SingleStub);

        Assert.Equal(0, run.Code);
        Assert.Contains(run.ReportLines, line => line.StartsWith("Cases.Stub::Unsupported/0#0x", StringComparison.Ordinal));
        Assert.Contains("warning: 1 method(s) were not transpiled", run.Log);
    }

    [Fact]
    public void StrictStubs_WithStub_ExitsThree_AndListsStubOnStderr()
    {
        var run = Run(Shape.SingleStub, new[] { "--strict-stubs" });

        Assert.Equal(3, run.Code);
        Assert.Contains("not transpiled: Cases.Stub::Unsupported/0#0x", run.Log);
        // The report is written even on the strict failure path.
        Assert.Contains(run.ReportLines, line => line.StartsWith("Cases.Stub::Unsupported/0#0x", StringComparison.Ordinal));
    }

    [Fact]
    public void StrictStubs_WithoutStub_StaysExitZero_AndPrintsNoSummary()
    {
        var run = Run(Shape.NoStub, new[] { "--strict-stubs" });

        Assert.Equal(0, run.Code);
        Assert.DoesNotContain("not transpiled", run.Log);
        Assert.DoesNotContain("were not transpiled", run.Log);
        Assert.Empty(run.ReportLines.Where(line => line.Length > 0));
    }

    [Fact]
    public void StubBody_PrintsDiagnosticBeforeThrow()
    {
        var run = Run(Shape.SingleStub);

        var cpp = run.Cpp;
        var diagnostic = "std::fprintf(stderr, \"il2cpp: not transpiled: Cases.Stub::Unsupported\\n\");";
        Assert.Contains(diagnostic, cpp);
        Assert.Contains("il2cpp_throw_new(", cpp);
        Assert.True(cpp.IndexOf(diagnostic, StringComparison.Ordinal)
                    < cpp.IndexOf("il2cpp_throw_new(", StringComparison.Ordinal),
            "the stderr diagnostic must precede the managed throw");
        Assert.Contains("#include <cstdio>", cpp);
    }

    [Fact]
    public void Overloads_ProduceTwoReportLines_AndSummaryCountsBoth()
    {
        var run = Run(Shape.Overloads);

        var u = run.ReportLines.Where(line => line.StartsWith("Cases.Ov::U/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, u.Length);
        Assert.Contains(u, line => line.StartsWith("Cases.Ov::U/1#0x", StringComparison.Ordinal));
        Assert.Contains(u, line => line.StartsWith("Cases.Ov::U/2#0x", StringComparison.Ordinal));
        Assert.Equal(2, u.Select(line => line[line.IndexOf('#')..]).Distinct().Count());
        Assert.Contains("warning: 2 method(s) were not transpiled", run.Log);
    }

    [Fact]
    public void StaleReport_IsOverwritten_NotAppended()
    {
        const string stale = "Cases.Stale::Old/0#0xDEADBEEF";
        var run = Run(Shape.SingleStub, staleReport: stale);

        Assert.Equal(0, run.Code);
        Assert.DoesNotContain(stale, run.ReportLines);
        Assert.Single(run.ReportLines.Where(line => line.Length > 0));
    }

    [Fact]
    public void UnwritableReportPath_WithStrict_ExitsNonZero()
    {
        var run = Run(Shape.SingleStub, new[] { "--strict-stubs" }, reportIsDirectory: true);

        Assert.NotEqual(0, run.Code);
        Assert.Contains("cannot write stub report", run.Log);
    }

    [Fact]
    public void FailedTranspile_WritesErrorMarker_NotAnEmptyReport()
    {
        var run = Run(Shape.SingleStub, missingAssembly: true);

        Assert.Equal(2, run.Code);
        Assert.Contains("status=error", run.ReportLines);
    }

    [Fact]
    public void StubReport_MissingValue_IsParseError()
    {
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "--stub-report" }));
        Assert.Contains("--stub-report requires a file path", ex.Message);
    }

    private sealed record RunResult(int Code, string[] ReportLines, string Log, string Cpp);

    private static RunResult Run(Shape shape, string[]? extra = null, string? staleReport = null,
        bool reportIsDirectory = false, bool missingAssembly = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "il2cpp_stub_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dll = Path.Combine(root, "stubcase.dll");
        if (!missingAssembly)
            File.WriteAllBytes(dll, Synthesize(shape).ToArray());
        var gen = Path.Combine(root, "gen");
        var report = reportIsDirectory ? Path.Combine(root, "report-dir") : Path.Combine(root, "stubs.txt");
        if (reportIsDirectory) Directory.CreateDirectory(report);
        else if (staleReport != null) File.WriteAllText(report, staleReport + "\n");
        var log = Path.Combine(root, "run.log");
        var args = new List<string>(extra ?? Array.Empty<string>());
        args.Add("--stub-report");
        args.Add(report);
        args.Add("--log");
        args.Add(log);
        args.Add(dll);
        args.Add(gen);
        var code = Program.RunMain(args.ToArray());
        var reportText = File.Exists(report) ? File.ReadAllText(report) : "";
        var logText = File.Exists(log) ? File.ReadAllText(log) : "";
        // Read the generated TU before deleting the tree (the stub body test
        // asserts on its contents).
        var cppPath = Path.Combine(gen, "src", shape == Shape.Overloads ? "Cases_Ov.cpp" : "Cases_Stub.cpp");
        var cpp = File.Exists(cppPath) ? File.ReadAllText(cppPath) : "";
        try { Directory.Delete(root, true); } catch { /* best effort */ }
        return new RunResult(code, SplitLines(reportText), logText, cpp);
    }

    private static string[] SplitLines(string text) =>
        text.Length == 0 ? Array.Empty<string>() : text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    private enum Shape
    {
        NoStub,
        SingleStub,
        Overloads,
    }

    //--------------------------------------------------------------------------
    // Synthesize a minimal PE (same MetadataBuilder/ManagedPEBuilder pattern as
    // ProgramNestedWiringTests).
    //   SingleStub: Cases.Stub::Unsupported(arglist) + Cases.Stub::Main
    //   NoStub:     the same shape with a plain `ret` body
    //   Overloads:  Cases.Ov::U(int), Cases.Ov::U(int,int) both `arglist`
    //--------------------------------------------------------------------------
    private static MemoryStream Synthesize(Shape shape)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("stubcase.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var voidSig = mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 });                // static, 0 params, void
        var oneIntSig = mdb.GetOrAddBlob(new byte[] { 0x00, 0x01, 0x01, 0x08 });        // static, (int32), void
        var twoIntSig = mdb.GetOrAddBlob(new byte[] { 0x00, 0x02, 0x01, 0x08, 0x08 });  // static, (int32,int32), void

        var ilStream = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(ilStream);

        if (shape == Shape.Overloads)
        {
            var u1 = AddBody(bodyEncoder, ArglistBody);
            var u2 = AddBody(bodyEncoder, ArglistBody);
            var ovMain = AddBody(bodyEncoder, e =>
            {
                e.LoadConstantI4(1);
                e.Call(MetadataTokens.MethodDefinitionHandle(1));
                e.LoadConstantI4(1);
                e.LoadConstantI4(2);
                e.Call(MetadataTokens.MethodDefinitionHandle(2));
                e.OpCode(ILOpCode.Ret);
            });
            mdb.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                MethodImplAttributes.IL, mdb.GetOrAddString("U"), oneIntSig, u1, default(ParameterHandle));
            mdb.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                MethodImplAttributes.IL, mdb.GetOrAddString("U"), twoIntSig, u2, default(ParameterHandle));
            mdb.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                MethodImplAttributes.IL, mdb.GetOrAddString("Main"), voidSig, ovMain, default(ParameterHandle));
            mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
                mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Ov"), objRef,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            return Serialize(mdb, ilStream);
        }

        var body = AddBody(bodyEncoder, shape == Shape.SingleStub
            ? ArglistBody
            : (Action<InstructionEncoder>)(e => e.OpCode(ILOpCode.Ret)));
        var mainBody = AddBody(bodyEncoder, e =>
        {
            e.Call(MetadataTokens.MethodDefinitionHandle(1)); // Unsupported (row 1)
            e.OpCode(ILOpCode.Ret);
        });
        var unsupported = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Unsupported"), voidSig, body, default(ParameterHandle));
        mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"), voidSig, mainBody, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Stub"), objRef,
            MetadataTokens.FieldDefinitionHandle(1), unsupported);
        return Serialize(mdb, ilStream);
    }

    private static void ArglistBody(InstructionEncoder e)
    {
        e.OpCode(ILOpCode.Arglist); // StackFold LOUD: varargs unsupported
        e.OpCode(ILOpCode.Ret);
    }

    private static MemoryStream Serialize(MetadataBuilder mdb, BlobBuilder ilStream)
    {
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
