# off-c-drive.ps1 -- keep EVERY artifact of this project off the system drive.
#
# User directive (2026-11-01): "别把你的产物弄到C盘去，就在本目录的.opencode里面搞，
# 主子代理都必须遵守." Every scratch artifact -- compiler temp files, NuGet packages,
# ccache/sccache, harness reports, per-case scratch dirs -- must live under
# F:\il2cpp\.opencode.
#
# Why this is load-bearing (measured, not theoretical):
#   * C: free space fell to 5.59 GB during two parallel builds and g++ then failed with
#     "error writing to C:\Users\...\Temp\ccXXXX.s: No space left on device", which the
#     harness surfaces as a PHANTOM "cmake build failed" (a fake regression).
#   * The default NuGet global-packages folder is C:\Users\<user>\.nuget\packages and this
#     repo's own restore output filled 487 MB / 3580 files there.
#
# Dot-source this from a script -- `. (Join-Path $PSScriptRoot "off-c-drive.ps1")` -- or run
# it before a manual build. It is idempotent and cheap.
#
# The NuGet side is ALSO pinned declaratively in the repo's tracked NuGet.config
# (globalPackagesFolder + cleared fallbackPackageFolders), so a `dotnet restore` from any
# worktree lands in .opencode\nuget\packages even without this file. NUGET_PACKAGES is set
# here too so an explicitly-exported value cannot silently move it back onto C:.

$script:Il2CppScratchRoot = "F:\il2cpp\.opencode"

function Initialize-Il2CppScratch {
    param([string]$Root = $script:Il2CppScratchRoot)

    # TMPDIR is honoured by POSIX-ish children (msys2 tools), TEMP/TMP by Windows ones.
    $temp = Join-Path $Root "tmp\temp"
    $nuget = Join-Path $Root "nuget\packages"
    foreach ($d in @($temp, $nuget, (Join-Path $Root "tmp\casecwd"), (Join-Path $Root "tmp\diffharness"))) {
        if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
    }

    $env:TEMP = $temp
    $env:TMP = $temp
    $env:TMPDIR = $temp
    $env:NUGET_PACKAGES = $nuget
}

function Assert-Il2CppOffCDrive {
    # Guard for harness entry points: fail loudly rather than silently writing to C:.
    param([string]$Root = $script:Il2CppScratchRoot)
    foreach ($name in @("TEMP", "TMP", "NUGET_PACKAGES")) {
        $v = [Environment]::GetEnvironmentVariable($name)
        if (-not $v) { continue }
        if ($v -match '^[Cc]:') {
            throw "off-c-drive: $name points at the system drive ('$v'); dot-source scripts/off-c-drive.ps1 first"
        }
    }
    if (-not (Test-Path -LiteralPath $Root)) {
        throw "off-c-drive: scratch root '$Root' does not exist"
    }
}

Initialize-Il2CppScratch