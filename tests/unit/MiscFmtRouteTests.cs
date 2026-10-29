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
    // Test harness.
    //--------------------------------------------------------------------------
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
