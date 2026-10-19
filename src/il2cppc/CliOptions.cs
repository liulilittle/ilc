namespace Il2Cpp.Transpiler;

public enum ReflectionGranularity
{
    Members,
    All,
}

public sealed class CliOptions
{
    // GN-19 (GL): the accepted --arch tokens + the normalized canonical name for
    // each. 11 canonical arches + 2 width aliases (mips32 -> mips, ppc32 -> ppc).
    // Bare "riscv" is NOT accepted (ambiguous width); "wasm" is deliberately not
    // added. The big-endian-capable families (s390x / mips-BE / ppc64-BE) are
    // listed in the help but REJECTED at transpile time (G12 little-endian
    // contract, see ValidateEndianness below) -- never a silent wrong-byte layout.
    private static readonly (string Token, string Canonical)[] ArchTokens =
    {
        ("x86", "x86"), ("x64", "x64"),
        ("arm32", "arm32"), ("arm64", "arm64"),
        ("riscv32", "riscv32"), ("riscv64", "riscv64"),
        ("s390x", "s390x"),
        ("mips", "mips"), ("mips32", "mips"), ("mips64", "mips64"),
        ("ppc", "ppc"), ("ppc32", "ppc"), ("ppc64", "ppc64"),
    };

    public static bool IsValidArch(string token) =>
        ArchTokens.Any(t => string.Equals(t.Token, token, StringComparison.OrdinalIgnoreCase));

    public static string NormalizeArch(string token)
    {
        foreach (var (t, canonical) in ArchTokens)
        {
            if (string.Equals(t, token, StringComparison.OrdinalIgnoreCase))
                return canonical;
        }
        return token.ToLowerInvariant();
    }

    // S2 (friendly compiler): single shared usage strings. Referenced by the
    // parse-error messages here, the help pre-scan and the audit subcommand in
    // Program.cs. The "usage:" keyword is kept verbatim (existing error text
    // stays byte-stable).
    public const string Usage =
        "usage: il2cppc [--arch x86|x64|arm32|arm64] [--reflection-list <manifest>] [--reflection-granularity all|members] [--manifest <file>] [--requested-execution-level asInvoker|highestAvailable|requireAdministrator] [--ui-access] [--log <file>] [--quiet] [--verbose] <input-assembly> <output-dir>";
    public const string AuditUsage = "usage: il2cppc audit-internalcalls <assembly>";

