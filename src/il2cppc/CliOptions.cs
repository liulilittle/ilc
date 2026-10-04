namespace Il2Cpp.Transpiler;

public enum ReflectionGranularity
{
    Members,
    All,
}

public sealed class CliOptions
{
    // S2 (friendly compiler): single shared usage strings. Referenced by the
    // parse-error messages here, the help pre-scan and the audit subcommand in
    // Program.cs. The "usage:" keyword is kept verbatim (existing error text
    // stays byte-stable).
    public const string Usage =
        "usage: il2cppc [--reflection-list <manifest>] [--reflection-granularity all|members] [--log <file>] [--quiet] [--verbose] <input-assembly> <output-dir>";
    public const string AuditUsage = "usage: il2cppc audit-internalcalls <assembly>";

    public string InputAssembly { get; init; } = "";
    public string OutputDir { get; init; } = "";
    public bool ReflectionListProvided { get; init; }
    public IReadOnlyList<string> ReflectionList { get; init; } = Array.Empty<string>();
    public ReflectionGranularity ReflectionGranularity { get; init; } = ReflectionGranularity.Members;
    public string LogPath { get; init; } = "";
    public bool Quiet { get; init; }
    public bool Verbose { get; init; }
}

public sealed class CliParseException : Exception
{
    public CliParseException(string message) : base(message) { }
}

public static class CliParser
{
    public static CliOptions Parse(string[] args)
    {
        string? manifestPath = null;
        var granularity = ReflectionGranularity.Members;
        string logPath = "";
        var quiet = false;
        var verbose = false;
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--reflection-list":
                    if (i + 1 >= args.Length)
                        throw new CliParseException("option --reflection-list requires a manifest file path");
                    manifestPath = args[++i];
                    break;
                case "--reflection-granularity":
                    if (i + 1 >= args.Length)
                        throw new CliParseException("option --reflection-granularity requires a value (all|members)");
                    granularity = ParseGranularity(args[++i]);
                    break;
                case "--log":
                    if (i + 1 >= args.Length)
                        throw new CliParseException("option --log requires a file path");
                    logPath = args[++i];
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                default:
                    positionals.Add(args[i]);
                    break;
            }
        }

        // S6: --quiet and --verbose are mutually exclusive (user misuse -> parse error).
        if (quiet && verbose)
            throw new CliParseException("options --quiet and --verbose are mutually exclusive");

        if (positionals.Count != 2)
            throw new CliParseException(CliOptions.Usage);

        // Reject empty/whitespace positionals: an empty output-dir would make
        // CodeGenerator generate into Path.Combine("", "src") == "src" (the
        // CALLER'S working directory) and silently succeed; an empty
        // input-assembly would fail later with a confusing FileNotFoundException.
        // Both are user misuse -> parse error (exit 1), never a partial generation.
        if (string.IsNullOrWhiteSpace(positionals[0]) || string.IsNullOrWhiteSpace(positionals[1]))
            throw new CliParseException(
                $"input-assembly and output-dir must be non-empty paths ({CliOptions.Usage})");

        return new CliOptions
        {
            InputAssembly = positionals[0],
            OutputDir = positionals[1],
            ReflectionListProvided = manifestPath != null,
            ReflectionList = manifestPath != null ? ReadManifest(manifestPath) : Array.Empty<string>(),
            ReflectionGranularity = granularity,
            LogPath = logPath,
            Quiet = quiet,
            Verbose = verbose,
        };
    }

    private static ReflectionGranularity ParseGranularity(string value) =>
        value.Equals("all", StringComparison.OrdinalIgnoreCase) ? ReflectionGranularity.All
        : value.Equals("members", StringComparison.OrdinalIgnoreCase) ? ReflectionGranularity.Members
        : throw new CliParseException($"invalid --reflection-granularity '{value}' (expected 'all' or 'members')");

    private static IReadOnlyList<string> ReadManifest(string path)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            throw new CliParseException($"cannot read reflection manifest '{path}': {ex.Message}");
        }

        var names = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;
            names.Add(trimmed);
        }
        return names;
    }
}

public static class ReflectionManifest
{
    // When entries is null, writes opts.ReflectionList as-is (behaviour unchanged); the
    // generator consumes the manifest (mode B filtering) and passes the filtered result
    // here, which writes out the filtered effective reflection surface.
    public static void Write(CliOptions opts, IReadOnlyList<string>? entries = null)
    {
        var lines = new List<string>
        {
            "# il2cppc reflection manifest (mode B)",
            $"# granularity: {(opts.ReflectionGranularity == ReflectionGranularity.All ? "all" : "members")}",
        };
        lines.AddRange(entries ?? opts.ReflectionList);
        File.WriteAllLines(Path.Combine(opts.OutputDir, "reflection-manifest.txt"), lines);
    }
}
