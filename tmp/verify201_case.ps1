param([Parameter(Mandatory)][string]$Case)
$ErrorActionPreference = "Stop"
$Root = "E:\il2cpp"
$caseDir = "$Root/tests/cases/$Case"
$tmp = "$Root/tmp/verify201"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Invoke-NativeRun {
    param([string]$ExePath)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $ExePath
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $p = [System.Diagnostics.Process]::new()
    $p.StartInfo = $psi
    $null = $p.Start()
    $outTask = $p.StandardOutput.ReadToEndAsync()
    $errTask = $p.StandardError.ReadToEndAsync()
    $p.WaitForExit()
    return [pscustomobject]@{
        Out  = $outTask.GetAwaiter().GetResult()
        Err  = $errTask.GetAwaiter().GetResult()
        Code = $p.ExitCode
    }
}

# 1. build net48 oracle exe
dotnet build "$caseDir/$Case.csproj" -c Release -o "$caseDir/out" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build $Case failed" }

# 1.5 mylib.dll if present
if (Test-Path "$caseDir/mylib") {
    $mylibSrc = Get-ChildItem "$caseDir/mylib" -Filter *.cpp | Select-Object -First 1
    if ($mylibSrc) {
        g++ -shared -o "$caseDir/out/mylib.dll" $mylibSrc.FullName
        if ($LASTEXITCODE -ne 0) { throw "build mylib.dll for $Case failed" }
    }
}

# 2. reference run (net48 oracle)
$exp = Invoke-NativeRun "$caseDir/out/$Case.exe"
if ($exp.Code -ne 0) { throw "reference run $Case failed (code $($exp.Code))" }

# 3. transpile (Release il2cppc, NOT rebuilt)
$gen = "$caseDir/gen"
Remove-Item $gen -Recurse -Force -ErrorAction SilentlyContinue
& "$Root/src/il2cppc/bin/Release/net8.0/il2cppc.exe" "$caseDir/out/$Case.exe" $gen *> "$tmp/${Case}_transpile.log"
if ($LASTEXITCODE -ne 0) { throw "transpile $Case failed (code $($LASTEXITCODE))" }

# 4. cmake configure + build (mingw, parallel 16)
$buildDir = "$caseDir/build_mingw"
cmake -S $gen -B $buildDir -G "MinGW Makefiles" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "cmake configure $Case failed" }
cmake --build $buildDir --parallel 16 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "cmake build $Case failed" }

# 5. run generated exe
$actual = Invoke-NativeRun "$buildDir/il2cpp_gen_exe.exe"
if ($actual.Code -ne 0) { throw "gen run $Case failed (code $($actual.Code))" }

# 6. compare (stdout / stderr split, LF join, same semantics as scripts/run-tests.ps1)
function Split-Lines([string]$text) {
    if ($null -eq $text) { return @() }
    $lines = @($text -split "`r?`n")
    if ($lines.Count -gt 0 -and $lines[-1] -eq '') { $lines = $lines[0..($lines.Count-2)] }
    return @($lines)
}
$eOut = ((Split-Lines $exp.Out) -join "`n")
$eErr = ((Split-Lines $exp.Err) -join "`n")
$aOut = ((Split-Lines $actual.Out) -join "`n")
$aErr = ((Split-Lines $actual.Err) -join "`n")
$diffOut = @(Compare-Object @($eOut -split "`n") @($aOut -split "`n"))
$diffErr = @(Compare-Object @($eErr -split "`n") @($aErr -split "`n"))
$diffCount = $diffOut.Count + $diffErr.Count
$eLines = @($eOut -split "`n"); if ($eLines.Count -eq 1 -and $eLines[0] -eq '') { $eLines = @() }
$aLines = @($aOut -split "`n"); if ($aLines.Count -eq 1 -and $aLines[0] -eq '') { $aLines = @() }

# persist captures for record (tmp only, never touches tracked files)
$eOut | Set-Content -Path "$tmp/${Case}_expected.txt" -Encoding utf8NoBOM
$aOut | Set-Content -Path "$tmp/${Case}_actual.txt" -Encoding utf8NoBOM

"=== $Case : expected_lines=$($eLines.Count) actual_lines=$($aLines.Count) diff=$diffCount ==="
if ($diffCount -gt 0) {
    "DIFF OUT:"
    $diffOut | Select-Object -First 20
    "DIFF ERR:"
    $diffErr | Select-Object -First 20
    exit 1
} else {
    "PASS: $Case (0 diff)"
    exit 0
}