    // GN-19 (GL): multi-section English help. The first line keeps the
    // "usage: il2cppc" prefix that CliTests asserts with Contains() (byte-stable);
    // the single-line Usage/AuditUsage constants above remain the parse-error
    // echo (CliOptions.cs parse paths + Program.cs audit-missing-arg).
    public const string HelpText =
@"usage: il2cppc [--arch <arch>] [--reflection-list <manifest>] [--reflection-granularity all|members] [--manifest <file>] [--requested-execution-level <level>] [--ui-access] [--log <file>] [--quiet] [--verbose] <input-assembly> <output-dir>
usage: il2cppc audit-internalcalls <assembly>
       il2cppc --version | --help | -h

NAME
  il2cppc -- transpiles a .NET Framework 4.8 assembly (IL + metadata) into a
  native C++17 CMake project for a target architecture.

SYNOPSIS
  il2cppc [OPTIONS] <input-assembly> <output-dir>
  il2cppc audit-internalcalls <assembly>
  il2cppc --version | --help | -h

OPTIONS
  --arch <arch>                  target architecture (default: host width).
                                 See ARCHITECTURES for the accepted values.
  --reflection-list <manifest>   mode B reflection manifest (one full type
                                 name per line; '#' comments and blank lines
                                 are ignored).
  --reflection-granularity <g>   all | members (default: members).
  --manifest <file>              embed this Windows application manifest into
                                 the generated executable. Embedded verbatim
                                 when the file is UTF-8; otherwise transcoded to
                                 UTF-8 with its declaration rewritten. Mutually
                                 exclusive with --requested-execution-level.
  --requested-execution-level <l>
                                 asInvoker | highestAvailable |
                                 requireAdministrator -- synthesize the standard
                                 manifest with that requestedExecutionLevel.
  --ui-access                    set uiAccess=""true"" in the generated manifest
                                 (requires --requested-execution-level).
  --log <file>                   tee a copy of stdout + stderr into <file>.
  --quiet                        suppress the stdout diagnostics (errors and
                                 warnings on stderr are unaffected).
  --verbose                      per-type progress to stdout (mutually
                                 exclusive with --quiet).
  -h, --help                     print this help and exit 0.
  --version                      print the transpiler version and exit 0.

SUBCOMMANDS
  help [audit-internalcalls]     print this help (or the audit help).
  audit-internalcalls <assembly> audit the InternalCall/icall wiring of an
                                 assembly (exit 0 on success, 2 on a broken
                                 assembly, 1 when <assembly> is missing).

ARCHITECTURES
  32-bit (pointer size 4): x86, arm32, riscv32, mips, mips32, ppc, ppc32
  64-bit (pointer size 8): x64, arm64, riscv64, mips64, ppc64, s390x
  Aliases: mips32 == mips, ppc32 == ppc (both 4/32-bit).
  A bare 'riscv' is NOT accepted (ambiguous width); 'wasm' is not a target.
  Big-endian targets have NO supported bytes layout yet: s390x (big-endian
  by design) and the explicit big-endian variants (ppc64-BE, mips-BE) are
  rejected at transpile time (G12 little-endian contract).

EXIT CODES
  0  success (also --help/-h/--version, the help subcommand, and a
     successful audit-internalcalls run).
  1  command-line usage error (bad option / arch value / granularity, missing
     manifest, --quiet with --verbose, big-endian target, audit without an
     assembly, unopenable --log path).
  2  transpile failure (metadata load / decode / emission) or an audit run
     whose assembly cannot be read.

EXAMPLES
  il2cppc app.dll gen
  il2cppc --arch arm64 --reflection-list types.txt --log build.log app.dll gen
  il2cppc --arch riscv64 app.dll gen-riscv64
  il2cppc --requested-execution-level requireAdministrator app.dll gen
  il2cppc --manifest app.manifest app.dll gen

WINDOWS APPLICATION MANIFEST
  The generated executable embeds an RT_MANIFEST resource (id 1) when a
  manifest is requested. Sources, in precedence order:
    1. --manifest <file>                (verbatim when UTF-8)
    2. --requested-execution-level <l>  (synthesized, with --ui-access)
    3. the sidecar <input-assembly>.manifest (e.g. App.exe.manifest)
  An explicit source (1 or 2) suppresses the sidecar. A manifest that is not
  UTF-8 is transcoded to UTF-8 and its XML declaration rewritten; an encoding
  this build cannot decode is rejected, never embedded corrupt (a mismatch
  between bytes and declaration makes Windows refuse to start the image).
  uiAccess=""true"" only takes effect for a binary that is Authenticode-signed
  and installed in a secure location -- Windows enforces both; an unsigned
  build requesting it will not start.
  With no manifest source the project emits no .rc file at all, so the
  non-Windows build path is byte-identical to before.

ENVIRONMENT
  IL2CPP_MINGW_ROOT / IL2CPP_MSVC_GENERATOR / IL2CPP_WSL_DISTRO /
  IL2CPP_NDK_ROOT / NDK_ROOT: toolchain discovery used by
  scripts/build-app.ps1 and scripts/run-tests.ps1 (the transpiler itself does
  not read the environment).

NOTES
  * The target pointer width is fixed at 4 or 8 bytes (G1). The generated C++
    carries a static_assert(sizeof(void*) == this width) interlock, so a
    host-width mismatch fails the native build instead of silently drifting.
  * Only little-endian targets are supported (G12 contract); big-endian archs
    are loudly rejected before any code is generated -- never a silent
    wrong-byte output.

";

    public string InputAssembly { get; init; } = "";
    public string OutputDir { get; init; } = "";
    public bool ReflectionListProvided { get; init; }
    public IReadOnlyList<string> ReflectionList { get; init; } = Array.Empty<string>();
    public ReflectionGranularity ReflectionGranularity { get; init; } = ReflectionGranularity.Members;
    public string LogPath { get; init; } = "";
    public bool Quiet { get; init; }
    public bool Verbose { get; init; }

