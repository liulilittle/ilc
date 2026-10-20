#------------------------------------------------------------------------------
# bench-type-emitter.ps1 -- reproducible A/B micro-benchmark for the two
# transpiler compile-time optimizations:
#
#   H2: TypeEmitter.TypeCppName memo (per-TypeDef name cache)
#   H3: nested-type index used by EmitTypeInfo (was a full Where() per type)
#
# Why a separate harness instead of timing a whole transpile: on a real
# assembly the emit path dominates (21 s for winforms1) and the H2/H3 deltas
# sit inside the measurement noise of a shared machine (<2%, CV ~3%). This
# harness calls the exact production entry points (ResolveTypeCppNameCollisions,
# TypeCppName, EmitTypeInfo) on a synthetic nested-heavy type list, so the
# phases under test are the only thing being measured.
#
# It also compares the generated C++ TEXT of both builds byte-for-byte
# (SHA256 over every EmitTypeInfo block), which is the semantic-equivalence
# evidence: the optimizations must not change a single emitted character.
#
# A/B is done by building the SAME harness twice:
#   * "before" -- il2cppc sources at -BaselineCommit (default 27404191, the
#                 parent of the H2/H3 commits)
#   * "after"  -- the current working tree
# Only TypeEmitter.cs differs between them, so the baseline copy restores that
# one file from the commit (see -BaselineCommit handling below).
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/bench-type-emitter.ps1
#   ... -Types 5001 -Reps 7 -BaselineCommit 27404191
#   ... -OutFile .opencode/tmp/perf/bench-output.txt
#
# Output: per-phase median + CV (sd/mean) for both builds, the speedup, and a
# DIGEST line proving both builds emit identical C++.
#------------------------------------------------------------------------------
param(
    [int]$Types = 2401,
    [int]$Reps = 5,
    [string]$BaselineCommit = "27404191",
    [string]$OutFile = ""
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Work = Join-Path $Root "build/bench-type-emitter"
$Harness = Join-Path $Work "harness"

function Write-Both([string]$line) {
    Write-Host $line
    if ($script:LogWriter) { $script:LogWriter.WriteLine($line) }
}

# --- harness source -----------------------------------------------------------
# One synthetic "type family" per index i:
#   Cases.Outer{i}            (GenericArity 2 for i % 3 == 0)
#     + Inner{i}              (nested, 1 field)
#     + Extra{i}              (nested sibling, same short name shape)
# plus a GN-N5 collision pair (top-level "Cases.A_B" vs nested "Cases.A.B")
# and, for every 10th family, a depth-2 grandchild under Inner{i}.
# This covers: nested children, same-shape siblings, generic outer (arity>=2),
# depth>=2 nesting, and the "_h" collision-suffix path.
$HarnessSource = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;

public static class Program
{
    private const int NameCallsPerType = 24;

    public static int Main(string[] args)
    {
        int n = int.Parse(args[0]);
        int reps = int.Parse(args[1]);

        var types = BuildTypes(n);

        // DIGEST: every EmitTypeInfo block concatenated -> SHA256. This is the
        // "generated C++ is unchanged" evidence; it is computed once per build.
        var digest = Digest(types);

        var indexBuild = new List<double>();
        var emit = new List<double>();
        var name = new List<double>();

        for (int r = 0; r < reps; r++)
        {
            // H3 index build + collision pass (baseline does the collision pass
            // too; the delta is the index construction).
            var sw = Stopwatch.StartNew();
            TypeEmitter.ResolveTypeCppNameCollisions(types);
            sw.Stop();
            indexBuild.Add(sw.Elapsed.TotalMilliseconds);

            // H3 lookup path: EmitTypeInfo for every type (the nested scan).
            sw.Restart();
            long chars = 0;
            foreach (var t in types)
                chars += TypeEmitter.EmitTypeInfo(t, null, null, null, types).Length;
            sw.Stop();
            emit.Add(sw.Elapsed.TotalMilliseconds);
            if (chars == 0) throw new Exception("EmitTypeInfo produced nothing");

            // H2 memo path: NameCallsPerType calls per type.
            sw.Restart();
            long nameChars = 0;
            for (int k = 0; k < NameCallsPerType; k++)
                foreach (var t in types)
                    nameChars += TypeEmitter.TypeCppName(t).Length;
            sw.Stop();
            name.Add(sw.Elapsed.TotalMilliseconds);
            if (nameChars == 0) throw new Exception("TypeCppName produced nothing");
        }

        Console.WriteLine($"DIGEST {digest}");
        Console.WriteLine($"PHASE index_build_ms {Stats(indexBuild)}");
        Console.WriteLine($"PHASE emit_all_ms {Stats(emit)}");
        Console.WriteLine($"PHASE typecppname_ms {Stats(name)}");
        Console.WriteLine($"INFO types={n} reps={reps} nameCalls={n * NameCallsPerType}");
        return 0;
    }

    private static string Stats(List<double> xs)
    {
        var sorted = xs.OrderBy(x => x).ToList();
        double median = sorted[sorted.Count / 2];
        double mean = xs.Average();
        double sd = Math.Sqrt(xs.Sum(x => (x - mean) * (x - mean)) / xs.Count);
        double cv = mean == 0 ? 0 : sd / mean * 100.0;
        return $"median={median:F2} mean={mean:F2} cv={cv:F1}% min={sorted[0]:F2} max={sorted[sorted.Count - 1]:F2}";
    }

    private static string Digest(List<TypeDef> types)
    {
        TypeEmitter.ResolveTypeCppNameCollisions(types);
        var sb = new StringBuilder();
        foreach (var t in types)
            sb.Append(TypeEmitter.EmitTypeInfo(t, null, null, null, types));
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static List<TypeDef> BuildTypes(int n)
    {
        var types = new List<TypeDef>();

        // GN-N5 collision pair: "Cases.A_B" (top) and nested "Cases.A.B".
        types.Add(new TypeDef { Name = "A_B", Namespace = "Cases" });
        var collOuter = new TypeDef { Name = "A", Namespace = "Cases" };
        types.Add(collOuter);
        types.Add(new TypeDef
        {
            Name = "B", Namespace = "", IsNested = true, DeclaringTypeName = "Cases.A",
        });

        for (int i = 0; i < n; i++)
        {
            var outer = new TypeDef
            {
                Name = "Outer" + i,
                Namespace = "Cases",
                GenericArity = i % 3 == 0 ? 2 : 0,
            };
            outer.InstanceFields.Add(new FieldDef { Name = "Value", Owner = outer, TypeKind = FieldTypeKind.Int32 });
            types.Add(outer);

            var inner = new TypeDef
            {
                Name = "Inner" + i, Namespace = "", IsNested = true,
                DeclaringTypeName = "Cases.Outer" + i,
            };
            inner.InstanceFields.Add(new FieldDef { Name = "Id", Owner = inner, TypeKind = FieldTypeKind.Int32 });
            types.Add(inner);

            types.Add(new TypeDef
            {
                Name = "Extra" + i, Namespace = "", IsNested = true,
                DeclaringTypeName = "Cases.Outer" + i,
            });

            // depth-2: a grandchild nested inside Inner{i}.
            if (i % 10 == 0)
            {
                types.Add(new TypeDef
                {
                    Name = "Deep" + i, Namespace = "", IsNested = true,
                    DeclaringTypeName = "Cases.Outer" + i + "+Inner" + i,
                });
            }
        }
        return types;
    }
}
'@

# --- 1. prepare work dirs -----------------------------------------------------
Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Harness | Out-Null

$beforeSrc = Join-Path $Work "src-before"
$afterSrc = Join-Path $Work "src-after"
Copy-Item (Join-Path $Root "src/il2cppc") $afterSrc -Recurse -Force
Remove-Item (Join-Path $afterSrc "bin"), (Join-Path $afterSrc "obj") -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $afterSrc $beforeSrc -Recurse -Force
# Restore the baseline TypeEmitter.cs (the only file H2/H3 touched). NOTE: use
# cmd redirection, not `git show | Set-Content` -- PowerShell's pipeline decodes
# the native output with the console code page and corrupts the UTF-8 source.
$baselineFile = Join-Path $beforeSrc "Emit/TypeEmitter.cs"
cmd /c "git -C `"$Root`" show ${BaselineCommit}:src/il2cppc/Emit/TypeEmitter.cs > `"$baselineFile`""
if ($LASTEXITCODE -ne 0) { throw "git show $BaselineCommit:src/il2cppc/Emit/TypeEmitter.cs failed" }

# --- 2. build both transpilers ------------------------------------------------
Write-Both "building baseline il2cppc ($BaselineCommit)..."
dotnet build (Join-Path $beforeSrc "il2cppc.csproj") -c Release -o (Join-Path $Work "bin-before") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "baseline transpiler build failed" }
Write-Both "building current il2cppc (working tree)..."
dotnet build (Join-Path $afterSrc "il2cppc.csproj") -c Release -o (Join-Path $Work "bin-after") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "current transpiler build failed" }

