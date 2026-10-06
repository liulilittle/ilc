<#
.SYNOPSIS
  One-command build for an IL2CPP-transpiled application.
  P-F friendly-compiler one-click entry (P-F-AUDIT-1 双审定案 §5/§6).

.DESCRIPTION
  Takes a managed program (.csproj or already-built .exe/.dll) and produces one
  of the three P-F build forms with a single command:

    exe     -- a native executable (Windows .exe / Linux ELF), self-hosted main
    static  -- a distributable static library package
               (lib<name>.a + libil2cpp_runtime.a + include/) linkable by an
               external host (tests/capi style)
    shared  -- a loadable C-API library (Windows .dll / Linux .so) consumed via
               dlopen/LoadLibrary + il2cpp_entry_main; NEVER co-link it with the
               static runtime (two runtime instances would own the GC/string pool)

  Pipeline: [dotnet build] -> il2cppc transpile -> cmake configure -> cmake build
  --target <form> -> artifacts + side native libraries (P/Invoke SHARED modules)
  copied next to the artifact so dlopen/LoadLibrary find them at runtime.

  Toolchains are discovered from the environment / parameters ONLY (no
  machine-specific paths): IL2CPP_MINGW_ROOT, IL2CPP_MSVC_GENERATOR,
  IL2CPP_WSL_DISTRO, IL2CPP_NDK_ROOT.

  Toolchain differences (friendly-compiler S8): msvc exists ONLY in the PS1
  mirror (build-app.sh targets POSIX/WSL hosts, which have no Visual Studio --
  an architectural exclusion, not an oversight). ndk targets Android
  arm64-v8a and therefore produces a static/shared LIBRARY (no native exe
  runner); wsl targets Linux ELF and runs natively inside the distro.

.EXAMPLE
  pwsh scripts/build-app.ps1 -Input MyApp/MyApp.csproj -Kind exe
  pwsh scripts/build-app.ps1 -Input app.exe -Kind static -Toolchain mingw -LibName myapp
  pwsh scripts/build-app.ps1 -Input app.csproj -Kind shared -Toolchain wsl
  pwsh scripts/build-app.ps1 -Input app.csproj -Kind exe -Log build-app.log
#>
param(
    [Parameter(Mandatory = $true)]
    [Alias("Input")]
    [string]$TargetInput,
    [ValidateSet("exe", "static", "shared")]
    [string]$Kind = "exe",
    [ValidateSet("mingw", "msvc", "wsl", "ndk")]
    [string]$Toolchain = "mingw",
    [string]$OutDir = "",
    [switch]$Clean,
    [string]$Config = "Release",
    [string]$LibName = "",
    [string]$Arch = "",
    [string]$Log = ""
)
$ErrorActionPreference = "Stop"

# Root anchored at the script location (scripts/ -> root), matching
# run-tests.ps1 so -File mode is CWD-independent.
$Root = Split-Path -Parent $PSScriptRoot

function Write-Err([string]$msg) { Write-Host "ERROR: $msg" -ForegroundColor Red }