    // G1 (cross-platform arch, root gate): the transpiler's TARGET pointer
    // width. --arch x86/x64/arm32/arm64 normalizes to PointerSize 4|8; when
    // the flag is absent TargetArch is null and PointerSize defaults to the
    // HOST width (IntPtr.Size) -- preserving the historical x64 byte-for-byte
    // output (zero-regression iron rule).
    public string? TargetArch { get; init; }
    public int PointerSize { get; init; } = IntPtr.Size;

    // Windows application manifest (nb10). Three sources, in precedence order:
    //   1. --manifest <file>              -- embed that XML (verbatim when UTF-8)
    //   2. --requested-execution-level    -- synthesize the standard manifest
    //      <asInvoker|highestAvailable|requireAdministrator>
    //   3. the SIDECAR <input-assembly>.manifest (App.exe.manifest), the same
    //      external-manifest convention Windows itself honors
    // ManifestPath is the RESOLVED source ("" = no manifest requested);
    // ManifestIsGenerated marks the synthesized form (needed because the
    // generated text must be written by the generator, not copied).
    public string ManifestPath { get; init; } = "";
    public bool ManifestIsGenerated { get; init; }
    public string RequestedExecutionLevel { get; init; } = "";
    public bool UiAccess { get; init; }

    // nb10 (HIGH-1): the EXACT bytes to embed for a file-sourced manifest,
    // already normalized to a form Windows can parse. An XML declaration's
    // encoding attribute is authoritative for the reader, so a file that is
    // UTF-16/UTF-32 (or a supported 8-bit codepage) must be transcoded AND its
    // declaration rewritten -- otherwise the embedded RT_MANIFEST is declared
    // one encoding while carrying another and CreateProcess fails with
    // SideBySide "Invalid Xml syntax" (event id 59). A file that is already
    // valid UTF-8 is carried through byte-for-byte (verbatim), BOM included.
    public byte[] ManifestBytes { get; init; } = Array.Empty<byte>();
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
        string? appManifestPath = null;
        string requestedExecutionLevel = "";
        var uiAccess = false;
        var granularity = ReflectionGranularity.Members;
        string logPath = "";
        var quiet = false;
        var verbose = false;
        var arch = "";
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--arch":
                    if (i + 1 >= args.Length)
                        throw new CliParseException("option --arch requires a value (x86|x64|arm32|arm64|riscv32|riscv64|s390x|mips|mips64|ppc|ppc64)");
                    arch = ParseArch(args[++i]);
                    break;
                case "--reflection-list":
                    if (i + 1 >= args.Length)
                        throw new CliParseException("option --reflection-list requires a manifest file path");
                    manifestPath = args[++i];
                    break;
                case "--manifest":
                    if (i + 1 >= args.Length)
                        throw new CliParseException("option --manifest requires a Windows application manifest file path");
                    appManifestPath = args[++i];
                    break;
                case "--requested-execution-level":
                    if (i + 1 >= args.Length)
                        throw new CliParseException("option --requested-execution-level requires a value (asInvoker|highestAvailable|requireAdministrator)");
                    requestedExecutionLevel = ParseRequestedExecutionLevel(args[++i]);
                    break;
                case "--ui-access":
                    uiAccess = true;
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

        // nb10: the two manifest sources are alternatives (both describe the
        // SAME artifact); accepting both would silently pick one.
        if (appManifestPath != null && requestedExecutionLevel.Length > 0)
            throw new CliParseException(
                "options --manifest and --requested-execution-level are mutually exclusive "
                + "(an explicit manifest file already carries its requestedExecutionLevel)");
        if (uiAccess && requestedExecutionLevel.Length == 0)
            throw new CliParseException(
                "option --ui-access requires --requested-execution-level "
                + "(the generated manifest is the only place uiAccess is expressible)");

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