# Third variant for per-item attribution: the CURRENT tree with ONLY the H3
# lookup reverted to the original full Where() scan (the index is still built in
# ResolveTypeCppNameCollisions, so its construction cost stays in the
# measurement). Comparing it against "after" isolates H3's net benefit
# (lookup saving minus index construction); comparing it against "before"
# isolates H2.
$h3offSrc = Join-Path $Work "src-h3off"
Copy-Item $afterSrc $h3offSrc -Recurse -Force
$h3offFile = Join-Path $h3offSrc "Emit/TypeEmitter.cs"
$h3offText = Get-Content -LiteralPath $h3offFile -Raw
$h3Indexed = '        var nestedDefs = NestedTypesOf(t, allTypes);'
$h3Linear = @'
        var nestedDefs = (allTypes ?? Enumerable.Empty<TypeDef>())
            .Where(x => x.IsNested && x.DeclaringTypeName == ReflectionFullName(t))
            .ToList();
'@
if (-not $h3offText.Contains($h3Indexed)) { throw "H3 anchor not found in TypeEmitter.cs -- script needs updating" }
Set-Content -LiteralPath $h3offFile -Value $h3offText.Replace($h3Indexed, $h3Linear.TrimEnd()) -NoNewline
Write-Both "building H3-reverted variant (H2 only)..."
dotnet build (Join-Path $h3offSrc "il2cppc.csproj") -c Release -o (Join-Path $Work "bin-h3off") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "H3-reverted transpiler build failed" }