function Assert-Exit0([string]$what) {
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

# P-F-FIX D1: run a native command without letting its STDERR abort the script.
# Windows PowerShell 5.1 (and PS7 with PSNativeCommandUseErrorActionPreference)
# can turn native stderr lines (compiler warnings -- gcc16 -Warray-bounds on
# formatext.cpp:436 / numberformat.cpp:412) into NativeCommandError which, under
# $ErrorActionPreference=Stop, terminates a build that ACTUALLY succeeded
# (exit=0). Fix: merge 2>&1 under a local Continue so stderr is rendered
# non-fatally, then judge success ONLY by $LASTEXITCODE.
function Invoke-Native([string]$Exe, [string[]]$ArgsList, [string]$What) {
    $ErrorActionPreference = "Continue"
    try {
        & $Exe @ArgsList 2>&1 | ForEach-Object { $_.ToString() }
        $rc = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = "Stop"
    }
    if ($rc -ne 0) { throw "$What failed (exit $rc)" }
}

function ConvertTo-WslPath([string]$winPath) {
    $drive = $winPath.Substring(0, 1).ToLower()
    $rest = $winPath.Substring(3).Replace("\", "/")
    return "/mnt/$drive/$rest"
}

# ---------------------------------------------------------------------------
# 1. Locate the input and produce a managed assembly to transpile
# ---------------------------------------------------------------------------
# ($Input is a reserved automatic variable, so the public parameter is named
# -TargetInput with an -Input alias; internally we use $TargetInput.)
$InputPath = $TargetInput
if (-not (Test-Path -LiteralPath $InputPath)) { throw "input not found: $InputPath" }
$InputItem = Get-Item -LiteralPath $InputPath
$InputIsProject = $InputItem.PSIsContainer -or $InputItem.Extension -eq ".csproj"
# The per-app working directory: for a .csproj/.exe FILE that is its parent
# directory; for a directory input it is the directory itself.
$inputDir = if ($InputItem.PSIsContainer) { $InputItem.FullName } else { $InputItem.DirectoryName }
$assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($InputItem.Name)
if ($LibName -eq "") { $LibName = $assemblyName }

$staging = ""
if ($InputIsProject) {
    $staging = Join-Path $inputDir "out_app"
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    Invoke-Native dotnet @("build", $InputItem.FullName, "-c", $Config, "-o", $staging) "dotnet build $($InputItem.Name)"
    # net48 Exe targets produce <name>.exe; net8 console apps produce a
    # <name>.dll (the apphost exe carries the runtime config). Transpile the
    # managed assembly with metadata.
    $transpileInput = if (Test-Path (Join-Path $staging "$assemblyName.dll")) {
        Join-Path $staging "$assemblyName.dll"
    } else { Join-Path $staging "$assemblyName.exe" }
    if (-not (Test-Path -LiteralPath $transpileInput)) {
        throw "dotnet build produced no managed assembly at $staging (expected $assemblyName.exe/.dll)"
    }
} else {
    $transpileInput = $InputItem.FullName
}

# ---------------------------------------------------------------------------
# 2. Transpile to a CMake project (il2cppc <input> <gen>)
# ---------------------------------------------------------------------------
$gen = Join-Path $inputDir "gen_app"
if ($Clean) { Remove-Item $gen -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $gen | Out-Null

# The transpiler itself is a net8 console app: use the built dll when present,
# otherwise build it once (self-contained one-click).
$il2cppcDll = Join-Path $Root "src/il2cppc/bin/$Config/net8.0/il2cppc.dll"
if (-not (Test-Path -LiteralPath $il2cppcDll)) {
    Invoke-Native dotnet @("build", (Join-Path $Root "src/il2cppc/il2cppc.csproj"), "-c", $Config) "transpiler build"
}
$transpileArgs = @("exec", $il2cppcDll)
if ($Log -ne "") { $transpileArgs += @("--log", $Log) }
# G4-x86: forward the target architecture to the transpiler (--arch) so the
# generated layout + G1 entry interlock match the cmake -A platform (a default
# host-width transpile + -A Win32 build is rejected by the interlock by design).
$archToken = switch ($Arch) {
    "Win32" { "x86" }
    "x64"   { "x64" }
    "ARM64" { "arm64" }
    "arm32" { "arm32" }
    default { "" }
}
if ($archToken -ne "") { $transpileArgs += @("--arch", $archToken) }
$transpileArgs += @($transpileInput, $gen)
Invoke-Native dotnet $transpileArgs "il2cppc transpile"

# ---------------------------------------------------------------------------
# 3. cmake configure + build (generator/toolchain per -Toolchain)
# ---------------------------------------------------------------------------
$build = Join-Path $gen "build_$Toolchain"
if ($Clean) { Remove-Item $build -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $build | Out-Null

# --target per -Kind
$target = switch ($Kind) {
    "exe"    { "il2cpp_gen_exe" }
    "static" { "il2cpp_gen" }
    "shared" { "il2cpp_gen_shared" }
}

$cmakeArgs = @()
$genExtra = @("-DCMAKE_BUILD_TYPE=$Config")
if ($LibName) { $genExtra += "-DIL2CPP_LIB_NAME=$LibName" }
$configFlag = @()

switch ($Toolchain) {
    "mingw" {
        # MinGW native (MSYS2/MinGW-w64): the mingw-w64.cmake toolchain finds
        # the compiler from IL2CPP_MINGW_ROOT / MINGW_ROOT / PATH -- no machine
        # path is hardcoded. It also static-links libgcc/libstdc++ so the exe
        # does not require those runtime DLLs alongside.
        if ($env:IL2CPP_MINGW_ROOT) {
            $env:PATH = "$env:IL2CPP_MINGW_ROOT\bin;" + $env:PATH
        }
        $toolchainFile = Join-Path $Root "cmake/mingw-w64.cmake"
        $cmakeArgs = @("-S", $gen, "-B", $build, "-G", "MinGW Makefiles",
                       "-DCMAKE_TOOLCHAIN_FILE=$toolchainFile") + $genExtra
    }
    "msvc" {
        $generator = if ($env:IL2CPP_MSVC_GENERATOR) { $env:IL2CPP_MSVC_GENERATOR } else { "Visual Studio 17 2022" }
        $cmakeArgs = @("-S", $gen, "-B", $build, "-G", $generator)
        if ($Arch -ne "") { $cmakeArgs += @("-A", $Arch) }
        $configFlag = @("--config", $Config)
    }
    "wsl" {
        # WSL channel (locked to Ubuntu1804 by default; IL2CPP_WSL_DISTRO to
        # override). WSL cmake 3.10 lacks -S/-B, so cd into the build dir.
        $distro = if ($env:IL2CPP_WSL_DISTRO) { $env:IL2CPP_WSL_DISTRO } else { "Ubuntu1804" }
        $wslGen = ConvertTo-WslPath (Resolve-Path $gen)
        $wslBuild = ConvertTo-WslPath (Resolve-Path $build)
        $extra = ""
        if ($LibName) { $extra = " -DIL2CPP_LIB_NAME=$LibName" }
        Invoke-Native wsl @("-d", $distro, "bash", "-c", "cd `"$wslBuild`" && rm -rf * && cmake -G 'Unix Makefiles' -DCMAKE_BUILD_TYPE=$Config `"$wslGen`"$extra && cmake --build . --target $target") "wsl cmake build"
        $builtInWsl = $true
    }
    "ndk" {
        # Android NDK skeleton (P-F-AUDIT-1 §6/§8): reuses the NDK
        # android.toolchain.cmake with ABI per -Arch. P-F-FIX D3: the NDK root
        # comes from env ONLY (IL2CPP_NDK_ROOT / NDK_ROOT) -- cmake 3.22's ABI
        # try_compile does not propagate a plain -DANDROID_NDK cache var into
        # the sub-build, so it maps to the CMAKE_-prefixed CMAKE_ANDROID_NDK
        # (inherited by try_compile). G5: -Arch maps to -DANDROID_ABI so the
        # ABI is selectable (defaults to arm64-v8a when -Arch is empty).
        $ndk = if ($env:IL2CPP_NDK_ROOT) { $env:IL2CPP_NDK_ROOT } elseif ($env:NDK_ROOT) { $env:NDK_ROOT } else {
            throw "ndk toolchain needs IL2CPP_NDK_ROOT (or NDK_ROOT) env; no machine paths are hardcoded"
        }
        $abiTarget = switch ($Arch) {
            "ARM64" { "arm64-v8a" }
            "arm32" { "armeabi-v7a" }
            "x64"   { "x86_64" }
            "x86"   { "x86" }
            default { "" }
        }
        $toolchainFile = Join-Path $Root "cmake/android-ndk.cmake"
        $cmakeArgs = @("-S", $gen, "-B", $build, "-G", "Ninja", "-DCMAKE_BUILD_TYPE=$Config",
                       "-DCMAKE_TOOLCHAIN_FILE=$toolchainFile", "-DCMAKE_ANDROID_NDK=$ndk") + $genExtra
        if ($abiTarget -ne "") { $cmakeArgs += "-DANDROID_ABI=$abiTarget" }
    }
}

if (-not $builtInWsl) {
    Invoke-Native cmake $cmakeArgs "cmake configure"
    Invoke-Native cmake @("--build", $build, "--target", $target) + $configFlag "cmake build --target $target"
}

# ---------------------------------------------------------------------------
# 4. Deploy side native libraries (P/Invoke SHARED modules) next to the
#    artifact so dlopen/LoadLibrary find them at runtime
#    (mirrors run-tests.ps1 Deploy-NativeLibs).
# ---------------------------------------------------------------------------
function Deploy-NativeLibs([string]$buildDir, [string]$destDir) {
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    foreach ($ext in @("*.dll", "*.so")) {
        Get-ChildItem $buildDir -Recurse -Filter $ext -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -ne (Resolve-Path $destDir).Path } |
            ForEach-Object {
                try { Copy-Item $_.FullName (Join-Path $destDir $_.Name) -Force -ErrorAction Stop }
                catch { Write-Warning "deploy native lib failed (non-fatal): $_" }
            }
    }
}

# ---------------------------------------------------------------------------
# 5. Artifacts -> OutDir (default: <gen>/dist_<toolchain>)
#    Manual per-kind copy (the generated install() rules target `cmake --install`
#    users; build-app only builds the ONE selected --target, so it packages the
#    built artifacts directly).
# ---------------------------------------------------------------------------
if ($OutDir -eq "") { $OutDir = Join-Path $gen "dist_$Toolchain" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

function Get-BuildArtifact([string]$buildDir, [string[]]$patterns) {
    foreach ($pat in $patterns) {
        $hit = Get-ChildItem $buildDir -Recurse -Filter $pat -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $pat } | Select-Object -First 1
        if ($hit) { return $hit }
    }
    return $null
}

function Copy-PublicHeaders([string]$destDir) {
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    # generated public headers (generated.h, per-type .h, string-pool.h, ...)
    if (Test-Path (Join-Path $gen "include")) {
        Copy-Item (Join-Path $gen "include\*") $destDir -Recurse -Force
    }
    # the full runtime API surface for external consumers
    if (Test-Path (Join-Path $Root "src/runtime/include")) {
        Copy-Item (Join-Path $Root "src/runtime/include\*") $destDir -Recurse -Force
    }
}

# MinGW runtime DLLs (libgcc/libstdc++/winpthread) -- not hardcoded: sourced
# from the toolchain's bin dir, so exe/shared artifacts run on machines that
# have no MinGW in PATH (only relevant for the mingw toolchain).
function Deploy-MingwRuntime([string]$destDir) {
    if ($Toolchain -ne "mingw") { return }
    $binDir = ""
    if ($env:IL2CPP_MINGW_ROOT) { $binDir = Join-Path $env:IL2CPP_MINGW_ROOT "bin" }
    else {
        $gccPath = (Get-Command gcc.exe -ErrorAction SilentlyContinue).Source
        if ($gccPath) { $binDir = Split-Path -Parent $gccPath }
    }
    if (-not $binDir -or -not (Test-Path $binDir)) { return }
    foreach ($rt in @("libgcc_s_seh-1.dll", "libstdc++-6.dll", "libwinpthread-1.dll")) {
        if (Test-Path (Join-Path $binDir $rt)) {
            Copy-Item (Join-Path $binDir $rt) (Join-Path $destDir $rt) -Force
        }
    }
}

$libBase = if ($LibName) { $LibName } else { "il2cpp_gen" }

if ($Kind -eq "exe") {
    if ($builtInWsl) {
        $wslOut = ConvertTo-WslPath $OutDir
        Invoke-Native wsl @("-d", $distro, "bash", "-c", "cp `"$wslBuild/il2cpp_gen_exe`" `"$wslOut/il2cpp_gen_exe`"") "copy exe to outdir"
    } else {
        $exe = Get-BuildArtifact $build @("il2cpp_gen_exe.exe", "il2cpp_gen_exe")
        if (-not $exe) { throw "exe artifact not found under $build" }
        Copy-Item $exe.FullName (Join-Path $OutDir $exe.Name) -Force
    }
    Deploy-NativeLibs $build $OutDir
    Deploy-MingwRuntime $OutDir
} elseif ($Kind -eq "static") {
    # distributable three-piece set: lib<name>.a + libil2cpp_runtime.a + include/
    $libDir = Join-Path $OutDir "lib"
    New-Item -ItemType Directory -Force -Path $libDir | Out-Null
    $staticLib = Get-BuildArtifact $build @("lib$libBase.a", "$libBase.lib", "libil2cpp_gen.a", "il2cpp_gen.lib")
    if (-not $staticLib) { throw "static artifact not found under $build (expected lib$libBase.a / $libBase.lib)" }
    Copy-Item $staticLib.FullName (Join-Path $libDir $staticLib.Name) -Force
    $runtimeLib = Get-BuildArtifact $build @("libil2cpp_runtime.a", "il2cpp_runtime.lib")
    if ($runtimeLib) { Copy-Item $runtimeLib.FullName (Join-Path $libDir $runtimeLib.Name) -Force }
    Copy-PublicHeaders (Join-Path $OutDir "include")
} else {
    # shared: loadable C-API library (Windows .dll / Linux .so) + import lib
    # (Windows) + runtime lib + headers. SHARED consumers dlopen/LoadLibrary it
    # and call il2cpp_entry_main -- never co-link with the static runtime.
    $sharedLib = Get-BuildArtifact $build @("lib$libBase.dll", "$libBase.dll", "lib$libBase.so", "$libBase.so")
    if (-not $sharedLib) { throw "shared artifact not found under $build (expected lib$libBase.dll / lib$libBase.so)" }
    Copy-Item $sharedLib.FullName (Join-Path $OutDir $sharedLib.Name) -Force
    # import library so a host can LINK the C-API instead of dlsym/GetProcAddress
    $importLib = Get-BuildArtifact $build @("lib$libBase.dll.a", "$libBase.lib")
    if ($importLib) { Copy-Item $importLib.FullName (Join-Path $OutDir $importLib.Name) -Force }
    $libDir = Join-Path $OutDir "lib"
    New-Item -ItemType Directory -Force -Path $libDir | Out-Null
    $runtimeLib = Get-BuildArtifact $build @("libil2cpp_runtime.a", "il2cpp_runtime.lib")
    if ($runtimeLib) { Copy-Item $runtimeLib.FullName (Join-Path $libDir $runtimeLib.Name) -Force }
    Copy-PublicHeaders (Join-Path $OutDir "include")
    Deploy-NativeLibs $build $OutDir
    Deploy-MingwRuntime $OutDir
}

Write-Host ""
Write-Host "build-app OK: -Input $($InputItem.FullName) -Kind $Kind -Toolchain $Toolchain"
Write-Host "  transpiled project : $gen"
Write-Host "  build dir          : $build"
Write-Host "  artifacts out      : $OutDir"
Get-ChildItem $OutDir -Recurse -File | ForEach-Object { Write-Host "    $($_.FullName.Substring($OutDir.Length).TrimStart('\'))" }
