using Il2Cpp.Transpiler;
using Xunit;

public class CliTests
{
    [Fact]
    public void Main_GeneratesOutput()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        try
        {
            var code = Program.RunMain(new[] { FixturePath(), dir });
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, "CMakeLists.txt")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReflectionList_ParsesManifest()
    {
        var manifest = TempManifest(
            "# comment line",
            "",
            "Calc.Program",
            "   Calc.Math   ",
            "# another comment",
            "System.String");
        try
        {
            var opts = CliParser.Parse(new[] { "--reflection-list", manifest, "in.dll", "out" });
            Assert.True(opts.ReflectionListProvided);
            Assert.Equal(new[] { "Calc.Program", "Calc.Math", "System.String" }, opts.ReflectionList);
            Assert.Equal(ReflectionGranularity.Members, opts.ReflectionGranularity);
        }
        finally
        {
            File.Delete(manifest);
        }
    }

    [Fact]
    public void ReflectionList_DefaultIsMembers_WhenFlagAbsent()
    {
        var opts = CliParser.Parse(new[] { "in.dll", "out" });
        Assert.False(opts.ReflectionListProvided);
        Assert.Empty(opts.ReflectionList);
        Assert.Equal(ReflectionGranularity.Members, opts.ReflectionGranularity);
    }

    [Fact]
    public void ReflectionList_GranularityAll()
    {
        var manifest = TempManifest("Calc.Program");
        try
        {
            var opts = CliParser.Parse(new[] { "--reflection-granularity", "all", "--reflection-list", manifest, "in.dll", "out" });
            Assert.Equal(ReflectionGranularity.All, opts.ReflectionGranularity);
            Assert.Equal(new[] { "Calc.Program" }, opts.ReflectionList);
        }
        finally
        {
            File.Delete(manifest);
        }
    }

    [Fact]
    public void ReflectionList_FlagsAcceptedBeforePositionals()
    {
        var manifest = TempManifest("Calc.Program");
        try
        {
            var opts = CliParser.Parse(new[] { "--reflection-list", manifest, "in.dll", "out" });
            Assert.Equal("in.dll", opts.InputAssembly);
            Assert.Equal("out", opts.OutputDir);
        }
        finally
        {
            File.Delete(manifest);
        }
    }

