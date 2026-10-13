using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Il2Cpp.Transpiler.Opt;

namespace Il2Cpp.Transpiler;
public static class Program
{
    public static int Main(string[] args) => RunMain(args);

    // S1/S2: --version prints the informational version stamped into the
    // assembly by il2cppc.csproj (<InformationalVersion>1.0.0</InformationalVersion>).
    private static string VersionText =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";

    // S5: a TextWriter that mirrors every write onto a second writer -- the
    // stdout/stderr tee for --log <file>. TextWriter's base Write(string) calls
    // Write(char[]) which calls Write(char) per char; overriding the concrete
    // Write(string)/WriteLine(string) paths keeps the mirror output intact.
    private sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter _primary;
        private readonly TextWriter _mirror;

        public TeeTextWriter(TextWriter primary, TextWriter mirror)
        {
            _primary = primary;
            _mirror = mirror;
        }

        public override Encoding Encoding => _primary.Encoding;

        public override void Write(char value)
        {
            _primary.Write(value);
            _mirror.Write(value);
        }

        public override void Write(string? value)
        {
            _primary.Write(value);
            _mirror.Write(value);
        }

        public override void WriteLine(string? value)
        {
            _primary.WriteLine(value);
            _mirror.WriteLine(value);
        }

        public override void Flush()
        {
            _primary.Flush();
            _mirror.Flush();
        }
    }

    public static int RunMain(string[] args)
    {
        // S2 (friendly compiler): --help/-h/--version are pre-scanned across ALL
        // args BEFORE parse -- "--help" was previously treated as a positional
        // (bad-usage exit 1) instead of printing usage. Priority: help > version.
        // Both the `help` subcommand and `audit-internalcalls --help` print the
        // matching usage to STDOUT and exit 0.
        var isAudit = args.Length > 0 && args[0] == "audit-internalcalls";
        if (args.Length > 0 && args[0] == "help")
        {
            Console.Out.WriteLine(args.Length > 1 && args[1] == "audit-internalcalls"
                ? CliOptions.AuditUsage
                : CliOptions.HelpText);
            return 0;
        }
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.Out.WriteLine(isAudit ? CliOptions.AuditUsage : CliOptions.HelpText);
            return 0;
        }
        if (args.Contains("--version"))
        {
            Console.Out.WriteLine($"il2cppc {VersionText}");
            return 0;
        }

        // S3 (friendly compiler): the audit subcommand honors the same 0/1/2
        // exit contract as the main path. A missing <assembly> is user misuse
        // (usage to stderr, exit 1); a bad assembly path is a run failure
        // (prefix to stderr, exit 2); success stays 0. Previously both crashed
        // with an unhandled ArgumentException / FileNotFoundException (unstable
        // exit code + raw stack trace), breaking the documented contract.
        if (isAudit)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine($"il2cppc error: {CliOptions.AuditUsage}");
                return 1;
            }
            try
            {
                MethodRefResolver.AuditInternalCalls(args[1], Console.Out);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"il2cppc error: {ex.Message}");
                return 2;
            }
        }

        CliOptions opts;
        try
        {
            opts = CliParser.Parse(args);
            // GN-19 (GL, G12 little-endian contract): a big-endian target is loudly
            // rejected AFTER parse (s390x parses to PointerSize 8) but BEFORE any
            // code generation -- never "enumerable but generates wrong bytes". The
            // explicit -BE variant spellings are already rejected inside ParseArch.
            CliParser.ValidateEndianness(opts.TargetArch);
        }
        catch (CliParseException ex)
        {
            Console.Error.WriteLine($"il2cppc error: {ex.Message}");
            return 1;
        }

        // S5 (friendly compiler): --log <file> tees stdout/stderr into the log
        // for the whole run. The ORIGINAL streams are captured here and restored
        // in the finally (a test process never leaks the tee). An unopenable log
        // path is reported after parse and exits 1 -- never a generation attempt.
        var origOut = Console.Out;
        var origErr = Console.Error;
        var logStream = (StreamWriter?)null;
        try
        {
            if (!string.IsNullOrEmpty(opts.LogPath))
            {
                try
                {
                    logStream = new StreamWriter(opts.LogPath, append: false, Encoding.UTF8);
                    Console.SetOut(new TeeTextWriter(origOut, logStream));
                    Console.SetError(new TeeTextWriter(origErr, logStream));
                }
                catch (Exception ex)
                {
                    logStream?.Dispose();
                    logStream = null;
                    Console.Error.WriteLine($"il2cppc error: cannot open log file '{opts.LogPath}': {ex.Message}");
                    return 1;
                }
            }

            // S6 (friendly compiler): --quiet suppresses the stdout diagnostics
            // (symbols / pool reports / final summary); errors and warnings on
            // stderr are unaffected.
            if (opts.Quiet)
                Console.SetOut(TextWriter.Null);

            try
            {
                var types = MetadataLoader.LoadAssembly(opts.InputAssembly);
                // Nested type expansion (TypeEmitter.ExpandNestedTypes): MetadataLoader
                // skips IsNested TypeDefs, while the reference side (newobj / field cast /
                // method name hash) resolves nested types by SHORT name (Cases.Recurse+Node
                // -> "Node") -- without expansion the generated C++ references Node_class
                // with no definition. After expansion the nested types join the list as
                // independent top-level entries and CodeGenerator emits struct/descriptor/
                // methods normally. Must run after LoadAssembly and before Generate
                // (VTablePlanner.PlanAll needs the complete type graph).
                using (var pe = new PEReader(File.OpenRead(opts.InputAssembly)))
                    TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
                // SYMLN-1: load the assembly's Portable PDB (if any) so the
                // generator can emit C# #line directives. Native/missing PDBs
                // return null -> no #line (hand-written .il input unaffected).
                var symMap = MetadataLoader.LoadPortablePdb(opts.InputAssembly);
                if (symMap != null)
                    Console.WriteLine($"symbols: {symMap.MethodCount} method(s) mapped from Portable PDB -> #line C# line directives");
                else
                    Console.WriteLine("symbols: no Portable PDB found -> no #line C# line directives");
                // S6: --verbose passes a per-type progress callback to Generate
                // (invoked at the end of each type's loop; the generator swallows
                // callback exceptions so a progress hook can never fail a
                // successful transpile).
                var result = CodeGenerator.Generate(types, opts.OutputDir, opts.InputAssembly, symMap, opts,
                    opts.Verbose ? (Action<string>)(msg => Console.WriteLine(msg)) : null);
                if (opts.ReflectionListProvided)
                    WriteReflectionManifest(opts, types);
                WriteStringPoolReport(result, opts.OutputDir);
                WriteResourcePoolReport(opts.InputAssembly, opts.OutputDir);
                WriteLicmReport(types, opts.InputAssembly);
                WriteInlineReport(types, opts.InputAssembly);
                WriteTempSlotReport(types, opts.InputAssembly);
                Console.WriteLine($"generated {types.Count} type(s) -> {opts.OutputDir}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"il2cppc error: {ex.Message}");
                // S4: the inner exception's stack trace preserves the original
                // throw-point line (MethodContextException re-wrapping moved the
                // outer trace's frame to the wrap site).
                if (ex.InnerException?.StackTrace is { } innerStack)
                    Console.Error.WriteLine(innerStack);
                Console.Error.WriteLine(ex.StackTrace);
                return 2;
            }
        }
        finally
        {
            if (opts.Quiet) Console.SetOut(origOut);
            if (logStream != null)
            {
                Console.SetOut(origOut);
                Console.SetError(origErr);
                logStream.Dispose();
            }
        }
    }

    // Reflection mode B consumption (reflection-runtime-design.md section 4): the generator
    // reads the user manifest and filters the manifest types -- keeping only types
    // that truly exist in the assembly set (these are the types that will get
    // reflection metadata; the TypeInfo table emission uses Kept as its scope).
    // Unmatched entries are dropped with a warning (non-fatal: a user typo is
    // diagnosable without blocking generation). The filter applies only to the
    // reflection metadata scope; the Il2CppClass type descriptors are object-model
    // infrastructure that mode B does not trim (reflection-runtime-design.md section 5.1).
    private static void WriteReflectionManifest(CliOptions opts, List<TypeDef> types)
    {
        var filtered = TypeEmitter.FilterReflectionManifest(opts.ReflectionList, types);
        foreach (var dropped in filtered.Dropped)
            Console.Error.WriteLine(
                $"il2cppc warning: reflection manifest entry '{dropped}' not found in assembly -- dropped (mode B: only manifest types get reflection metadata)");
        ReflectionManifest.Write(opts, filtered.Kept);
        Console.WriteLine($"reflection manifest: {filtered.Kept.Count} type(s) in scope, {filtered.Dropped.Count} entry(ies) dropped");
    }

    // RES-1 (iron rule 18): embedded manifest resources -- the transpiler half
    // of the resource chain. LoadManifestResources reads the ManifestResource
    // table (MetadataLoader); RenderResourcePoolHeader emits the T3 g_strPool-
    // pattern static data file (include/internal/resource-pool.h): one inline
    // static byte array per payload + a g_resPool[] entry table (name/data/
    // length) + il2cpp_resource_pool_fill() calling il2cpp_resources_register
    // (the runtime's single registry writer -- D1). The runtime half is
    // src/runtime/src/resources.cpp (Assembly.GetManifestResourceStream /
    // GetManifestResourceNames / ResourceManager.GetString).
    //
    // Empty pool -> empty string (a zero-size static array is ill-formed; an
    // assembly with no resources emits no data file at all).
    public static string RenderResourcePoolHeader(IReadOnlyList<ManifestResourceDef> resources)
    {
        if (resources.Count == 0) return "";
        var w = new CppWriter();
        w.Line("#pragma once");
        w.Line("// generated by il2cppc - do not edit");
        w.Line("// RES-1 (iron rule 18): embedded manifest resources (T3 g_strPool pattern).");
        w.Line("// One static byte array per payload + the g_resPool[] entry table;");
        w.Line("// il2cpp_resource_pool_fill() hands the table to il2cpp_resources_register");
        w.Line("// (the runtime registry's single writer -- D1).");
        w.Line("#include \"il2cpp_resources.h\"");
        w.Line("namespace il2cpp {");
        for (var i = 0; i < resources.Count; i++)
        {
            var bytes = string.Join(",", resources[i].Data.Select(b => $"0x{b:X2}"));
            w.Line($"inline const uint8_t g_resPool_data_{i}[] = {{ {bytes} }};");
        }
        w.Line($"inline constexpr int32_t g_resPoolCount = {resources.Count};");
        w.Line("inline const ManifestResourceEntry g_resPool[] = {");
        w.Open();
        for (var i = 0; i < resources.Count; i++)
            w.Line($"    {{ \"{EscapeResourceName(resources[i].Name)}\", g_resPool_data_{i}, {resources[i].Length} }},");
        w.Close();
        w.Line("};");
        w.Line("inline void il2cpp_resource_pool_fill() {");
        w.Line("    il2cpp_resources_register(g_resPool, g_resPoolCount);");
        w.Line("}");
        w.Line("} // namespace il2cpp");
        return w.ToString();
    }

    // C++ string-literal escaping for resource names. MUST stay byte-identical
    // with CodeGenerator.EscapeC / StringPoolPass.EscapeCppLiteral (the same
    // 5 replacements) so a name round-trips through the generated table and
    // back without divergence.
    private static string EscapeResourceName(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t");

    // Write the resource pool data file into the gen output dir (no-op when
    // the pool is empty). Same convention as StringPoolPass.WritePoolHeader.
    private static void WriteResourcePoolHeader(IReadOnlyList<ManifestResourceDef> resources, string outDir)
    {
        var text = RenderResourcePoolHeader(resources);
        if (text.Length == 0) return;
        var path = Path.Combine(outDir, "include", "internal", "resource-pool.h");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // RES-1 (iron rule 18): load the assembly's embedded manifest resources and
    // emit the resource-pool.h data file. Diagnostic/emission: exceptions are
    // only warned about (never turn a successful transpile into a failure).
    private static void WriteResourcePoolReport(string assemblyPath, string outputDir)
    {
        try
        {
            var resources = MetadataLoader.LoadManifestResources(assemblyPath);
            WriteResourcePoolHeader(resources, outputDir);
            Console.WriteLine(
                $"resources: {resources.Count} embedded manifest resource(s) -> include/internal/resource-pool.h");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"il2cppc warning: resource pool emission skipped: {ex.Message}");
        }
    }

    // String pooling optimization (layers (1)+(2)+(3), string-pooling-design.md
    // section 2.1/section 2.2/section 2.3): Generate already builds the pool authoritatively in its
    // method loop (fold->collect, D1 single writer -- pool and emission share one
    // source). This function consumes Generate's result: it emits the deduped pool
    // as a static table data file (include/internal/string-pool.h --
    // g_strPool[N] + il2cpp_string_pool_fill()) and prints a diagnostic report.
    // Data-file emission + diagnostics: does not change already-generated method
    // bodies (ldstr is already wired by CodeGenerator to a single g_strPool[id]
    // load); exceptions are only warned about (never turn a successful transpile
    // into a failure).
    private static void WriteStringPoolReport(GenerateResult result, string outputDir)
    {
        try
        {
            var pool = result.Pool;
            var deduped = pool.TotalSites - pool.LiteralCount;
            StringPoolPass.WritePoolHeader(pool, outputDir);
            Console.WriteLine(
                $"string pool: {pool.LiteralCount} distinct literal(s) from {pool.TotalSites} site(s) " +
                $"across {result.Methods} method(s) ({result.Folded} constant concat(s) folded, {deduped} duplicate site(s) deduped)");
            if (pool.LiteralCount > 0)
                Console.WriteLine($"string pool table: g_strPool[{pool.LiteralCount}] -> {StringPoolPass.PoolHeaderPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"il2cppc warning: string pool report skipped: {ex.Message}");
        }
    }

    // LICM loop-invariant hoisting (bench gap str_len/object, see the LicmPass.cs
    // header comment): after Generate succeeds, pre-scan the method IR and report how
    // many pure length icalls (string/array Length) are hoistable.
    // Diagnostic output: does not change already-generated method bodies (TRANSFORM
    // hook point shared with CodeGenerator.cs, see docs/audit/bench-regression-check.md
    // section 4.4); pre-scan exceptions are only warned about.
    private static void WriteLicmReport(List<TypeDef> types, string assemblyPath)
    {
        try
        {
            var (hoisted, methods) = LicmPass.ScanAssembly(types, assemblyPath);
            Console.WriteLine($"licm: {hoisted} invariant length read(s) hoistable across {methods} method(s)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"il2cppc warning: licm pre-scan skipped: {ex.Message}");
        }
    }

    // Simple method inlining (bench gap object/string, see the InlinePass.cs header
    // comment): after Generate succeeds, pre-scan the method IR and report how many
    // call sites of small pure functions (body <= 5 statements, no EH, no recursion,
    // int32/void) are inlineable. Diagnostic output: does not change already-generated
    // method bodies (TRANSFORM hook point shared with CodeGenerator.cs, same convention
    // as LicmPass); pre-scan exceptions are only warned about.
    private static void WriteInlineReport(List<TypeDef> types, string assemblyPath)
    {
        try
        {
            var (inlined, methods) = InlinePass.ScanAssembly(types, assemblyPath);
            Console.WriteLine($"inline: {inlined} simple call site(s) inlineable across {methods} method(s)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"il2cppc warning: inline pre-scan skipped: {ex.Message}");
        }
    }

    // Temp slot reuse (stackframe-audit P1-1, see the TempSlotPass.cs header comment):
    // after Generate succeeds, pre-scan the method IR and report how many temp slots
    // per method the Rename transform would compress to (18 -> ~4-6 target). Diagnostic
    // output: does not change already-generated method bodies (TRANSFORM hook point
    // shared with CodeGenerator.cs, same convention as LicmPass/InlinePass); pre-scan
    // exceptions are only warned about.
    private static void WriteTempSlotReport(List<TypeDef> types, string assemblyPath)
    {
        try
        {
            var (before, after, methods) = TempSlotPass.ScanAssembly(types, assemblyPath);
            Console.WriteLine($"tempslot: {before} temp slot(s) -> {after} with reuse across {methods} method(s)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"il2cppc warning: tempslot pre-scan skipped: {ex.Message}");
        }
    }
}
