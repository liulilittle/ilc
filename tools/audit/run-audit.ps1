<#
.SYNOPSIS
  One-shot driver for the net48 mscorlib InternalCall audit.

.DESCRIPTION
  Runs the whole pipeline and leaves every artifact in -OutDir:

    1. enumerate-icall-reflection.ps1   (channel 1: runtime reflection, PS 5.1)
    2. MetadataIcallCounter             (channel 2: raw ECMA-335 metadata)
    3. cross-check                      (the two channel counts must be equal)
    4. IcallCallGraph                   (IL call graph + public-face reachability)
    5. IcallWiringProbe                 (real MethodRefResolver.BclResolve calls)
    6. parse_runtime_syms.py            (runtime decl/def/loud-stub index)
    7. parse_wiring_surface.py          (MethodMap.*.cs dispatch arms)
    8. locate_wiring_sites.py           (source line behind every decision)
    9. classify_icalls.py               (final status + P0..P3 + evidence)

  Each stage is independent and re-runnable; the driver is only a convenience
  so the report can be reproduced with one command.

  Requirements: Windows PowerShell 5.1 for stage 1 (it LoadFrom()s the
  Framework mscorlib), .NET SDK 8/9 for the C# tools, Python 3 for the analysis
  stages.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/run-audit.ps1

.EXAMPLE
  pwsh tools/audit/run-audit.ps1 -OutDir C:\tmp\icall -SkipBuild
#>
[CmdletBinding()]
param(
    [string] $MscorlibPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll',
    [string] $OutDir = '',
    [string] $Config = 'Debug',
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Tools = Join-Path $Root 'tools\audit'

if (-not $OutDir) {
    $OutDir = Join-Path ([System.IO.Path]::GetTempPath()) 'opencode\icall-audit'
}
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Step([string] $name) {
    Write-Host ''
    Write-Host "=== $name ===" -ForegroundColor Cyan
}

function Need([string] $path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "missing input: $path" }
}

Need $MscorlibPath

# --- 1. reflection channel (must run under Windows PowerShell 5.1) -----------
Step 'channel 1: reflection (Windows PowerShell 5.1)'
$reflOut = Join-Path $OutDir 'reflection.tsv'
$ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $ps51 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Tools 'enumerate-icall-reflection.ps1') `
    -MscorlibPath $MscorlibPath -OutFile $reflOut
if ($LASTEXITCODE -ne 0) { throw 'reflection channel failed' }

# --- 2/4/5. build the C# tools ----------------------------------------------
if (-not $SkipBuild) {
    Step 'build: MetadataIcallCounter / IcallCallGraph / IcallWiringProbe'
    $projects = @(
        @{ Name = 'meta';  Proj = 'MetadataIcallCounter\MetadataIcallCounter.csproj' },
        @{ Name = 'graph'; Proj = 'IcallCallGraph\IcallCallGraph.csproj' },
        @{ Name = 'probe'; Proj = 'IcallWiringProbe\IcallWiringProbe.csproj' }
    )
    foreach ($p in $projects) {
        $out = Join-Path $OutDir $p.Name
        dotnet build (Join-Path $Tools $p.Proj) -c $Config -o $out --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "build failed: $($p.Proj)" }
    }
}

$metaExe  = Join-Path $OutDir 'meta\MetadataIcallCounter.dll'
$graphExe = Join-Path $OutDir 'graph\IcallCallGraph.dll'
$probeExe = Join-Path $OutDir 'probe\IcallWiringProbe.dll'
foreach ($p in @($metaExe, $graphExe, $probeExe)) { Need $p }

# --- 2. metadata channel ----------------------------------------------------
Step 'channel 2: metadata (System.Reflection.Metadata)'
$metaOut = Join-Path $OutDir 'metadata.tsv'
dotnet $metaExe $MscorlibPath $metaOut
if ($LASTEXITCODE -ne 0) { throw 'metadata channel failed' }

# --- 3. channel cross-check -------------------------------------------------
Step 'cross-check: channel counts and join keys'
$refl = @(Get-Content -LiteralPath $reflOut)
$meta = @(Get-Content -LiteralPath $metaOut)
Write-Host "reflection rows: $($refl.Count)"
Write-Host "metadata   rows: $($meta.Count)"
if ($refl.Count -ne $meta.Count) {
    throw "CHANNEL MISMATCH: reflection=$($refl.Count) metadata=$($meta.Count)"
}

# The join key is the exact (declaring type, method name, signature, kind)
# tuple. Signature RENDERING must therefore be identical on both channels -- a
# mismatch here would be a formatter difference, not a real disagreement, so it
# is reported separately and treated as a hard failure only when the counts
# differ.
$reflKeys = $refl | ForEach-Object { $p = $_ -split "`t"; "$($p[0])|$($p[1])|$($p[2])|$($p[4])" }
$metaKeys = $meta | ForEach-Object { $p = $_ -split "`t"; "$($p[0])|$($p[1])|$($p[2])|$($p[4])" }
$odd = @(Compare-Object ($reflKeys | Sort-Object) ($metaKeys | Sort-Object))
if ($odd.Count -eq 0) {
    Write-Host 'join keys identical' -ForegroundColor Green
} else {
    Write-Host "join-key differences: $($odd.Count)" -ForegroundColor Yellow
    $odd | Select-Object -First 20 | Format-Table -AutoSize | Out-Host
    throw "CHANNEL KEY MISMATCH: $($odd.Count) key(s) differ between reflection and metadata"
}

# --- 4. IL call graph -------------------------------------------------------
Step 'call graph (reachability)'
$graphOut = Join-Path $OutDir 'callgraph.json'
dotnet $graphExe $MscorlibPath $graphOut
if ($LASTEXITCODE -ne 0) { throw 'call graph failed' }

# --- 5. wiring probe --------------------------------------------------------
Step 'wiring probe (MethodRefResolver.BclResolve)'
$probeOut = Join-Path $OutDir 'probe.tsv'
dotnet $probeExe $MscorlibPath $probeOut
if ($LASTEXITCODE -ne 0) { throw 'wiring probe failed' }

# --- 6..9. analysis stages --------------------------------------------------
$py = Get-Command python -ErrorAction SilentlyContinue
if (-not $py) { throw 'python 3 is required for the analysis stages' }

Step 'analysis: runtime symbols'
& python (Join-Path $Tools 'parse_runtime_syms.py') $Root (Join-Path $OutDir 'runtime_syms.json')

Step 'analysis: wiring surface'
& python (Join-Path $Tools 'parse_wiring_surface.py') $Root (Join-Path $OutDir 'arms.json')

Step 'analysis: wiring sites'
& python (Join-Path $Tools 'locate_wiring_sites.py') $Root $OutDir

Step 'analysis: classification'
& python (Join-Path $Tools 'classify_icalls.py') $OutDir

Step 'render: docs/audit/icall-mscorlib-current.md'
& python (Join-Path $Tools 'render_report.py') $OutDir $Root

Step 'verify: every cited file:line'
& python (Join-Path $Tools 'verify_report.py') $OutDir $Root

Write-Host ''
Write-Host "artifacts in $OutDir" -ForegroundColor Green
Write-Host 'final data: classified.json'