        // nb10: resolve the Windows application-manifest source. An explicit
        // --manifest file must exist (same clean-error contract as
        // --reflection-list). Otherwise the sidecar "<assembly>.manifest"
        // (e.g. App.exe.manifest) is picked up automatically -- the very
        // external-manifest convention Windows honors -- so a project that
        // already ships one needs no extra flag.
        var resolvedManifest = "";
        var manifestBytes = Array.Empty<byte>();
        if (appManifestPath != null)
        {
            if (!File.Exists(appManifestPath))
                throw new CliParseException(
                    $"Windows application manifest not found: {appManifestPath}");
            resolvedManifest = appManifestPath;
            manifestBytes = LoadManifestBytes(appManifestPath);
        }
        else if (requestedExecutionLevel.Length == 0)
        {
            var sidecar = positionals[0] + ".manifest";
            if (File.Exists(sidecar))
            {
                resolvedManifest = sidecar;
                manifestBytes = LoadManifestBytes(sidecar);
            }
        }

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
            ManifestPath = resolvedManifest,
            ManifestBytes = manifestBytes,
            ManifestIsGenerated = requestedExecutionLevel.Length > 0,
            RequestedExecutionLevel = requestedExecutionLevel,
            UiAccess = uiAccess,
            // G1: TargetArch == "" when --arch is absent -> null (host width);
            // PointerSize = normalized target width (4|8), host IntPtr.Size default.
            // GN-19: 11-arch PointerSize table (aliases already normalized by
            // ParseArch, so mips32/ppc32 arrive as mips/ppc).
            TargetArch = string.IsNullOrEmpty(arch) ? null : arch,
            PointerSize = arch switch
            {
                "x86" or "arm32" or "riscv32" or "mips" or "ppc" => 4,
                "x64" or "arm64" or "riscv64" or "mips64" or "ppc64" or "s390x" => 8,
                _ => IntPtr.Size,
            },
        };
    }

    // nb10: the three requestedExecutionLevel values net48/Windows accept.
    // Canonical camelCase is returned (case-insensitive input) so the emitted
    // manifest is byte-stable.
    private static string ParseRequestedExecutionLevel(string value) => value.ToLowerInvariant() switch
    {
        "asinvoker" => "asInvoker",
        "highestavailable" => "highestAvailable",
        "requireadministrator" => "requireAdministrator",
        _ => throw new CliParseException(
            $"invalid --requested-execution-level '{value}' (expected one of: asInvoker, highestAvailable, requireAdministrator)"),
    };

    // G1 (cross-platform arch): validate the --arch value (case-insensitive)
    // and return its normalized canonical name (aliases mips32 -> mips, ppc32 ->
    // ppc). An unknown target is user misuse -> parse error (exit 1), never a
    // silent host-width layout. GN-19: 11 canonical + 2 aliases; a bare "riscv"
    // is rejected (ambiguous width); "wasm" is not a target. The explicit
    // big-endian variant spellings (mips-BE / ppc64-BE) are recognized here and
    // rejected loudly (they must NOT fall through to the generic invalid-arch
    // path -- the user should be told the arch is big-endian, not unknown).
    private static string ParseArch(string value)
    {
        var lower = value.ToLowerInvariant();
        var canonical = CliOptions.NormalizeArch(lower);
        if (!CliOptions.IsValidArch(lower))
        {
            // Explicit big-endian variant: <base>-BE where <base> is a be-capable
            // family (mips/ppc64/...). Recognized -> LOUD endianness rejection.
            if (lower.EndsWith("-be", StringComparison.Ordinal)
                && CliOptions.IsValidArch(lower[..^3]))
                throw BigEndianRejection(lower);

            throw new CliParseException(
                $"invalid --arch '{value}' (expected one of: x86, x64, arm32, arm64, riscv32, riscv64, s390x, mips, mips64, ppc, ppc64; aliases mips32->mips, ppc32->ppc)");
        }
        return canonical;
    }

    private static CliParseException BigEndianRejection(string arch) => new(
        $"big-endian target not supported ({arch}) (G12 little-endian contract); "
        + "rebuild for the matching little-endian target (--arch)");

