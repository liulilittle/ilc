#------------------------------------------------------------------------------
# build-unit-fixtures.ps1 -- build the net48 oracle-case fixtures consumed by
# the unit test suite (tests/unit) from tests/cases/<name>/obj/Release/net48/
# <name>.exe.
#
# Why: several unit tests open the REAL csc-built case exe as ground truth
# (StackFoldTests, StackFoldTests.InitializeArray, CodeGeneratorTests,
# SecurityStubTests, MetadataLoaderTests). A fresh scratch/CI checkout that has
# not built the cases yet fails those tests with
# InvalidOperationException("fixture ... not found"). The live repo baseline
# has the fixtures because the case sweeps built them; run this script BEFORE
# `dotnet test tests/unit` in any scratch/CI tree.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-unit-fixtures.ps1
#   powershell ... -File scripts/build-unit-fixtures.ps1 -Case base64case
#   powershell ... -File scripts/build-unit-fixtures.ps1 -ListOnly
#
# Case discovery: call sites in tests/unit/*.cs -- CaseExePath("name"),
# OpenCaseExe("name") and the Path.Combine(..., "tests", "cases", "name", ...)
# convention -- so newly referenced fixtures are picked up automatically.
#
# Output: <root>/tests/cases/<name>/obj/Release/net48/<name>.exe (the exact
# path the tests read). A case whose csproj is missing is a WARNING (trimmed
# trees may drop cases; the walk-up test resolvers then read an enclosing
# repo) and does not fail the run; a case that FAILS to build or produces no
# exe DOES fail the run (exit 1).
#------------------------------------------------------------------------------
param(
    [string]$Case = "",
    [switch]$ListOnly
)
$ErrorActionPreference = "Stop"
# Resolve the repo root from the script location (scripts/ -> root), matching
# run-tests.ps1 so -File mode is CWD-independent.
$Root = Split-Path -Parent $PSScriptRoot
$unitDir = Join-Path $Root "tests/unit"

$names = [System.Collections.Generic.SortedSet[string]]::new()
Get-ChildItem -LiteralPath $unitDir -Filter *.cs -File | ForEach-Object {
    $text = Get-Content -LiteralPath $_.FullName -Raw
    foreach ($m in [regex]::Matches($text, '(?:CaseExePath|OpenCaseExe)\("([A-Za-z0-9_\-]+)"\)'))
    {
        [void]$names.Add($m.Groups[1].Value)
    }
    foreach ($m in [regex]::Matches($text, '"tests",\s*"cases",\s*"([A-Za-z0-9_]+)"'))
    {
        [void]$names.Add($m.Groups[1].Value)
    }
}
if ($names.Count -eq 0)
{
    throw "no case fixture references found under $unitDir"
}
Write-Host ("discovered {0} case fixture reference(s): {1}" -f $names.Count, ($names -join ", "))
if ($ListOnly)
{
    return
}

$built = 0
$skipped = 0
$failed = 0
foreach ($name in $names)
{
    if ($Case -and $name -ne $Case) { continue }
    $csproj = Join-Path $Root "tests/cases/$name/$name.csproj"
    $exe = Join-Path $Root "tests/cases/$name/obj/Release/net48/$name.exe"
    if (-not (Test-Path -LiteralPath $csproj))
    {
        Write-Warning "case project not present, skipping: tests/cases/$name/$name.csproj"
        ++$skipped
        continue
    }
    Write-Host "=== fixture: $name ==="
    & dotnet build $csproj -c Release -v:m
    if ($LASTEXITCODE -ne 0)
    {
        Write-Warning "build FAILED: $name (exit $LASTEXITCODE)"
        ++$failed
        continue
    }
    if (-not (Test-Path -LiteralPath $exe))
    {
        Write-Warning "build succeeded but fixture missing: $exe"
        ++$failed
        continue
    }
    ++$built
}
Write-Host ("fixtures built=$built skipped=$skipped failed=$failed")
if ($failed -gt 0) { exit 1 }
