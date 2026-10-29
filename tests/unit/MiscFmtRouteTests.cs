using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Xunit;

public class MiscFmtRouteTests
{
    //--------------------------------------------------------------------------
    // impl-miscfmt (singlefmt residual): the two float NO-FORMAT surfaces.
    //
    //   1. Single.ToString() (no arguments) -> icall_single_to_string (the G7
    //      float text; -0.0f must render "0").
    //   2. Console.WriteLine(float) / Console.Write(float) and the console
    //      TextWriter surface Console.Out.Write*(float) must route to the
    //      FLOAT icalls (net48 float G7 text), never the double G15 icalls
    //      (the "documented deviation" the probe pins).
    //
    // Fixture: the real net48 csc-built probe-miscfmt case exe (the
    // CaseExePath convention; scripts/build-unit-fixtures.ps1 discovers it).
    //--------------------------------------------------------------------------
    [Fact]
    public void Emit_MiscFmt_ProbeCaseExe_FixtureBuilds()
    {
        var exe = CaseExePath("probe-miscfmt");
        Assert.True(File.Exists(exe));
        var types = MetadataLoader.LoadAssembly(exe);
        Assert.Contains(types, t => t.Name == "ProbeMiscFmt");
    }

    [Fact]
    public void Emit_MiscFmt_RoutesFloatNoFormatToFloatIcells()
    {
        var cpp = GenerateProbe();

        // 1. Single.ToString() no-argument stays on the G7 default formatter.
        Assert.Contains("icall_single_to_string", cpp);

        // 2. Console / console-stream float arms ride the FLOAT icalls.
        Assert.Contains("icall_console_write_line_f", cpp);
        Assert.Contains("icall_console_write_f", cpp);
        Assert.Contains("icall_console_stream_write_f", cpp);
        Assert.Contains("icall_console_stream_write_line_f", cpp);

        // The probe also carries double/decimal controls: those faces must
        // stay on the double icalls (no accidental reroute).
        Assert.Contains("icall_console_write_line_d", cpp);
        Assert.Contains("icall_console_write_d", cpp);
        Assert.Contains("icall_console_stream_write_line_d", cpp);
        Assert.Contains("icall_console_stream_write_d", cpp);
    }

    //--------------------------------------------------------------------------
    // tiebreak2 (console float + double 15-digit-base ties): the e2e case must
    // drive BOTH console faces through the runtime -- Console.WriteLine(float)
    // / Write(float) / Console.Out.Write*(float) on the FLOAT icalls (the G7
    // tie text) and the same four double arms on the DOUBLE icalls (the
    // 15-digit tie text). The expected bytes are the net48 oracle
    // (tests/cases/probe-tiebreak2, run by scripts/difftest.ps1).
    //--------------------------------------------------------------------------
    [Fact]
    public void Emit_Tiebreak2_RoutesBothConsoleFaces()
    {
        var cpp = GenerateProbeCase(CaseExePath("probe-tiebreak2"), "Cases_ProbeTiebreak2.cpp");

        // Float face: all four console float surfaces.
        Assert.Contains("icall_console_write_line_f", cpp);
        Assert.Contains("icall_console_write_f", cpp);
        Assert.Contains("icall_console_stream_write_f", cpp);
        Assert.Contains("icall_console_stream_write_line_f", cpp);

        // Double face: the same four arms stay on the double icalls.
        Assert.Contains("icall_console_write_line_d", cpp);
        Assert.Contains("icall_console_write_d", cpp);
        Assert.Contains("icall_console_stream_write_d", cpp);
        Assert.Contains("icall_console_stream_write_line_d", cpp);

        // The case also covers the Single.ToString(format) float engine and the
        // double ToString/format surfaces (both are the tie-corrected bases).
        Assert.Contains("icall_format_float_custom", cpp);
        Assert.Contains("icall_format_double_custom", cpp);
        Assert.Contains("icall_double_to_string", cpp);
    }

    //--------------------------------------------------------------------------
    // Test harness.
    //--------------------------------------------------------------------------
    private static string GenerateProbeCase(string exe, string cppFile)
    {
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            return File.ReadAllText(Path.Combine(dir, "src", cppFile));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string GenerateProbe()
    {
        var exe = CaseExePath("probe-miscfmt");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            return File.ReadAllText(Path.Combine(dir, "src", "Cases_ProbeMiscFmt.cpp"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string CaseExePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "cases", name, "obj", "Release", "net48", name + ".exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"fixture {name}.exe not found; build the case first (dotnet build tests/cases/{name}/{name}.csproj -c Release)");
    }
}