// GN-19 (GL, AOT absolute standard): the arch canonical names that are
    // big-endian BY DESIGN (no little-endian interpretation) must be loudly
    // rejected before any code generation -- the pointer-size table above gives
    // them a width (so the layout machinery stays 4|8 typed), but transpiling
    // them would silently emit big-endian byte data. This throws CliParseException
    // (the parse-error channel) -> exit 1, the same path as an invalid --arch.
    // The runtime keeps its compile-time __BYTE_ORDER__ #error as defense-in-depth
    // (bitconverter.cpp / encoding.cpp, unchanged).
    //
    // Called by Program.RunMain right after CliParser.Parse (s390x parses fine to
    // PointerSize 8 -- the arch Theory needs that -- but Generate must never see a
    // big-endian target). That keeps Parse itself pure table+mapping so the
    // Arch_Value theory stays green while the generate path loudly rejects.
    public static void ValidateEndianness(string? arch)
    {
        if (string.Equals(arch, "s390x", StringComparison.OrdinalIgnoreCase))
            throw BigEndianRejection(arch!);
    }

    private static ReflectionGranularity ParseGranularity(string value) =>
        value.Equals("all", StringComparison.OrdinalIgnoreCase) ? ReflectionGranularity.All
        : value.Equals("members", StringComparison.OrdinalIgnoreCase) ? ReflectionGranularity.Members
        : throw new CliParseException($"invalid --reflection-granularity '{value}' (expected 'all' or 'members')");

    // nb10 (HIGH-1): normalize a file-sourced Windows application manifest to
    // bytes Windows can actually parse, without changing its meaning.
    //
    // An XML reader honours the declaration's `encoding` pseudo-attribute, and
    // Windows fusion is stricter still: it accepts ONLY the hyphenated spelling
    // "UTF-8" for UTF-8. Getting this wrong is not cosmetic -- the embedded
    // RT_MANIFEST is rejected at CreateProcess time with SideBySide "Invalid Xml
    // syntax" (event id 59) and the produced executable never starts.
    //
    //   BOM (UTF-8/16/32)     -> decode with the BOM, rewrite the declaration
    //                            to utf-8; a UTF-8 BOM is preserved
    //   BOM-less UTF-16/32    -> detected from the XML prolog byte pattern,
    //                            then the same transcode
    //   declaration "utf-8"   -> bytes must be valid UTF-8, carried verbatim
    //   no declaration        -> bytes must be valid UTF-8, carried verbatim
    //   "utf8" alias          -> valid UTF-8, declaration rewritten
    //   "ascii"/"us-ascii"    -> all-ASCII bytes only, declaration rewritten
    //   any other name        -> ambiguous (GBK "一" = D2 BB is ALSO valid
    //                            UTF-8), so a valid-UTF-8 body is REFUSED rather
    //                            than silently reinterpreted; a known 8-bit
    //                            encoding is transcoded only when the bytes are
    //                            not valid UTF-8; everything else is an error.
    //
    // A NUL byte without a recognizable wide encoding is always an error: NUL is
    // legal UTF-8, so without this check a BOM-less UTF-16 file would pass the
    // UTF-8 test and be embedded as garbage.
    private static byte[] LoadManifestBytes(string path)
    {
        var bytes = NormalizeManifestBytes(path);

        // Encoding consistency is necessary but not sufficient: a manifest whose
        // XML is malformed (missing "?>", unclosed element, ...) builds fine and
        // then refuses to start with SideBySide "Invalid Xml syntax". Rejecting
        // it here turns a runtime-only failure into a parse error. DTDs are
        // prohibited: a manifest needs none, and allowing them would make a
        // build-time parser fetch/expand attacker-controlled content.
        var offset = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
        var text = System.Text.Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        var settings = new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        try
        {
            using var reader = System.Xml.XmlReader.Create(new StringReader(text), settings);
            while (reader.Read()) { }
        }
        catch (System.Xml.XmlException ex)
        {
            throw new CliParseException(
                $"Windows application manifest '{path}' is not well-formed XML: {ex.Message}");
        }
        return bytes;
    }

    // Encoding normalization only (see LoadManifestBytes for the contract).
    private static byte[] NormalizeManifestBytes(string path)
    {
        byte[] raw;
        try
        {
            raw = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CliParseException(
                $"cannot read Windows application manifest '{path}': {ex.Message}");
        }
        if (raw.Length == 0)
            throw new CliParseException($"Windows application manifest is empty: {path}");

        var strictUtf8 = new System.Text.UTF8Encoding(false, true);
        var bom = SniffBom(raw, out var bomLength);

        // 1. A BOM is authoritative: decode with it, then make the declaration
        //    truthful. A UTF-8 BOM stays in place so a well-formed file is
        //    reproduced byte-for-byte.
        if (bom != null)
        {
            string? text = null;
            try { text = bom.GetString(raw, bomLength, raw.Length - bomLength); }
            catch (System.Text.DecoderFallbackException) { }
            if (text == null)
                throw new CliParseException(
                    $"cannot decode Windows application manifest '{path}' (re-save it as UTF-8)");
            if (text.Length == 0)
                throw new CliParseException($"Windows application manifest is empty: {path}");
            var converted = System.Text.Encoding.UTF8.GetBytes(RewriteDeclaration(text));
            return bom is System.Text.UTF8Encoding ? PrependUtf8Bom(converted) : converted;
        }

        // 2. No BOM: the XML prolog's first four bytes identify UTF-16/UTF-32.
        var wide = SniffBomlessWideEncoding(raw);
        if (wide != null)
        {
            string? text = null;
            try { text = wide.GetString(raw); }
            catch (System.Text.DecoderFallbackException) { }
            if (text == null)
                throw new CliParseException(
                    $"cannot decode Windows application manifest '{path}' (re-save it as UTF-8)");
            if (text.Length == 0)
                throw new CliParseException($"Windows application manifest is empty: {path}");
            return System.Text.Encoding.UTF8.GetBytes(RewriteDeclaration(text));
        }

        // 3. A NUL byte here means the file is not ASCII-compatible text and we
        //    could not identify its encoding.
        if (Array.IndexOf(raw, (byte)0) >= 0)
            throw new CliParseException(
                $"Windows application manifest '{path}' contains NUL bytes but no recognizable "
                + "UTF-16/UTF-32 encoding (re-save it as UTF-8)");

        var declared = SniffDeclaredEncoding(raw);
        var validUtf8 = IsValidUtf8(strictUtf8, raw);

        if (declared == null)
        {
            if (!validUtf8)
                throw new CliParseException(
                    $"Windows application manifest '{path}' is not valid UTF-8 and declares no "
                    + "XML encoding (re-save it as UTF-8)");
            return raw;
        }

        if (IsExactUtf8Name(declared))
        {
            if (!validUtf8)
                throw new CliParseException(
                    $"Windows application manifest '{path}' declares encoding 'utf-8' but the "
                    + "bytes are not valid UTF-8 (re-save it as UTF-8)");
            return raw;
        }

        if (IsUtf8Alias(declared))
        {
            if (!validUtf8)
                throw new CliParseException(
                    $"Windows application manifest '{path}' declares encoding '{declared}' but "
                    + "the bytes are not valid UTF-8 (re-save it as UTF-8)");
            return System.Text.Encoding.UTF8.GetBytes(
                RewriteDeclaration(strictUtf8.GetString(raw)));
        }

        if (IsAsciiName(declared))
        {
            if (!IsAllAscii(raw))
                throw new CliParseException(
                    $"Windows application manifest '{path}' declares encoding '{declared}' but "
                    + "contains non-ASCII bytes (re-save it as UTF-8)");
            return System.Text.Encoding.UTF8.GetBytes(
                RewriteDeclaration(System.Text.Encoding.ASCII.GetString(raw)));
        }

        // 4. A pure-ASCII body is unambiguous for every ASCII-compatible
        //    declaration (ASCII and Latin-1 share 0x00-0x7F), so a legacy
        //    "iso-8859-1" label on an all-ASCII manifest is relabelled rather
        //    than refused.
        if (IsAllAscii(raw) && IsAsciiCompatibleName(declared))
            return System.Text.Encoding.UTF8.GetBytes(
                RewriteDeclaration(System.Text.Encoding.ASCII.GetString(raw)));

        // 5. Any other declared name. Valid-UTF-8 bytes are ambiguous: they can
        //    be a correctly-labelled legacy file or a UTF-8 file with a stale
        //    declaration. Refusing is the only lossless answer.
        if (validUtf8)
            throw new CliParseException(
                $"Windows application manifest '{path}' declares encoding '{declared}' but its "
                + "bytes are also valid UTF-8; refusing to guess (re-save it as UTF-8, or fix "
                + "the declaration)");

        var enc = TryGetBuiltinEncoding(declared);
        if (enc == null)
            throw new CliParseException(
                $"Windows application manifest '{path}' declares encoding '{declared}', which "
                + "this build cannot decode (re-save it as UTF-8)");
        string? decoded = null;
        try { decoded = enc.GetString(raw); }
        catch (System.Text.DecoderFallbackException) { }
        if (decoded == null)
            throw new CliParseException(
                $"cannot decode Windows application manifest '{path}' (re-save it as UTF-8)");
        return System.Text.Encoding.UTF8.GetBytes(RewriteDeclaration(decoded));
    }

    private static byte[] PrependUtf8Bom(byte[] bytes)
    {
        var withBom = new byte[bytes.Length + 3];
        withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
        bytes.CopyTo(withBom, 3);
        return withBom;
    }

    // A BOM-less wide encoding is identified by the XML prolog's first four
    // bytes: '<' (0x3C) is the only high byte, and the zero bytes carry the
    // byte order. UTF-32 is probed first because it shares 3C 00 with UTF-16LE.
    private static System.Text.Encoding? SniffBomlessWideEncoding(byte[] raw)
    {
        if (raw.Length < 4) return null;
        if (raw[0] == 0x3C && raw[1] == 0x00 && raw[2] == 0x00 && raw[3] == 0x00)
            return new System.Text.UTF32Encoding(false, false);
        if (raw[0] == 0x00 && raw[1] == 0x00 && raw[2] == 0x00 && raw[3] == 0x3C)
            return new System.Text.UTF32Encoding(true, false);
        if (raw[0] == 0x3C && raw[1] == 0x00 && raw[2] == 0x3F && raw[3] == 0x00)
            return new System.Text.UnicodeEncoding(false, false);
        if (raw[0] == 0x00 && raw[1] == 0x3C && raw[2] == 0x00 && raw[3] == 0x3F)
            return new System.Text.UnicodeEncoding(true, false);
        return null;
    }

    private static bool IsAllAscii(byte[] raw)
    {
        foreach (var b in raw)
            if (b >= 0x80) return false;
        return true;
    }

    // Byte-order mark -> the encoding it announces, plus its byte length.
    // UTF-32 is probed before UTF-16 (FF FE 00 00 also matches FF FE).
    private static System.Text.Encoding? SniffBom(byte[] raw, out int length)
    {
        length = 0;
        if (raw.Length >= 4 && raw[0] == 0xFF && raw[1] == 0xFE && raw[2] == 0x00 && raw[3] == 0x00)
        { length = 4; return new System.Text.UTF32Encoding(false, true); }
        if (raw.Length >= 4 && raw[0] == 0x00 && raw[1] == 0x00 && raw[2] == 0xFE && raw[3] == 0xFF)
        { length = 4; return new System.Text.UTF32Encoding(true, true); }
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        { length = 3; return new System.Text.UTF8Encoding(false); }
        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
        { length = 2; return new System.Text.UnicodeEncoding(false, false); }
        if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
        { length = 2; return new System.Text.UnicodeEncoding(true, false); }
        return null;
    }

    private static bool IsValidUtf8(System.Text.Encoding strictUtf8, byte[] bytes)
    {
        try { strictUtf8.GetString(bytes); return true; }
        catch (System.Text.DecoderFallbackException) { return false; }
    }

    // Windows fusion accepts ONLY "UTF-8" (case-insensitive, with the hyphen).
    // A declaration spelled "utf8" is valid XML but is rejected by the loader,
    // so it must be rewritten even though the bytes are already UTF-8.
    private static bool IsExactUtf8Name(string name) =>
        name.Equals("utf-8", StringComparison.OrdinalIgnoreCase);

    private static bool IsUtf8Alias(string name) =>
        name.Equals("utf8", StringComparison.OrdinalIgnoreCase);

    // "us-ascii"/"ascii" name the same byte range as UTF-8 for values < 0x80,
    // so an all-ASCII body can be relabelled losslessly.
    private static bool IsAsciiName(string name) =>
        name.Equals("us-ascii", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ascii", StringComparison.OrdinalIgnoreCase);

    // Encodings whose 0x00-0x7F range is ASCII: an all-ASCII body means the
    // same thing under any of them, so the label can be corrected losslessly.
    private static bool IsAsciiCompatibleName(string name) =>
        IsAsciiName(name)
        || name.Equals("iso-8859-1", StringComparison.OrdinalIgnoreCase)
        || name.Equals("latin1", StringComparison.OrdinalIgnoreCase)
        || name.Equals("latin-1", StringComparison.OrdinalIgnoreCase);

    // The 8-bit encoding this tool can transcode WITHOUT a codepage provider
    // (il2cppc does not reference System.Text.Encoding.CodePages).
    //
    // Wide encodings (utf-16/utf-32) are deliberately ABSENT: a real wide file
    // is already identified by its BOM or by the XML prolog's byte pattern, so
    // reaching this fallback with a wide declaration means the declaration and
    // the bytes contradict each other. Decoding those bytes as UTF-16 would
    // produce mojibake that is then embedded as a valid-looking manifest, which
    // is exactly the silent-corruption failure this code exists to prevent.
    private static System.Text.Encoding? TryGetBuiltinEncoding(string name) =>
        name.ToLowerInvariant() switch
        {
            "iso-8859-1" or "latin1" or "latin-1" => System.Text.Encoding.Latin1,
            _ => null,
        };

    // The `encoding` pseudo-attribute of the XML DECLARATION (an `encoding`
    // elsewhere is not a reader directive). The declaration itself is pure
    // ASCII in every encoding handled here, so a Latin-1 window is a safe view.
    private static string? SniffDeclaredEncoding(byte[] raw)
    {
        var window = System.Text.Encoding.Latin1.GetString(raw, 0, Math.Min(raw.Length, 512));
        var open = window.IndexOf("<?xml", StringComparison.Ordinal);
        if (open < 0) return null;
        var close = window.IndexOf("?>", open, StringComparison.Ordinal);
        if (close < 0) return null;
        var decl = window.Substring(open, close - open);
        return ReadEncodingValue(decl);
    }

    // Replace the declaration's encoding value with utf-8. A declaration with no
    // encoding attribute needs no rewrite: XML's default is UTF-8/UTF-16
    // auto-detection, which the transcoded bytes already satisfy.
    private static string RewriteDeclaration(string text)
    {
        var open = text.IndexOf("<?xml", StringComparison.Ordinal);
        if (open < 0) return text;
        var close = text.IndexOf("?>", open, StringComparison.Ordinal);
        if (close < 0) return text;
        var decl = text.Substring(open, close - open);
        if (ReadEncodingValue(decl) == null) return text;

        var at = decl.IndexOf("encoding", StringComparison.OrdinalIgnoreCase) + "encoding".Length;
        while (at < decl.Length && (decl[at] == ' ' || decl[at] == '\t')) at++;
        at++; // '='
        while (at < decl.Length && (decl[at] == ' ' || decl[at] == '\t')) at++;
        var quote = decl[at];
        var start = at + 1;
        var end = decl.IndexOf(quote, start);
        if (end < 0) return text;
        return text[..(open + start)] + "utf-8" + text[(open + end)..];
    }

    private static string? ReadEncodingValue(string decl)
    {
        var at = decl.IndexOf("encoding", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        at += "encoding".Length;
        while (at < decl.Length && (decl[at] == ' ' || decl[at] == '\t')) at++;
        if (at >= decl.Length || decl[at] != '=') return null;
        at++;
        while (at < decl.Length && (decl[at] == ' ' || decl[at] == '\t')) at++;
        if (at >= decl.Length || (decl[at] != '"' && decl[at] != '\'')) return null;
        var quote = decl[at];
        var start = at + 1;
        var end = decl.IndexOf(quote, start);
        if (end < 0) return null;
        return decl.Substring(start, end - start);
    }

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