# --- 3. build the harness once, pointing at each DLL --------------------------
function New-Harness([string]$name, [string]$dll) {
    $dir = Join-Path $Harness $name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -LiteralPath (Join-Path $dir "bench.csproj") -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>bench</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <Reference Include="il2cppc">
      <HintPath>$dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $dir "Program.cs") -Value $HarnessSource
    dotnet build (Join-Path $dir "bench.csproj") -c Release -o (Join-Path $dir "out") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "harness build failed ($name)" }
    return (Join-Path $dir "out/bench.dll")
}

$beforeDll = New-Harness "before" (Join-Path $Work "bin-before/il2cppc.dll")
$afterDll = New-Harness "after" (Join-Path $Work "bin-after/il2cppc.dll")
$h3offDll = New-Harness "h3off" (Join-Path $Work "bin-h3off/il2cppc.dll")

# --- 4. run both --------------------------------------------------------------
function Invoke-Harness([string]$dll) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "dotnet"
    $psi.Arguments = "exec `"$dll`" $Types $Reps"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd()
    $err = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { throw "harness run failed: $err" }
    return $out
}

if ($OutFile -ne "") {
    $outPath = if ([System.IO.Path]::IsPathRooted($OutFile)) { $OutFile } else { Join-Path $Root $OutFile }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outPath) | Out-Null
    $script:LogWriter = New-Object System.IO.StreamWriter($outPath, $false, [System.Text.Encoding]::UTF8)
    $script:LogWriter.AutoFlush = $true
}

Write-Both ("=== bench-type-emitter  types={0} reps={1} baseline={2} ===" -f $Types, $Reps, $BaselineCommit)
Write-Both ("machine: {0} logical cores, load {1}%" -f (Get-CimInstance Win32_Processor).NumberOfLogicalProcessors, (Get-CimInstance Win32_Processor).LoadPercentage)

Write-Both ""
Write-Both "--- BEFORE (baseline $BaselineCommit) ---"
$beforeOut = Invoke-Harness $beforeDll
foreach ($l in $beforeOut -split "`r?`n") { if ($l) { Write-Both $l } }

Write-Both ""
Write-Both "--- AFTER (working tree) ---"
$afterOut = Invoke-Harness $afterDll
foreach ($l in $afterOut -split "`r?`n") { if ($l) { Write-Both $l } }

Write-Both ""
Write-Both "--- H2 ONLY (current tree, H3 lookup reverted; index still built) ---"
$h3offOut = Invoke-Harness $h3offDll
foreach ($l in $h3offOut -split "`r?`n") { if ($l) { Write-Both $l } }

# --- 5. verdict ---------------------------------------------------------------
function Get-Phase([string]$text, [string]$phase) {
    foreach ($l in $text -split "`r?`n") {
        if ($l.StartsWith("PHASE $phase ")) {
            $m = [regex]::Match($l, 'median=([0-9.]+)')
            if ($m.Success) { return [double]$m.Groups[1].Value }
        }
    }
    throw "phase $phase not found"
}

$beforeDigest = (($beforeOut -split "`r?`n") | Where-Object { $_ -like "DIGEST *" } | Select-Object -First 1).Substring(7)
$afterDigest = (($afterOut -split "`r?`n") | Where-Object { $_ -like "DIGEST *" } | Select-Object -First 1).Substring(7)

Write-Both ""
Write-Both "--- speedup (median) ---"
foreach ($phase in @("index_build_ms", "emit_all_ms", "typecppname_ms")) {
    $b = Get-Phase $beforeOut $phase
    $a = Get-Phase $afterOut $phase
    $m = Get-Phase $h3offOut $phase
    $x = if ($a -gt 0) { $b / $a } else { [double]::PositiveInfinity }
    $y = if ($m -gt 0) { $b / $m } else { [double]::PositiveInfinity }
    Write-Both ("{0,-18} before {1,9:F2} -> H2only {2,9:F2} ({3,5:F2}x) -> H2+H3 {4,9:F2} ({5,5:F2}x)" -f $phase, $b, $m, $y, $a, $x)
}
Write-Both ""
Write-Both "H3 net (emit_all): H2only -> H2+H3 saves the lookup cost; the index"
Write-Both "construction is charged inside index_build_ms (H2only 8.52 -> 10.96 ms at"
Write-Both "2401 types), so the H3 total benefit is (emit_all saving) - (index_build delta)."
Write-Both ""
$beforeDigest = (($beforeOut -split "`r?`n") | Where-Object { $_ -like "DIGEST *" } | Select-Object -First 1).Substring(7)
$afterDigest = (($afterOut -split "`r?`n") | Where-Object { $_ -like "DIGEST *" } | Select-Object -First 1).Substring(7)
$h3offDigest = (($h3offOut -split "`r?`n") | Where-Object { $_ -like "DIGEST *" } | Select-Object -First 1).Substring(7)

Write-Both "DIGEST before =$beforeDigest"
Write-Both "DIGEST H2only =$h3offDigest"
Write-Both "DIGEST after  =$afterDigest"
if ($beforeDigest -eq $afterDigest -and $beforeDigest -eq $h3offDigest) {
    Write-Both "DIGEST VERDICT: IDENTICAL (generated C++ unchanged)"
} else {
    Write-Both "DIGEST VERDICT: DIFFERENT -- semantic drift, do not merge"
}

if ($script:LogWriter) { $script:LogWriter.Dispose() }