    [Fact]
    public void ReflectionList_MissingFile_ThrowsCleanError()
    {
        var missing = Path.Combine(Path.GetTempPath(), "il2cpp_missing_" + Guid.NewGuid().ToString("N") + ".txt");
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "--reflection-list", missing, "in.dll", "out" }));
        Assert.Contains("cannot read reflection manifest", ex.Message);
    }

    [Fact]
    public void ReflectionList_MissingValue_ThrowsCleanError()
    {
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "in.dll", "--reflection-list" }));
        Assert.Contains("requires a manifest file path", ex.Message);
    }

    [Fact]
    public void ReflectionList_InvalidGranularity_ThrowsCleanError()
    {
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "--reflection-list", "m.txt", "--reflection-granularity", "bogus", "in.dll", "out" }));
        Assert.Contains("invalid --reflection-granularity", ex.Message);
    }

    [Fact]
    public void Main_ReflectionList_WritesManifestFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        var manifest = TempManifest("Calc.Program");
        try
        {
            var code = Program.RunMain(new[] { "--reflection-list", manifest, FixturePath(), dir });
            Assert.Equal(0, code);
            var lines = File.ReadAllLines(Path.Combine(dir, "reflection-manifest.txt"));
            Assert.Contains("# il2cppc reflection manifest (mode B)", lines);
            Assert.Contains("# granularity: members", lines);
            Assert.Contains("Calc.Program", lines);
        }
        finally
        {
            Directory.Delete(dir, true);
            File.Delete(manifest);
        }
    }

    [Fact]
    public void Main_ReflectionList_FiltersManifestToAssemblyTypes()
    {
        // Mode B generation rule (reflection-runtime-design.md §4): generate reflection
        // metadata only for types on the manifest. Manifest entries must be validated
        // against actual assembly types first — the calc.dll fixture only contains
        // Calc.Program, so types absent from the manifest must be filtered (the first
        // step in consuming the manifest on the generation side).
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        var manifest = TempManifest("Calc.Program", "Calc.Math", "No.Such.Type");
        try
        {
            var code = Program.RunMain(new[] { "--reflection-list", manifest, FixturePath(), dir });
            Assert.Equal(0, code);
            var lines = File.ReadAllLines(Path.Combine(dir, "reflection-manifest.txt"));
            Assert.Contains("Calc.Program", lines);
            Assert.DoesNotContain("Calc.Math", lines);
            Assert.DoesNotContain("No.Such.Type", lines);
        }
        finally
        {
            Directory.Delete(dir, true);
            File.Delete(manifest);
        }
    }

    [Fact]
    public void Main_ReflectionList_WarnsOnDroppedTypes()
    {
        // Filtered manifest entries must warn (non-fatal) — user typos become
        // diagnosable but never block generation.
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        var manifest = TempManifest("Calc.Program", "Calc.Math");
        var originalErr = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            var code = Program.RunMain(new[] { "--reflection-list", manifest, FixturePath(), dir });
            Assert.Equal(0, code);
            var output = captured.ToString();
            Assert.Contains("Calc.Math", output);
            Assert.Contains("not found in assembly", output);
        }
        finally
        {
            Console.SetError(originalErr);
            Directory.Delete(dir, true);
            File.Delete(manifest);
        }
    }

    [Fact]
    public void Main_MissingReflectionListFile_FailsNonZero()
    {
        var missing = Path.Combine(Path.GetTempPath(), "il2cpp_missing_" + Guid.NewGuid().ToString("N") + ".txt");
        var code = Program.RunMain(new[] { "--reflection-list", missing, FixturePath(), "out" });
        Assert.Equal(1, code);
    }

    // Exit-code contract (user-guide.md §3.2): 0 = success, 1 = parse error,
    // 2 = transpile failure. Parse errors and failures go to STDERR with the
    // "il2cppc error:" prefix; success diagnostics go to stdout.

    [Fact]
    public void Parse_EmptyOutputDir_ThrowsCleanError()
    {
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "in.dll", "" }));
        Assert.Contains("non-empty paths", ex.Message);
    }

    [Fact]
    public void Parse_WhitespaceOutputDir_ThrowsCleanError()
    {
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "in.dll", "   " }));
        Assert.Contains("non-empty paths", ex.Message);
    }

    [Fact]
    public void Parse_EmptyInputAssembly_ThrowsCleanError()
    {
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "", "out" }));
        Assert.Contains("non-empty paths", ex.Message);
    }

    [Fact]
    public void Main_MissingInputAssembly_ExitsTwo_WithStderrError()
    {
        // user-guide.md §3.2: exit 2 = transpile failure (metadata load throws),
        // message + stack trace on stderr with the "il2cppc error:" prefix.
        var originalErr = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            var missing = Path.Combine(Path.GetTempPath(), "il2cpp_nonexistent_" + Guid.NewGuid().ToString("N") + ".dll");
            var code = Program.RunMain(new[] { missing, "out" });
            Assert.Equal(2, code);
            var output = captured.ToString();
            Assert.StartsWith("il2cppc error:", output);
            Assert.Contains("at Il2Cpp.Transpiler.Program.RunMain", output); // stack trace present
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void Main_ParseError_ExitsOne_WithStderrPrefix()
    {
        // user-guide.md §3.2: exit 1 = command-line parse error, message on stderr.
        var originalErr = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            var code = Program.RunMain(new[] { FixturePath() }); // too few positionals
            Assert.Equal(1, code);
            Assert.StartsWith("il2cppc error:", captured.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void Main_EmptyOutputDir_ExitsOne()
    {
        // An empty output-dir is user misuse -> parse error (exit 1), never a
        // silent generation into the caller's working directory.
        var code = Program.RunMain(new[] { FixturePath(), "" });
        Assert.Equal(1, code);
    }

    [Fact]
    public void Main_Success_PrintsGeneratedMessageToStdout()
    {
        // user-guide.md §3.2: exit 0 success prints "generated N type(s) -> <dir>"
        // to stdout (stdout = diagnostics, stderr = errors/warnings).
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        var originalOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            var code = Program.RunMain(new[] { FixturePath(), dir });
            Assert.Equal(0, code);
            Assert.Contains("generated 1 type(s) -> ", captured.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReflectionList_GranularityValues_AreCaseInsensitive()
    {
        // user-guide.md §3.1: granularity values are case-insensitive.
        var manifest = TempManifest("Calc.Program");
        try
        {
            var opts = CliParser.Parse(new[] { "--reflection-granularity", "ALL", "--reflection-list", manifest, "in.dll", "out" });
            Assert.Equal(ReflectionGranularity.All, opts.ReflectionGranularity);
            opts = CliParser.Parse(new[] { "--reflection-granularity", "MeMbErS", "--reflection-list", manifest, "in.dll", "out" });
            Assert.Equal(ReflectionGranularity.Members, opts.ReflectionGranularity);
        }
        finally
        {
            File.Delete(manifest);
        }
    }

    [Fact]
    public void ReflectionList_GranularityMissingValue_ThrowsCleanError()
    {
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "in.dll", "out", "--reflection-granularity" }));
        Assert.Contains("requires a value", ex.Message);
    }

    [Fact]
    public void ReflectionList_FlagsAcceptedAfterPositionals()
    {
        // user-guide.md §3.1: options may appear before, between, or after the positionals.
        var manifest = TempManifest("Calc.Program");
        try
        {
            var opts = CliParser.Parse(new[] { "in.dll", "out", "--reflection-list", manifest });
            Assert.Equal("in.dll", opts.InputAssembly);
            Assert.Equal("out", opts.OutputDir);
            Assert.True(opts.ReflectionListProvided);
        }
        finally
        {
            File.Delete(manifest);
        }
    }

    // RES-1 continuation (iron rule 18): the resource-pool.h DATA FILE is the
    // emit-side contract -- Program.RunMain must write include/internal/
    // resource-pool.h when the assembly has embedded resources, and must NOT
    // write it (nor include it from generated.h) when there are none.
    [Fact]
    public void Main_ResourceFixture_WritesResourcePoolHeader()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        try
        {
            var code = Program.RunMain(new[] { FixtureResxPath(), dir });
            Assert.Equal(0, code);

            var poolPath = Path.Combine(dir, "include", "internal", "resource-pool.h");
            Assert.True(File.Exists(poolPath), "resource-pool.h must be written when resources exist");
            var pool = File.ReadAllText(poolPath);
            Assert.Contains("g_resPool", pool);
            Assert.Contains("il2cpp_resource_pool_fill", pool);

            var generated = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("#include \"resource-pool.h\"", generated);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Main_NoResourceFixture_WritesNoResourcePoolHeader()
    {
        // calc.dll has no embedded resources -> resource-pool.h is neither
        // written nor included from generated.h (conditional emit, same
        // convention as the empty string pool).
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        try
        {
            var code = Program.RunMain(new[] { FixturePath(), dir });
            Assert.Equal(0, code);
            Assert.False(File.Exists(Path.Combine(dir, "include", "internal", "resource-pool.h")));
            var generated = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.DoesNotContain("resource-pool.h", generated);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // Output-dir organization (string-pool.h / resource-pool.h consistency):
    // both data files land under include/internal/ of the output dir, both are
    // conditional on a non-empty pool, both are included from generated.h only
    // when emitted.
    [Fact]
    public void Main_ResourceFixture_WritesStringAndResourcePoolsToInternalDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        try
        {
            var code = Program.RunMain(new[] { FixtureResxPath(), dir });
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, "include", "internal", "string-pool.h")));
            Assert.True(File.Exists(Path.Combine(dir, "include", "internal", "resource-pool.h")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string TempManifest(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), "il2cpp_manifest_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    // The test host's working directory is the bin output directory, so relative
    // paths cannot resolve to the repo root (same convention as the other tests).
    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "out", "calc.dll");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "fixture calc.dll not found; run scripts/build-fixtures.ps1 first");
    }

    // RES-1 fixture: resx.exe carries 2 embedded manifest resources (the resx
    // blob + hello.bin) and 4 string literals -- the resource-pool.h and
    // string-pool.h emission probes. Same walk-up convention as FixturePath.
    private static string FixtureResxPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "out", "resx.exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "fixture resx.exe not found; run scripts/build-fixtures.ps1 first");
    }
}
