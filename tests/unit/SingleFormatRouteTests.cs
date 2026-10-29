using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using System.Text.RegularExpressions;
using Xunit;

public class SingleFormatRouteTests
{
    //--------------------------------------------------------------------------
    // impl-singlefmt (floatfix 7b0ee161 residual): Single.ToString non-R
    // formats (F/E/G/N/P/custom) must NOT route to the double engine -- net48
    // Number.FormatSingle converts the widened double at a FLOAT digit base
    // (7 significant digits; 9 for E precision > 6 / G precision > 7), which
    // the 15-digit double base cannot reproduce (net48 0.1f "F10" ->
    // "0.1000000000" vs the double engine "0.1000000015"; 0.1f "G10" ->
    // "0.100000001"; 16777216f "F" -> "16777220.00"). The binding routes
    // ToString(format[, provider]) to icall_format_float_custom
    // (MethodMap.Format.cs) and the boxed composite items ({0:F10}) must stay
    // on the same engine (icall_format_composite_item -> the float icall).
    // Fixture: the real net48 csc-built probe-singlefmt case exe (the
    // CaseExePath convention; build with scripts/build-unit-fixtures.ps1).
    //--------------------------------------------------------------------------
    [Fact]
    public void Emit_SingleFormat_CallsFloatCustomIcall_NotDouble()
    {
        var cpp = GenerateProbe();

        // ToString(format, IFormatProvider) arms on the probe's float receivers.
        Assert.Contains("icall_format_float_custom", cpp);
        // No float ToString(format) call may fall back to the double engine.
        Assert.DoesNotContain("icall_format_double_custom", cpp);
        // The parameterless controls stay on the G7 default formatter.
        Assert.Contains("icall_single_to_string", cpp);
    }

    [Fact]
    public void Emit_SingleFormat_ProbeCaseExe_FixtureBuilds()
    {
        // The e2e oracle surface: the net48 probe exe exists and exposes the
        // Case probe type (the difftest probe-singlefmt runs its records
        // byte-for-byte; this pins the fixture the unit suite documents).
        var exe = CaseExePath("probe-singlefmt");
        Assert.True(File.Exists(exe));
        var types = MetadataLoader.LoadAssembly(exe);
        Assert.Contains(types, t => t.Name == "ProbeSingleFmt");
    }

    //--------------------------------------------------------------------------
    // Test harness.
    //--------------------------------------------------------------------------
    private static string GenerateProbe()
    {
        var exe = CaseExePath("probe-singlefmt");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            return File.ReadAllText(Path.Combine(dir, "src", "Cases_ProbeSingleFmt.cpp"));
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
