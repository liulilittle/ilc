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
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
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
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            File.Delete(manifest);
        }
    }

    [Fact]
    public void Main_ReflectionList_FiltersManifestToAssemblyTypes()
    {
        // Mode B generation rule (reflection-runtime-design.md §4): generate reflection
        // metadata only for types on the manifest. Manifest entries must be validated
        // against actual assembly types first —the calc.dll fixture only contains
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
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            File.Delete(manifest);
        }
    }

    [Fact]
    public void Main_ReflectionList_WarnsOnDroppedTypes()
    {
        // Filtered manifest entries must warn (non-fatal) —user typos become
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
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
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
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
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
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
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
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
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
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Friendly-compiler deepening (S1-S6): --help/--version/--log/--quiet/
    // --verbose and the audit subcommand exit-code contract. All these live in
    // the CliTests class (xUnit serialises tests WITHIN a class, so the
    // Console.SetOut/SetError redirections never race a parallel class).
    //--------------------------------------------------------------------------

    [Fact]
    public void Version_ExitsZero_PrintsVersion()
    {
        var originalOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            var code = Program.RunMain(new[] { "--version" });
            Assert.Equal(0, code);
            Assert.Contains("1.0.0", captured.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Help_ExitsZero_PrintsUsage()
    {
        var originalOut = Console.Out;
        try
        {
            foreach (var args in new[] { new[] { "--help" }, new[] { "-h" }, new[] { "help" } })
            {
                var captured = new StringWriter();
                Console.SetOut(captured);
                var code = Program.RunMain(args);
                Assert.Equal(0, code);
                Assert.Contains("usage: il2cppc", captured.ToString());
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Help_Subcommand_Audit()
    {
        var originalOut = Console.Out;
        try
        {
            foreach (var args in new[] { new[] { "help", "audit-internalcalls" }, new[] { "audit-internalcalls", "--help" } })
            {
                var captured = new StringWriter();
                Console.SetOut(captured);
                var code = Program.RunMain(args);
                Assert.Equal(0, code);
                Assert.Contains("audit-internalcalls", captured.ToString());
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // GN-19 (GL): the --help output is the multi-section HelpText (not the
    // single-line Usage). Assert the section markers exist so a future regression
    // to the one-line usage is caught.
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void Help_Output_HasStructuredSections(string arg)
    {
        var originalOut = Console.Out;
        try
        {
            var captured = new StringWriter();
            Console.SetOut(captured);
            var code = Program.RunMain(new[] { arg });
            Assert.Equal(0, code);
            var output = captured.ToString();
            Assert.StartsWith("usage: il2cppc", output);
            Assert.Contains("NAME", output);
            Assert.Contains("SYNOPSIS", output);
            Assert.Contains("OPTIONS", output);
            Assert.Contains("SUBCOMMANDS", output);
            Assert.Contains("ARCHITECTURES", output);
            Assert.Contains("EXIT CODES", output);
            Assert.Contains("EXAMPLES", output);
            Assert.Contains("ENVIRONMENT", output);
            Assert.Contains("NOTES", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // GN-19 (GL): the ARCHITECTURES section lists all 11 canonical arches, the
    // aliases and their pointer widths, so the enumeration surface is visible.
    [Fact]
    public void Help_Architectures_ListAllArches()
    {
        var originalOut = Console.Out;
        try
        {
            var captured = new StringWriter();
            Console.SetOut(captured);
            var code = Program.RunMain(new[] { "--help" });
            Assert.Equal(0, code);
            var output = captured.ToString();
            foreach (var arch in new[] { "x86", "x64", "arm32", "arm64", "riscv32", "riscv64", "s390x", "mips", "mips64", "mips32", "ppc", "ppc64", "ppc32" })
                Assert.Contains(arch, output);
            Assert.Contains("32-bit", output);
            Assert.Contains("64-bit", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Audit_MissingArg_ExitsOne()
    {
        var originalErr = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            var code = Program.RunMain(new[] { "audit-internalcalls" });
            Assert.Equal(1, code);
            Assert.StartsWith("il2cppc error:", captured.ToString());
            Assert.Contains("audit-internalcalls", captured.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void Audit_Success_ExitsZero()
    {
        var originalOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            var code = Program.RunMain(new[] { "audit-internalcalls", FixturePath() });
            Assert.Equal(0, code);
            Assert.Contains("SUMMARY", captured.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Audit_BadAssembly_ExitsTwo()
    {
        var originalErr = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            var missing = Path.Combine(Path.GetTempPath(), "il2cpp_nonexistent_" + Guid.NewGuid().ToString("N") + ".dll");
            var code = Program.RunMain(new[] { "audit-internalcalls", missing });
            Assert.Equal(2, code);
            Assert.Contains("il2cppc error:", captured.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void Log_WritesFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        var log = Path.Combine(Path.GetTempPath(), "il2cpp_log_" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var code = Program.RunMain(new[] { "--log", log, FixturePath(), dir });
            Assert.Equal(0, code);
            Assert.True(File.Exists(log), "log file must be written");
            Assert.Contains("generated", File.ReadAllText(log));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (File.Exists(log)) File.Delete(log);
        }
    }

    [Fact]
    public void Log_Unwritable_ExitsOne()
    {
        var originalErr = Console.Error;
        var captured = new StringWriter();
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_logdir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Console.SetError(captured);
            // --log targeting a directory cannot be opened as a file -> exit 1
            var code = Program.RunMain(new[] { "--log", dir, FixturePath(), "out" });
            Assert.Equal(1, code);
            Assert.StartsWith("il2cppc error:", captured.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Quiet_SuppressesInfo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        var originalOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            var code = Program.RunMain(new[] { "--quiet", FixturePath(), dir });
            Assert.Equal(0, code);
            var output = captured.ToString();
            Assert.DoesNotContain("generated", output);
            Assert.DoesNotContain("symbols", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Verbose_ShowsProgress()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        var originalOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            var code = Program.RunMain(new[] { "--verbose", FixturePath(), dir });
            Assert.Equal(0, code);
            Assert.Contains("emit", captured.ToString());
            Assert.Contains("Calc.Program", captured.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void QuietVerbose_MutualExclusion_ExitsOne()
    {
        var originalErr = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            var code = Program.RunMain(new[] { "--quiet", "--verbose", FixturePath(), "out" });
            Assert.Equal(1, code);
            Assert.Contains("mutually exclusive", captured.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    // G1 (cross-platform arch): --arch normalizes to the target pointer width.
    [Fact]
    public void Arch_DefaultIsHostWidth_WhenFlagAbsent()
    {
        var opts = CliParser.Parse(new[] { "in.dll", "out" });
        Assert.Null(opts.TargetArch);
        Assert.Equal(IntPtr.Size, opts.PointerSize);
    }

    [Theory]
    [InlineData("x86", 4)]
    [InlineData("arm32", 4)]
    [InlineData("riscv32", 4)]
    [InlineData("mips", 4)]
    [InlineData("ppc", 4)]
    [InlineData("x64", 8)]
    [InlineData("arm64", 8)]
    [InlineData("riscv64", 8)]
    [InlineData("mips64", 8)]
    [InlineData("ppc64", 8)]
    [InlineData("s390x", 8)]
    [InlineData("X86", 4)]
    public void Arch_Value_NormalizesToPointerSize(string arch, int expected)
    {
        var opts = CliParser.Parse(new[] { "--arch", arch, "in.dll", "out" });
        Assert.Equal(arch.ToLowerInvariant(), opts.TargetArch);
        Assert.Equal(expected, opts.PointerSize);
    }

    [Fact]
    public void Arch_InvalidValue_IsUserMisuse()
    {
        var originalErr = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            var code = Program.RunMain(new[] { "--arch", "ia64", FixturePath(), "out" });
            Assert.Equal(1, code);
            Assert.Contains("--arch", captured.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void Arch_MissingValue_IsUserMisuse()
    {
        var originalErr = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            var code = Program.RunMain(new[] { "--arch", FixturePath(), "out" });
            Assert.Equal(1, code);
            Assert.Contains("--arch", captured.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    // GN-19 (GL): the 32/64 aliases normalize to the canonical family name and
    // pointer width -- mips32 == mips (4), ppc32 == ppc (4).
    [Theory]
    [InlineData("mips32", "mips")]
    [InlineData("ppc32", "ppc")]
    [InlineData("MIPS32", "mips")]
    public void Arch_Alias_NormalizesToCanonical(string input, string canonical)
    {
        var opts = CliParser.Parse(new[] { "--arch", input, "in.dll", "out" });
        Assert.Equal(canonical, opts.TargetArch);
        Assert.Equal(4, opts.PointerSize);
    }

    // GN-19 (GL): a bare "riscv" is NOT accepted -- the width must be explicit
    // (riscv32/riscv64). User misuse -> exit 1.
    [Fact]
    public void Arch_BareRiscv_IsUserMisuse()
    {
        var originalErr = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            var code = Program.RunMain(new[] { "--arch", "riscv", FixturePath(), "out" });
            Assert.Equal(1, code);
            Assert.Contains("--arch", captured.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    // GN-19 (GL, AOT absolute standard): s390x is big-endian-only and big-endian
    // targets must be LOUDLY rejected at transpile time (never "enumerable but
    // generates wrong byte data"). ppc64-BE / mips-BE are the explicit BE-variant
    // spellings. All are user misuse -> CliParseException -> exit 1.
    [Theory]
    [InlineData("s390x")]
    [InlineData("mips-BE")]
    [InlineData("ppc64-BE")]
    public void Arch_BigEndian_RejectedIsUserMisuse(string arch)
    {
        var originalErr = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            var code = Program.RunMain(new[] { "--arch", arch, FixturePath(), "out" });
            Assert.Equal(1, code);
            Assert.Contains("big-endian target not supported", captured.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    //--------------------------------------------------------------------------
    // nb10: Windows application manifest. Three sources (--manifest file,
    // --requested-execution-level synthesis, the sidecar <assembly>.manifest),
    // the mutual-exclusion / value validation, and the emitted artifacts
    // (app.manifest + app.rc with `1 24`, plus the CMake RC arm).
    //--------------------------------------------------------------------------
    [Fact]
    public void Manifest_RequestedExecutionLevel_SynthesizesAllThreeLevels()
    {
        foreach (var (token, canonical) in new[]
                 {
                     ("asInvoker", "asInvoker"),
                     ("highestavailable", "highestAvailable"),
                     ("requireadministrator", "requireAdministrator"),
                 })
        {
            var opts = CliParser.Parse(new[] { "--requested-execution-level", token, "in.dll", "out" });
            Assert.Equal(canonical, opts.RequestedExecutionLevel);
            Assert.True(opts.ManifestIsGenerated);
            Assert.Equal("", opts.ManifestPath);
            Assert.False(opts.UiAccess);
        }
    }

    [Fact]
    public void Manifest_InvalidLevel_ThrowsCleanError()
    {
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "--requested-execution-level", "admin", "in.dll", "out" }));
        Assert.Contains("invalid --requested-execution-level", ex.Message);
        Assert.Contains("requireAdministrator", ex.Message);
    }

    [Fact]
    public void Manifest_MissingValue_ThrowsCleanError()
    {
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "in.dll", "--manifest" }));
        Assert.Contains("requires a Windows application manifest file path", ex.Message);
    }

    [Fact]
    public void Manifest_MissingFile_ThrowsCleanError()
    {
        var missing = Path.Combine(Path.GetTempPath(), "il2cpp_missing_" + Guid.NewGuid().ToString("N") + ".manifest");
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "--manifest", missing, "in.dll", "out" }));
        Assert.Contains("Windows application manifest not found", ex.Message);
    }

    [Fact]
    public void Manifest_ExplicitFile_IsResolvedVerbatim()
    {
        var path = TempManifestXml("<assembly/>");
        try
        {
            var opts = CliParser.Parse(new[] { "--manifest", path, "in.dll", "out" });
            Assert.Equal(path, opts.ManifestPath);
            Assert.False(opts.ManifestIsGenerated);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Manifest_Sidecar_IsPickedUpAutomatically()
    {
        // The Windows external-manifest convention: <assembly>.manifest next to
        // the assembly needs no flag.
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_sidecar_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var asm = Path.Combine(dir, "App.exe");
            File.WriteAllText(asm, "");
            var sidecar = asm + ".manifest";
            File.WriteAllText(sidecar, "<assembly/>");
            var opts = CliParser.Parse(new[] { asm, Path.Combine(dir, "out") });
            Assert.Equal(sidecar, opts.ManifestPath);
            Assert.False(opts.ManifestIsGenerated);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Manifest_NoSource_LeavesManifestPathEmpty()
    {
        var opts = CliParser.Parse(new[] { "in.dll", "out" });
        Assert.Equal("", opts.ManifestPath);
        Assert.False(opts.ManifestIsGenerated);
    }

    [Fact]
    public void Manifest_FileAndLevel_AreMutuallyExclusive()
    {
        var path = TempManifestXml("<assembly/>");
        try
        {
            var ex = Assert.Throws<CliParseException>(() =>
                CliParser.Parse(new[] { "--manifest", path, "--requested-execution-level", "asInvoker", "in.dll", "out" }));
            Assert.Contains("mutually exclusive", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Manifest_UiAccess_RequiresLevel()
    {
        var ex = Assert.Throws<CliParseException>(() =>
            CliParser.Parse(new[] { "--ui-access", "in.dll", "out" }));
        Assert.Contains("--ui-access requires --requested-execution-level", ex.Message);
    }

    [Fact]
    public void Main_RequestedExecutionLevel_EmitsManifestAndRc()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        try
        {
            var code = Program.RunMain(new[]
                { "--requested-execution-level", "requireAdministrator", "--ui-access", FixturePath(), dir });
            Assert.Equal(0, code);

            var manifest = File.ReadAllText(Path.Combine(dir, "app.manifest"));
            Assert.Contains("requestedExecutionLevel level=\"requireAdministrator\" uiAccess=\"true\"", manifest);
            Assert.Contains("urn:schemas-microsoft-com:asm.v3", manifest);

            var rc = File.ReadAllText(Path.Combine(dir, "app.rc"));
            Assert.Contains("1 24 \"app.manifest\"", rc);

            var cmake = File.ReadAllText(Path.Combine(dir, "CMakeLists.txt"));
            Assert.Contains("enable_language(RC)", cmake);
            // app.rc belongs to the EXECUTABLE source list only.
            var exeBlock = cmake.Substring(cmake.IndexOf("add_executable(il2cpp_gen_exe", StringComparison.Ordinal));
            Assert.Contains("app.rc", exeBlock);
            var libBlock = cmake.Substring(cmake.IndexOf("add_library(il2cpp_gen STATIC", StringComparison.Ordinal));
            Assert.DoesNotContain("app.rc", libBlock.Substring(0, libBlock.IndexOf("add_executable", StringComparison.Ordinal)));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Main_NoManifest_EmitsNoRcAndNoRcLanguage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        try
        {
            var code = Program.RunMain(new[] { FixturePath(), dir });
            Assert.Equal(0, code);
            Assert.False(File.Exists(Path.Combine(dir, "app.rc")));
            Assert.False(File.Exists(Path.Combine(dir, "app.manifest")));
            var cmake = File.ReadAllText(Path.Combine(dir, "CMakeLists.txt"));
            Assert.DoesNotContain("enable_language(RC)", cmake);
            Assert.DoesNotContain("app.rc", cmake);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Main_ManifestFile_IsEmbeddedVerbatim()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        const string xml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<assembly manifestVersion=\"1.0\" xmlns=\"urn:schemas-microsoft-com:asm.v1\">\n"
            + "  <trustInfo xmlns=\"urn:schemas-microsoft-com:asm.v2\"><security>\n"
            + "    <requestedPrivileges xmlns=\"urn:schemas-microsoft-com:asm.v3\">\n"
            + "      <requestedExecutionLevel level=\"highestAvailable\" uiAccess=\"false\"/>\n"
            + "    </requestedPrivileges></security></trustInfo>\n"
            + "</assembly>\n";
        var path = TempManifestXml(xml);
        try
        {
            var code = Program.RunMain(new[] { "--manifest", path, FixturePath(), dir });
            Assert.Equal(0, code);
            Assert.Equal(xml, File.ReadAllText(Path.Combine(dir, "app.manifest")));
            Assert.Contains("1 24 \"app.manifest\"", File.ReadAllText(Path.Combine(dir, "app.rc")));
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private static string TempManifestXml(string xml)
    {
        var path = Path.Combine(Path.GetTempPath(), "il2cpp_appmanifest_" + Guid.NewGuid().ToString("N") + ".manifest");
        File.WriteAllText(path, xml);
        return path;
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
