param(
  [Parameter(Mandatory=$true)][string]$Name
)
$ErrorActionPreference = "Stop"
$root = "E:\il2cpp"
$dir = Join-Path $root "tests\cases\$Name"
$il2cppc = Join-Path $root "src\il2cppc\bin\Release\net8.0\il2cppc.dll"
if (-not (Test-Path $il2cppc)) { throw "il2cppc.dll missing" }

Set-Location $dir
Write-Host "=== [$Name] build net48 ==="
& dotnet build "$Name.csproj" -c Release -o out 2>&1 | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host "=== [$Name] expected.txt (net48) ==="
$expected = & ".\out\$Name.exe" 2>&1
$expected | Set-Content -Path expected.txt -Encoding utf8
if (-not (Test-Path expected.txt)) { throw "expected.txt not written" }
if ((Get-Item expected.txt).Length -eq 0) { throw "expected.txt empty" }

Write-Host "=== [$Name] translate ==="
if (Test-Path gen) { Remove-Item gen -Recurse -Force }
if (Test-Path build_mingw) { Remove-Item build_mingw -Recurse -Force }
& dotnet $il2cppc "out\$Name.exe" gen 2>&1 | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { throw "translate failed" }

Write-Host "=== [$Name] cmake ==="
& cmake -S gen -B build_mingw -G "MinGW Makefiles" 2>&1 | Select-Object -Last 2
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }

Write-Host "=== [$Name] cmake build ==="
& cmake --build build_mingw 2>&1 | Select-Object -Last 5
if ($LASTEXITCODE -ne 0) { throw "cmake build failed" }

Write-Host "=== [$Name] gen run ==="
$genExe = ".\build_mingw\il2cpp_gen_exe.exe"
if (-not (Test-Path $genExe)) { $genExe = ".\build_mingw\il2cpp_gen_exe" }
$actual = & $genExe 2>&1
$rc = $LASTEXITCODE
$actual | Set-Content -Path actual.txt -Encoding utf8
Write-Host "gen exit code: $rc"
if ($rc -ne 0) { Write-Host "RESULT=CRASH exit=$rc"; exit 2 }
if (-not (Test-Path actual.txt)) { Write-Host "RESULT=CRASH no actual.txt"; exit 2 }
if ((Get-Item actual.txt).Length -eq 0) { Write-Host "RESULT=CRASH actual.txt empty"; exit 2 }

$expLines = @(Get-Content expected.txt)
$actLines = @(Get-Content actual.txt)
if ($expLines.Count -eq 0) { Write-Host "RESULT=FAIL expected empty"; exit 3 }
if ($actLines.Count -eq 0) { Write-Host "RESULT=FAIL actual empty"; exit 3 }
$diff = Compare-Object $expLines $actLines
if ($null -eq $diff -or $diff.Count -eq 0) {
  Write-Host "RESULT=PASS lines=$($actLines.Count)"
  exit 0
} else {
  Write-Host "RESULT=DIFF count=$($diff.Count)"
  $diff | Select-Object -First 8 | ForEach-Object { Write-Host ("    " + $_.SideIndicator + " " + $_.InputObject) }
  exit 1
}
